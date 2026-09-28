# Daml.Ledger.Abstractions

Transport-agnostic abstractions for Daml ledger clients.

`ILedgerClient` is the composition of three capability interfaces, each
covering one slice of ledger interaction:

- `ILedgerWriter` — submit commands, create contracts, exercise choices.
- `ILedgerReader` — query the participant's ledger end.
- `ILedgerStreamer` — subscribe to contract events and active-contract-set
  snapshots.

A transport implements the split interfaces (and therefore `ILedgerClient`);
a consumer that only needs to read or stream can depend on `ILedgerReader` or
`ILedgerStreamer` alone. Implementations live in their own packages,
published to NuGet.org from
[`canton-dotnet-sdk`](https://github.com/peacefulstudio/canton-dotnet-sdk):

- `Canton.Ledger.Grpc.Client` — gRPC client for Canton participants
- `Canton.Ledger.Rest.Client` — HTTP client for the Canton JSON Ledger API
- `Canton.Ledger.Testing` — in-memory fakes (`FakeLedgerClient` and friends)
  for unit-testing application code without a live participant, no mocking
  framework required

Derived convenience methods — single-`Party` overloads, the throwing
`ExerciseAsync` wrappers, and the create-by-exercise helpers
(`TryCreateOneByExerciseAsync`, `TryCreateManyByExerciseAsync`, and their
throwing `CreateOneByExerciseAsync`/`CreateManyByExerciseAsync` twins) — live in
`Daml.Ledger.Abstractions.Extensions` and are opt-in via `using
Daml.Ledger.Abstractions.Extensions;`. The capability interfaces stay free of
this sugar, so a consumer that only needs the `SubmitterInfo`-based
primitives never has it in scope.

Two interfaces, one client. The transport packages above register the same
client instance under two interfaces: `ILedgerClient` (this package) and
`Canton.Ledger.Abstractions.ICantonLedgerClient` (the ledger repo), where
`ICantonLedgerClient : ILedgerClient` adds the operations specific to a
Canton participant — fire-and-forget submission, the command completion
stream, connected-synchronizer and Ledger API version discovery, and
offset/id point reads. Picture the surface as a stack: at the bottom the
three capability slices (`ILedgerWriter`, `ILedgerReader`,
`ILedgerStreamer`); above them `ILedgerClient`, which is nothing but the
three combined; above that `ICantonLedgerClient`, which is `ILedgerClient`
plus the Canton-only operations; and at the top the concrete transport
clients, which implement `ICantonLedgerClient` and add nothing public of
their own. Depend on the lowest layer that has what you need: a capability
slice when you only read or stream, `ILedgerClient` for portable
application code (it is all this codegen's generated extensions ever
require), and `ICantonLedgerClient` only where you call a Canton-specific
operation. Never downcast to a concrete client class — every method the
concretes expose is already on one of the registered interfaces, and
staying on the interface keeps your code mockable, decoratable, and
transport-swappable.

Cancellation is never an error outcome. When the caller's
`CancellationToken` fires, every operation on these interfaces — the
structured `Try*` primitives, the throwing wrappers in
`Daml.Ledger.Abstractions.Extensions`, and the streaming methods — must
surface `OperationCanceledException`; cancellation is never wrapped in an
`ExerciseOutcome.InfraError` outcome or a `LedgerOperationException`.
Previews before `0.4.0-preview.1` masked cancellation of the throwing
convenience wrappers as `LedgerOperationException`, so consumers added
call-site guards re-checking the token after a failed call; on
`0.4.0-preview.1` and later those guards are dead code and can be deleted
(see the CHANGELOG entry for that release).

Ledger positions are the `LedgerOffset` value type, never a raw `long`.

Generated codegen output (`Try<Choice>Async` extensions, projector helpers)
references this package — never a transport-specific one — so consumers
that only need projectors do not transitively depend on a gRPC stack.

Versioned in lockstep with `Daml.Runtime` and `Daml.Codegen.CSharp`. The
`Canton.Ledger.*` transport packages track this line by minor: a client
`0.N.x` embeds `Daml.* 0.N.x`, so the package version alone tells you which
Daml line a transport carries, while patch and `-preview.N` counters evolve
independently per repo.

Interface-only package. `Daml.Ledger.Abstractions.Testing.Conformance` ships the
shared behavioral conformance kit — a transport implementation subclasses it
to verify it upholds this package's documented contract (cancellation,
unclassified-row surfacing, terminal checkpoint, seeded-row ordering) —
beyond that, implementers carry their own contract tests.
