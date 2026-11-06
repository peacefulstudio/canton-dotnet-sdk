# Packages

All 16 packages share one version and publish together. The README holds the authoritative
[package table](../../../../README.md#nuget-packages); each package also has its own README:

| Package | README |
|---|---|
| `Daml.Runtime` | [README](../../../../src/Daml.Runtime/README.md) |
| `Daml.Codegen.CSharp` | [README](../../../../src/Daml.Codegen.CSharp/README.md) |
| `Daml.Codegen.Intermediate` | [README](../../../../src/Daml.Codegen.Intermediate/README.md) |
| `Daml.Codegen.CSharp.MSBuild` | [README](../../../../src/Daml.Codegen.CSharp.MSBuild/README.md) |
| `Daml.Ledger.Abstractions` | [README](../../../../src/Daml.Ledger.Abstractions/README.md) |
| `Canton.Ledger.Abstractions` | [README](../../../../src/Canton.Ledger.Abstractions/README.md) |
| `Canton.Ledger.Kernel` | [README](../../../../src/Canton.Ledger.Kernel/README.md) |
| `Canton.Ledger.Grpc.Client` | [README](../../../../src/Canton.Ledger.Grpc.Client/README.md) |
| `Canton.Ledger.Rest.Client` | [README](../../../../src/Canton.Ledger.Rest.Client/README.md) |
| `Canton.Ledger.Pqs.Client` | [README](../../../../src/Canton.Ledger.Pqs.Client/README.md) |
| `Canton.Ledger.OpenTelemetry` | [README](../../../../src/Canton.Ledger.OpenTelemetry/README.md) |
| `Canton.Ledger.Grpc` | [README](../../../../src/Canton.Ledger.Grpc/README.md) |
| `Canton.Ledger.Rest` | [README](../../../../src/Canton.Ledger.Rest/README.md) |
| `Canton.Ledger.Testing` | [README](../../../../src/Canton.Ledger.Testing/README.md) |
| `Daml.Ledger.Abstractions.Testing.Conformance` | [README](../../../../src/Daml.Ledger.Abstractions.Testing.Conformance/README.md) |
| `Daml.Codegen.Testing.Conformance` | [README](../../../../src/Daml.Codegen.Testing.Conformance/README.md) |

The [API reference](../api/index.md) documents the public types of every package above.

## Wire-level types

`Canton.Ledger.Grpc` (protoc output) and `Canton.Ledger.Rest` (the NSwag/Refit client) contain the
generated Ledger API wire types. They are left out of the [API reference](../api/index.md): the
messages and services are defined by Canton, not by this SDK, and are documented upstream.

- gRPC: [Ledger API gRPC reference](https://docs.canton.network/reference/grpc-ledger-api-reference/overview)
- JSON: [JSON Ledger API OpenAPI reference](https://docs.canton.network/reference/json-api-reference/overview)

The hand-written clients that wrap them, `Canton.Ledger.Grpc.Client` and `Canton.Ledger.Rest.Client`,
and the hand-written `*Api` interfaces in `Canton.Ledger.Rest` stay in the reference.
