# Ledger clients quickstart

Resolve `ICantonLedgerClient` (from `Canton.Ledger.Abstractions`) from the container; the
transport package you reference decides whether it speaks gRPC or the JSON Ledger API.

## Register a client

```bash
dotnet add package Canton.Ledger.Grpc.Client
```

```csharp
services.AddCantonLedger(configuration);
```

This reads client options from the `Canton:Ledger` configuration section and registers an OAuth2
client-credentials token provider when `Canton:Auth` is populated. Use an `https://` address for
anything beyond local development. The other authentication modes (explicit sections, static
token, unauthenticated) are described in the
[`Canton.Ledger.Grpc.Client` README](../../../../src/Canton.Ledger.Grpc.Client/README.md#authentication).

## Submit commands

Generated template types plug straight into the client:

```csharp
var outcome = await ledgerClient.TryCreateAsync(
    asset,
    submitter: new Party("Alice::1234..."),
    workflowId: "create-asset");
```

## Pick a transport

| You want | Package |
|---|---|
| gRPC (the default) | [`Canton.Ledger.Grpc.Client`](../../../../src/Canton.Ledger.Grpc.Client/README.md) |
| The HTTP JSON Ledger API | [`Canton.Ledger.Rest.Client`](../../../../src/Canton.Ledger.Rest.Client/README.md) |
| Typed queries over active contracts | [`Canton.Ledger.Pqs.Client`](../../../../src/Canton.Ledger.Pqs.Client/README.md) |
| Unit tests without a participant | [`Canton.Ledger.Testing`](../../../../src/Canton.Ledger.Testing/README.md) |

## Go deeper

- [Ledger clients](../../../../docs/public/ledger-clients.md)
- [Configuration reference](../../../../docs/public/configuration-reference.md)
- [Observability](../../../../docs/public/observability.md)
