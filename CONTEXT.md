# Canton .NET SDK

Lets .NET applications talk to a Canton participant with full Daml type safety. The codegen
generates strongly-typed C# from Daml `.dar` archives: a JVM-side helper wraps
`daml-lf-archive` to decode a DAR into an intermediate representation, and a .NET-side emitter
consumes that representation and writes idiomatic C#. The ledger client (`Canton.Ledger.*`)
submits commands and reads streams over the gRPC or JSON Ledger API using those generated
types. End-user applications have no JVM dependency.

## Language

### Codegen

**Intermediate AST** (IR):
The protobuf message exchanged between the JVM helper and the C# emitter. Mirrors the
shape of `Ast.PackageSignature` from `daml-lf-archive` but is owned by this repo so
upstream renames cannot break the wire.
_Avoid_: "intermediate JSON", "AST blob", "IR JSON"

**Intermediate DAR**:
The top-level `IntermediateDar { main, dependencies }` IR message for a single `.dar`. The
on-disk artefact handed from the JVM helper to the C# emitter.
_Avoid_: "decoded DAR", "AST file"

**JVM helper**:
The Scala binary that reads a `.dar` via `daml-lf-archive` and emits an Intermediate DAR.
Runs only at codegen time — never at application runtime. Coupling to `daml-lf-archive`
is confined to this binary. Shipped as a JAR inside the `dpm codegen-cs` bundle and
executed against the host JDK (a dpm install precondition). It is
also packaged standalone as `daml-dar-to-proto`: from `0.6.0-preview.1`, GitHub releases
attach the runnable jar and the Intermediate DAR proto schema, so non-C# SDKs can run the
JVM helper directly to turn a `.dar` into an Intermediate DAR.
_Avoid_: "Scala helper", "decoder service", "ast extractor"

**AstToIntermediate translator**:
The single function inside the JVM helper that maps
`Dar[(PackageId, Ast.PackageSignature)]`, together with the static party analyses, to
`IntermediateDar`. It, the decoders and the party-expression analyzer beside it are the only
code that depends on Digital Asset's own Scala case-class shapes; everything outside the JVM
helper depends on the Intermediate AST.
_Avoid_: "AST converter", "Scala-to-proto mapper"

**C# emitter**:
The .NET binary that consumes an Intermediate DAR and writes `.cs` files. Implemented by
`CSharpCodeGenerator`, which walks the model that `IntermediateDarReader` builds from the
`IntermediatePackage` proto.
Shipped as a self-contained, single-file .NET binary inside the `dpm codegen-cs` bundle —
one per target RID, with the .NET runtime statically bundled — so consumers do not need
a host .NET runtime to run codegen. **DPM does not embed the emitter**: DPM is the
dispatcher (Go binary), the OCI bundle's entrypoint spawns the emitter as a child process
(alongside the JVM helper), and the two communicate via the on-disk Intermediate DAR
proto file. The emitter is not a DLL and is never loaded into DPM's address space.
_Avoid_: "the codegen", "generator" (both ambiguous between JVM helper + C# emitter),
"the emitter DLL", "the plugin DLL", "the in-process emitter"

**Full decode**:
The JVM helper's default decode: `decodeArchivePayload` (returns full `Ast.Package`) keeps the
signatory, observer and controller expressions so the static party analysis can read them,
then erases everything but data signatures and the analysis verdicts. It is
**patch-version-sensitive** — a patch release that changes a party expression changes the
Intermediate AST. `dpm codegen-cs` always runs it.
_Avoid_: "full parse", "expression decode"

**Schema-mode decode**:
The JVM helper's `--schema-only` opt-out: `decodeArchivePayloadSchema` (returns
`Ast.PackageSignature`) strips expressions and choice bodies, so every party analysis is
`Dynamic`. It is **patch-version-insensitive** — two patch-different versions of the same
package produce identical Intermediate ASTs.
_Avoid_: "signature decode", "lite parse"

**`dpm codegen-cs`**:
The dpm component that runs the codegen pipeline end-to-end: invokes the bundled JVM
helper on the input DAR, hands the Intermediate DAR to the bundled C# emitter, writes
`.cs` to the configured output directory. Distributed as a multi-platform OCI artifact
(`linux/amd64`, `linux/arm64`, `darwin/arm64`, `windows/amd64`); stock `dpm` fetches the
right RID lazily on first invocation (requires `DPM_AUTO_INSTALL=true`) and dispatches
to its launcher at `dpm codegen-cs` invocation time. Users opt in by listing every
component they need — SDK ones and `codegen-cs` — under `components:` in `daml.yaml`,
with no `sdk-version` key (the two are mutually exclusive by upstream design).
The toolchain's supply chain is the OCI registry plus stock dpm — the codegen
toolchain is not distributed as a `dotnet tool`, a Docker image, or a NuGet
package. (The `Daml.Codegen.CSharp` emitter library is separately published
as a NuGet library for programmatic use.)
_Avoid_: "the cli", "codegen-cs tool", "codegen-cs plugin", "the container"

**Package name table**:
The one place that decides, for a Daml package, what each of its types is called in C# and
where it lives: the emitted name of every top-level type, an interface's marker name, the
namespace of every module, and which records are emitted nested inside a template as a
choice's argument. Built once per package, immutable, and shared by the emitter and the
cross-package resolver, so a type is spelled the same wherever it is declared or referenced.
A package that cannot be named without a clash never yields one: building it fails codegen.
_Avoid_: "name cache", "type-name sets", "naming helpers"

**`PackageEmitContext`**:
The immutable value the C# emitter threads through its emit methods, one per Daml module:
the package, the module being emitted and its namespace, the package-wide data-type lookup,
the package's name table, and the view-record markers. It holds no name sets of its own —
emitters ask the package name table. `PackageEmitContext.ForPackage` scans a package once and
hands back one context per module; read-only during emission.
_Avoid_: "codegen state", "the current-package fields", "emit scratch"

**Cross-package resolver** (`DarCrossPackageResolver`):
The DAR-scoped module that resolves a `DamlTypeRef` to a C# name. A reference into the package
being emitted is answered from that package's name table, a reference into a dependency from
the dependency's own name table, and a standard-library type from the runtime mapping. It owns
the archive lookup, the index of each dependency's data-type kinds, and the set of external
package ids it has discovered — read after emission to emit a `<PackageReference>` per id.
Lives for one `Generate` call and resolves against an `IDarSource`.
_Avoid_: "type resolver", "package resolver service", "the cross-package cache"

**`PartyAnalysis`**:
The pure module that reasons about a template's parties: classifying controller / signatory /
observer sets as statically-resolvable or `Dynamic`, unioning them, partitioning them into
controller-params and observer-only-params, and validating a `DamlPartyAnalysis` against the
real template fields. Shared dependency of `ChoiceEmitter` and `SubmissionExtensionsEmitter`;
party sets in, partitioned params out, so it is trivially unit-testable.
_Avoid_: "party helper", "the party utils", "controller logic"

**`DamlTypeMapper`**:
The module that turns a `DamlType` into C#: `MapType` (→ a C# type name), `ToValue` and
`FromValue` (→ serialize / deserialize expressions). An instance constructed per package over a
`PackageEmitContext` and the cross-package resolver, which it calls into for cross-package names
— it does not own resolution. Pure functions of its inputs: `DamlType` in, C# fragment out, with
a trivially-constructible context, so it is unit-testable without a real DAR.
_Avoid_: "type converter", "the mapping switch", "serializer"

**`SubmissionExtensionsEmitter`**:
The module that emits the template *create / submission* path — `TryCreateAsync`, the optional
`Observers(payload)` helper, and the per-template `<Template>SubmissionExtensions` class —
deriving signatories and observers from the payload via `PartyAnalysis`. Distinct from
`ChoiceEmitter`: creating a contract is not exercising a choice.
_Avoid_: "submitter", "the create wrapper", "named-submitter partial"

**`ChoiceEmitter`**:
The module that emits the C# to *exercise* a choice: the
`Choice<TOwner, TArg, TResult>` descriptor with its result decoder, the typed `Try<Choice>Async`
exercisers (both the contract-id-returning and the value-returning flavour, partials of one
class), and the interface-choice extensions. An instance constructed per package over a
`PackageEmitContext`, the cross-package resolver, the codegen options, the package's
`DamlTypeMapper`, and the shared `PartyAnalysis`; methods take `(IndentWriter, template/interface)`.
It *calls* the mapper for every type fragment and *reads* — does not own — the resolved
choice-argument metadata. Which flavour a choice gets is decided by the pure
`ChoiceEmitter.ReturnsContractIds` predicate over its return type, unit-tested directly; both
flavours project the result through the descriptor, so no result record is synthesised.
Distinct from `SubmissionExtensionsEmitter`: creating a contract is not exercising a choice.
_Avoid_: "choice helper", "the exercise writer", "the async wrapper generator", "choice-arg owner"

**Descriptor witness**:
A generated `static` value whose *type* names two or more types that belong together, passed
as an ordinary argument so a call site infers all of them at once. C# performs no partial
type-argument inference — a caller either supplies every type argument or none — so whenever
a surface needs two related types and only one is spellable at the call site, the pair travels
on a descriptor instead. `ViewDescriptor<TInterface, TView>` pairs an interface marker with its
view record, `Choice<TOwner, TArg, TResult>` pairs a template or interface with a choice's
argument and result, and `KeyDescriptor<TTemplate, TKey>` pairs a keyed template with its key
type. A descriptor may be empty (a pure type witness, like `ViewDescriptor`) or carry codecs
(like `Choice`, and like `KeyDescriptor`, which carries the key decode). Each `Choice` and
`ViewDescriptor` is a plain static on the type it describes. `KeyDescriptor` is instead
reached through the `static abstract` `IHasKey.Key` member, so generic code finds it without
reflection — that is the forward convention new descriptors follow.
_Avoid_: "marker", "phantom type" (those name the facet interface, not the value), "the
metadata object", "type token"

**Active contract**:
A contract the participant reports as currently in its active contract set, together with the
provenance the snapshot delivers alongside it — the synchronizer it sits on, and the offset of
the update that last created or assigned it. A drained snapshot returning only the payload has
*discarded* that provenance, not been denied it. `ActiveContract<TContract>` is where the pair
lives. It composes over the contract shapes rather than widening them, so contract equality
stays contract equality and two snapshots of one contract remain one contract observed twice.
_Avoid_: "ACS row", "live contract", "current contract"

**Last-update offset**:
The offset of the most recent update that created or assigned a contract. It orders and it
identifies — active contracts can be sequenced by it, one can be quoted by it, and it is what a
by-key lookup exposes so a consumer can impose an order the client deliberately declines to
promise — but it does **not** resume, because it may point at an already-pruned update. Both it
and a resume offset are update offsets, which is precisely why naming it plainly matters. A
consumer persisting resume state takes it from the snapshot's terminal checkpoint, never from a
per-contract offset.
_Avoid_: "offset" unqualified, "creation offset", "contract offset", "recency"

**Equatable array**:
`EquatableArray<T>`, the type every list member of the event and stream records carries: a
read-only struct that owns a copy of its elements, compares by content, and whose `default` is
empty. It makes "never `null`, equal by content" a property of the member's *type* rather than
of a guard a reviewer has to remember at each entry point — nothing a caller passes, sets
through `with`, or reaches by reflection can put a `null` there, and two records built from
equal lists are equal. Construction copies, through `EquatableArray.Create` or a collection
expression; reads through `IReadOnlyList<T>` still work. Not `Daml`-prefixed, because it carries
no Daml semantics: the Daml `List a` *payload* type stays `IReadOnlyList<T>`.
_Avoid_: "owned list", "value list", "immutable list" (a BCL family with reference equality),
"borrowed list" (every list member owns its elements)

**Contract key**:
The value a keyed template nominates to identify its contracts. It is a property of an
*active contract*, not of a template payload — the ledger carries it on every `Created` and
`Assigned` event (the only ledger events that bear one), and on the nodes of a prepared
transaction that create, fetch, exercise or query by key, so it is read, not computed. A
payload that has never been to the ledger has no key. Absent from the Daml-LF 2.x line until
Daml-LF 2.3 reintroduced it, first served by Canton 3.5.1: a package compiled at a Daml-LF 2.x
target below 2.3 has no keys to carry, and a Canton 3.x participant below 3.5.1 sends none of
these. Keys are not unique: several active contracts may share one, lookups return a first
match by an order the ledger only partly guarantees — contracts created in the current
transaction first, then explicitly disclosed contracts in the order the command listed them,
then contracts known to the participant in no guaranteed order — and enforcing uniqueness is
the consuming application's job, never the engine's.
A generated payload type carries a static `Key` **descriptor witness**. That is not a
counter-example to the above: a static describes the *template*, stating which key type this
template's contracts are looked up by. It never asserts that a payload instance knows its own
key — an accessor deriving the key from a payload instance was tried and abandoned once the
key-expression translation surface proved far larger than a simple field projection, so the
key instead arrives on the wire as a property of the active contract, never derived from the
payload.
_Avoid_: "the key projection" (that names the Daml-side expression, not the value),
"key accessor", "computing a contract's key", "primary key", "contract id", "unique key"

**Key hash**:
The participant's own digest of a contract key, relayed by the ledger client without
modification in substance — Base64-encoded from the participant's `ByteString` on the gRPC
path, passed through as-is on the REST path. The client never computes or verifies one: it is
the participant's statement about its own key, and a client-computed hash would be a
fabricated value wearing a checksum.
_Avoid_: "key digest", "key fingerprint", "computed hash"

**Key type**:
The generated C# type of a contract key — a record, a tuple, or a bare primitive — fully
constructible and serializable by a caller who has never seen the contract. Distinct from a
**key value**, which is an inhabitant of it. The key type is what makes by-key commands
possible: the caller builds a key value out of data it already holds. Generated for every
keyed template, independent of whether anything about the key can be analysed.
_Avoid_: "the key", "key record" (a key may be a tuple or a bare `Party`)

**Key expression**:
The Daml-side projection from a template payload to a key value. It exists in the Daml-LF
archive and is deliberately **not** carried in the Intermediate AST: representing it means
representing arbitrary value construction, and a partial representation yields a silently
wrong key in a published package. Its absence is a decision, not a gap. It is never inlined
in the archive — the compiler lifts it to a generated top-level value that the template body
applies — so reading one is evaluation, not field access.
_Avoid_: "key projection" unqualified, "the key function", "a record of field projections"

**Supported input**:
The set of Daml-LF versions a released codegen accepts. Supported means
**proven by a fixture exercised in CI** — not merely claimed, and not merely emittable by
some compiler. A fixture proves more than one thing and they are counted separately. The
version its target names is proven on the **emit** path *and* on the **read** path, because
the main package is decoded at that version — so 2.2 and 2.3 are read-proven by the main
packages of `defaulttarget` and `contractkeys`, and would move with them if either were
retargeted. The versions its dependency packages carry are proven on the **read** path only.
At the pinned SDK the per-module component packages behind `daml-prim` and `daml-stdlib` are
Daml-LF 2.1 and travel unchanged inside every DAR that toolchain builds whatever its target,
so 2.1 alone is read-proven by every fixture in the tree and no retarget can remove it. A
version covered on neither path is a gap, and is named as one.
_Avoid_: "the supported envelope" (jargon; say what it is), "supported SDK version" (the SDK
range and the Daml-LF range no longer coincide and must be stated separately), reading a
fixture's target as the whole of what that fixture proves

**Default Daml-LF version**:
Daml-LF **2.2** — what the compiler emits when a project sets no target, on both the 3.4 and
3.5 lines, silently and with no diagnostic. Project scaffolding does not write a target
either, so this is the version an ordinary consumer produces without deciding to. The
`defaulttarget` conformance fixture pins no target for exactly this reason, so the default is
covered on both the read and the emit path.
_Avoid_: treating 2.1 as "the normal case", "LF 2.x" as a single accepted range (the JVM read
path enumerates versions; the .NET path does not)

**Maintainers**:
The parties responsible for a contract key. Declared in Daml as a function *from the key
type* to a list of parties — never from the payload — so a maintainer analysis is a party
projection whose **projection source** is the key.
_Avoid_: "key owners", "key signatories"

**Projection source**:
The binder a static party projection reads from: the template payload (`signatory`,
`observer`), the contract key (`maintainer`), or a choice argument (choice-level
`controller` / `observer`). Carried on every party-analysis verdict, because a projection is
only meaningful against the binder it was written against. A projection whose source cannot
be determined is `Dynamic`.
_Avoid_: "the binder" alone, "field owner"

### Ledger client domain

Terms specific to the `Canton.Ledger.*` client. `Active contract` and `Contract key` are shared
with the codegen terms above and are not repeated here.

**Transport**:
The Ledger API a client speaks to its participant: gRPC (`Canton.Ledger.Grpc.Client`) or JSON
(`Canton.Ledger.Rest.Client`). Both serve the same `ICantonLedgerClient` surface, so
application code does not change with the transport.
_Avoid_: Backend, channel, protocol

**Template**:
A concrete Daml contract type. Templates can be created on-ledger and carry a template id.
_Avoid_: Contract type, class

**Interface**:
A Daml abstraction that templates implement. An interface is never created directly; a
contract is observed *through* it.
_Avoid_: Trait, abstract template

**Interface view**:
The participant-computed projection of a contract seen through a given interface. Present on a
created event only when the request asked for it.
_Avoid_: Interface projection, view record

**Act-as**:
A party whose authorization a submission asserts (the party that signs).
_Avoid_: Submitter, signatory party

**Read-as**:
A party whose contract visibility a submission reads in, without asserting its authorization.
_Avoid_: Observer party, reader

**Submitter info**:
The act-as and read-as party sets one submission or subscription carries (`SubmitterInfo`).
A subscription's visibility is the union of those parties. A single `Party` converts to it as
the sole act-as party.
_Avoid_: Credentials, identity

**User**:
A participant-local identity that a bearer token authenticates as. It holds user rights —
act-as, read-as and execute-as over parties or any party, participant admin, identity-provider
admin — and those rights, not the token, decide what a call may do. A party lives on the
ledger; a user lives only on its participant.
_Avoid_: Account, party

**Execute-as**:
A user right to prepare and execute submissions as a party without reading as it. Act-as
implies it.
_Avoid_: Submit-only right

**Token provider**:
The source of the bearer token a transport attaches to each call (`ITokenProvider`): a static
token, or an OAuth2 client-credentials grant.
_Avoid_: Auth handler, credential store

**Command id**:
The deduplication identifier of a submission (`CommandId`): the participant treats a second
submission with the same command id from the same user and act-as parties within the
deduplication period as a duplicate. A retry after an unknown outcome resubmits with the same
command id. Distinct from a workflow id, which only correlates and is never deduplicated.
_Avoid_: Request id, correlation id

**Completion**:
The participant's verdict on one submitted command — accepted with its update id, or rejected
with a status — delivered on the completion stream to the parties that submitted it. It is how
a fire-and-forget `SubmitAsync` learns what happened.
_Avoid_: Receipt, acknowledgement

**Exercise outcome**:
The result a `Try*` write returns (`ExerciseOutcome<T>`) instead of throwing: `One` value,
`None` or `Many` where exactly one was expected, a structured `DamlError`, an `InfraError` from
the transport, or `CommittedUndecodable` — the command committed but the response could not be
decoded, so the caller reads the transaction by its update id and never resubmits.
_Avoid_: Result, response

**Exercise-result projection**:
Reading a committed transaction to find the exercise of one choice on one contract — matched
through the template or the interface it was exercised on — and decoding that choice's result
into an **Exercise outcome**. A result that fails to decode is `CommittedUndecodable`; an
exercise of the same choice on another contract in the same transaction is not a match. The
contract ids it reports are the choice's **returned contracts** — those its return value
names — never inferred from the contracts the transaction created, which depend on the
submitter's stakeholder rights and may include contracts the choice did not return.
_Avoid_: Projector (that names one generated method, not the concept), result extraction

**Generated type registry**:
The process-wide table (`GeneratedTypeReaders`) that maps a Daml identifier to the generated
code that decodes it — a template's create-argument reader, its key descriptor, and the choice
descriptors of a template or interface. Each generated package fills it from its own module
initializer; nothing scans for types, so a hand-written type is never in it. A lookup tries the
exact package id, then the one type declaring the same module and entity, and answers
`Resolved`, `Missing` or `Ambiguous`. The JSON transport decodes payloads through it, and a
hand-built exercise finds its choice's result decoder there. When the JSON transport finds
no single generated type for a node, it carries that node's payload as an **Undecoded value**
instead of failing the transaction.
_Avoid_: Type resolver (that names the codegen's `DarCrossPackageResolver`), type index, assembly scan

**Undecoded value**:
A Daml value read over the JSON Ledger API whose type has no single generated binding in the
process, carried as the Daml-LF JSON the participant sent (`DamlUndecodedJson`). A created
node keeps an empty payload record and holds the JSON in `UndecodedPayload`; an exercised
node's argument and result are the value itself. The target type's own JSON reader decodes it
later, through `FromDamlValue` or `ProjectChoiceResult`, once the binding is loaded.
_Avoid_: Unknown value, raw value

**Commit state**:
Whether a failed call's command reached the ledger (`CommitState`): `NotCommitted` (retry
freely), `Committed` (never resubmit) or `Unknown` (retry only with the same command id). It
follows the kind of call: a failed read is always `NotCommitted`, and a write the participant
may have received without answering is `Unknown`. A
participant rejection of `DUPLICATE_COMMAND` is `Committed` (the ledger already accepted that
command id; `Unknown` when its `accepted` metadata is `"false"`), and `SUBMISSION_ALREADY_IN_FLIGHT`
is `Unknown`; the error id is checked before the category.
_Avoid_: Retryable flag, success flag

**Failure contract**:
How any ledger call reports that it did not succeed, the same on either transport. A throwing
call raises one exception kind (`LedgerOperationException`) carrying the transport status
(`Grpc`, `Http`, `NoResponse` or `UndecodableBody`), the error category and the commit state.
A `Try*` call returns an exercise outcome. A stream ends with a terminal stream error.
Caller cancellation is not a failure; it stays a cancellation. A caller error (a bad argument, a
value that cannot be encoded) is outside the contract.
_Avoid_: "error mode", "exception contract"

**Disclosed contract**:
A contract attached to a submission as its created-event blob, so a party that does not see it
natively can still use it. The blob comes from a created event read by a party that does see it: by contract id with
`GetDisclosureAsync`, or from an active-contract snapshot opened to include it.
_Avoid_: Shared contract, attached contract

**Interactive submission**:
Submitting in three steps: the participant prepares the commands (interprets and hashes them
without executing), the acting parties sign the hash outside the participant, and the
participant executes the signed submission. The prepared transaction is opaque to the SDK; a
signer that does not trust the preparing participant decodes it before signing.
_Avoid_: Two-phase submit, offline submission

**External party**:
A party whose signing key is held outside every participant. It is onboarded by signing its own
topology transactions, and it acts only through interactive submission.
_Avoid_: Wallet party, self-custody party

**Vetting**:
A participant's declaration that it will use a package on a synchronizer. A transaction can use
only packages every involved participant has vetted.
_Avoid_: Package approval, package upload

**Participant Query Store** (PQS):
A PostgreSQL projection of a participant's ledger that `IPqsClient` queries for active
contracts. It is fed from the participant's update stream, so it trails the ledger.
_Avoid_: Read replica, ACS database, query store unqualified

**Unclassified event**:
A ledger event the participant delivered on a typed stream that the client could not attribute
to the requested template or interface — surfaced to the consumer rather than dropped.
_Avoid_: Dropped event, unmatched event, unknown event

**No-silent-drop invariant**:
The guarantee that every delivered event is either classified to the requested type or
surfaced as an unclassified event — the client never silently discards one.
_Avoid_: Lossless streaming, best-effort delivery

**Synchronizer**:
A Canton component that sequences and mediates transactions between participant nodes. A
participant may be connected to several at once, and every transaction commits on exactly one.
_Avoid_: Domain, sequencer, mediator

**Reassignment**:
The movement of a contract from one synchronizer to another — unassigned on the source,
assigned on the target — observed as paired stream events.
_Avoid_: Transfer, migration, move

### Ledger client streams

**Ledger offset**:
A position in the participant's update stream (`LedgerOffset`); `LedgerOffset.Begin` precedes
every update. A subscription starts strictly after its start offset and, when given an end
offset, delivers the update at it and completes.
_Avoid_: Sequence number, raw offset

**Active-contract-set snapshot**:
The active contracts the subscribing parties are stakeholders of at one offset, streamed by
`SubscribeActiveAsync` and closed by exactly one terminal checkpoint — emitted even when the
snapshot is empty — or by a stream error when the transport fails mid-snapshot.
_Avoid_: ACS dump, contract query

**ACS-delta stream**:
The stakeholder-based update stream `SubscribeAsync` serves: creates, archives and
reassignments of the subscribing parties' contracts, never exercises. It shares its visibility
basis with the active-contract-set snapshot, so a snapshot followed by a resume from its
terminal checkpoint rebuilds exactly the snapshot's contracts plus every later change.
_Avoid_: Transaction stream, flat stream

**Ledger-effects stream**:
The witness-based update stream `SubscribeLedgerEffectsAsync` serves: every event the
subscribing parties witnessed, exercises included. Its visibility basis differs from the
snapshot's, so a stakeholder resume does not resume it.
_Avoid_: Tree stream, transaction-tree stream

**Stakeholder resume**:
The resume ticket (`StakeholderResume`) an active-contract-set snapshot's terminal checkpoint
hands back. Only the ACS-delta stream accepts it; its raw offset stays reachable for a
deliberate cross-basis resume.
_Avoid_: Snapshot offset, resume token

**Pruned offset**:
The offset up to which the participant has deleted its history (`PrunedOffsets`);
`LedgerOffset.Begin` means nothing is pruned. A subscription starting before it fails, so a
consumer whose resume offset has fallen behind it rebuilds from an active-contract-set
snapshot.
_Avoid_: Retention horizon, ledger begin

### Ledger client transport behaviour

**Pagination loop**:
The JSON transport's way of consuming a ledger stream — repeated bounded windows, each
re-POSTed from the last offset observed, stitched into one continuous typed event stream that
is indistinguishable at the call site from a gRPC subscription. An end offset is a termination
condition on the loop, not a second code path, so one loop serves an open-ended tail and a
bounded range alike.
_Avoid_: Polling loop, long-poll, websocket stream

**Resume offset**:
The offset a consumer hands back to restart a stream where it left off. Always an *update*
offset — the offset of the containing transaction or reassignment. An event carries that same
offset; its position inside the update is the node id, which is never a resume offset. A
decode failure therefore resumes at the containing update, and the consumer must be idempotent
across that whole update rather than across the one event that failed.
_Avoid_: Event offset, per-event offset, resume point

**Offset checkpoint**:
A resume offset the participant emits into a stream on its own, with no event attached, so a
consumer following a quiet ledger still learns how far it has read. The participant bounds the
gap between two of them by the emission delay it advertises in its version features. Both
transports relay them and neither manufactures one: a stream that yields no event and no
offset checkpoint has told the consumer nothing new, and the consumer's resume offset stands.
_Avoid_: Heartbeat, keep-alive, synthetic checkpoint

**Completion window**:
One bounded completions response on the JSON transport, closed by the participant on its entry
limit or its idle timeout. It is the unit the pagination loop consumes, not something a
consumer sees: the enumeration a consumer holds may end at any time, possibly having yielded
nothing, and a caller that wants to keep following reopens from the highest offset it has
observed and supplies its own backoff. The gRPC live tail and the JSON loop both satisfy that
contract by not ending on their own — the loop reopens the next window from the last offset it
observed, so a window closing is invisible to the consumer. The same unit read from
`/v2/updates` is an update window.
_Avoid_: Completion stream, live tail

**Update window**:
One bounded updates response on the JSON transport, closed by the participant on its entry
limit or its idle timeout — the completion window's counterpart on `/v2/updates`. Both
endpoints window the same way, which is why one pagination loop serves both.
_Avoid_: Update page, update batch

**Documented default**:
A value the client supplies for an absent field because the served document states what its
absence means — the empty ledger end read from an offsetless response, or an omitted Optional
record field read as None. The participant did say it, by omission, so decoding it is reading
rather than guessing.
_Avoid_: Assumed default, implicit value, sensible fallback

**Fabricated value**:
A value the client invents where the participant gave it none and nothing documents what
absence means — an unparseable offset becoming a stream restart, a rejected empty identifier
becoming a default instance. Always a defect: it converts a decode failure into confident
wrong data, and the consumer cannot tell the difference.
_Avoid_: Fallback, default, best-effort value

**Participant error**:
A failure the participant reported itself — it received the request, decided, and answered
with an error it had classified. The client learns both that the operation failed and what
class of failure it was.
_Avoid_: Server error, ledger error, backend failure

**Redacted error**:
A participant error whose classification the participant withheld on purpose because the
failure is security-sensitive: the envelope arrives with a placeholder in place of the error
code, a fixed cause and no category. The client learns the transport status and recovers the
coarse class from it, so an invalid token stays distinguishable from a dead network. The
placeholder is never read as an error id; a redacted error is an unstructured failure, on both
transports.
_Avoid_: NA error, unknown error, redacted Daml error

**Transport fault**:
A failure of the connection carrying a request or stream, where the participant's answer never
arrived — so nothing is known about whether the participant acted at all. The JSON transport
reports one, a deadline overrun included, with the `NoResponse` status.
_Avoid_: Network error, connection error, RPC failure

**Decode failure**:
A payload the participant delivered and the client could not read — the transport worked and
the message arrived, but it violates the wire contract the client decodes against. Distinct
from a transport fault, which delivered nothing, and from a participant error, which the
participant chose to send. A throwing call reports it with the `UndecodableBody` status, and a
`Try*` write the participant applied reports it as `CommittedUndecodable`.
_Avoid_: Parse error, deserialization error, bad response

**Typed decode**:
Reading a wire value with the Daml type in scope, so the value is resolved against what the
template declares rather than inferred from the JSON alone. The untyped read is strictly
weaker and is what remains when no type is available — an exercise result on a stream, where
the choice name is known but its generated CLR return type is not. Where a type is in scope and
goes unused, an empty record and a Unit are indistinguishable, and the client answers
confidently with the wrong one.
_Avoid_: Deserialization, generic decode, dynamic decode

**Raw-JSON preservation**:
Keeping the participant's JSON value — its shape intact, not yet decoded against a Daml type —
because the type needed to interpret it is not in scope when the value arrives. The converter
that cannot know the template keeps the value; the later pass that knows it decodes. A
top-level JSON null is the exception: it is not kept, and reaches the later pass as an absent
value rather than one to decode. What survives is the value, not its lexical form: nothing may
rely on it for byte-identical forwarding or hashing. Not caution, and not deferral: it is the
only correct move when the information required to decode has not arrived, and the alternative
is to guess a shape and call it a value.
_Avoid_: Lazy parsing, deferred deserialization, raw passthrough

### Ledger client spec supply chain

**Annotation patch**:
A reviewable diff layering only the client's metadata — HTTP bindings and generation-enabling
options — onto pristine upstream protos, making the JSON API spec generatable. Patches retire
wholesale when upstream ships annotated protos, whoever authors them.
_Avoid_: Proto fix, fork patch, spec hack

**Vendored spec**:
The committed OpenAPI document generated from patched protos at the pinned Canton version — the
hashable input the JSON client is generated from. It describes what the client generates, never
what the participant does.
_Avoid_: Fork spec, shipped spec, tapir spec

**Served document**:
The OpenAPI document the participant itself publishes at the Canton version the client runs
against — the authority on what an operation accepts, returns, requires or refuses. Where it
and the vendored spec disagree, the disagreement is a delta to adapt, never a fact about the
ledger.
_Avoid_: Tapir spec, real spec, the spec

**Off-spec endpoint**:
A route the JSON Ledger API serves that cannot derive from the annotated protos, so the
generator can never emit a client for it — reaching one always takes hand-written code.
_Avoid_: JSON-only endpoint, custom endpoint, extra endpoint

**Drift check**:
The proof that a committed artifact still re-derives byte-identically from its pinned inputs.
Red means an output or an input changed without the other regenerating — it guards the chain,
it never repairs it.
_Avoid_: Determinism check, regen gate, golden test

**Adaptation transform**:
A named piece of runtime code compensating for one specific mismatch between the server's wire
reality and the spec-derived contract. Each retires individually when its conformance test
passes without it.
_Avoid_: Shim, workaround, band-aid

**Conformance test** (ledger client sense):
A test bound to one adaptation transform, asserting that the raw server wire shape maps to the
adapted shape. It is the retirement instrument: when it passes without the transform, the
transform retires.
_Avoid_: Contract test, wire test

**Conformance corpus**:
The Daml packages that state, executably, which type shapes and Daml-LF versions the codegen
claims to emit. Shipped as the `Daml.Codegen.Testing.Conformance` package, carrying the
compiled types together with the DAR they were generated from, so the two cannot drift apart.
Its Daml-LF target and SDK version are the corpus's own: the ledger client's tests name
neither, and a question about which version a package carries is answered by the corpus.
Despite the shared word it
is not a conformance test and is not built from them: a corpus is what a test runs against, and
one corpus serves the offline emitter checks here and the live round-trips in the client.
_Avoid_: Test fixture, testdata, richtypes (name the package, not one member)

**Conformance kit**:
The abstract xUnit suite (`LedgerClientConformanceTests<TProbe>`, shipped as
`Daml.Ledger.Abstractions.Testing.Conformance`) that an `ILedgerClient` implementation subclasses
to prove it keeps the behavioural contract. Each stream check runs once against the template
reads and once against the interface reads, so the probe the subclass supplies must implement the
kit's `IConformanceProbe`. Distinct from the **Conformance corpus**, which is the Daml model the
codegen is checked against.
_Avoid_: Parity suite, contract tests

**Integration suite**:
The every-endpoint proof — tests driving the generated client against a live LocalNet across
the whole spec surface. Conformance tests live inside it, but it is broader than they are.
_Avoid_: E2e tests, smoke tests

### Ledger client package surface

**Nameable surface**:
The set of types a consumer must be able to write down in their own code — the
transport-neutral interfaces, the `Add*` registration extensions, options and payload types,
and the test doubles they construct by hand. A type belongs to the nameable surface only when a
consumer must name it; that necessity, not the type's role, is what makes it public.
_Avoid_: Public API, exported types, the surface

**Wired implementation**:
A client a consumer receives through dependency injection and never names — resolved as its
interface, constructed by the registration extension. It is internal by default, because
nothing outside the package needs to spell it.
_Avoid_: Concrete client, implementation class, the impl

**Test double**:
A hand-constructed stand-in a consumer instantiates directly in their own tests. It is nameable
by definition, which is why it stays public while the wired implementation it stands in for
does not.
_Avoid_: Fake, mock, stub

### Releasing

**Public mirror**:
The public repository. It receives source through promotion but authors its own `.github/`.
_Avoid_: Public repo, OSS twin

**Promote**:
Copy the `.gitpublic`-listed set from a release-tagged `dev` commit into a pull request on the
public mirror.
_Avoid_: Publish, sync, dry-run promote

**Release tag**:
The `v<Version>` pair. The internal tag marks which `dev` commit went out and starts
promotion; anyone may create it, and only the owner moves it (ruleset bypass) to a
fix-up commit before release. The public tag is placed automatically on the
first green `main` commit carrying an untagged `<Version>`; it ships and never moves.
_Avoid_: Stage, re-bless

**Hold**:
The `hold-release` label on an open public pull request. While any such pull request is
open, no public release tag is placed.
_Avoid_: Freeze, block

**Internal entry**:
A CHANGELOG entry under an `### Internal` heading. It is never promoted to the public
CHANGELOG.
_Avoid_: Private entry

## Example dialogue

> **Dev**: Where does the JVM dependency go? I thought consumers shouldn't need a JDK.
>
> **Domain expert**: They don't. The JVM helper only runs at codegen time — when you
> regenerate against a new DAR. The generated `.cs` is plain .NET; once it lands in your
> repo (or a NuGet package generated from a DAR), the JVM is gone from the picture.
>
> **Dev**: So if Splice ships a patch release, do we have to regenerate?
>
> **Domain expert**: Only if the patch changed a signatory, observer or controller expression.
> The default full decode reads those expressions, so such a patch changes the generated
> code. Schema-mode decode is patch-version-insensitive — the Intermediate AST is identical,
> so the C# emitter produces byte-identical output — at the cost of every party analysis
> being `Dynamic`.
>
> **Dev**: And if DA renames an internal `PackageSignature` case class in a `daml-lf-archive`
> release?
>
> **Domain expert**: Only the JVM helper has to change. The Intermediate AST stays stable;
> the C# emitter doesn't notice.
