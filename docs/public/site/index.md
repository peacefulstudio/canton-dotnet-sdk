# Canton .NET SDK

Build .NET applications on a Canton/Daml ledger with full type safety. `dpm codegen-cs` turns a
Daml `.dar` archive into strongly-typed C#, and one transport-neutral `ICantonLedgerClient`
submits it over gRPC or the JSON Ledger API, with a PQS client, authentication, OpenTelemetry
tracing and in-memory test doubles alongside.

## Start here

- [Getting started](articles/getting-started.md): install the tools and generate C# from a DAR.
- [Using the ledger clients](articles/ledger-clients-quickstart.md): connect, authenticate and submit.
- [Packages](articles/packages.md): which of the 16 NuGet packages you need.
- [API reference](api/index.md): every public type in the packaged assemblies.

## Guides

| Topic | Page |
|---|---|
| Code generation | [codegen](../codegen.md) |
| Type mappings | [type-mappings](../type-mappings.md) |
| Ledger clients | [ledger-clients](../ledger-clients.md) |
| Configuration | [configuration-reference](../configuration-reference.md) |
| Observability | [observability](../observability.md) |
| Architecture | [architecture-overview](../architecture-overview.md) |
| Canton versions | [cross-version-matrix](../cross-version-matrix.md) |
