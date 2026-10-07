# Observability

The Canton Ledger API clients emit distributed tracing spans using the BCL's
`System.Diagnostics.Activity`/`ActivitySource` types. The clients themselves take no
OpenTelemetry package reference — wiring an exporter is entirely opt-in.

## Activity sources

Five well-known `ActivitySource` names cover every client, listed by
`Canton.Ledger.Kernel.Telemetry.LedgerActivitySourceNames.All`:

| Source name | Client |
|---|---|
| `Canton.Ledger.Grpc.Client.LedgerClient` | gRPC `LedgerClient` (submit, read, stream) |
| `Canton.Ledger.Grpc.Client.AdminClient` | gRPC `AdminClient` |
| `Canton.Ledger.Rest.Client.RestLedgerClient` | JSON Ledger API `RestLedgerClient` |
| `Canton.Ledger.Rest.Client.RestAdminClient` | JSON Ledger API `IAdminClient` |
| `Canton.Ledger.Pqs.Client.PqsClient` | PQS `PqsClient` |

The layout is one source per client, not one source for the whole SDK, by design. A source name
attributes a span to the transport that produced it, so a host can enable, filter or sample the
gRPC, JSON and PQS paths independently, and a trace backend can group spans by transport without
parsing span names. All five share the `Canton.Ledger.` prefix, so a host that wants every one of
them registers them with a single wildcard, `AddSource("Canton.Ledger.*")`, or with the one call
described below.

## Registering an exporter

`Canton.Ledger.OpenTelemetry` provides one `TracerProviderBuilder` extension,
`AddCantonLedgerInstrumentation()`, that registers all five sources above plus Npgsql's own
`"Npgsql"` source (Npgsql 10.0.3 starts this source directly, via its built-in
`NpgsqlActivitySource` — no `Npgsql.OpenTelemetry` package reference is needed):

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddCantonLedgerInstrumentation()
        .AddOtlpExporter());
```

A trace started by the caller spans every transport. One live LocalNet run submits a command over
gRPC, reads the update stream, submits a command over the JSON Ledger API and queries PQS under a
single parent `Activity`, and asserts that the gRPC, REST and PQS spans all carry that parent's
trace id.

A host that only uses the REST client and never calls `AddCantonLedgerInstrumentation()` pulls
in no OpenTelemetry SDK dependency at all — that package is the only place in the Canton Ledger
API client libraries that references the OpenTelemetry SDK.

## `traceparent` propagation across transports

Both the gRPC and JSON (REST) transports send requests through the BCL's `SocketsHttpHandler`
(via `Grpc.Net.Client` and `HttpClient` respectively). `SocketsHttpHandler` injects a W3C
`traceparent` header from the ambient `Activity.Current` automatically, independent of whether
any `ActivitySource` listener — OpenTelemetry or otherwise — is registered. Neither transport
needs an extra instrumentation package (such as `OpenTelemetry.Instrumentation.GrpcNetClient` or
`OpenTelemetry.Instrumentation.Http`) for a caller's ambient trace to reach the ledger: parenting
a call under an `Activity` is enough. This is exercised directly against a real loopback socket
for both transports, so a caller's parent activity — whether started by
`AddCantonLedgerInstrumentation()`'s own sources or by unrelated instrumentation the host already
runs — carries its trace id onto the wire without further configuration, as long as the
SDK-created `SocketsHttpHandler` stays in place.

No `Grpc.Net.Client` instrumentation package is registered for the gRPC transport, by design.
`OpenTelemetry.Instrumentation.GrpcNetClient` has no stable release on nuget.org, only
prereleases, and `AddCantonLedgerInstrumentation()` does not take a prerelease dependency on
behalf of every consumer. Nothing is lost on the wire: the `traceparent` header still reaches the
participant on every gRPC call through `SocketsHttpHandler`, as the
[gRPC propagation test](../../tests/Canton.Ledger.Grpc.Client.Tests/GrpcTraceparentPropagationTests.cs)
shows against a real loopback socket. A host that wants the extra per-call client spans from that
package can register it itself.

A host that replaces the primary handler — via `LedgerClientOptions.ConfigureChannel` assigning a
new `GrpcChannelOptions.HttpHandler` on the gRPC side, or via the REST named client's
`ConfigurePrimaryHttpMessageHandler(Func<HttpMessageHandler>)` — takes over from that
`SocketsHttpHandler`. Automatic `traceparent` injection is a property of that specific handler
type, not of the pipeline in general, so a replacement handler (a stub, a recording handler, or
any implementation that isn't itself backed by `SocketsHttpHandler`) does not inject the header on
its own; a host that replaces the primary handler and still wants propagation must add it itself
on that handler.

## PQS span attributes

The PQS client's spans come from Npgsql's own instrumentation. Npgsql 10 renamed its
semantic-convention attributes to match the current OpenTelemetry semantic conventions for
databases; the attributes an exporter sees are the new names, not the older ones some
third-party dashboards and docs still reference. The SDK emits only the stable names and never
emits the deprecated ones alongside them: the old names are deprecated in the OpenTelemetry
database semantic conventions, so emitting both would double every attribute on every PQS span
to serve conventions that are being retired. A dashboard or query still keyed on a legacy name
needs to move to the stable one:

| Npgsql 10 attribute | Legacy attribute it replaces |
|---|---|
| `db.system.name` | `db.system` |
| `db.query.text` | `db.statement` |
| `db.namespace` | `db.name` |

## See also

- [Configuration reference](configuration-reference.md) — client option binding, environment
  variables, and health-check registration.
- [Architecture overview](architecture-overview.md) — how the client packages fit together.
