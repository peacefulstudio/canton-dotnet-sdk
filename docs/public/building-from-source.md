# Building from source

Prerequisites, build and test commands, and the repository layout, for contributors and anyone building the SDK locally.

Back to the [README](../../README.md).

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — the exact pinned version is in [`global.json`](../../global.json)
- Network access to `repo1.maven.org` on the first build — `Canton.Ledger.Grpc` downloads the Canton Ledger API proto JARs from Maven Central and checks each against a pinned SHA-256 before generating the gRPC stubs; allow that host if you build behind a proxy
- (Optional) [Daml SDK via dpm](https://docs.daml.com/) for testing with real DAR files

## Build

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
(see [`global.json`](../../global.json)), where `dotnet test` takes `--project` or
`--solution` and otherwise discovers from the current directory.
`--minimum-expected-tests 1` makes a run that discovers nothing fail loudly:
`Zero tests ran` means the runner found no tests, never that they passed. If a
run reports zero, `dotnet build` and then execute the test binary directly —
`./tests/Daml.Runtime.Tests/bin/Debug/net10.0/Daml.Runtime.Tests`.

## Create NuGet Packages

```bash
dotnet pack -c Release
```

## Repository layout

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
├── docs/public/                          # guides, configuration reference, architecture overview, benchmark results
├── jvm-helper/                           # Scala DAR → IntermediateDar decoder bundled by dpm codegen-cs
├── proto/                                # intermediate DAR proto schema
├── samples/
│   ├── QuickstartExample/                # Working example against the src projects
│   └── TokenStandardV2/                  # Splice Token Standard V2 packages from NuGet.org
└── CONTEXT.md                            # domain model and architecture overview
```
