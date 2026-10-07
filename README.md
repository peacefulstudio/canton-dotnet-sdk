# Canton .NET SDK

[![CI](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml/badge.svg)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml)
[![Release](https://img.shields.io/github/v/release/peacefulstudio/canton-dotnet-sdk?label=latest%20stable)](https://github.com/peacefulstudio/canton-dotnet-sdk/releases/latest)
[![latest preview](https://img.shields.io/github/v/release/peacefulstudio/canton-dotnet-sdk?include_prereleases&label=latest%20preview)](https://github.com/peacefulstudio/canton-dotnet-sdk/releases)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-white.svg)](https://dotnet.microsoft.com/)

Build .NET applications on a Canton/Daml ledger with full type safety. `dpm codegen-cs` turns a
Daml `.dar` archive into strongly-typed C#: records for every template and data type, typed
choices, and `Try…Async` methods that create contracts and exercise choices. One transport-neutral
`ICantonLedgerClient` then submits them over gRPC or the JSON Ledger API, with a PQS client,
authentication, OpenTelemetry tracing and in-memory test doubles alongside.

Maintained by [Peaceful Studio](https://peaceful.studio), which also offers
[consulting](#consulting) for teams building on Canton.

## NuGet Packages

All 16 packages share one version and publish together from one `v*` tag, alongside the
`dpm codegen-cs` component of the same version. Upgrade them together.

| Package | Use it for |
|---|---|
| [`Daml.Runtime`](https://www.nuget.org/packages/Daml.Runtime/) | The types generated code compiles against: `Party`, `ContractId<T>`, commands, outcomes, JSON serialization. Every consumer references it. |
| [`Daml.Codegen.CSharp`](https://www.nuget.org/packages/Daml.Codegen.CSharp/) | The C# emitter as a library, for driving code generation from your own tooling. |
| [`Daml.Codegen.Intermediate`](https://www.nuget.org/packages/Daml.Codegen.Intermediate/) | The Intermediate DAR contract the emitter consumes. |
| [`Canton.Ledger.Grpc.Client`](https://www.nuget.org/packages/Canton.Ledger.Grpc.Client/) | The gRPC ledger and admin clients (`AddLedgerClient` / `AddAdminClient`). The default transport. |
| [`Canton.Ledger.Rest.Client`](https://www.nuget.org/packages/Canton.Ledger.Rest.Client/) | The same client surface over the HTTP JSON Ledger API (`AddRestLedgerClient`). |
| [`Canton.Ledger.Pqs.Client`](https://www.nuget.org/packages/Canton.Ledger.Pqs.Client/) | Typed, SQL-injection-safe queries over active contracts in the Participant Query Store. |
| [`Daml.Ledger.Abstractions`](https://www.nuget.org/packages/Daml.Ledger.Abstractions/) | The transport-agnostic `ILedgerClient` that generated code submits through. |
| [`Canton.Ledger.Abstractions`](https://www.nuget.org/packages/Canton.Ledger.Abstractions/) | The transport-neutral Canton contract layer: `ICantonLedgerClient`, `IAdminClient`, `ITokenProvider`, `IPqsClient`. |
| [`Canton.Ledger.Kernel`](https://www.nuget.org/packages/Canton.Ledger.Kernel/) | Shared client plumbing: OAuth2 and static token providers, telemetry naming, opt-in retry pipeline. |
| [`Canton.Ledger.OpenTelemetry`](https://www.nuget.org/packages/Canton.Ledger.OpenTelemetry/) | One `AddCantonLedgerInstrumentation()` call registers every Canton client `ActivitySource`. |
| [`Canton.Ledger.Grpc`](https://www.nuget.org/packages/Canton.Ledger.Grpc/) | Raw gRPC stubs generated from the Canton Ledger API protos. |
| [`Canton.Ledger.Rest`](https://www.nuget.org/packages/Canton.Ledger.Rest/) | Raw Refit surface over the JSON Ledger API (experimental, `CANTONREST001`). |
| [`Daml.Runtime.Grpc`](https://www.nuget.org/packages/Daml.Runtime.Grpc/) | Conversion between Ledger API proto `Value`/`Record` and `DamlValue`/`DamlRecord`. |
| [`Canton.Ledger.Testing`](https://www.nuget.org/packages/Canton.Ledger.Testing/) | In-memory fakes (`FakeLedgerClient`, `FakeAdminClient`, `FakePqsClient`) for unit tests without a participant. |
| [`Daml.Ledger.Abstractions.Testing.Conformance`](https://www.nuget.org/packages/Daml.Ledger.Abstractions.Testing.Conformance/) | An abstract xUnit suite any `ILedgerClient` implementation subclasses to verify the behavioral contract. |
| [`Daml.Codegen.Testing.Conformance`](https://www.nuget.org/packages/Daml.Codegen.Testing.Conformance/) | The conformance corpus: generated types plus their DARs, for live-ledger round-trip tests. Not for production. |

`Daml.Codegen.CSharp.Cli`, the emitter CLI inside `dpm codegen-cs`, is source-only, not on NuGet.

## Quick Start

The walkthrough uses the `Iou` model in
[`samples/QuickstartExample/daml/Iou.daml`](samples/QuickstartExample/daml/Iou.daml): a template
signed by an `issuer`, observed by an `owner`, with a `Transfer` choice the owner controls.

### 1. Generate C# from a DAR

`dpm codegen-cs` is a dpm component. List it under `components:` in your `daml.yaml`, next to the
SDK components your project uses (`components:` replaces the `sdk-version:` key):

```yaml
components:
  - oci://ghcr.io/peacefulstudio/dpm-codegen-cs:0.6.0-preview.4
```

Other versions are on the [releases page](https://github.com/peacefulstudio/canton-dotnet-sdk/releases);
use the tag without its leading `v`, and pin the NuGet packages to the same version. The component
decodes the DAR with a bundled JVM helper, so a JDK must be on `PATH`. Set `DPM_AUTO_INSTALL=true`
so dpm fetches the component on first use, then build the DAR and generate:

```bash
dpm build -o iou.dar
dpm codegen-cs --dar ./iou.dar --out ./Generated
```

Each Daml module becomes a C# namespace: here `Iou`, holding the `Iou.Iou` template record, its
`Iou.Iou.Transfer` choice argument and the `IouExtensions` / `IouSubmissionExtensions` submission
helpers. The checked-in output is in
[`samples/QuickstartExample/Generated/Iou/`](samples/QuickstartExample/Generated/Iou/).

### 2. Connect, submit and read

```bash
dotnet add package Daml.Runtime --version 0.6.0-preview.4
dotnet add package Daml.Ledger.Abstractions --version 0.6.0-preview.4
dotnet add package Canton.Ledger.Grpc.Client --version 0.6.0-preview.4
dotnet add package Microsoft.Extensions.DependencyInjection
```

Register the clients, allocate two parties, create an `Iou`, transfer it, and read what the new
owner holds:

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Iou;
using Microsoft.Extensions.DependencyInjection;
using IouContract = Iou.Iou;

var services = new ServiceCollection();
services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
services.AddAdminClient(options => options.GrpcAddress = "https://localhost:5001");

await using var provider = services.BuildServiceProvider();
var ledger = provider.GetRequiredService<ICantonLedgerClient>();
var admin = provider.GetRequiredService<IAdminClient>();

var alice = (await admin.AllocatePartyAsync("alice")).Party;
var bob = (await admin.AllocatePartyAsync("bob")).Party;

var created = await ledger.TryCreateAsync(
    new IouContract(Issuer: alice, Owner: bob, Currency: "USD", Amount: 100m));
var iouId = created switch
{
    ExerciseOutcome<ContractId<IouContract>>.One ok => ok.Result,
    _ => throw new InvalidOperationException(created.ToString()),
};

var transferred = await iouId.TryTransferAsync(
    ledger, new IouContract.Transfer(NewOwner: alice), bob);
if (transferred is not ExerciseOutcome<ContractId<IouContract>>.One)
    throw new InvalidOperationException(transferred.ToString());

foreach (var iou in await ledger.SnapshotAsync<IouContract>(alice))
    Console.WriteLine($"{iou.Id}: {iou.Data.Amount} {iou.Data.Currency} from {iou.Data.Issuer}");
```

Every generated `Try…Async` returns an `ExerciseOutcome<T>` instead of throwing: `One` on success,
and `DamlError`, `InfraError`, `None`, `Many` or `CommittedUndecodable` otherwise, so you switch on
the result rather than catching. `TryCreateAsync(payload)` reads the submitting party off the
payload's signatory, and `TryTransferAsync` takes one `Party` per choice controller.

The throwing calls (`GetLedgerEndAsync`, `SubmitAndWaitAsync`, the point and typed reads) report
failure the same way on both transports: they raise `LedgerOperationException`, whose `Category`
and `CommitState` tell you whether retrying is safe, and a stream ends with a terminal
`StreamError` entry. Catch that exception, not `RpcException` or `HttpRequestException`.

To use the JSON Ledger API instead, reference `Canton.Ledger.Rest.Client`, use its namespace in
place of `Canton.Ledger.Grpc.Client`, and replace the two registrations with one; the rest is
unchanged:

```csharp
services.AddRestLedgerClient(options => options.HttpAddress = "http://localhost:7575");
```

## Go deeper

| Topic | Page | Read this when you want to... |
|---|---|---|
| Code generation | [codegen.md](docs/public/codegen.md) | see what `dpm codegen-cs` emits, or build commands by hand |
| Type mappings | [type-mappings.md](docs/public/type-mappings.md) | know how a Daml type maps to C# (`Optional`, `Numeric`, `Party`) |
| CLI reference | [cli-reference.md](docs/public/cli-reference.md) | look up a `dpm codegen-cs` or emitter flag |
| DAR to NuGet | [dar-to-nuget.md](docs/public/dar-to-nuget.md) | publish your DAR's bindings as a NuGet package |
| Ledger clients | [ledger-clients.md](docs/public/ledger-clients.md) | use the ledger, admin and PQS clients, or authenticate |
| Configuration | [configuration-reference.md](docs/public/configuration-reference.md) | find every client option, default and environment variable |
| Observability | [observability.md](docs/public/observability.md) | wire OpenTelemetry and trace propagation |
| Raw Ledger API | [raw-ledger-api.md](docs/public/raw-ledger-api.md) | call the JSON or gRPC Ledger API without the clients |
| Architecture | [architecture-overview.md](docs/public/architecture-overview.md) | see how codegen, runtime and clients fit together |
| Canton versions | [cross-version-matrix.md](docs/public/cross-version-matrix.md) | check which Canton releases are supported and tested |
| Intermediate DAR | [intermediate-dar.md](docs/public/intermediate-dar.md) | diff your own Daml decoder against ours in CI |
| Benchmarks | [benchmarks](docs/public/benchmarks/README.md) | compare submission, stream and read performance over gRPC, REST and PQS |
| Proposal Appendix A | [proposal-appendix-a-as-shipped.md](docs/public/proposal-appendix-a-as-shipped.md) | compare the grant proposal's examples to the real API |
| Status and roadmap | [status-and-roadmap.md](docs/public/status-and-roadmap.md) | check platform support, coverage and what is planned |
| Building from source | [building-from-source.md](docs/public/building-from-source.md) | build, test or navigate the repository |

Each package also has its own README under [`src/`](src/), such as
[`Canton.Ledger.Testing`](src/Canton.Ledger.Testing/README.md) for unit tests against fakes.

## Status

Active and pre-1.0: under SemVer 0.x, any release may change the public API without a major-version
bump (see the [CHANGELOG](CHANGELOG.md)). CI builds and tests on Ubuntu, Windows and macOS, on amd64
and arm64, against Canton 3.5.x; see [status and roadmap](docs/public/status-and-roadmap.md) and
[Canton versions](docs/public/cross-version-matrix.md).

## Contributing

Contributions are welcome from anyone in the Daml and C# community. See
[CONTRIBUTING.md](CONTRIBUTING.md) for the dev setup, the red-green TDD requirement and the branch
model, and the [Code of Conduct](CODE_OF_CONDUCT.md). Questions, bug reports and feature requests go
to [GitHub issues](https://github.com/peacefulstudio/canton-dotnet-sdk/issues); security-sensitive
bugs follow [SECURITY.md](SECURITY.md).

## Project stewardship

`canton-dotnet-sdk` is currently developed and maintained by **Peaceful Studio OÜ** (Estonia). It is
licensed under Apache-2.0 with the explicit intent of community ownership: if and when adoption
warrants neutral governance, Peaceful Studio commits to transferring this repository to a
community-led organisation under the same license terms. No CLA required.

## Consulting

Beyond this SDK, Peaceful Studio works with teams building on Canton: Daml smart contract and
application development, integrating existing .NET systems with a Canton ledger, and operating
validator nodes. If that would help your project, please write to
[info@peaceful.studio](mailto:info@peaceful.studio) or visit [peaceful.studio](https://peaceful.studio).

## License

Apache-2.0. Copyright 2026 Peaceful Studio OÜ. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
