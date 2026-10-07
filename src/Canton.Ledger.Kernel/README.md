# Canton.Ledger.Kernel

Part of the [Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk). The transport-neutral client kernel for Canton participant nodes: the `Authentication`, `Telemetry`, `Resilience`, `Streams`, `Trees`, `Results`, and `Wire` modules — the token providers that implement `Canton.Ledger.Abstractions.ITokenProvider`, the OpenTelemetry `ActivitySource` naming convention, an opt-in Polly retry pipeline, and the decisions both transports must make identically once their own wire vocabulary is decoded: rebuilding a transaction's hierarchy from its node ids, folding an exercise outcome, and naming a participant body that could not be read. Both the gRPC client and the JSON client consume this package as peers — neither depends on the other. `Authentication` sits at the bottom of the kernel's namespace DAG (it depends on neither of the other modules), so it can later be extracted into its own package without a breaking change.

## Installation

```bash
dotnet add package Canton.Ledger.Kernel --version 0.6.0
```

## Key Types

| Type | Purpose |
|------|---------|
| `StaticTokenProvider` | Returns a fixed token string. Use for short-lived processes or testing. Implements `Canton.Ledger.Abstractions.ITokenProvider` |
| `ClientCredentialsProvider` | OAuth2 client-credentials flow with thread-safe TTL cache (`SemaphoreSlim` + `Volatile` reads/writes) |
| `ClientCredentialsOptions` | Config: `Domain`, `ClientId`, `ClientSecret`, `Audience`, `TokenEndpoint`, `SafetyMargin`, `AllowInsecureTokenEndpoint`, `TokenAcquisitionTimeout`, `Tls` |
| `Telemetry.LedgerActivitySourceNames` | The well-known source names of every Canton client (`All` plus one constant each), so a host can register the whole set without referencing a concrete client assembly |
| `Telemetry.LedgerActivityTagNames` | The SDK-owned `daml.*` / `canton.*` / `retry.*` span attribute names the clients emit (`All` plus one constant each), so a dashboard query, sampling rule or redaction filter can name one without hardcoding the string |
| `Resilience.RetryOptions` | Config for the opt-in retry pipeline, set via `LedgerClientOptions.Retry` / `RestLedgerClientOptions.Retry`. `Enabled` defaults to `false` |

## Usage

### Client-credentials (OAuth2) via DI

```csharp
services.AddCantonAuth(configuration.GetSection("Canton:Auth"));
```

```json
{
  "Canton": {
    "Auth": {
      "Domain": "dev-peaceful.eu.auth0.com",
      "ClientId": "my-client-id",
      "ClientSecret": "my-client-secret",
      "Audience": "https://canton.network/"
    }
  }
}
```

`Domain` accepts either a bare hostname (e.g. `dev-peaceful.eu.auth0.com`) or an absolute https URL (e.g. `https://auth.example.com`, or `https://auth.example.com/tenant-a` for per-tenant subpaths). If `Domain` is a bare hostname, it is treated as `https://{hostname}`. If it is an absolute URL, that URL is used as the base. In both cases, `/oauth/token` is appended, preserving any existing path (`https://auth.example.com/tenant-a` → `https://auth.example.com/tenant-a/oauth/token`). Userinfo, query, and fragments are rejected. `ClientCredentialsProvider` caches tokens until `expires_in - SafetyMargin` (default 30s) and concurrent callers share a single HTTP request during refresh.

Plaintext `http` endpoints (whether from `Domain` or `TokenEndpoint`) are rejected by default, because the token request POSTs the client secret in cleartext — anyone on the network path could read it. To accept the risk against a local endpoint (e.g. `http://localhost:8080` during development), set `AllowInsecureTokenEndpoint` to `true`; the provider then logs a warning at construction.

To override the token endpoint (e.g., non-standard OAuth2 servers like Keycloak's `/realms/{realm}/protocol/openid-connect/token`):

```json
{
  "Canton": {
    "Auth": {
      "TokenEndpoint": "https://custom.example.com/oauth/token",
      "ClientId": "...",
      "ClientSecret": "..."
    }
  }
}
```

### Static token via DI

```csharp
services.AddCantonStaticAuth("eyJ...");
```

### Action-based configuration

```csharp
services.AddCantonAuth(options =>
{
    options.Domain = "https://auth.example.com";
    options.ClientId = "my-client-id";
    options.ClientSecret = "my-client-secret";
    options.Audience = "https://canton.network/";
});
```

### Registration precedence

Only unkeyed `ITokenProvider` registrations participate in the default selection; keyed providers remain independent. The unauthenticated gRPC and REST registration paths install the exact `ITokenProvider.None` singleton instance as their fallback. `AddCantonAuth` and `AddCantonStaticAuth` replace that exact unkeyed fallback with their explicit provider, so either may be called before or after those unauthenticated client registrations. Both preserve every pre-existing non-`None` unkeyed provider without resolving or constructing it.

### Unauthenticated access

When no `ITokenProvider` is registered, `AddLedgerClient`/`AddAdminClient` register `ITokenProvider.None` as a default. Clients detect this and skip the Authorization header. Use this for local development with unauthenticated Canton nodes.

### OpenTelemetry `ActivitySource` naming (`Canton.Ledger.Kernel.Telemetry`)

Every client's `ActivitySource` follows one naming convention, applied internally to `Canton.Ledger.Kernel` and its transports. `LedgerActivitySourceNames` and `LedgerActivityTagNames` expose the resulting well-known names, so a host registers listeners or writes dashboard queries without hardcoding strings:

```csharp
using var listener = new ActivityListener
{
    ShouldListenTo = source => LedgerActivitySourceNames.All.Contains(source.Name)
};
```

BCL `System.Diagnostics.Activity` only — no OpenTelemetry SDK dependency.

### Opt-in retry pipeline (`Canton.Ledger.Kernel.Resilience`)

`RetryOptions` configures the retry pipeline each transport builds for itself, via `LedgerClientOptions.Retry` (gRPC) or `RestLedgerClientOptions.Retry` (REST):

```csharp
using Canton.Ledger.Kernel.Resilience;

services.AddLedgerClient(options =>
{
    options.Retry = new RetryOptions
    {
        Enabled = true,
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(200),
    };
});
```

`RetryOptions.Enabled` defaults to `false`, in which case the transport applies no retry behavior — a genuine no-op. The pipeline is transport-neutral: it references no `Grpc.Core` or `System.Net.Http` types, and knows nothing about ledger command semantics. A retried command can double-submit unless the caller reuses a stable `command_id`/`submission_id` for ledger-side dedup — the pipeline itself confers no idempotency.

## Internals

- `Streams.StreamEventClassifier` — the single implementation of the no-silent-drop stream-classification policy that both the gRPC and HTTP projectors delegate to: which decoded shape becomes which `UnclassifiedKind`, the missing-synchronizer rule, and the decode-failure fallback with its warning. Wire decoding stays per-transport; the classifier consumes a `DecodedStreamEvent<TSynchronizerScope>` and hands back the synchronizer scope it validated, so no projector can reach a `SynchronizerId` without passing the rule. Internal — visible to the transports via `InternalsVisibleTo`, not part of the package's public surface
- `Streams.IStreamArms<TEvent, TMarker, TPayload>` with `ContractArms<T>` and `InterfaceArms<TInterface, TView>` — the union constructors for the two stream families, one static builder per arm over transport-neutral values, so each transport's projection core builds `ContractStreamEvent<T>` or `InterfaceStreamEvent<TInterface, TView>` directly. Internal, like the classifier
- `Streams.ContractSnapshotEntryArms<T>` and `InterfaceSnapshotEntryArms<TInterface, TView>` — convert a projected stream event into the family's active-contract snapshot entry (Created with its disclosure, Unclassified, and an Unassigned downgraded to an Unclassified `UnassignedEvent`), shared by both transports. Internal
- `IHttpClientFactory` named client `"CantonAuth"` — no `using` on the `HttpClient` (factory manages handler lifetime)
- `TimeProvider` for testable time (pass `FakeTimeProvider` in tests)
- `Volatile.Read`/`Volatile.Write` for cache fields — write order: token before expiry (matches read order)
- Validates `expires_in > 0` and `access_token` non-empty after deserialization
- Token endpoint is resolved once at construction — unresolvable options (no `TokenEndpoint`, invalid `Domain`, plaintext `http` without `AllowInsecureTokenEndpoint`) throw `InvalidOperationException` when the provider is constructed, not at the first token request

## Related Packages

- `Canton.Ledger.Abstractions` — transport-neutral contract layer that declares `ITokenProvider`
- `Canton.Ledger.Grpc.Client` — gRPC client that consumes `ITokenProvider`
- `Canton.Ledger.Pqs.Client` — PQS query client
