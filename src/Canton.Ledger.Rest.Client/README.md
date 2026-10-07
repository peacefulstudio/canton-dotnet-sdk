# Canton.Ledger.Rest.Client

HTTP client for the Canton Ledger API over the JSON Ledger API (`/v2/...`), part of the [Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk). `AddRestLedgerClient` registers a full implementation of the transport-neutral `Daml.Ledger.Abstractions.ILedgerClient` and the Canton participant surface `Canton.Ledger.Abstractions.ICantonLedgerClient`, so business logic written against those interfaces runs unchanged over HTTP or gRPC.

## Installation

```bash
dotnet add package Canton.Ledger.Rest.Client --version 0.6.0-preview.4
```

## Key Types

| Type | Purpose |
|------|---------|
| `ICantonLedgerClient` (from `Canton.Ledger.Abstractions`) | **The type to resolve from the container.** Everything on `ILedgerClient` plus the Canton-only submit / reassignment / discovery / point-read / disclosure-read (`GetDisclosureAsync<T>`) / transaction-tree / traffic-cost surface, served over the JSON Ledger API |
| `ILedgerClient` (from `Daml.Ledger.Abstractions`) | Command operations and reads, bounded or open-ended: `TryCreateAsync`, `TryExerciseAsync`, `SubmitAndWaitAsync`, `TrySubmitAndWaitForTransactionAsync`, `SubscribeAsync`, `SubscribeActiveAsync`, `GetLedgerEndAsync` |
| `ServiceCollectionExtensions.AddRestLedgerClient` | The entry point — registers the HTTP adapter behind the five transport-neutral service types, plus `IAdminClient`. Its `ActivitySource` name is `LedgerActivitySourceNames.RestLedgerClient` |
| `IAdminClient` (from `Canton.Ledger.Abstractions`) | Participant administration — parties, users and rights, packages and DARs — served over the JSON Ledger API. Its `ActivitySource` name is `LedgerActivitySourceNames.RestAdminClient` |
| `RestLedgerClientOptions` | Config: `HttpAddress` (required, e.g. `http://localhost:7575`), `UserId` (optional; the participant derives it from the caller's token when omitted), `Retry` (opt-in, disabled by default), `StreamWindowLimit` / `StreamWindowIdleTimeout` (the bounds sent on every window the pagination loop opens, the limit also sizing each ACS page; 200 entries and 250 ms by default), `Tls` (opt-in mTLS material, unconfigured by default) |
| `TlsOptions` (from `Canton.Ledger.Kernel.Security`) | Nested at `RestLedgerClientOptions.Tls` and bound from `Canton:Rest:Tls`. Client identity as `ClientCertificatePemPath` (+ `ClientCertificateKeyPemPath`), `ClientCertificatePkcs12Path` (+ `ClientCertificatePkcs12Password`) or an already-loaded `ClientCertificate`; private-CA trust as `CertificateAuthorityBundlePemPath` or `CertificateAuthorities`, with `RevocationMode`. Applied to the `HttpClient` named `ServiceCollectionExtensions.HttpClientName`, so it covers the adapter and the raw Refit interfaces alike. Left unconfigured that client is untouched — OS trust store, no client certificate — and a host supplying a primary handler of its own for the named client overrides it |
| `HealthCheckBuilderExtensions.AddRestLedgerClient` | `IHealthChecksBuilder` extension probing the participant over HTTP via `GET /v2/state/ledger-end` |
| `MalformedTransactionTreeException` | Thrown when the node ids on a transaction's events cannot describe a tree, so no hierarchy can be reconstructed from them. Declared in `Canton.Ledger.Abstractions` and shared with the gRPC transport, so one `catch` covers both |

The raw, per-service Refit interfaces live in the `Canton.Ledger.Rest.Client.Raw` namespace behind the `CANTONREST001` experimental diagnostic; consume them only through `AddRestLedgerRawApis` when you need an endpoint the adapter does not surface.

## Registration

```csharp
// Config-based — binds RestLedgerClientOptions from the given section
services.AddRestLedgerClient(configuration.GetSection("Canton:Rest"));

// Action-based
services.AddRestLedgerClient(options => options.HttpAddress = "http://localhost:7575");
```

Resolve the transport-neutral interfaces — the container binds and validates the options and owns
the `HttpClient`, so nothing here is constructed or disposed by hand:

```csharp
await using var provider = services.BuildServiceProvider();

var ledgerClient = provider.GetRequiredService<ICantonLedgerClient>();
```

`AddRestLedgerClient` registers the HTTP adapter behind the full Canton surface `ICantonLedgerClient` and the four narrower service types it serves — `ILedgerClient`, `ILedgerReader`, `ILedgerWriter` and `ILedgerStreamer` — so a consumer injects the full Canton surface from REST exactly as from gRPC. All five are **transient**, unlike the gRPC transport's singletons, because the adapter is a thin per-resolution wrapper over the shared `HttpClient`: two injection points receive two adapters, not one shared instance. `IAdminClient` is registered alongside them as a fallback singleton: an `IAdminClient` registered before it is kept, and the gRPC transport's `AddAdminClient` or `AddCantonLedger` takes precedence whether it is called before or after, so a host that pairs REST ledger traffic with gRPC administration keeps the gRPC admin client. Authentication reuses the shared `Canton.Ledger.Abstractions.ITokenProvider` — register one (for example `AddCantonStaticAuth(...)` or client-credentials auth) and the client attaches the bearer token to every request.

Add `AddRestLedgerRawApis(...)` alongside it to also register the opt-in raw Refit surface.

## Reads, writes, and streams

- **Writes** (`TryCreateAsync`, `TryExerciseAsync`, `SubmitAndWaitAsync`, `TrySubmitAndWaitForTransactionAsync`) submit through `/v2/commands/submit-and-wait[-for-transaction]`. The `Try*` methods return a structured `ExerciseOutcome` (a `DamlError` on a structured participant error, an `InfraError` on a transport failure or per-call timeout, a `CommittedUndecodable` when the command committed but the response could not be decoded, or `TryExerciseAsync` could not read its choice result — do not resubmit, read the transaction by its `UpdateId`); `SubmitAndWaitAsync` throws `LedgerOperationException`, matching the gRPC transport's throwing contract.
- **Transaction trees** — `TrySubmitAndWaitForTransactionTreeAsync` and `GetUpdateTreeByOffsetAsync` return a committed transaction with its parent/child hierarchy intact: which exercise caused which sub-creates and sub-exercises. Both are `ICantonLedgerClient` members, so a consumer reaches either through the injected interface on either transport. Both always ask the participant for the ledger-effects view, since hierarchy is only meaningful over creates and exercises. The participant reports that hierarchy as node ids on the ordinary event list — each exercise states the highest node id in the subtree it caused — so the tree is rebuilt from the same response the flat read decodes, not a second request. Node ids that cannot describe a tree fail loudly rather than yielding a silently wrong tree: a `CommittedUndecodable` outcome on the submit path, and on the point read a `LedgerOperationException` (`UndecodableBody`, `NotCommitted`) whose `InnerException` is a `MalformedResponseException` carrying the `MalformedTransactionTreeException` — catch the base type there, not the derived one. Node-id gaps left by the participant's own party filtering are normal and tolerated, and an event whose parent exercise was filtered out attaches to the nearest enclosing exercise the parties can still see, or surfaces as a root when none remains. Project a tree back to the flattened shape with `TransactionTreeExtensions.ToTransactionResult()` rather than calling twice — on the submit path neither shape is a superset of the other on the wire, since the flat `TrySubmitAndWaitForTransactionAsync` keeps the server-default ACS-delta view; on the point read both calls send the identical request and differ only in projection.
- **Streaming reads** — `SubscribeActiveAsync` is an ACS snapshot ending in a terminal checkpoint, read page by page over `POST /v2/state/active-contracts-page` at one pinned offset, with `StreamWindowLimit` as each page's size, so a snapshot larger than the participant's list cap still arrives whole (Canton 3.5.10 or later); `SubscribeAsync` / `SubscribeLedgerEffectsAsync` are offset-range reads over the pagination loop, which re-POSTs a bounded window from the last offset it observed and stitches the windows into one continuous stream. A failure ends any of them with a terminal in-band `StreamError` rather than a throw, matching the gRPC transport.
- **Interface subscriptions carry the participant-computed view.** Subscribing an interface marker projects the matching `interfaceViews` entry's `viewValue` onto each row, not the implementing template's `createArgument`; a view the participant could not compute surfaces as `Unclassified(InterfaceViewUnavailable)` rather than an empty payload. `QueryActiveAsync<TInterface, TView>` materializes that snapshot into typed view records, exactly as over gRPC.
- **Command completions** — `CompletionStreamAsync` reads `POST /v2/commands/completions`, whose success body is a JSON array, and yields `CommandAccepted` / `CommandRejected` / `Checkpoint` per entry. It is a live tail over the same pagination loop the offset-range reads use: each call returns one participant-bounded window, closed once the entry limit is reached or no completion has arrived for the idle timeout, and the loop re-POSTs the next window from the last offset it observed for as long as the caller enumerates. A window that yields nothing is reopened at the same offset, so a quiet ledger does not end the enumeration and a call site cannot tell this stream from the gRPC one. `StreamWindowLimit` and `StreamWindowIdleTimeout` on the options bound each window (`limit` and `stream_idle_timeout_ms` on the wire), defaulting to 200 entries and 250 ms. The `Checkpoint` entries are the participant's own offset checkpoints, relayed as they arrive; this client manufactures none, so a quiet stream leaves the caller's resume offset where it stood. A non-success response ends the enumeration with a terminal `StreamError` carrying the HTTP status code, the parsed participant message, and the participant's error id where it named one — the in-band fault contract the gRPC transport honours for this method. A success body that will not decode (malformed JSON, or a completion carrying an unparseable offset or deduplication duration) ends it the same way, with `StatusCode` `0` since the transport reported no failure, as does a window whose entries carry no offset the next window could resume from, since following it would re-read what was just delivered. A window whose request gets no answer (a connection failure, or a timeout the caller did not cause) ends it with a terminal `StreamError` whose `Status` is `NoResponse` and whose `SourceException` is the transport exception, on the first window as on any later one, and a caller cancelling gets an `OperationCanceledException` rather than a `StreamError`.
- **`GetCompletionsAsync`** reads `POST /v2/commands/command-completions` through the same window loop, for the given parties across every user's submissions, and sends no `userId`.
- **Traffic-cost estimation** — `EstimateTrafficCostAsync(submission, timeout, cancellationToken)` asks the participant what a submission would consume in synchronizer traffic before committing to it, over `POST /v2/interactive-submission/prepare`, where cost estimation is on by default (the request sends no `estimateTrafficCost` hints). Nothing reaches the ledger — the participant interprets the commands, answers, and the prepared transaction is discarded — so the call costs about what a submission costs, but the caller's token needs only *read* rights for the `actAs` parties rather than act rights. The answer projects into the shared `Canton.Ledger.Abstractions.TrafficCostEstimate` (`EstimatedAt`, `ConfirmationRequestCost`, `ConfirmationResponseCost`, `TotalCost`, all in bytes), the same record the gRPC client returns. A participant that sends no estimation — one with traffic control disabled, for instance — yields `null` rather than a zeroed record; an estimation that is present and reports zero is a genuine zero-cost estimate. The method is an `ICantonLedgerClient` member, so a consumer prices a submission through the injected interface whichever transport is registered. A rejected request throws `LedgerOperationException` with the participant's category and error id, as on gRPC. One per-transport difference: the cost does not reach a span, because this client's spans are emitted per HTTP request by the pipeline handler rather than per client method.
- **An open-ended live tail is served over plain HTTP.** `SubscribeAsync` / `SubscribeLedgerEffectsAsync` with `toOffset: null` follow the ledger through the pagination loop for as long as the caller enumerates, as does `CompletionStreamAsync`, which takes no end offset and is therefore always a tail; an end offset is a termination condition on the same loop, so a range wider than the participant's entry cap pages rather than failing. Every window sends `limit` and `stream_idle_timeout_ms`, bounded by `StreamWindowLimit` and `StreamWindowIdleTimeout` on the options. Ask before you subscribe: the registered adapter implements `Canton.Ledger.Abstractions.IUnboundedStreamingCapability`, so a consumer holding the injected `ICantonLedgerClient` casts to that interface and reads `SupportsUnboundedStreaming` — now `true` here, as on the gRPC client and on `FakeLedgerClient`.
- **Errors are parsed, not passed through raw.** A participant's non-success response on any call, `GetLedgerEndAsync` included, is decoded into the participant's category, error id and message before it reaches the caller — as a `LedgerOperationException` (with `Status` `TransportStatus.Http(code)`) on the throwing methods, or an `ExerciseOutcome.DamlError` / `InfraError` on the `Try*` methods.
- **Every call reports failure through the same contract as the gRPC client.** A connection failure or a deadline overrun, the per-call `timeout` included, raises `LedgerOperationException` with `Status` `TransportStatus.NoResponse` and the original exception as `InnerException`, not `HttpRequestException` or `TaskCanceledException`; a response body that is missing or cannot be read raises it with `Status` `UndecodableBody`, and a typed point read whose payload does not fit `T` does too, with a `MalformedResponseException` as `InnerException`. Catch `LedgerOperationException`, and branch on `Category` and `CommitState` (a failed read is always `NotCommitted`; a write that got no answer is `Unknown`) for an answer that holds on both transports. Only your own cancellation surfaces as `OperationCanceledException`.

## Generated bindings

JSON Ledger API payloads do not say which Daml type a value has, so the client decodes each one against the generated binding for its template or interface. Each generated package registers its types with the SDK's generated type registry from a module initializer, and the CLR runs a module initializer only when something first touches that assembly, not when the assembly is referenced or loaded.

On the first lookup that finds no generated type registered for the payload's exact identifier, even when an older version of the same module and entity is registered, the client therefore loads every library the application's `deps.json` lists that depends on `Daml.Runtime`, runs each one's module initializer, and looks again. This runs at most once per process. A binding you reference but have not used yet is found without any setup, and a library that fails to load is skipped.

A host with no `deps.json` — a custom `AssemblyLoadContext`, a plugin host — registers each generated assembly itself, once, before the first call that reads its payloads:

```csharp
RuntimeHelpers.RunModuleConstructor(typeof(AnyGeneratedType).Module.ModuleHandle);
```

`AnyGeneratedType` is any type from the generated assembly. Bindings generated before the registry existed register nothing and are not found this way; regenerate them with the current codegen, including the bindings of the packages you depend on.

The client finds generated types only through that registry; it does not scan loaded assemblies. A node whose template, interface or choice has no single generated binding loaded, whether because it is not registered or because two are, does not fail the transaction: it is carried as `DamlUndecodedJson`, the Daml-LF JSON exactly as the participant sent it, and the rest of the transaction decodes. `CreatedContract.UndecodedPayload` and `TreeEvent.Created.UndecodedCreateArguments` hold such a created node's arguments, and are `null` for a node that decoded. Reading one through `FromDamlValue<T>` decodes it with the target type's own JSON reader.

## Retry

Off by default. Opt in to have transient transport failures retried with exponential backoff and jitter, the same `Canton.Ledger.Kernel` pipeline the gRPC client uses:

```csharp
services.AddRestLedgerClient(options =>
{
    options.HttpAddress = "http://localhost:7575";
    options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 3, Delay = TimeSpan.FromMilliseconds(200) };
});
```

The retry handler sits outermost in the HTTP pipeline, so every attempt resolves a fresh bearer token and emits its own client span, plus a `RestLedgerClient.RetryAttempt` span carrying `retry.attempt` and `retry.delay_ms`. That span name is a wire-visible constant, not a type a consumer names. So a retried attempt can replay the request body, the handler buffers request content in memory before the first attempt — enabling retry therefore costs one in-memory copy of each request body, which is worth weighing against a large DAR upload. Retried requests reuse the `command_id` fixed above the retry boundary, so ledger-side deduplication makes a resubmission idempotent — the pipeline itself confers no idempotency.

Retry differs from the gRPC pipeline in one deliberate way: **only exceptions are retried** — a refused/reset/DNS-failed connection (`HttpRequestException`) and a client-side request timeout, the HTTP analogues of gRPC `Unavailable`/`DeadlineExceeded`. A participant that *answers* with `429`, `503`, or a gateway `5xx` is a response, not an exception, and is surfaced to the caller unretried.

A retried submission whose first attempt committed while its response was lost comes back from the participant as `DUPLICATE_COMMAND`. As in the gRPC client, the REST client resolves that rejection to success: it reads the committed transaction at the rejection's `completion_offset` and returns it. If the rejection carries no usable `completion_offset`, reports `accepted` as `false`, or the read at that offset fails, the `DUPLICATE_COMMAND` error is surfaced unchanged.

## Health checks

```csharp
services.AddHealthChecks().AddRestLedgerClient();
```

Probes the participant with `GET /v2/state/ledger-end`, which is not gated behind `participant_admin`, so a least-privilege deployment still reports healthy. The check resolves the HTTP adapter's own registration, so a host wiring both transports gets a check that probes the HTTP endpoint specifically rather than whichever transport won the `ILedgerClient` registration.

## Participant administration

`IAdminClient` serves the same contract over the JSON Ledger API as the gRPC transport does, with differences the JSON Ledger API imposes:

- `ListKnownPackagesAsync` throws `NotSupportedException`: the participant serves no JSON route for it. Use `ListVettedPackagesAsync`, or the gRPC `IAdminClient`.
- `GetTimeAsync` and `SetTimeAsync` throw `NotSupportedException`: the participant serves no JSON route for its TimeService. Use the gRPC `IAdminClient`.
- `UploadDarAsync` sends no `submissionId`: `POST /v2/dars` takes the DAR bytes alone.
- `GetCommandStatusAsync`, `PruneAsync`, `UpdatePartyIdentityProviderIdAsync`, and `DeleteUserAsync` with a non-empty `identityProviderId`, throw `NotSupportedException`.

`GetPartiesAsync` costs one `GET /v2/parties/{party}` round trip per party. Mutations — `AllocatePartyAsync`, `CreateUserAsync`, `GrantUserRightsAsync`, `RevokeUserRightsAsync`, `DeleteUserAsync` and `UploadDarAsync` — are sent once even with `Retry` enabled, because replaying one the participant already applied would fail or duplicate it.

## Tracing

The client emits an OpenTelemetry HTTP client span per request. Subscribe to it by name:

```csharp
tracing.AddSource(LedgerActivitySourceNames.RestLedgerClient);
tracing.AddSource(LedgerActivitySourceNames.RestAdminClient);
```

`RestAdminClient` spans one internal activity per `IAdminClient` call, named after the method (for example `RestAdminClient.GetUserAsync`), with the HTTP client spans nested under it.

`Canton.Ledger.OpenTelemetry`'s `AddCantonLedgerInstrumentation()` registers both sources alongside every other Canton client source.

## Related Packages

- `Canton.Ledger.Abstractions` — the transport-neutral Canton participant contracts this client implements
- `Canton.Ledger.Grpc.Client` — the gRPC client implementing the same surface
- `Canton.Ledger.Testing` — in-memory fakes for unit-testing against these contracts
- `Canton.Ledger.Rest` — the raw Refit surface this adapter is built on

## Support

For questions, bug reports and feature requests, open a
[GitHub issue](https://github.com/peacefulstudio/canton-dotnet-sdk/issues).
This package is maintained by [Peaceful Studio](https://peaceful.studio).
For consulting beyond this SDK (Daml development, .NET integration with
Canton, validator operations), please write to
[info@peaceful.studio](mailto:info@peaceful.studio).
