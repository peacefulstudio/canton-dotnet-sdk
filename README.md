# Canton .NET SDK

[![CI](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml/badge.svg)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml)
[![Release](https://img.shields.io/github/v/release/peacefulstudio/canton-dotnet-sdk?label=latest%20stable)](https://github.com/peacefulstudio/canton-dotnet-sdk/releases/latest)
[![latest preview](https://img.shields.io/github/v/release/peacefulstudio/canton-dotnet-sdk?include_prereleases&label=latest%20preview)](https://github.com/peacefulstudio/canton-dotnet-sdk/releases)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-white.svg)](https://dotnet.microsoft.com/)

Build .NET applications on a Canton/Daml ledger with full type safety. The SDK has two
halves that work together:

- **Code generation.** `dpm codegen-cs` turns a Daml `.dar` archive into strongly-typed C#:
  one record per template and data type, typed choices, and `Try…Async` extension methods
  that create contracts and exercise choices.
- **Ledger clients.** gRPC and JSON Ledger API clients for Canton participant nodes, a
  Participant Query Store (PQS) client, authentication, OpenTelemetry tracing and in-memory
  test doubles, all consuming those generated types.

Maintained by [Peaceful Studio](https://peaceful.studio), which also offers
[consulting](#consulting) for teams building on Canton.

## NuGet Packages

[![latest stable](https://img.shields.io/github/v/release/peacefulstudio/canton-dotnet-sdk?label=latest%20stable)](https://github.com/peacefulstudio/canton-dotnet-sdk/releases/latest)
[![latest](https://img.shields.io/github/v/release/peacefulstudio/canton-dotnet-sdk?include_prereleases&label=latest)](https://github.com/peacefulstudio/canton-dotnet-sdk/releases)

All 16 packages share one version and publish together from one `v*` tag, alongside the
`dpm codegen-cs` component of the same version. Upgrade them together.

### Code generation

| Package | Use it for |
|---|---|
| [`Daml.Runtime`](https://www.nuget.org/packages/Daml.Runtime/) | The types generated code compiles against — `Party`, `ContractId<T>`, `DamlRecord`, commands, outcomes and JSON serialization. Every consumer references it. |
| [`Daml.Codegen.CSharp`](https://www.nuget.org/packages/Daml.Codegen.CSharp/) | The C# emitter as a library, for driving code generation from your own tooling. `dpm codegen-cs` bundles it; most applications never reference it. |
| [`Daml.Codegen.Intermediate`](https://www.nuget.org/packages/Daml.Codegen.Intermediate/) | The Intermediate DAR contract — the generated `intermediate_dar.proto` types and the shared Daml model the emitter consumes. |

### Ledger clients

| Package | Use it for |
|---|---|
| [`Canton.Ledger.Grpc.Client`](https://www.nuget.org/packages/Canton.Ledger.Grpc.Client/) | The gRPC ledger and admin clients, registered with `AddLedgerClient` / `AddAdminClient`. The default transport. |
| [`Canton.Ledger.Rest.Client`](https://www.nuget.org/packages/Canton.Ledger.Rest.Client/) | The same `ICantonLedgerClient` / `IAdminClient` surface over the HTTP JSON Ledger API, registered with `AddRestLedgerClient`. |
| [`Canton.Ledger.Pqs.Client`](https://www.nuget.org/packages/Canton.Ledger.Pqs.Client/) | Typed, SQL-injection-safe queries over active contracts in the Participant Query Store — the Npgsql-backed `IPqsClient`. |
| [`Daml.Ledger.Abstractions`](https://www.nuget.org/packages/Daml.Ledger.Abstractions/) | The transport-agnostic `ILedgerClient` (`ILedgerWriter` / `ILedgerReader` / `ILedgerStreamer`) that generated code submits through. |
| [`Canton.Ledger.Abstractions`](https://www.nuget.org/packages/Canton.Ledger.Abstractions/) | The transport-neutral Canton contract layer — `ICantonLedgerClient`, `IAdminClient`, `ITokenProvider` and `IPqsClient`, plus the neutral completion, connected-synchronizer and reassignment type families both transports implement. |
| [`Canton.Ledger.Kernel`](https://www.nuget.org/packages/Canton.Ledger.Kernel/) | Shared client plumbing: OAuth2 client-credentials and static token providers, the OpenTelemetry `ActivitySource` naming convention and an opt-in Polly retry pipeline. Comes with the clients. |
| [`Canton.Ledger.OpenTelemetry`](https://www.nuget.org/packages/Canton.Ledger.OpenTelemetry/) | OpenTelemetry SDK integration — registers every Canton client `ActivitySource` with a single `AddCantonLedgerInstrumentation()` call. |
| [`Canton.Ledger.Grpc`](https://www.nuget.org/packages/Canton.Ledger.Grpc/) | Raw gRPC stubs generated from the Canton Ledger API protos, for calls the high-level client does not wrap. |
| [`Canton.Ledger.Rest`](https://www.nuget.org/packages/Canton.Ledger.Rest/) | Raw Refit-generated surface over the Canton JSON Ledger API — experimental (`CANTONREST001`), typically consumed through `Canton.Ledger.Rest.Client`. |
| [`Daml.Runtime.Grpc`](https://www.nuget.org/packages/Daml.Runtime.Grpc/) | Conversion between Ledger API proto `Value`/`Record` messages and `Daml.Runtime`'s `DamlValue`/`DamlRecord`. |

### Testing

| Package | Use it for |
|---|---|
| [`Canton.Ledger.Testing`](https://www.nuget.org/packages/Canton.Ledger.Testing/) | In-memory test doubles (`FakeLedgerClient`, `FakeAdminClient`, `FakePqsClient`, `FakeTokenProvider`) and event/result builders for unit-testing business logic without a live participant. |
| [`Daml.Ledger.Abstractions.Testing.Conformance`](https://www.nuget.org/packages/Daml.Ledger.Abstractions.Testing.Conformance/) | An abstract xUnit suite any `ILedgerClient` implementation subclasses to verify the documented behavioral contract. |
| [`Daml.Codegen.Testing.Conformance`](https://www.nuget.org/packages/Daml.Codegen.Testing.Conformance/) | The conformance corpus: compiled generated types plus the DAR they came from, for live-ledger round-trip testing. Not for production use. |

`Daml.Codegen.CSharp.Cli` — the emitter CLI that the `dpm codegen-cs` component runs — ships
in this repository as source only; it is not published to NuGet.

## Quick Start

The walkthrough uses the `Iou` model in
[`samples/QuickstartExample/daml/Iou.daml`](samples/QuickstartExample/daml/Iou.daml): a
template signed by an `issuer`, observed by an `owner`, with a `Transfer` choice the owner
controls.

### 1. Generate C# from a DAR

`dpm codegen-cs` is a dpm component. List it under `components:` in your `daml.yaml`,
next to the SDK components your project uses (`components:` replaces the `sdk-version:` key):

```yaml
components:
  - oci://ghcr.io/peacefulstudio/dpm-codegen-cs:0.6.0-preview.3
```

Other versions are on the [releases page](https://github.com/peacefulstudio/canton-dotnet-sdk/releases);
use the tag without its leading `v`, and pin the NuGet packages to the same version. The
component decodes the DAR with a bundled JVM helper, so a JDK must be on `PATH`. Set
`DPM_AUTO_INSTALL=true` so dpm fetches the component on first use, then build the DAR and
generate:

```bash
dpm build -o iou.dar
dpm codegen-cs --dar ./iou.dar --out ./Generated
```

Each Daml module becomes a C# namespace — here `Iou`, holding the `Iou.Iou` template record,
its `Iou.Iou.Transfer` choice argument and the `IouExtensions` / `IouSubmissionExtensions`
submission helpers. `-n <prefix>` puts the modules under a namespace prefix instead; see the
[CLI Reference](#cli-reference). The checked-in output for this model is in
[`samples/QuickstartExample/Generated/Iou/`](samples/QuickstartExample/Generated/Iou/).

### 2. Connect, submit and read

Add the runtime, the gRPC client and the dependency-injection container to the project that
compiles the generated code:

```bash
dotnet add package Daml.Runtime --version 0.6.0-preview.3
dotnet add package Daml.Ledger.Abstractions --version 0.6.0-preview.3
dotnet add package Canton.Ledger.Grpc.Client --version 0.6.0-preview.3
dotnet add package Microsoft.Extensions.DependencyInjection
```

Register the clients, allocate two parties, create an `Iou`, transfer it, and read what the
new owner holds:

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
if (transferred is not ExerciseOutcome<TransferResult>.One)
    throw new InvalidOperationException(transferred.ToString());

foreach (var iou in await ledger.SnapshotAsync<IouContract>(alice))
    Console.WriteLine($"{iou.Id}: {iou.Data.Amount} {iou.Data.Currency} from {iou.Data.Issuer}");
```

What the generated code does for you:

- `TryCreateAsync(payload)` reads the submitting party off the payload — the template's
  signatory, `issuer` — so you never restate it.
- `TryTransferAsync` takes one `Party` per choice controller (`owner`) and returns a typed
  `TransferResult` carrying the new contract's `ContractId<Iou>`.
- Every `Try…Async` returns an `ExerciseOutcome<T>` instead of throwing: `One` on success,
  and `DamlError`, `InfraError`, `None`, `Many` or `CommittedUndecodable` otherwise, so you
  switch on the result rather than catching.

To use the JSON Ledger API instead of gRPC, reference `Canton.Ledger.Rest.Client`, swap the
`using Canton.Ledger.Grpc.Client;` directive for `using Canton.Ledger.Rest.Client;`, and
replace the two registrations with one — it registers both `ICantonLedgerClient` and
`IAdminClient`, and the rest of the code is unchanged:

```csharp
services.AddRestLedgerClient(options => options.HttpAddress = "http://localhost:7575");
```

### Next steps

- Authenticate against a secured participant: [Authentication](#authentication).
- Query contracts through PQS: [PQS Client Usage](#pqs-client-usage).
- Unit-test the code above without a participant: swap in `FakeLedgerClient` from
  [`Canton.Ledger.Testing`](src/Canton.Ledger.Testing/README.md).
- Publish your DAR's bindings as a NuGet package: [DAR to NuGet Pipeline](#dar-to-nuget-pipeline).
- Choose between `ILedgerClient` and `ICantonLedgerClient` in your own code:
  [`src/Daml.Ledger.Abstractions/README.md`](src/Daml.Ledger.Abstractions/README.md).

## Status

Active. Curated public releases land here; ongoing development happens
in a private development repository. Issues, discussions, and pull
requests are welcome on this repo.

This project is pre-1.0: under SemVer 0.x, any release may change the
public API without a major-version bump (see the versioning note at the
top of the [CHANGELOG](CHANGELOG.md)). The first release published to
NuGet.org is `0.1.8-preview.1`.

## Platform Support

CI builds and tests on every supported OS × architecture. Each badge reflects the latest `main` run.

**.NET — every package in `Daml.Codegen.CSharp.slnx`**

| Ubuntu | Windows | macOS |
|---|---|---|
| [![ubuntu amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-ubuntu-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![windows amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-windows-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![macos amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-macos-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) |
| [![ubuntu arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-ubuntu-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![windows arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-windows-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![macos arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-macos-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) |

**JVM helper (`jvm-helper/`, the DAR decoder inside `dpm codegen-cs`)**

| Ubuntu | Windows | macOS |
|---|---|---|
| [![ubuntu amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-ubuntu-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![windows amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-windows-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![macos amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-macos-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) |
| [![ubuntu arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-ubuntu-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | ![windows arm64 not supported](https://img.shields.io/badge/arm64-not%20supported-lightgrey?logo=data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0id2hpdGUiIGQ9Ik0wIDMuNDQ5TDkuNzUgMi4xdjkuNDUxSDBtMTAuOTQ5LTkuNjAyTDI0IDB2MTEuNEgxMC45NDlNMCAxMi42aDkuNzV2OS40NTFMMCAyMC42OTlNMTAuOTQ5IDEyLjZIMjRWMjRsLTEyLjktMS44MDEiLz48L3N2Zz4=&logoColor=white) | [![macos arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-macos-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) |

## Coverage

[![coverage (C#)](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/coverage-csharp.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml)
[![coverage (Scala)](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/coverage-scala.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml)

## Code Generation

### Features

- **Strongly-typed contracts**: Generated record types for all Daml templates and data types
- **Choice support**: Typed choice arguments and results, and `Try<Choice>Async` extension methods with one `Party` parameter per controller
- **Interfaces, contract keys and upgrades**: Interface views and implementations, contract-key types and by-key commands, and an upgrade marker on every template
- **JSON serialization**: Built-in support for the JSON Ledger API
- **Modern C#**: Uses C# 12+ features (records, primary constructors, file-scoped namespaces); key-bearing DARs require C# 13 (`partial` property support — .NET 9 SDK or later on the build machine)
- **Nullable reference types**: Full nullable annotation support
- **Cross-platform**: Works on Windows, macOS, and Linux
- **NuGet pipeline**: Generate complete `.csproj` files for publishing DAR dependencies as NuGet packages

### Generate Code

```bash
# Generate C# from a DAR file
dpm codegen-cs --dar ./my-project.dar --out ./generated -n MyCompany.Contracts

# With verbose output
dpm codegen-cs --dar ./my-project.dar --out ./generated -v 2
```

> **Architecture note.** `dpm codegen-cs` accepts a `.dar` because the OCI
> bundle pairs a DAR → IntermediateDar decoder (a JVM helper) with the C#
> emitter from this repo. The emitter CLI in this repo consumes only the
> decoded IntermediateDar proto (`--intermediate`); it cannot read a `.dar`
> directly. See [CLI Reference](#cli-reference).

### Building Commands by Hand

The generated `Try…Async` methods cover the common path. To assemble a submission yourself —
several commands in one transaction, or a workflow id — build the commands from the generated
types (this is the compiled [`samples/QuickstartExample`](samples/QuickstartExample/Program.cs)):

```csharp
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using IouContract = Iou.Iou;

var alice = new Party("Alice::1220deadbeef");
var bob = new Party("Bob::1220deadbeef");
var charlie = new Party("Charlie::1220deadbeef");

var iou = new IouContract(
    Issuer: alice,
    Owner: bob,
    Currency: "USD",
    Amount: 1000.00m
);

var createCmd = CreateCommand.For(iou);

var contractId = new ContractId<IouContract>("00abc123");
var transferCmd = ExerciseCommand.For(
    contractId,
    IouContract.ChoiceTransfer.Name,
    new IouContract.Transfer(NewOwner: charlie).ToRecord());

var submission = CommandsSubmission.Single(createCmd)
    .WithActAs(alice)
    .WithWorkflowId(new WorkflowId("iou-issuance"));
```

Once a submission commits, don't scan the resulting transaction's
`CreatedContracts` by hand — the `Daml.Runtime.Contracts.TransactionResultExtensions`
helpers project it back to typed contract ids:

```csharp
using Daml.Runtime.Contracts;

// tx is the TransactionResult of a committed submission
ContractId<IouContract> issued = tx.Single<IouContract>();     // throws unless exactly one Iou was created
ContractId<IouContract>? maybe = tx.TrySingle<IouContract>();  // null when none, throws on more than one
```

`All<T>()` returns every created contract of a type, in transaction order;
see [`src/Daml.Runtime/README.md`](src/Daml.Runtime/README.md) for the
worked example and the upgrade-safe matching rules.

## Ledger Clients

`Canton.Ledger.*` and `Daml.Runtime.Grpc` are the C# client libraries for
Canton participant nodes: gRPC and JSON Ledger API transports, a
Participant Query Store (PQS) client, authentication, telemetry, and
in-memory test doubles.

### Client Features

#### Ledger Client (`Canton.Ledger.Grpc.Client`, `Canton.Ledger.Rest.Client`)
- Create contracts from generated Daml template types
- Exercise choices on contracts
- Submit batched commands atomically
- Stream transactions and active-contract snapshots as typed events
- Full async/await support

#### Admin Client (`Canton.Ledger.Grpc.Client`, `Canton.Ledger.Rest.Client`)
- Allocate and manage parties
- Create and manage users
- Grant and revoke user rights

#### PQS Client (`Canton.Ledger.Pqs.Client`)
- Query active contracts by template type
- Type-safe filters using C# expressions — field names derived from generated bindings
- Parameterized SQL queries — no SQL injection by construction
- Composable `Filter.Or` / `Filter.And` combinators
- OpenTelemetry tracing via `ActivitySource`

#### Client kernel (`Canton.Ledger.Kernel`)
- `Authentication`: OAuth2 client-credentials flow with thread-safe TTL token caching and automatic refresh
- Static token and unauthenticated modes behind a single `ITokenProvider` abstraction
- `IServiceCollection` integration with options validation at startup
- `Telemetry`: the shared `ActivitySource` naming convention; `Resilience`: the opt-in Polly retry pipeline

### Ledger Client Usage

The clients are entered through dependency injection: register them on an `IServiceCollection`, then
resolve the transport-neutral `ICantonLedgerClient` and `IAdminClient`. The container owns the gRPC
channel and the client lifetime, binds and validates `LedgerClientOptions` at startup, and injects
whichever `ITokenProvider` is registered.

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
services.AddAdminClient(options => options.GrpcAddress = "https://localhost:5001");

await using var provider = services.BuildServiceProvider();
var ledgerClient = provider.GetRequiredService<ICantonLedgerClient>();
var adminClient = provider.GetRequiredService<IAdminClient>();

var party = await adminClient.AllocatePartyAsync("alice");
var submitter = party.Party;

var outcome = await ledgerClient.TryCreateAsync(
    new MyTemplate("field1", "field2"),
    submitter);

var contractId = outcome switch
{
    ExerciseOutcome<ContractId<MyTemplate>>.One ok => ok.Result,
    ExerciseOutcome<ContractId<MyTemplate>>.DamlError err => throw new InvalidOperationException(err.ErrorId),
    _ => throw new InvalidOperationException(outcome.GetType().Name),
};
```

`TryCreateAsync(payload, submitter)` is the client's own method and works for any template;
the generated `TryCreateAsync(payload)` overload from the [Quick Start](#quick-start) derives
the submitter for you. A host that reads its settings from configuration can register both
clients and their authentication in one call with `services.AddCantonLedger(configuration)`,
which reads the `Canton:Ledger` and `Canton:Auth` sections; with no `ITokenProvider` registered
the clients run unauthenticated, which suits a local participant with open access.

### PQS Client Usage

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Pqs.Client;
using Daml.Runtime.Contracts;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddPqsClient(options =>
    options.ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs");

await using var provider = services.BuildServiceProvider();
var pqsClient = provider.GetRequiredService<IPqsClient>();

// Query all active contracts of a template type
var agreements = await pqsClient.QueryAsync<Agreement>();

// Query with type-safe filters — field names resolve from codegen [DamlField] metadata
var partyId = "party::alice";
var filtered = await pqsClient.QueryAsync<Agreement>(
    Filter.Or(
        Filter.Field<Agreement>(a => a.Initiator, partyId),
        Filter.Field<Agreement>(a => a.Counterparty, partyId)));

// Fetch a single contract by ID
var contractId = new ContractId<Agreement>("...");
var contract = await pqsClient.FetchByIdAsync<Agreement>(contractId);

// Check if a contract exists
var exists = await pqsClient.ExistsAsync<Agreement>(contractId);
```

`Filter.Where<T>(predicate)` accepts a richer C# predicate — comparisons, nested records,
`Optional`, list and map members, variants; the
[`Canton.Ledger.Pqs.Client` README](src/Canton.Ledger.Pqs.Client/README.md) lists what it
translates.

### Using Generated Types with the Clients

Templates generated by `dpm codegen-cs` (see [Generate Code](#generate-code)) plug straight into the clients:

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Pqs.Client;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
services.AddPqsClient(options =>
    options.ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs");

await using var provider = services.BuildServiceProvider();
var ledgerClient = provider.GetRequiredService<ICantonLedgerClient>();
var pqsClient = provider.GetRequiredService<IPqsClient>();

var owner = new Party("Alice::1234...");

// Create a contract from a generated template type
var asset = new Asset(owner, "My Asset", 100m);
var createOutcome = await ledgerClient.TryCreateAsync(asset, owner);
var contractId = createOutcome switch
{
    ExerciseOutcome<ContractId<Asset>>.One ok => ok.Result,
    _ => throw new InvalidOperationException(createOutcome.GetType().Name),
};

// Exercise a choice — ExerciseCommand.For takes the contract id, the choice name,
// and the encoded choice argument (a DamlValue, here a record via ToRecord()).
var command = ExerciseCommand.For(
    contractId,
    new ChoiceName("Transfer"),
    new Asset.Transfer(NewOwner: new Party("Bob::5678...")).ToRecord());

var exerciseOutcome = await ledgerClient.TryExerciseAsync<ContractId<Asset>>(command, owner);

// Query the same contracts via PQS
var assets = await pqsClient.QueryAsync<Asset>(
    Filter.Field<Asset>(a => a.Owner, owner.Value));
```

### Authentication

`Canton.Ledger.Kernel` ships as a dependency of `Canton.Ledger.Grpc.Client`, `Canton.Ledger.Rest.Client` and `Canton.Ledger.Pqs.Client`. Register an authentication provider explicitly; the built-in providers may be added before or after unauthenticated `AddLedgerClient` or `AddRestLedgerClient` registrations:

```csharp
using Canton.Ledger.Kernel.Authentication;

// OAuth2 client-credentials with automatic refresh and caching
services.AddCantonAuth(configuration.GetSection("Canton:Auth"));

// ...or a fixed token for short-lived processes
services.AddCantonStaticAuth("eyJ...");
```

```json
{
  "Canton": {
    "Auth": {
      "Domain": "my-tenant.eu.auth0.com",
      "ClientId": "my-client-id",
      "ClientSecret": "my-client-secret",
      "Audience": "https://canton.network/"
    }
  }
}
```

When no `ITokenProvider` is registered, the clients run unauthenticated (`ITokenProvider.None`) and log a warning at construction. See the [`Canton.Ledger.Kernel` README](src/Canton.Ledger.Kernel/README.md) for the full options reference, including custom token endpoints (e.g. Keycloak).

### Canton Version Compatibility

The clients target Canton Ledger API v2. `Canton.Ledger.Grpc` downloads the Canton Ledger API proto files from Maven Central during its build.

| SDK Version | Canton Version |
|-----------------|----------------|
| 0.6.0 and later | 3.5.x |

Before `0.6.0` the `Canton.Ledger.*` packages had their own version line:

| `Canton.Ledger.*` Version | Canton Version |
|-----------------|----------------|
| 0.4.1 – 0.5.x | 3.5.x |
| 0.4.0 | 3.4.x |
| 0.2.x | 3.4.x |
| 0.1.x | 3.4.x |

The clients support Canton 3.5 only — running them against a 3.4.x participant is untested and unsupported. This release's vendored protos and JSON Ledger API spec are pinned at Canton `3.5.18`. Any `3.5.x` patch release is fine for the gRPC client: the vendored surface is stable within the minor. The REST client needs Canton `3.5.10` or later, the first patch that serves `POST /v2/state/active-contracts-page`, which every REST active-contract-set read pages over.

A weekly [cross-version matrix](docs/public/cross-version-matrix.md) runs the live-ledger suites against the baseline Canton release, other 3.5 patches and a non-gating `dev` canary, and publishes a results table per run.

### Further Reading

- [Configuration reference](docs/public/configuration-reference.md) — every client option and its default, environment-variable naming, the authentication precedence rules, `PostConfigure` hooks, and health-check registration.
- [Architecture overview](docs/public/architecture-overview.md) — how the codegen pipeline, `Daml.Runtime`, and the `Canton.Ledger.*` client packages fit together.
- [Observability](docs/public/observability.md) — the `ActivitySource` names, wiring an OpenTelemetry exporter, and `traceparent` propagation across transports.
- [Streaming benchmarks](docs/public/benchmarks/README.md) — measured submission latency, stream delivery latency and read throughput over gRPC, REST and PQS.
- [Intermediate DAR reference](docs/public/intermediate-dar.md) — for external SDK authors who want to diff their own Daml decoder against ours in CI, with no JVM in their runtime.
- [Proposal Appendix A, as shipped](docs/public/proposal-appendix-a-as-shipped.md) — how the original grant proposal's code examples compare to the real, compiling API.
- Each package's own README under [`src/`](src/) — for example [`Canton.Ledger.Grpc.Client`](src/Canton.Ledger.Grpc.Client/README.md), [`Canton.Ledger.Rest.Client`](src/Canton.Ledger.Rest.Client/README.md) and [`Canton.Ledger.Testing`](src/Canton.Ledger.Testing/README.md).

## Project Structure

```
canton-dotnet-sdk/
├── src/
│   ├── Canton.Ledger.Abstractions/       # Transport-neutral Canton contract layer (NuGet package)
│   ├── Canton.Ledger.Grpc/               # gRPC stubs generated from the Canton Ledger API protos (NuGet package)
│   ├── Canton.Ledger.Grpc.Client/        # gRPC ledger and admin clients (NuGet package)
│   ├── Canton.Ledger.Kernel/             # Token providers, telemetry naming, retry pipeline (NuGet package)
│   ├── Canton.Ledger.OpenTelemetry/      # OpenTelemetry SDK integration (NuGet package)
│   ├── Canton.Ledger.Pqs.Client/         # Participant Query Store client (NuGet package)
│   ├── Canton.Ledger.Rest/               # Raw Refit surface over the JSON Ledger API (NuGet package)
│   ├── Canton.Ledger.Rest.Client/        # JSON Ledger API client (NuGet package)
│   ├── Canton.Ledger.Testing/            # In-memory test doubles (NuGet package)
│   ├── Daml.Codegen.CSharp/              # C# emitter library (NuGet package)
│   │   ├── CodeGen/                      # C# code generation logic
│   │   └── IntermediateDarReader.cs      # proto-to-model adapter
│   ├── Daml.Codegen.Intermediate/        # Intermediate DAR contract (NuGet package)
│   │   └── Model/                        # shared Daml model; generated intermediate_dar.proto types compile in from proto/
│   ├── Daml.Codegen.CSharp.Cli/          # proto-path emitter CLI (run by the dpm codegen-cs OCI bundle; source-only)
│   ├── Daml.Codegen.Testing.Conformance/ # conformance corpus + harness (NuGet package)
│   ├── Daml.Runtime/                     # Runtime library (NuGet package)
│   │   ├── Commands/                     # Ledger command types
│   │   ├── Contracts/                    # Contract and template base types
│   │   ├── Data/                         # Daml primitive types
│   │   └── Serialization/                # JSON serialization
│   ├── Daml.Runtime.Grpc/                # proto Value/Record ↔ DamlValue/DamlRecord bridge (NuGet package)
│   ├── Daml.Ledger.Abstractions/         # Transport-agnostic ILedgerClient (NuGet package)
│   └── Daml.Ledger.Abstractions.Testing.Conformance/  # ILedgerClient conformance test kit (NuGet package)
├── tests/                                # one test project per package, plus client parity and integration suites
├── benchmarks/                           # gRPC, REST and PQS streaming benchmarks
├── conformance/                          # Daml conformance corpora (source of the shipped fixture DARs)
├── docs/public/                          # configuration reference, architecture overview, benchmark results
├── jvm-helper/                           # Scala DAR → IntermediateDar decoder bundled by dpm codegen-cs
├── proto/                                # intermediate DAR proto schema
├── samples/
│   ├── QuickstartExample/                # Working example against the src projects
│   └── TokenStandardV2/                  # Splice Token Standard V2 packages from NuGet.org
└── CONTEXT.md                            # domain model and architecture overview
```

## CLI Reference

Two layers share one flag surface:

- **`dpm codegen-cs`** — the OCI bundle. Takes a `.dar`, decodes it to an
  IntermediateDar proto with its bundled JVM helper, then runs the emitter
  CLI below on the proto. This is the only place the DAR → IntermediateDar
  decode ships.
- **`Daml.Codegen.CSharp.Cli`** — the emitter CLI in this repo. Takes the
  IntermediateDar proto via `--intermediate`; it does not read `.dar` files.

### Bundle usage

```
dpm codegen-cs --dar <path-to-dar> --out <output-dir> [--publish-nuget --nuget-config <path> --nuget-source <name>] [--] [emitter-options...]
```

Options other than `--dar`/`--out` and the three publishing options are forwarded to the
emitter CLI. `--publish-nuget` packs the generated project and pushes the package after
emission; it implies `--generate-project` and requires `--nuget-config` (a `NuGet.config`
path) and `--nuget-source` (a source name in it).

### Emitter CLI

Output of `dotnet run --project src/Daml.Codegen.CSharp.Cli -- --help`
(the `--output-directory` default is the invoking directory):

```
Description:
  Generate C# code from an IntermediateDar proto

Usage:
  Daml.Codegen.CSharp.Cli [options]

Options:
  --intermediate <intermediate> (REQUIRED)  Path to an IntermediateDar proto file produced by the JVM helper.
  -o, --output-directory <o>                Output directory for generated sources [default: the invoking directory]
  -n, --namespace <n>                       Namespace prefix for the main package's generated code: each Daml module is emitted under <prefix>.<Module>, the prefix is elided when the module name already starts with it, and dependency packages are never prefixed (default: no prefix, the namespace is the module name)
  -v, --verbosity <v>                       Verbosity level: 0=errors only, 1=warnings, 2=info, 3=debug [default: 1]
  -r, --root <r>                            Regular expression to filter which templates to generate (default: .*)
  --nullable                                Enable nullable reference types in generated code
  --generate-project                        Generate a .csproj file for the generated code
  --include-dependencies                    Generate code for dependency packages as well
  --target-framework <target-framework>     Target framework for the generated project (e.g., net10.0) [default: net10.0]
  --runtime-version <runtime-version>       Version of Daml.Runtime package to reference
  --emitter-counter <emitter-counter>       4th segment of the generated NuGet version (Major.Minor.Patch.Generation). Defaults to 0; set a monotonic counter to distinguish republished builds of the same source. Overridden by --release-counters, which resolves the segment as a codegen-generation ordinal. [default: 0]
  --release-counters <release-counters>     Path to a JSON release-counter store. When set, the 4th NuGet version segment is resolved from this store as a codegen-generation ordinal keyed by --codegen-version, overriding --emitter-counter. The store is created on first use and atomically updated when a new codegen version is first seen.
  --codegen-version <codegen-version>       Codegen-tool version that keys the release-counter generation ordinal (the 4th NuGet version segment). Every package produced by one codegen version shares the ordinal, which increments when the version changes. Defaults to this emitter build's informational version (AssemblyInformationalVersionAttribute) with any '+' build metadata stripped.
  --package-license <package-license>       SPDX license expression emitted in the generated .csproj's <PackageLicenseExpression>. Defaults to Apache-2.0. [default: Apache-2.0]
  --version-suffix <version-suffix>         SemVer prerelease suffix appended to generated package versions, e.g. 'preview.2'. Mirrors the emitter prerelease tag. No leading dash.
  --repository-url <repository-url>         Repository URL emitted in the generated .csproj's <PackageProjectUrl>/<RepositoryUrl>/<RepositoryType>. When omitted, those elements are not emitted.
  -?, -h, --help                            Show help and usage information
  --version                                 Show version information
```

### Standalone DAR-to-proto tool

The DAR to IntermediateDar decode is packaged for standalone publication as
`daml-dar-to-proto`, a runnable jar emitting the schema defined in
`proto/intermediate_dar.proto`.

From `0.6.0-preview.1`, each `v*` release attaches `daml-dar-to-proto-<version>.jar`,
`intermediate_dar-<version>.proto`, a `SHA256SUMS` checksum file and
`intermediate-fixtures-<version>.tar.gz` (a schema-only `.binpb` plus a canonical-JSON
rendering per conformance-corpus DAR, with a `manifest.json` recording each DAR's and
each output's SHA-256) to the GitHub release.

```
java -jar daml-dar-to-proto-<version>.jar --dar contracts.dar --out intermediate.binpb
```

The jar's own interface, for reference: `--dar` and `--out` select input and
output, `--schema-only` opts into the patch-version-insensitive schema-mode
decode (the default full decode is patch-version-sensitive and additionally
captures the party expressions that enable typed-actAs codegen), and `--version`
prints the tool's version. Exit codes: 0 on success (and `--help`/`--version`),
1 on runtime failure, 2 on usage error.

## DAR to NuGet Pipeline

The code generator supports creating NuGet packages from DAR files, including proper handling of dependencies. This enables you to publish your Daml contracts as private NuGet packages that can be consumed by C# applications.

### Basic NuGet Package Generation

Generate a complete NuGet-ready project from a DAR file:

```bash
# Generate C# code with a .csproj file
dpm codegen-cs --dar ./my-contracts.dar \
    --out ./generated \
    -n MyCompany.Contracts \
    --generate-project \
    --target-framework net10.0

# Build and pack
cd generated
dotnet pack -c Release
```

This creates:
```
generated/
├── My.Contracts.csproj      # Ready for NuGet packaging
├── README.md                # Packed as the package readme
├── icon.png                 # Packed as the package icon
├── MyCompany/
│   └── Contracts/
│       └── Main/
│           └── Iou.cs       # Generated template code
└── ... other modules
```

### Generated Project File

The `--generate-project` flag creates a `.csproj` file with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PackageId>My.Contracts</PackageId>
    <Version>1.0.0.0</Version>
    <Description>C# bindings for Daml package my-contracts</Description>
    <Authors>Generated by Daml.Codegen.CSharp</Authors>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>icon.png</PackageIcon>
    <PackageTags>daml;canton;codegen;generated;my-contracts</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
    <None Include="icon.png" Pack="true" PackagePath="\" />
    <PackageReference Include="Daml.Runtime" Version="0.6.0-preview.3" />
    <PackageReference Include="Daml.Ledger.Abstractions" Version="0.6.0-preview.3" />
  </ItemGroup>

</Project>
```

The `<Version>` is the 4-part `Major.Minor.Patch.Generation` scheme (see
`--emitter-counter`/`--release-counters` in the [CLI Reference](#cli-reference)),
and `<Nullable>enable</Nullable>` is emitted by default; pass `--nullable false` to omit it.

`Daml.Runtime` and `Daml.Ledger.Abstractions` default to the code generator's own
version, so generated code and the runtime it compiles against stay in lockstep and
the block above shows whichever generator produced it. `--runtime-version` overrides
that default; pinning a runtime older than the generator fails to compile.

When the generated code references types from another DAR package, that package
gets a `<PackageReference>` too, versioned `Major.Minor.Patch.*-*`. The DAR fixes
the intrinsic `Major.Minor.Patch`, but the generation ordinal and any prerelease
tag are chosen when *that* package is published and cannot be read out of the DAR,
so the reference floats over whichever of them exist on the feed and `dotnet pack`
records the resolved version. A run started with `--release-counters` is publishing a
whole family under one generation ordinal and is therefore producing those dependencies
itself, so it pins them to the exact co-produced version instead.

### Including Dependencies

When your DAR depends on other packages, use `--include-dependencies` to generate code for all dependencies:

```bash
# Generate code for main package AND all dependencies
dpm codegen-cs --dar ./my-app.dar \
    --out ./generated \
    --generate-project \
    --include-dependencies
```

This emits the dependency modules into the same single project, each under its own
namespace directory, with one `.csproj` named for the main package:
```
generated/
├── My.App.csproj
├── <main package modules>/
└── <dependency modules>/
```

Standard-library packages (`daml-prim`, `daml-stdlib`, `ghc-stdlib`) are not emitted,
because `Daml.Runtime` provides them. Two unrelated dependencies that declare the same
module name still fail the run. Combined with `--generate-project`, the flag currently
produces duplicate-type warnings (CS0436), because each dependency is emitted inline
and also referenced as a package.

With or without the flag, the main package's `.csproj` references each non-stdlib
dependency as a package, named after its Daml package name:

```xml
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
  <None Include="icon.png" Pack="true" PackagePath="\" />
  <PackageReference Include="Daml.Runtime" Version="0.6.0-preview.3" />
  <PackageReference Include="Daml.Ledger.Abstractions" Version="0.6.0-preview.3" />
  <PackageReference Include="Daml.Finance" Version="2.0.0.*-*" />
  <PackageReference Include="Some.Library" Version="1.5.0.*-*" />
</ItemGroup>
```

### Complete Workflow Example

Here's a complete example of converting a Daml project to NuGet packages:

```bash
# 1. Build your Daml project
cd my-daml-project
dpm build -o my-project.dar

# 2. Generate C# code and its project file
dpm codegen-cs --dar ./my-project.dar \
    --out ./csharp-bindings \
    -n MyCompany.Daml \
    --generate-project \
    --include-dependencies \
    --target-framework net10.0 \
    -v 2

# 3. Build and pack the generated project
cd csharp-bindings
dotnet build -c Release
dotnet pack -c Release -o ../nuget-packages

# 4. Publish to your private NuGet feed
dotnet nuget push ../nuget-packages/*.nupkg \
    --source https://nuget.mycompany.com/v3/index.json \
    --api-key $NUGET_API_KEY
```

### Using Generated Packages

Once published, consume the packages in your C# application:

```bash
# Add the generated package
dotnet add package My.Project --version 1.0.0
```

```csharp
using MyCompany.Daml.Main;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;

// Use strongly-typed contracts
var contract = new MyTemplate(
    Owner: new Party("Alice"),
    Data: "Some data"
);

var createCmd = CreateCommand.For(contract);
```

## Type Mappings

| Daml Type | C# Type | Runtime Type |
|-----------|---------|--------------|
| `Int` | `long` | `DamlInt64` |
| `Numeric n` | `decimal` | `DamlNumeric` |
| `Text` | `string` | `DamlText` |
| `Bool` | `bool` | `DamlBool` |
| `Unit` | `DamlUnit` | `DamlUnit` |
| `Party` | `Party` (readonly record struct) | `DamlParty` |
| `Date` | `DateOnly` | `DamlDate` |
| `Time` | `DateTimeOffset` | `DamlTimestamp` |
| `ContractId T` | `ContractId<T>` | `DamlContractId` |
| `Optional a` | `T?`, or `Optional<T>` | `DamlOptional` |
| `Optional (Optional a)` | `Optional<Optional<T>>` | `DamlOptionalChain`, one level per `Optional` |
| `List a` | `IReadOnlyList<T>` | `DamlList` |
| `TextMap a` | `IReadOnlyDictionary<string, T>` | `DamlTextMap` |
| `GenMap k v` | `IReadOnlyDictionary<K, V>` | `DamlGenMap` |
| Record | `record` class | `DamlRecord` |
| Variant | Abstract record + derived | `DamlVariant` |
| Enum | `enum` | `DamlEnum` |

An `Optional a` maps to `T?` wherever C# nullable syntax can carry it. Four
positions it cannot: an `Optional` over a type variable, an
`Optional` passed as a type argument to a generated generic, an `Optional`
used as a `GenMap` key (`IReadOnlyDictionary`'s key type parameter is
`notnull`), and an `Optional` nested directly inside another `Optional`, where
`T??` does not exist and `Some None` would collapse into `None`. All four map
to `Optional<T>` (`Daml.Runtime.Stdlib`), a `Some`/`None` pair read through
`Match`, `HasValue`, `TryGetValue` or `GetValueOrDefault()`. For the first
three the wire encoding is unchanged, so which representation a field gets
does not change the payload it serializes to. A nested chain is the
exception: every level of it writes the array form — `[]` when absent, `[v]`
when present — which is what a participant accepts in a nested position.

Every `Numeric n` maps to `decimal` whatever the declared scale, which the
generated type does not carry. A value works when it is representable as a
`decimal` and valid for the declared `Numeric n` — at most 38 significant
digits total, `n` of them after the decimal point; the generated type does
not enforce that scale, so an out-of-range value is rejected by the
participant. A participant pads a Numeric out to its declared scale, so a
`Numeric 37` slot carrying `1.5` arrives with 36 trailing zeros; those zeros are
stripped and the narrowing retried, so padding alone never fails. Stripping is
attempted only after the exact narrowing fails, so a mantissa that already fits
keeps the scale it arrived with — `1.50` stays `1.50`.

Two cases throw `OverflowException` from `DamlNumeric.Value` rather than round
silently: a value still needing more than 28 fractional digits once stripped
(`decimal` holds 0-28, Daml-LF allows up to 37), and a magnitude beyond
`decimal.MaxValue`. Magnitude is a separate axis from scale — every `Numeric n`
admits 38 significant digits, so even a `Numeric 0` field can exceed `decimal`.
The runtime type is `BigInteger`-backed and carries any legal Daml-LF Numeric
without loss; only the narrowing to `decimal` can fail.

`Party` serializes as a plain JSON string (not an object) so payloads
round-trip against PQS and the JSON Ledger API; conversions to and from
`string` are explicit so a party can never be silently mistaken for an
arbitrary string.

## Generated Code Example

Given this Daml template (the model vendored at
[`samples/QuickstartExample/daml/Iou.daml`](samples/QuickstartExample/daml/Iou.daml)):

```daml
template Iou
  with
    issuer : Party
    owner : Party
    currency : Text
    amount : Decimal
  where
    signatory issuer
    observer owner

    choice Transfer : ContractId Iou
      with
        newOwner : Party
      controller owner
      do create this with owner = newOwner
```

the codegen produces (abridged from the checked-in emitter output at
[`samples/QuickstartExample/Generated/Iou/Iou.cs`](samples/QuickstartExample/Generated/Iou/Iou.cs);
elisions marked `…`). The C# namespace is the Daml module name, so the `Iou` module lands in
`namespace Iou;` — from another namespace the template is `Iou.Iou`, or bind an alias such as
`using IouContract = Iou.Iou;`:

```csharp
namespace Iou;

/// <summary>
/// Generated from Daml template Iou:Iou
/// </summary>
public sealed partial record Iou(
    [property: DamlFieldAttribute("issuer")] Party Issuer,
    [property: DamlFieldAttribute("owner")] Party Owner,
    [property: DamlFieldAttribute("currency")] string Currency,
    [property: DamlFieldAttribute("amount")] decimal Amount
) : ITemplate, IHasChoices<Iou>, IDamlRecord<Iou>
{
    /// <summary>Gets the template identifier.</summary>
    public static Identifier TemplateId { get; } = new("61d1c8472218a119a9e73167b9e9af82bfed91bf5ae5a89a82da27c10ea7f763", "Iou", "Iou");

    // … package id/name/version properties, Daml-LF JSON decoder and Archive choice metadata elided …

    /// <summary>Converts this value to a DamlRecord.</summary>
    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("issuer", Issuer.ToDamlValue()),
        DamlField.Create("owner", Owner.ToDamlValue()),
        DamlField.Create("currency", new DamlText(Currency)),
        DamlField.Create("amount", new DamlNumeric(Amount))
    );

    /// <summary>Creates an instance from a DamlRecord.</summary>
    public static Iou FromRecord(DamlRecord record) => new Iou(
        Issuer: Party.FromDamlValue(record.GetRequiredField("issuer").As<DamlParty>()),
        Owner: Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
        Currency: record.GetRequiredField("currency").As<DamlText>().Value,
        Amount: record.GetRequiredField("amount").As<DamlNumeric>().Value
    );

    /// <summary>
    /// Exercise the Transfer choice.
    /// This choice is consuming and will archive the contract.
    /// </summary>
    public static Choice<Iou, Transfer, ContractId<Iou>> ChoiceTransfer { get; } = new()
    {
        Name = new ChoiceName("Transfer"),
        Consuming = true,
        ArgumentEncoder = arg => arg.ToRecord(),
        ArgumentDecoder = val => Transfer.FromRecord(val.As<DamlRecord>()),
        ResultDecoder = val => new ContractId<Iou>(val.As<DamlContractId>().Value),
        // …
    };
}
```

The same file also emits the typed `TransferResult` projection plus
`IouExtensions.TryTransferAsync` and `IouSubmissionExtensions.TryCreateAsync`
extension methods that submit through an `ILedgerWriter` (which every `ILedgerClient`
is); the choice-argument record `Iou.Transfer` is emitted alongside in
[`Iou.Transfer.cs`](samples/QuickstartExample/Generated/Iou/Iou.Transfer.cs).
See [`samples/QuickstartExample`](samples/QuickstartExample/Program.cs) for a
complete, runnable rendition of this shape.

## Talking to the Ledger API Directly

The highest-level path is the generated extension methods
(`IouSubmissionExtensions.TryCreateAsync`, `IouExtensions.TryTransferAsync`, …)
submitting through a `Daml.Ledger.Abstractions.ILedgerClient`
implementation: `Canton.Ledger.Grpc.Client` (gRPC), `Canton.Ledger.Rest.Client` (JSON
Ledger API), or — for unit-testing application code without a live
participant — the `Canton.Ledger.Testing` in-memory fakes. Below that, the runtime types
map directly onto the wire formats.

### JSON Ledger API (v2)

The generated `TemplateId` and `DamlJsonSerializer` output plug straight
into the JSON Ledger API v2 command endpoints — here
`POST /v2/commands/submit-and-wait` (the port is the participant's JSON
Ledger API port; `7575` is the conventional default):

```csharp
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using IouContract = Iou.Iou;

var http = new HttpClient { BaseAddress = new Uri("http://localhost:7575") };

var alice = new Party("Alice::1220deadbeef");
var iou = new IouContract(Issuer: alice, Owner: new Party("Bob::1220deadbeef"), Currency: "USD", Amount: 100m);

var request = new
{
    commands = new object[]
    {
        new
        {
            CreateCommand = new
            {
                templateId = IouContract.TemplateId.ToString(),
                createArguments = JsonNode.Parse(DamlJsonSerializer.Serialize(iou.ToRecord())),
            },
        },
    },
    commandId = Guid.NewGuid().ToString(),
    userId = "ledger-api-user",
    actAs = new[] { alice.Value },
};

var response = await http.PostAsJsonAsync("/v2/commands/submit-and-wait", request);
response.EnsureSuccessStatusCode();
```

`IouContract.TemplateId.ToString()` renders the package-id reference format
(`<package-id>:<module>:<entity>`); the API also accepts the
package-name format (`#<package-name>:<module>:<entity>`), which can be
built from the generated `PackageName` property.

### gRPC Ledger API

The runtime types can be converted to/from the gRPC protobuf types with
`Daml.Runtime.Grpc`; `Canton.Ledger.Grpc.Client` packages that conversion behind
`ILedgerClient` so most applications never touch the protos, and `Canton.Ledger.Grpc`
exposes the raw stubs for the rest. See the
[Canton documentation](https://docs.canton.network/) for raw gRPC
integration details.

## Building from Source

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — the exact pinned version is in [`global.json`](global.json)
- Network access to `repo1.maven.org` on the first build — `Canton.Ledger.Grpc` downloads the Canton Ledger API proto JARs from Maven Central and checks each against a pinned SHA-256 before generating the gRPC stubs; allow that host if you build behind a proxy
- (Optional) [Daml SDK via dpm](https://docs.daml.com/) for testing with real DAR files

### Build

```bash
# Clone the repository
git clone https://github.com/peacefulstudio/canton-dotnet-sdk.git
cd canton-dotnet-sdk

# Build
dotnet build

# Run the whole test suite
dotnet test --solution Daml.Codegen.CSharp.slnx --minimum-expected-tests 1

# Run one test project
dotnet test --project tests/Daml.Runtime.Tests/Daml.Runtime.Tests.csproj --minimum-expected-tests 1

# Run the sample
dotnet run --project samples/QuickstartExample
```

The repo runs on [Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro)
(see [`global.json`](global.json)), where `dotnet test` takes `--project` or
`--solution` and otherwise discovers from the current directory.
`--minimum-expected-tests 1` makes a run that discovers nothing fail loudly:
`Zero tests ran` means the runner found no tests, never that they passed. If a
run reports zero, `dotnet build` and then execute the test binary directly —
`./tests/Daml.Runtime.Tests/bin/Debug/net10.0/Daml.Runtime.Tests`.

### Create NuGet Packages

```bash
dotnet pack -c Release
```

## Contributing

Contributions are welcome from anyone in the Daml and C# community. See
[CONTRIBUTING.md](CONTRIBUTING.md) for the dev setup, the red-green TDD
requirement, and the branch model. The per-PR checklist itself lives in
the PR template and is filled in when you open a PR. By participating
you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

For questions, bug reports and feature requests, open a
[GitHub issue](https://github.com/peacefulstudio/canton-dotnet-sdk/issues).

For security-sensitive bugs, please follow [SECURITY.md](SECURITY.md)
instead of opening a public issue.

## Project stewardship

`canton-dotnet-sdk` is currently developed and maintained by **Peaceful
Studio OÜ** (Estonia). The project is licensed under
Apache-2.0 with the explicit intent of community ownership: if and
when adoption warrants neutral governance, Peaceful Studio commits to
transferring this repository to a community-led organisation under the
same license terms. Contributions welcome from anywhere in the
Daml and C# ecosystem; no CLA required.

## Consulting

Beyond this SDK, Peaceful Studio works with teams building on Canton:
Daml smart contract and application development, integrating existing
.NET systems with a Canton ledger, and operating validator nodes. If
that would help your project, please write to
[info@peaceful.studio](mailto:info@peaceful.studio) or visit
[peaceful.studio](https://peaceful.studio).

## Roadmap

### Completed

- [x] IntermediateDar proto reader covering the full Daml-LF type surface
- [x] Interface support (`IDamlInterface`, `IHasView<TView>`, `IImplements<TInterface>`)
- [x] Contract key support (key type, `<Choice>ByKeyCommand` builders, and a non-nullable `Contract<T, TKey>.Key` decoded from the created event's `contractKey`)
- [x] Package upgrade support (`IUpgradeable` marker interface)
- [x] Generic types (type parameters on records and variants)
- [x] DAR dependencies and NuGet pipeline
- [x] gRPC, JSON Ledger API and PQS clients shipped in the SDK under one version
- [x] End-to-end Canton integration tests — generated conformance types round-tripped against a live participant over gRPC and the JSON Ledger API, plus a cross-transport parity suite

### Planned

- [ ] Source generator (compile-time Roslyn codegen)

## License

Apache-2.0. Copyright 2026 Peaceful Studio OÜ. Licensed under the
[Apache License 2.0](LICENSE). See [LICENSE](LICENSE) for the full text
and [NOTICE](NOTICE) for attribution requirements.

## Related Projects

- [Daml SDK](https://github.com/digital-asset/daml) - The Daml smart contract language
- [Canton](https://www.canton.network/) - Privacy-enabled blockchain infrastructure
- [Java Codegen](https://docs.daml.com/app-dev/bindings-java/codegen.html) - Official Java code generator
- [TypeScript Codegen](https://docs.daml.com/app-dev/bindings-ts/daml2js.html) - Official TypeScript code generator
