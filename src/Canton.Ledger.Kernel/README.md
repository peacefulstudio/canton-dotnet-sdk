# Canton.Ledger.Kernel

Part of the [Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk). The transport-neutral client kernel for Canton participant nodes: the `Authentication`, `Telemetry`, `Resilience`, `Streams`, `Trees`, `Results`, and `Wire` modules — the token providers that implement `Canton.Ledger.Abstractions.ITokenProvider`, the OpenTelemetry `ActivitySource` naming convention, an opt-in Polly retry pipeline, and the decisions both transports must make identically once their own wire vocabulary is decoded: rebuilding a transaction's hierarchy from its node ids, folding an exercise outcome, and naming a participant body that could not be read. Both the gRPC client and the JSON client consume this package as peers — neither depends on the other. `Authentication` sits at the bottom of the kernel's namespace DAG (it depends on neither of the other modules), so it can later be extracted into its own package without a breaking change.

## Installation

```bash
dotnet add package Canton.Ledger.Kernel --version 0.8.0
```

## Key Types

| Type | Purpose |
|------|---------|
| `StaticTokenProvider` | Returns a fixed token string. Use for short-lived processes or testing. Implements `Canton.Ledger.Abstractions.ITokenProvider` |
| `ClientCredentialsProvider` | OAuth2 client-credentials flow with thread-safe TTL cache (`SemaphoreSlim` + `Volatile` reads/writes) |
| `ClientCredentialsOptions` | Config: `Domain`, `ClientId`, `ClientSecret`, `Audience`, `TokenEndpoint`, `SafetyMargin`, `AllowInsecureTokenEndpoint`, `TokenAcquisitionTimeout`, `TokenFailureCooldown`, `Tls` |
| `Telemetry.LedgerActivitySourceNames` | The well-known source names of every Canton client (`All` plus one constant each), so a host can register the whole set without referencing a concrete client assembly |
| `Telemetry.LedgerActivityTagNames` | The SDK-owned `daml.*` / `canton.*` / `retry.*` span attribute names the clients emit (`All` plus one constant each), so a dashboard query, sampling rule or redaction filter can name one without hardcoding the string |
| `Resilience.RetryOptions` | Config for the opt-in retry pipeline, set via `LedgerClientOptions.Retry` / `RestLedgerClientOptions.Retry`. `Enabled` defaults to `false` |
| `Resilience.RawCallReplay` | `Allow()` returns a scope inside which raw calls (the gRPC `CreateCallInvoker()` escape hatch, the REST raw Refit tier) are re-sent by the retry pipeline. Outside it they are sent once |
| `Wire.CreatedEventBlob` | `Decode(ReadOnlyMemory<byte>)` reads the `createdEventBlob` of a disclosed contract into a `DecodedCreatedEventBlob`: template id, package name, contract id, signatories (key maintainers included), observers, key maintainers, creation time and the raw create argument. `ReadCreateArgument()`, `ReadContractKey()` and `ReadContract<T>()` / `ReadContract<T, TKey>()` read the create argument and key as `DamlValue`s or decode them into a generated template. The blob carries no interface view. An unreadable blob or an unsupported serialization version throws `MalformedResponseException` |

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
      "Audience": "https://daml.com/jwt/aud/participant/<participant-id>"
    }
  }
}
```

`Audience`, when set, must equal the participant's `target-audience`; by default Canton accepts `https://daml.com/jwt/aud/participant/<participant-id>`. Leave it unset only if your identity provider stamps that audience into the token itself.

`Domain` accepts either a bare hostname (e.g. `dev-peaceful.eu.auth0.com`) or an absolute https URL (e.g. `https://auth.example.com`, or `https://auth.example.com/tenant-a` for per-tenant subpaths). If `Domain` is a bare hostname, it is treated as `https://{hostname}`. If it is an absolute URL, that URL is used as the base. In both cases, `/oauth/token` is appended, preserving any existing path (`https://auth.example.com/tenant-a` → `https://auth.example.com/tenant-a/oauth/token`). Userinfo, query, and fragments are rejected. `ClientCredentialsProvider` caches tokens until `expires_in - SafetyMargin` (default 30s) and concurrent callers share a single HTTP request during refresh. After a failed fetch, calls fail at once with that failure, without reaching the identity provider, for a cooldown window of `TokenFailureCooldown.InitialDelay` (500 ms by default) that doubles per consecutive failure up to `MaxDelay` (30 s) and resets on success; set `TokenFailureCooldown.Enabled` to `false` to fetch on every call that finds no cached token. It never re-sends a ledger call or a token request; it only makes calls fail fast for the window after a failed token fetch.

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
    options.Audience = "https://daml.com/jwt/aud/participant/<participant-id>";
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

Raw calls are the exception to "retried when enabled": the gRPC `CreateCallInvoker()` escape hatch and the REST raw Refit tier are sent exactly once, because the SDK cannot know whether a call it did not build is idempotent. A caller that knows better makes the call inside `using (RawCallReplay.Allow()) { ... }`, awaiting it before the scope is disposed.

## Internals

- `Wire.ExternalPartyIdentity` — recomputes an external party's public-key fingerprint the way Canton does (SHA-256 over a four-byte hash purpose of 12 and the key bytes, as a `1220`-prefixed multihash; an Ed25519 `SubjectPublicKeyInfo` contributes its raw 32-byte key) and checks a generated topology against it, so both transports fail closed on a fingerprint or party namespace that is not the caller's key, and on an allocated party that is not the one the allocation's onboarding transaction is for, which `ExternalPartyTopologyVerifier.ExpectedPartyOf` derives as the mapping's hint in the namespace of its signing key. Internal — visible to the transports via `InternalsVisibleTo`, not part of the package's public surface
- `Wire.PreparedSubmissionIntegrity` and `Wire.PreparedTransactionHasher` — recompute the hash of a prepared transaction the way Canton does under hashing schemes V2 and V3 and refuse a participant's hash that differs, so both transports fail closed on a prepared transaction whose hash is not the one the signer would sign; any other scheme is refused too. Internal — visible to the transports via `InternalsVisibleTo`, not part of the package's public surface
- `Wire.ExternalPartyTopologyVerifier` — decodes the topology transactions a participant generated for an external party and fails closed unless they are exactly the onboarding Canton generates for the request: one `PartyToParticipant` add-replace at serial 1, wrapped as topology proto version 30 or 31 and decoded with that version's schema (any other version is refused), for `hint::fingerprint`, the requested key alone at signing threshold 1, each requested participant with its requested permission plus exactly one generating participant, every one of them hosting the party (under `v31` a hosting state of Active; Nominated, Freezing and Onboarding are refused, as the `v30` onboarding flag is), Canton's confirmation threshold (every confirming participant when the request leaves it at zero), and no field the vendored protos do not describe (each transaction must re-serialize to the bytes received). `Wire.TopologyMultiHash` recomputes the multi-hash the caller signs (SHA-256 with hash purpose 11 per transaction, then purpose 55 over the sorted, de-duplicated, length-prefixed hashes), so a multi-hash that covers anything else is refused too. A difference raises `MalformedResponseException`. Internal, like `ExternalPartyIdentity`
- `Topology/Proto/` — the Canton topology, crypto and versioned-message protos (`com/digitalasset/canton/...`), vendored unmodified from Canton tag `v3.6.4` (commit `72bba4ca58c62aafee337035af885e47594e98ec`, `community/base/src/main/protobuf/`), except `protocol/v31/topology.proto`, vendored unmodified from Canton tag `v3.7.1` (commit `e23eddfdc926c2e0f64d58221faa964398a9ecd6`, same path, sha256 `672b07645f7919f1355068f957053f468dbac5b32c9adb296aea76f6ac479688`); the `v30` files it imports differ between the two tags by one comment only. `scalapb/scalapb.proto` comes from ScalaPB `v0.11.14`, the version Canton builds with. They compile to `internal` C# message types only (no gRPC services), so the package's public surface carries no topology API. Bump them together with the Canton line the verifier targets
- `Wire/Proto/` — the Daml-LF `transaction.proto` and `value.proto` (`com/digitalasset/daml/lf/`), vendored unmodified from Canton tag `v3.6.4` (commit `72bba4ca58c62aafee337035af885e47594e98ec`, `community/daml-lf/transaction/src/main/protobuf/`). They compile to `internal` C# message types only; `Wire.CreatedEventBlob` reads the `Versioned` wrapper and the `FatContractInstance` it carries through them, and reads serialization versions `2.1`, `2` and `3`. Bump them with the Canton line, and check the `FatContractInstance` field numbers when a version is added
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
