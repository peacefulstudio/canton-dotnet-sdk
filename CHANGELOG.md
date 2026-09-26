# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog tracks the packages published from this repo together,
because they are versioned in lockstep:

- `Daml.Codegen.CSharp` — C# emitter library (NuGet package)
- `Daml.Codegen.Intermediate` — intermediate DAR contract (protobuf types + shared Daml model)
- `Daml.Runtime` — runtime types referenced by generated code
- `Daml.Ledger.Abstractions` — transport-agnostic ledger client contract
  (`ILedgerClient`, composed of `ILedgerWriter`/`ILedgerReader`/`ILedgerStreamer`)
- `Daml.Ledger.Abstractions.Testing.Conformance` — behavioral conformance test kit
  for `ILedgerClient` implementations
- `Daml.Codegen.Testing.Conformance` — compiled conformance corpus types + embedded DAR

> **Versioning and stability.** This project is pre-1.0: under SemVer 0.x, any
> release may change the public API without a major-version bump. The first
> release published to NuGet.org is `0.1.8-preview.1`; the `0.1.0`–`0.1.7`
> sections below record internal development milestones whose packages were
> never published to a public feed.

## [Unreleased]

### Added

- CI: `daml-dar-to-proto-<version>.jar`, `intermediate_dar-<version>.proto` and a `SHA256SUMS`
  checksum file are now attached to the draft release a `v*` tag push creates, and the
  assembled jar's `--version` now reports the release version instead of the `0.0.0-dev`
  placeholder every prior release shipped with (#49).

### Changed

### Deprecated

### Removed

### Fixed

### Security

## [0.5.0-preview.3] — 2026-09-18

### Added

- Generated records, templates, variants and enums now decode Daml-LF JSON through emitted
  readers. Choice arguments, choice results and contract keys support instantiated generic
  records and variants, lists, optionals, tuples and other Daml generic shapes without
  reflecting over their CLR types.
- Generated interface markers expose typed choice descriptors and raw JSON readers, so a
  ledger client can decode an interface-owned exercise argument and result using the same
  descriptors as template choices. `GeneratedTypeReaders` provides registration helpers
  for clients that discover generated types at startup; discovery may still use reflection.
- The conformance packages include generic choice results, tuple and optional tuple keys,
  and a keyed template implementing an interface with a non-Unit choice.

### Changed — BREAKING

- Regenerate bindings when upgrading: `IDamlRecord<TSelf>` now requires
  `__ReadDamlLfJson(JsonElement, DamlLfJsonDecodeContext)`. Hand-written record implementers
  must provide that member. Hand-built `Choice<>` descriptors require an argument decoder
  and argument/result JSON readers; hand-built `KeyDescriptor<>` instances require a key
  JSON reader.
- Interface-choice async methods now return `ExerciseOutcome<TResult>` for the choice's
  declared result, replacing `ExerciseOutcome<TransactionResult>`. Update outcome switches
  and remove manual result decoding. `Choice<>` and `IExercises<>` now accept interface
  owners as well as templates.
- `ExerciseOutcome<T>` adds `CommittedUndecodable`: the command committed, but its response
  could not be decoded. Update exhaustive switches and do not resubmit a committed command.
  `LedgerOperationException.CommitState` distinguishes `NotCommitted`, `Committed` and
  `Unknown`; retry an unknown submission only with its original command ID. Downstream
  throwing wrappers can use `CommittedWithoutDetail` for committed `None`/`Many` outcomes.
- `Optional<>`, `Either<>` and the contract/interface stream event unions now round-trip
  through `System.Text.Json` using a `$case` discriminator. This CLR serialization format
  is separate from Daml-LF wire JSON. `Unit` also round-trips, and `Set<>`, `Map<,>` and
  `NonEmpty<>` reject null elements rather than retaining invalid values.
- Generated and constrained record decoders now count each flat, present `Optional`'s
  carried value as an additional nesting level, so a recursive record containing an
  `Optional` child reaches the 128-level depth limit at 64 records rather than 128. An
  absent `Optional` adds no level. The obsolete `Type`-keyed overloads keep the old,
  uncounted behavior.
- `DiscriminatedUnionJson.Write` now also enforces a fixed `MaxSafeWriteDepth` of 20,
  independent of any caller-configured `JsonSerializerOptions.MaxDepth`: the depth actually
  enforced is `Math.Min(configuredMaxDepth, 20)`. A `MaxDepth` larger than 20 no longer opts
  into recursion deeper than 20 on this write path. This replaces the prior guarantee of no
  hardcoded cap below the configured `MaxDepth`.

### Deprecated

- Reflection-based `DamlLfJsonReader` and `DamlLfJsonDecoders` overloads are obsolete.
  Move payload decoding to generated readers and descriptor readers. The retained
  reflection overloads do not gain support for nested generated generic types, and this
  release does not establish AOT support.
- `ExerciseCommand.ForInterface<TInterface>` is obsolete. Call `ExerciseCommand.For<TOwner>`,
  which resolves the same identifier for templates and interface markers.

### Fixed

- Fix generated variant compilation when JSON reader helper names collide with generated
  type, constructor or payload names.
- Fix `RelTime` field metadata, nullable annotations on erased choice descriptors, and
  empty top-level list and map decoding through the retained reflection reader.
- On macOS x64 (`osx-x64`) only, writing a deeply-nested `Optional`, `Either`, or
  contract/interface stream-union value through `DiscriminatedUnionJson` could overflow the
  real call stack before its own depth guard tripped, instead of raising the expected
  `JsonException`. The guard now also checks `RuntimeHelpers.TryEnsureSufficientExecutionStack()`
  before recursing, so it trips before the native stack is exhausted on every platform and
  architecture.

## [0.5.0-preview.2] — 2026-09-08

### Changed — BREAKING

- **Every generated type moves: the C# namespace is now the Daml module name** (`Daml.Codegen.CSharp`,
  `Daml.Codegen.Testing.Conformance`, the `dpm codegen-cs` CLI). The package-derived namespace is gone:
  module `Splice.Ans` emits into `namespace Splice.Ans;` under `Splice/Ans/`, and a package with several
  modules emits one namespace per module (`splice-amulet` goes from one namespace to twelve) instead of
  flattening them into one. `-n`/`--namespace` (`CodeGenOptions.NamespacePrefix`) is now a prefix on the
  main package's modules only — `-n Acme.Ledger` + module `Splice.Amulet` → `Acme.Ledger.Splice.Amulet`,
  with the prefix elided when the module already starts with it (`Acme.Ledger.Token` stays
  `Acme.Ledger.Token`) — and dependency packages emitted with `--include-dependencies` keep their bare
  module names, so a locally generated dependency tree is interchangeable with the published bindings.
  The emit now fails, naming every module involved, when two or more modules would share a namespace or a
  namespace would spell an emitted type's fully qualified name. `ContractIdentifiers` is one class per
  module, written inside the module directory (`Generated/RichTypes/ContractIdentifiers.cs`, not
  `Generated/ContractIdentifiers.cs`). Two modules of one package declaring a same-named choice now emit
  two `<Choice>Result` records in two namespaces, a shape the flattened namespace could not express.

  Migration: rename `using` directives to the module names, which for the conformance package is a
  case-only change — `using Daml.Codegen.Testing.Conformance.Richtypes;` →
  `using Daml.Codegen.Testing.Conformance.RichTypes;` (`ContractKeys` and `DefaultTarget` likewise, and
  the `contractkeys` corpus's second module surfaces: `ScheduleKey` and `ScheduleView` now live in
  `Daml.Codegen.Testing.Conformance.KeyBuilders`) — and update any
  `<Compile Include … Link="Generated\Richtypes\…">` paths to the new directory case; add one `using` per
  module you consume from a multi-module package; drop any code that relied on `-n` prefixing a
  dependency. A single-segment module that declares a same-named type (`Iou:Iou`) yields `namespace Iou`
  containing `record Iou`, so a consumer writes `Iou.Iou` or binds an alias
  (`using IouContract = Iou.Iou;`). `-n` combined with `--include-dependencies` now compiles for the first
  time: cross-package references are spelled with the referent module's namespace, which is a namespace
  the run actually emits.
- **`CodeGenOptions.RootNamespace` is renamed `CodeGenOptions.NamespacePrefix`**
  (`Daml.Codegen.CSharp`). The option never named a root namespace: it is prepended to the main package's
  module namespaces, elided when a module name equals the prefix or starts with it at a segment boundary,
  and never applied to dependency packages. The old name also spelled MSBuild's own `RootNamespace`
  property, which means something else — a project's default namespace for newly added files and resource
  manifest names — inside the same toolchain. Note this was a vocabulary collision, not a wiring one: the
  MSBuild integration reads `$(DamlNamespace)` and never fell back to `$(RootNamespace)`, so a consumer's
  `<RootNamespace>` could not steer codegen under either name. Rename the assignment; the accepted values
  and the emitted output are unchanged. The rest of the surface keeps its names: the CLI still takes
  `-n`/`--namespace`, and the MSBuild integration still reads `$(DamlNamespace)`.
- **The list members of the event and stream records are now `EquatableArray<T>`**
  (`Daml.Runtime.Contracts`, `Daml.Runtime.Streams`): a `readonly struct`, declared in
  `Daml.Runtime.Contracts`, that owns a copy of its elements, compares by content, and whose `default` is
  the empty array. The members: on `CreatedEvent` — `WitnessParties`, `Signatories`, `Observers`;
  `ArchivedEvent` — `WitnessParties`; `ExercisedEvent` — `ActingParties`, `WitnessParties`,
  `CaughtExceptions`; `TransactionResult` — `CreatedContracts`, `ArchivedContractIds`, `ExercisedEvents`;
  `CreatedContract` — `WitnessParties`, `Signatories`, `Observers`, `InterfaceIds`; `TransactionTree` —
  `RootEvents`; `TreeEvent.Created` — `WitnessParties`, `Signatories`, `Observers`, `InterfaceIds`;
  `TreeEvent.Exercised` — `ActingParties`, `WitnessParties`, `ChildEvents`; and `WitnessParties` on every
  contract-bearing arm of `ContractStreamEvent<T>`, `InterfaceStreamEvent<TInterface, TView>`,
  `AcsSnapshotEntry<T>` and `InterfaceAcsSnapshotEntry<TInterface, TView>`. `CaughtException.Metadata`,
  `CommandsSubmission`, `DamlRecord` and the `Daml.Runtime.Stdlib` types are unchanged.

  Reads typed as `IReadOnlyList<T>` compile unchanged — the struct implements it, and each such
  conversion boxes; `[]` and `[a, b]` arguments compile unchanged too. A `List<T>` or array argument
  becomes `EquatableArray.Create(x)` (`[.. x]` also compiles, at the cost of a second copy):

  ```csharp
  new CreatedEvent(witnessParties: EquatableArray.Create(parties), …);
  ```

  A `null` test on one of these members — `is null`, `?? []` — no longer compiles, since there is no
  `null` to test for. Equality on those members is now content equality everywhere: `CreatedEvent`,
  `ArchivedEvent`, `TransactionTree`, `TreeEvent.Created`, `TreeEvent.Exercised` and the four stream
  families compared them by list identity, so two events built from equal but separately allocated lists
  were unequal and now compare equal; `ExercisedEvent`, `TransactionResult` and `CreatedContract` already
  compared by content and drop their hand-written `Equals`/`GetHashCode`. None of these members can hold
  `null` any more: the type has no `null` state, so neither a constructor argument nor a `with`
  expression can produce one. Reflection is the one place this shows up as a behaviour change rather than
  a type change — a `ConstructorInfo.Invoke` passing `null` for one of these parameters used to throw
  `ArgumentNullException` and now yields the empty array instead. Separately, `EquatableArray.Create` (and
  so a collection expression) throws `ArgumentException` on a `null` element when the element type is a
  reference type, so every reference-element member — `RootEvents`, `ChildEvents`, both `InterfaceIds`,
  `ArchivedContractIds`, `CreatedContracts`, `ExercisedEvents` and `CaughtExceptions` — never holds a
  `null` entry (the `Party` members were never at risk: `Party` is a struct).

  Because `ChildEvents` and `RootEvents` are recursive, `TreeEvent.Exercised` and `TransactionTree` now
  compare structurally over the whole subtree; `TreeEvent.Exercised.GetHashCode` hashes its children by
  count rather than by content, so computing a deep tree's hash — as a set member or dictionary key —
  costs the node, not its subtree, and cannot overflow the stack. Equality still recurses: comparing two
  distinct but structurally equal trees, including a set lookup that finds an equal-hashing entry,
  descends to their depth.
- **`ExerciseOutcome<T>.Many` is now `Many(EquatableArray<string> ContractIds)` and holds at least two
  ids** (`Daml.Runtime.Outcomes`). Three things about this one arm changed together. Its ids moved from
  `IReadOnlyList<string>` to `EquatableArray<string>`, finishing the migration above: this was the last
  list member on the outcome surface still carried as a raw `IReadOnlyList<string>` rather than a
  deliberate carve-out, and unlike its neighbours it never took a defensive copy and compared by list
  identity, so a producer could mutate the list after handing it over and the record silently changed
  with it; `Many` now owns a copy of its ids and compares them by content. The `Count` argument is gone:
  `Count` is a computed property returning `ContractIds.Count`. The two were independent members, so
  `new Many(5, ["00a"])` was representable and silently wrong — and the runtime's only user-facing use of
  a `Many` builds its exception message from `Count` alone, so a writer whose two members disagreed
  produced a message telling the user to go collect contract ids that are not there. And the arm now
  enforces the arity its name claims: `Many` means more than one candidate filled a slot that expected
  exactly one, so fewer than two ids throws `ArgumentException` — at construction and through a `with`
  expression alike. Zero candidates is what `None` says and one is what `One` says, so a `Many` below two
  was a meaningless value every consumer's `switch` had to defend against or silently mis-handle.

  Reads are unaffected: `many.Count` and `many.ContractIds` both compile unchanged, the struct implements
  `IReadOnlyList<string>`, and a `["00a", "00b"]` argument compiles unchanged too. A construction site
  drops its first argument — `new Many(ids.Count, ids)` becomes `new Many(EquatableArray.Create(ids))` —
  a `List<string>` or `string[]` argument becomes `EquatableArray.Create(x)`, a positional pattern or
  deconstruction now matches one element rather than two, `many with { Count = n }` no longer compiles
  because there is no longer a `Count` to set, and a `null` test on the member no longer compiles because
  there is no `null` to test for. A `null` *element* now fails at run time rather than being stored:
  `EquatableArray.Create` throws `ArgumentException` on one, so `new Many(["00a", null])` throws at
  construction where the old `new Many(2, new[] { "00a", null })` accepted and kept it. A producer that
  builds a `Many` from a list of matches branches on its count the way generated code does: none to
  `None`, one to `One`, more to `Many`. This reaches generated code — the emitter emits
  `EquatableArray.Create(...)` and the one-argument form at the `Many` construction site, and reaches
  that site only when more than one contract matched — so regenerate your bindings as part of this
  upgrade rather than after it.
- **`TransactionResultExtensions.All<T>()` now returns `EquatableArray<ContractId<T>>`**
  (`Daml.Runtime.Contracts`) instead of `IReadOnlyList<ContractId<T>>`. It already allocated and owned the
  array it handed back, and it sits directly beside `TransactionResult.CreatedContracts`, which moved to
  `EquatableArray<T>` above; the two agree again. Callers using `var`, `foreach`, the indexer, `Count` or
  LINQ compile unchanged, and the result still converts implicitly to `IReadOnlyList<ContractId<T>>`
  wherever one is expected — including as a declared variable type or an argument — so in practice this
  breaks only a caller who names the return type in a position that admits no conversion, such as an
  `override` or an interface implementation. As with the members above, a `null` test on the result —
  `tx.All<Iou>() ?? []`, `?.Count`, `is null` — no longer compiles, since there is no `null` to test for;
  `All<T>()` never returned one.
- **The create-by-exercise `*Many*` helpers now carry `EquatableArray<ContractId<TTemplate>>`**
  (`Daml.Ledger.Abstractions.Extensions`) instead of `IReadOnlyList<ContractId<TTemplate>>` — the success
  payload of `TryCreateManyByExerciseAsync`, and the return type of its throwing twin
  `CreateManyByExerciseAsync`. Both project through `TransactionResultExtensions.All<T>()`, which the
  entry above moved to `EquatableArray<ContractId<T>>`, so the old signatures boxed that array straight
  back into an interface on the way out: a caller got a value-comparing sequence from `All<T>()` and, from
  the helper feeding on it, one whose declared type compares by reference under `==`, with nothing at the
  call site to mark the difference. The two shapes agree again and the box is gone.

  Reads compile unchanged — `var`, `foreach`, the indexer, `Count`, LINQ — and the awaited result of
  `CreateManyByExerciseAsync` still converts implicitly to `IReadOnlyList<ContractId<TTemplate>>` wherever
  one is expected, including as a declared variable type or an argument. That one breaks only where the
  task is handed on un-awaited and `Task<T>`'s invariance admits no conversion: a variable or field typed
  `Task<IReadOnlyList<…>>`, or a member returning one straight through — an `override` or interface
  implementation included. An `async` member that `return await`s compiles unchanged.
  `TryCreateManyByExerciseAsync` is the sharper break, because the outcome type itself changed and
  `ExerciseOutcome<T>` is invariant: a `switch` arm, `is` pattern or declared variable naming
  `ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>` no longer compiles and becomes
  `ExerciseOutcome<EquatableArray<ContractId<TTemplate>>>`. The `None`, `Many`, `DamlError` and
  `InfraError` arms are unaffected in shape, and the outcome object stays a record class, so a `null` test
  on it compiles as before; it is the array — the `One` payload, and the awaited result of the throwing
  twin — that loses `?? []`, `?.Count` and `is null`, since there is no `null` to test for and `All<T>()`
  never handed one back. One change is silent, in code that still compiles: `==` on two of these arrays —
  the awaited result of `CreateManyByExerciseAsync`, or two `One` payloads — was box identity, so
  separately produced sequences were never equal, and it now compares content. `Equals` and a dictionary
  keyed on one of them are unaffected, and so is `==` on the outcome records themselves: all three already
  reached the boxed struct's own content equality.
- **A generated record with a `List`, `TextMap` or `GenMap` field now compares by content**
  (`Daml.Codegen.CSharp`, `Daml.Runtime`, and every binding regenerated from them). Such a record gains an
  emitted `Equals` / `GetHashCode` pair and a copying `init` accessor per collection member: lists compare
  element by element in order, maps key by key independently of insertion order, every other member
  through `EqualityComparer<T>.Default` exactly as the synthesized equality did, and each collection is
  copied at the constructor and at every `init` — so a producer that keeps the collection it passed can no
  longer change an already-computed hash. Until now the record-synthesized equality read those members by
  reference: two values decoded from the same ledger payload were unequal, a `with` expression handed the
  caller's live collection through untouched, and neither value was findable in a `HashSet` or as a
  dictionary key.

  The member types are unchanged — `List a` is still `IReadOnlyList<T>`, `TextMap a` and `GenMap` still
  `IReadOnlyDictionary<…>` — so no constructor signature or `Deconstruct` moves and no call site needs
  editing. Every property of such a record is now declared in the record body instead of being synthesized
  from its primary-constructor parameter, each carrying its own `DamlFieldAttribute` and XML-doc summary:
  that keeps the property metadata order `DamlLfJsonReader` reads a record's Daml field order from, and
  reflection over the attribute answers exactly as before. What changes is behaviour, in both directions:
  code that relied on two such records being unequal (a de-duplication pass keyed on reference identity, a
  cache expecting a miss, a test asserting `NotBe`) now sees them equal, and code that mutated a
  collection after handing it to a record no longer sees the record follow. The comparison and the copy
  live in the new public `Daml.Runtime.Data.DamlFieldCollections`, which the emitted members call and
  which hand-written code carrying the same shapes can call too. Migration: regenerate your bindings
  against this version, re-run any equality-sensitive assertions over generated types, and refresh any
  vendored generated tree or pinned output hash — the emitted tree moves for every package carrying such a
  field. This is the defect the runtime had already closed by hand on
  `ExerciseOutcome<T>.Many.ContractIds`, `CaughtException.Metadata`, `DamlTextMap.Values` and
  `ExerciseOutcome<T>.DamlError.Metadata`; those four sites are unchanged.
- **`ExerciseOutcome<T>.DamlError` now owns its `Metadata` and compares it by content**
  (`Daml.Runtime.Outcomes`), closing on the union's last remaining member the hole the `Many` entry above
  closed for `ContractIds`. The member type is unchanged — still `IReadOnlyDictionary<string, string>`, so
  every read compiles and behaves as before, `metadata["cid"]` included. What changes is what the value
  means: the record-synthesized equality compared the dictionary by reference, so two errors decoded from
  the same participant trailer were unequal and `==`, `Equals` and a dictionary lookup all disagreed with
  what a consumer reading the fields saw, and a producer that kept its own dictionary could change an
  outcome after handing it over. `DamlError` now copies the dictionary at construction and on `init` and
  compares it key by key, independently of insertion order, with a `GetHashCode` that agrees — matching
  what `CaughtException.Metadata` and `DamlTextMap` already do for the same shape. Four behaviour changes
  follow: two `DamlError` values with equal metadata now compare equal where they used to be unequal
  unless they shared the dictionary instance; `outcome.Metadata` is a copy, so it is no longer
  reference-equal to the dictionary you passed; `new DamlError(…, null!)` now throws
  `ArgumentNullException` naming `Metadata` instead of storing the `null`; and the copy uses the default
  comparer, so a producer that supplied a `StringComparer.OrdinalIgnoreCase` dictionary loses
  case-insensitive lookup — that normalization is what keeps equality symmetric, and both Canton parsers
  already build their metadata ordinal. Nothing on the wire moves — the outcome has no JSON converter and
  is not serialized.
- **`AddDamlConverters` now registers `EquatableArrayJsonConverterFactory` and requires the
  `EquatableArray<T>` constructor parameters of the types it serializes**
  (`Daml.Runtime.Serialization`). The migration above left the event and stream records write-only:
  `System.Text.Json` wrote their list members as JSON arrays because `EquatableArray<T>` implements
  `IReadOnlyList<T>`, but reading any of them back — an empty array included — threw
  `NotSupportedException`, so no migrated record round-tripped. The new converter reads and writes
  `EquatableArray<T>` through the caller's own options, so element converters still apply and a `Party`
  stays a bare string; a failure on any element is reported as a `JsonException` naming the index. A JSON
  `null` for one of these members is rejected rather than read as the empty array: the struct has no
  `null` state, so a wire `null` is a producer disagreement, not a value — write `[]`, or declare the
  member `EquatableArray<T>?`.

  **The breaking part is the omitted member**: a payload that simply left one out used to deserialize
  silently to an empty array, which reads as "the event named no witnesses" rather than "the producer sent
  no such field", and it now throws `JsonException` naming the parameter. No converter can close that — an
  absent property never reaches one — so a `JsonTypeInfo` modifier does it instead, marking required
  exactly those constructor parameters whose type is a closed `EquatableArray<T>`. Requiredness changes
  nowhere else: an ordinary nullable parameter that carries no default, such as
  `TransactionResult.CommandId` — which the Ledger API omits on transactions this participant did not
  submit — still reads as `null`, and so does a generated record's `Optional` field. The modifier composes
  onto whatever `TypeInfoResolver` the caller already installed, so a source-generated
  `JsonSerializerContext` survives. Members that are init-only properties rather than constructor
  parameters, such as `CreatedContract.InterfaceIds` and `TransactionResult.ExercisedEvents`, keep
  absent-means-empty, as does one declared `EquatableArray<T>?`. Callers building `JsonSerializerOptions`
  by hand pick the converter up by adding `DamlJsonConverters.All`, but only `AddDamlConverters` installs
  the modifier.
- **`EquatableArray<T>` carries its JSON converter in a type-level `[JsonConverter]` attribute**
  (`Daml.Runtime`). The struct now names `EquatableArrayJsonConverterFactory` the way `ContractId<T>`,
  `Party` and `SynchronizerId` already named theirs, and on the reflection path — the default — that is
  purely additive: every write is byte-identical to what it was, `[]` and `["Alice::122012ab"]` included,
  and `JsonSerializer.Deserialize<ArchivedEvent>(json)` on bare `JsonSerializerOptions` now converts the
  list instead of throwing the `NotSupportedException` the migration left behind there.

  **The breaking part is the source-generated path.** A type-level `[JsonConverter]` makes the
  `System.Text.Json` generator emit the factory for a member of this type rather than collection metadata,
  and the factory's converter converts elements through the reflection-based `JsonSerializer` overloads,
  which need a `JsonTypeInfo` the context no longer registers. So a `JsonSerializerContext` that wrote
  `record Holder(EquatableArray<long> Numbers)` as `{"Numbers":[1,2]}` now throws
  `JsonException: Cannot write element 0 of EquatableArray<Int64>: JsonTypeInfo metadata for type
  'System.Int64' was not provided by TypeInfoResolver`, and a consumer build that was clean now reports
  `IL2026` and `IL3050` from the generated context — an error rather than a warning wherever
  `TreatWarningsAsErrors` is set. Neither is earned by any change on the consumer's side. Migration:
  declare the element type on the context — `[JsonSerializable(typeof(long))]`, and one per element type
  actually serialized, which across the migrated event and stream records means `Party`, `string`,
  `Identifier`, `CreatedContract`, `ExercisedEvent`, `TreeEvent` and `CaughtException`. Both directions
  then work, which is a better position than this path held before, where the write worked and the read
  threw. `EquatableArray<T>`'s is the only converter here that recurses back through the caller's options
  for its elements, which is why `ContractId<T>`'s long-standing attribute never cost this. What the
  attribute does not replace is `AddDamlConverters`, still what supplies the rest of the posture: only the
  registered path refuses an omitted list rather than reading it as the empty array, and only it sets
  `RespectNullableAnnotations`. A converter on `JsonSerializerOptions.Converters` continues to take
  precedence over the attribute.
- **`LedgerOffset`, `CommandId`, `ChoiceName` and `WorkflowId` change their JSON shape** (`Daml.Runtime`,
  `Daml.Runtime.Commands`). Each wrote itself as an object wrapping its member — `{"Value":7}`,
  `{"Value":"cmd-1"}` — and could not read that object back: all four are `readonly record struct`s
  exposing only a get-only `Value`, and `System.Text.Json` prefers a struct's implicit parameterless
  constructor unless told otherwise, so it default-constructed them and dropped the value. Each now
  carries a `[JsonConverter]` of its own, also listed in `DamlJsonConverters.All`, and travels as the bare
  scalar the participant puts on the wire: `LedgerOffset` as a JSON number (`7`), the other three as JSON
  strings (`"cmd-1"`), joining `Party`, `SynchronizerId` and `ContractId<T>`. A consumer reading or
  writing these payloads outside the runtime updates the field shape; a consumer who only persisted and
  reloaded runtime types gets back what they wrote for the first time.

  The loss was silent for `LedgerOffset`, whose offset of `7` read back as `Begin` and restarted a resumed
  stream from the beginning of the ledger, and deferred for the three ids, whose read yielded a non-null
  value throwing `InvalidOperationException` on every `Value` access at the use site. A JSON `null` on a
  non-nullable member of any of the four is now a `JsonException` naming the type, and an offset that is
  negative, fractional, in exponent form, or outside `Int64` is refused rather than read as some other
  position; `WorkflowId` alone still accepts the blank id the Ledger API declares `workflow_id` may carry.
  Recovery needs the member to be present either way: a converter never runs for a property the payload
  omits, so an absent `CompletionOffset` still reads as `Begin`. No member, type or signature changed.

  With these four converters registered, `TransactionResult`, `SubmitAndWaitResult` and
  `StakeholderResume` round-trip to an equal value — `TransactionResult` with `DamlValueJsonConverter`
  added alongside for the `DamlRecord` payloads its created contracts carry, an older gap of the same
  kind: `DamlValue` is abstract and `DamlValueJsonConverter` is registered only inside
  `DamlJsonSerializer`'s own options rather than in `DamlJsonConverters.All`, so without it a record
  holding a `DamlRecord` writes its field values as `{}`; add it and `CreatedEvent` and `CreatedContract`
  round-trip too. Two gaps have no such workaround. `TreeEvent` is abstract and carries no
  `[JsonDerivedType]`, so a `TransactionTree` writes its root events as `{}`. And `ICommand` is a bare
  interface with no `[JsonDerivedType]`, so an `ExerciseCommand` — and a `CommandsSubmission` carrying
  one — still does not read back, whatever these converters recover from its `ChoiceName` and `WorkflowId`
  members. Both are instances of a wider gap that predates this release: a declared-abstract type writes
  only its base's members and cannot be read back.
- **A payload that omits a `LedgerOffset` member is refused on `AddDamlConverters` options**
  (`Daml.Runtime`). `LedgerOffset`'s default value is `Begin`, a real ledger position, so a document that
  never mentioned `CompletionOffset` read back as a transaction committed at the start of the ledger, and
  a `StakeholderResume` without its `Offset` resumed a stream from the beginning — a consumer could not
  tell omission apart from a participant that actually reported `Begin`. A `JsonSerializerOptions` built
  with `AddDamlConverters` now marks required every constructor parameter typed `LedgerOffset` that
  carries no C# default, alongside the `EquatableArray<T>` parameters it already required, so an absent
  one is a `JsonException` naming the member instead. `StakeholderResume` declares
  `[method: JsonConstructor]` for it: as a `readonly record struct` it was read through the implicit
  parameterless constructor, which left its `Offset` an init-only property no requiredness could reach.
  Migration: a consumer whose payloads legitimately omit an offset declares the member `LedgerOffset?`,
  the shape the stream events already carry for an offset a participant need not report, or gives the
  parameter a C# default; both stay optional, as do init-only `LedgerOffset` properties. Bare
  `JsonSerializer` options are unchanged — requiredness has never been reachable from the
  `[JsonConverter]` attribute alone — and the identity structs are deliberately left out:
  `default(CommandId)`, `default(ChoiceName)` and `default(WorkflowId)` throw on any access to `Value`, so
  an omitted id is already loud rather than silently plausible.
- **`Set<T>` (`Daml.Runtime`) reads and writes through `System.Text.Json` as a bare JSON array** —
  `["alice","bob"]`, and `[]` when empty — where it used to write the object
  `{"Elements":["alice","bob"],"Count":2}` and could read no object payload at all, its own write
  included: the `Set(IEnumerable<T> elements)` parameter binds to no property, because the
  `IReadOnlySet<T> Elements` beside it matches the name and not the type, so `System.Text.Json` refused
  the type with `InvalidOperationException` before reading a byte. The array is the shape
  `EquatableArray<T>` already travels as, and it is the CLR round trip that is this path's contract — the
  Daml-LF spelling `{"map":[["alice",{}]]}` stays the Daml-LF path's and is not what `System.Text.Json`
  owes. `Set<T>` names `SetJsonConverterFactory` in a `[JsonConverter]` attribute, so a host that never
  calls `AddDamlConverters()` gets the shape too; the factory also joins `DamlJsonConverters.All`.
  Elements are written in the order the set enumerates them, which for a set built from a sequence is the
  order of first occurrence in it, so the payload is reproducible.

  Migration: a payload persisted by an earlier version under the `{"Elements":…,"Count":…}` object is not
  read by this one — nothing ever read it, so re-writing it from the `Set<T>` in hand is the whole of the
  upgrade. A payload repeating an element collapses it, as set semantics require, so a read set can be
  smaller than the array that stated it. A payload that *omits* a `Set<T>` member is refused on
  `AddDamlConverters` options — see the entry below for that refusal and the annotation it depends on —
  and read as `null` on bare ones, because `RespectNullableAnnotations` refuses a stated `null` token, not
  an absent member, and the modifier that closes that gap rides on `AddDamlConverters`. The Ledger API
  path is untouched: `ToRecord`/`FromRecord` still produce and consume the `Map k Unit` record.
- **A payload that omits a non-nullable `Set<T>`, `Map<TKey, TValue>` or `NonEmpty<T>` member is now
  refused** (`Daml.Runtime`). `JsonSerializerOptions` built with `AddDamlConverters` now mark required
  every constructor parameter typed as one of those three Daml stdlib collections that is declared
  non-nullable and carries no C# default, alongside the `EquatableArray<T>` and `LedgerOffset` parameters
  they already required. An omitted member used to bind to `null` in silence even where the declared type
  forbids one, because `RespectNullableAnnotations` refuses a *stated* `null` token and never sees an
  absent member: `{"Name":"cold"}` read into `record Vault(string Name, Set<Party> Keyholders)` produced a
  `Vault` whose non-nullable `Keyholders` was null, and the failure surfaced as a `NullReferenceException`
  at the first use of it, arbitrarily far from the parse that caused it. It is now a `JsonException`
  naming `Keyholders` at the parse.

  Migration: a consumer whose payloads legitimately omit one of these collections declares the member
  nullable —

  ```csharp
  record Vault(string Name, Set<Party>? Keyholders);
  ```

  — the shape codegen already emits for a Daml `Optional` of one, and the only shape a stated `null` was
  ever read into — or gives the parameter a C# default; both stay optional, as do init-only properties of
  those types.

  **What the refusal does not reach.** Bare `JsonSerializer` options are unchanged: requiredness has never
  been reachable from the `[JsonConverter]` attribute alone, so a host that never calls
  `AddDamlConverters()` still reads an omitted member as `null`. The refusal also rides on the declaring
  type's nullable annotations, which a nullable-oblivious declaration does not carry: every parameter on
  one reads as nullable whether or not it was written `Set<T>?`, so none is marked required. Generated
  code emitted with `DamlNullable=false` (`--nullable false`), which omits the `#nullable enable` the
  emitter otherwise writes, and any hand-written type compiled under `#nullable disable`, therefore still
  bind an absent member to `null` exactly as before — the refusal reaches nullable-annotated declarations
  only. And the three collections are the whole of the change: an absent non-nullable `string`,
  `ContractId<T>` or host-declared reference-type parameter still binds to `null`, and whether the runtime
  should refuse those too is still open.
- **All four stream-failure records — `AcsSnapshotEntry<T>.StreamError`,
  `ContractStreamEvent<T>.StreamError`, `InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError` and
  `InterfaceStreamEvent<TInterface, TView>.StreamError` — gain a `string? ErrorId`**, the Canton built-in
  or Daml-defined error identifier the transport decoded from the participant's structured error, under
  the name `ExerciseOutcome<T>.DamlError.ErrorId` already carries on the write path — nullable here, where
  the write path's is not, because one stream arm covers both the structured and the unstructured fault.
  Until now a terminal stream fault arrived as a status code, a message and a category, and none of the
  three classifies it: `ContentionOnSharedResources` covers both `STALE_STREAM_AUTHORIZATION`, which
  Canton documents as resolved by reopening the stream, and
  `JSON_API_MAXIMUM_LIST_ELEMENTS_NUMBER_REACHED`, which reopening reproduces exactly, and both map to the
  same transport status (HTTP 409, gRPC `ABORTED`) — so a consumer deciding whether to retry had nothing
  but the participant's prose to match on. Read the new member as an identity rather than parsing it. It
  is `null` when the fault carried no structured error to decode; a transport that parsed none leaves it
  `null` rather than inventing a sentinel.

  **The parameter sits between `Category` and `SourceException`, not at the end**: `SourceException` stays
  last on all four records, and appending would leave a transport re-wrapping a stream fault positionally
  one silent bind away from dropping the id — the same reason `InfraError`'s own `Category` sits ahead of
  `SourceException` rather than after it. A `string` does not convert to `Exception?`, so
  `new StreamError(statusCode, message, category, exception)` fails to compile — pass the exception by
  name:

  ```csharp
  new StreamError(statusCode, message, category, SourceException: exception);
  ```

  A bare `null` in that fourth slot still compiles and now binds `ErrorId`, which costs nothing: it left
  `SourceException` null before and leaves both null now. `Deconstruct` moves from four parameters to
  five, so a positional pattern binding four elements no longer matches, and the generated record equality
  and `GetHashCode` weigh `ErrorId` too — two stream errors agreeing on status code, message, category and
  source exception are unequal when only one carries an id. The change is binary-breaking either way.
  `StreamerSnapshot.SnapshotAsync` forwards the id onto the `LedgerOperationException` it throws for a
  faulted snapshot, whose `ErrorId` previously only a write-path `DamlError` filled, so the code survives
  that rethrow instead of being dropped there; that exception's infrastructure constructor gains a
  trailing optional `errorId`, which every existing call site still binds without change. That path sets
  `ErrorId` with no `Metadata` beside it, so a non-null `ErrorId` no longer implies a non-null `Metadata`
  the way it did while only a `DamlError` set it — a catch site that read one off the strength of the
  other has to null-check it now. A transport that already parses the error id should now populate it.
- **`CodeGenOptions.UseRecordTypes` and `CodeGenOptions.UsePrimaryConstructors` are removed**
  (`Daml.Codegen.CSharp`). Both defaulted to `true`, and the emitter now always produces the positional
  `sealed record` shape — the only shape it ever produced compilable code for. Set to `false`,
  `UseRecordTypes` made `RecordEmitter` and the nested choice-argument path emit a record with no members
  whose own `ToRecord` / `FromRecord` still referenced them, so the generated source did not build; the
  template path only survived because it bolted `required` properties on afterwards. A caller deletes the
  two assignments: one who left the defaults gets byte-identical output, and one who set either to `false`
  has no replacement because there was never a working alternative shape. Zero-field records, templates
  and nested choice arguments keep their memberless form, which is selected by the field count and is
  unchanged. `UseFileScopedNamespaces` and `GenerateXmlDocs` are unaffected: each now carries a test
  compiling its `false`-path output.
- **`CreateClient()` on `LedgerClientConformanceTests<TProbe>` must now seed one archived `TProbe`**
  (`Daml.Ledger.Abstractions.Testing.Conformance`), at an offset no later than the one `GetLedgerEndAsync`
  returns, reaching the ACS-delta subscription as an `Archived` event and the ledger-effects subscription
  as a consuming `Exercised` event. `Acs_delta_subscription_never_yields_Exercised` and
  `Ledger_effects_subscription_never_yields_Archived` used to assert only that each shape omits the other
  shape's variant, so an implementation whose projector drops every archival event passed both vacuously —
  certified on the very axis those checks exist to convey, while conveying nothing. Each now also asserts
  the archival signal is present. Both stay always-on and the nine opt-in checks are untouched. Adopters
  already seeding a create plus its consuming archive on both streams within `(Begin, ledger end]` change
  nothing; the rest add that pair to their fixture. Only presence is checked, so the archived contract id
  need not match a `Created` earlier in the same window.

### Added

- **`DamlLfJsonReader.ReadValue` (`Daml.Runtime`): top-level LF-JSON decoding.** Four overloads —
  `ReadValue<T>(JsonElement, DamlJsonDeserializationLimits?)`, `ReadValue<T>(string, ...)` and the two
  `Type`-taking siblings — decode a top-level LF-JSON value against the Daml type it is expected to carry.
  The reader previously exposed only `ReadRecord`, so `exerciseResult`, `choiceArgument` and `contractKey`
  had no public entry point whenever the value at the top of the payload was not a generated record: a
  choice returning `Unit`, a variant, an enum, a scalar or a `ContractId` could be parsed but not decoded.
  `ReadValue` covers those and generated records alike, so a caller holding an unconstrained result type
  parameter — the shape a typed exercise has at its call site — reaches one entry point instead of
  branching on what the type turned out to be, which is why `T` carries no constraint. The limit guards
  match `ReadRecord` exactly: the shared hardened defaults apply when `limits` is omitted, the
  configuration is validated on every overload, and the two `string` overloads enforce the input-length
  bound before parsing.

  `ReadValue` refuses rather than guesses. A target outside the Daml type mapping throws
  `NotSupportedException` instead of falling through to a plausible-looking arm, because an unconstrained
  result type makes a wrong-but-decodable target easy to pass: a `string` requested against a
  `Party`-returning choice would otherwise decode as `DamlText` with no diagnostic at all. For the same
  reason a top-level enum must be a generated Daml enum, one carrying the companion the reader reads wire
  constructor names from, so an arbitrary CLR enum is refused rather than decoded against its own member
  names — reading an enum as a field of a generated record is unaffected. Top-level `Optional<T>`,
  `IReadOnlyList<T>`, `IReadOnlyDictionary<K, V>`, `Tuple2`/`Tuple3`, `Either`, `Map`, `Set` and `NonEmpty`
  are refused as a family, with a distinct message pointing at the same remedy: read them as a field of a
  generated record, or decode the value without the reader.
- `ContractStreamEventExtensions` (`Daml.Runtime`): `ToContract()` on the two contract-stream variants
  that carry a payload — `ContractStreamEvent<T>.Created` and `ContractStreamEvent<T>.Assigned`. The
  keyless overload pairs the row's contract ID with its decoded payload into `Contract<T>`; the keyed
  `ToContract<T, TKey>()` also decodes the row's wire-level `Key` through the template's `Key` witness and
  carries the ledger's hash of it, so a consumer reads `contract.Key.Value` and `contract.Key.Hash` off a
  live stream instead of hand-decoding `created.Key!.Value` and dropping the hash — including across a
  reassignment, which is why `Assigned` gets the hop too. A keyed projection of a row that carried no key
  throws `InvalidOperationException` rather than guessing. This is the stream twin of
  `AcsSnapshotEntryExtensions.ToContract`; the variants' raw `ContractKey? Key` slot is unchanged, and the
  remaining variants carry no payload to project.
- **`ActiveContract<TContract>` (`Daml.Runtime.Contracts`) pairs a contract with the provenance its
  active-contract-set snapshot row carried**: `LastUpdateOffset`, the offset of the update that last
  created or assigned the contract, and `SynchronizerId`. `TContract` is unconstrained, so a
  `Contract<T>`, a `Contract<T, TKey>` or a consumer's own interface-view pairing all wrap in it. The
  offset orders and identifies but does **not** resume — it may point at an already-pruned update, so a
  consumer persisting resume state still takes it from the snapshot's terminal checkpoint.
  `AcsSnapshotEntry<T>.Created.ToActiveContract()` (`Daml.Runtime.Streams`), keyless and keyed, and
  `SnapshotActiveAsync<T>()` / `SnapshotActiveAsync<T, TKey>()`
  (`Daml.Ledger.Abstractions.Extensions.StreamerSnapshot`) project a snapshot row, and a whole drained
  snapshot, into those carriers. `ToContract()` and `SnapshotAsync()` are unchanged and still discard the
  row's offset, synchronizer id and witness parties; their XML docs now say so and name the twin that
  keeps the first two.
- **`EquatableArray<T>` gains a read surface and a readable `ToString()`** (`Daml.Runtime.Contracts`): the
  zero-copy `AsSpan()`, plus `ToArray()`, `CopyTo(T[], int)`, `IsEmpty`, `Contains(T)` and `IndexOf(T)`.
  `ToString()` renders `[alice, bob]`, and `[]` for both an empty and a `default` array, so the 34
  migrated event and stream members no longer print as
  ``Daml.Runtime.Contracts.EquatableArray`1[Daml.Runtime.Data.Party]`` in assertion failures and logs. The
  struct also implements `ICollection<T>` and `IList<T>`: LINQ sizes `ToArray` and `ToList` up front
  instead of growing a buffer, `Count` reads the count instead of enumerating, and `Enumerable.Last`,
  `ElementAt` and `Skip` — which probe `IList<T>` and never `IReadOnlyList<T>` — index straight to the
  element rather than walking. Those interfaces' mutating members (`Insert`, `RemoveAt`, `Add`, `Remove`,
  `Clear`, the settable indexer) are implemented explicitly and throw `NotSupportedException`, and
  `IsReadOnly` is `true`, so the type's own surface stays read-only and a throwing member is unreachable
  without casting. `ToArray()` returns the shared `Array.Empty<T>()` for an empty or `default` array and a
  fresh copy otherwise, and `AsSpan()` on a `default` array yields an empty span rather than throwing.
  `Contains` and `IndexOf` use `EqualityComparer<T>.Default`, so they agree with the type's equality.
  `Enumerator.Reset()` is now an explicit `IEnumerator` implementation, matching `List<T>.Enumerator`: a
  `foreach` is unaffected, and reaching `Reset` requires casting to `IEnumerator`. Because `ChildEvents`
  and `RootEvents` are recursive, rendering an exercise node's children through the new `ToString()` would
  have walked its whole subtree on the call stack, and a deep tree overflows it — which kills the process
  rather than throwing. `TreeEvent.Exercised` therefore renders `ChildEvents.Count = n` in place of its
  children, so rendering a node costs the node exactly as hashing one does; every other member renders its
  elements.
- `NotSupportedLedgerClient` (`Daml.Ledger.Abstractions.Testing.Conformance`): an abstract `ILedgerClient`
  whose full 12-member surface throws `NotSupportedException` by default. A conformance fake written
  against `ILedgerClient` directly had to stub every member it did not exercise itself — up to 10
  `throw new NotSupportedException()` bodies per file — because the interface carries no
  partial-implementation base. A transport author's own conformance fakes derive from this and override
  only the behavior under test.

### Changed

- `CommandsSubmission` (`Daml.Runtime`) now compares its `Commands`, `ActAs`, `ReadAs` and
  `DisclosedContracts` lists element by element instead of by list identity, and hashes their contents.
  The record-synthesized equality compared the backing `IReadOnlyList<>` members by reference, which the
  defensive copies below would otherwise have narrowed to near-identity — no two submissions can share a
  list instance any more. Equality widens instead: two submissions holding separately-allocated but equal
  lists used to be unequal and now compare equal, so an assertion that two submissions differ only because
  their lists were allocated apart will flip. Two submissions built from one shared array stay equal, as
  they were before. An absent list stays distinct from an empty one. No member, type or signature changed,
  so this is not source- or binary-breaking.

### Fixed

- `CommandsSubmission` (`Daml.Runtime`) now copies the `Commands`, `ActAs`, `ReadAs` and
  `DisclosedContracts` lists it is given, at the primary constructor and at every `init` accessor — so
  through `with` expressions and the `WithActAs`/`WithReadAs`/`WithDisclosedContracts` builders too. The
  record stored the caller's array, so a caller that kept the array it passed could change a built
  submission afterwards, including the `ActAs`/`ReadAs` authorization data. `WithSubmitter` already
  copied, so the type was internally inconsistent. A `null` command list now throws
  `ArgumentNullException` at the boundary instead of failing later with an unattributed
  `NullReferenceException`; the three optional list slots still accept `null` as "carries none".
- `SubmitterInfo` (`Daml.Runtime`) now carries a `[JsonConverter]` of its own, so a record that holds one
  round-trips. `ActAs` and `ReadAs` are get-only properties backed by private fields, and
  `System.Text.Json` had neither a setter nor a `[JsonConstructor]` to assign through — a
  `[JsonConstructor]` was never available to it, because the constructor takes `IReadOnlySet<Party>`,
  which the serializer cannot instantiate. So it wrote `{"ActAs":["alice"],"ReadAs":["bob"]}` and read
  back `default(SubmitterInfo)`, whose every `ActAs` access throws `InvalidOperationException` far from
  the read that caused it — the same silent loss `LedgerOffset` and `CommandId` are fixed for above. No
  shipped call site puts a `SubmitterInfo` in a serialized graph, so nothing exercised the path; the type
  is closed here rather than when the first call site reaches it. The emitted shape is unchanged: the two
  party sets stay JSON arrays of the bare party strings `Party` itself travels as, named under whatever
  `JsonSerializerOptions.PropertyNamingPolicy` the surrounding record is serialized with. A payload that
  omits `ActAs`, or carries it as null, empty, or a non-array, is now a `JsonException` naming
  `SubmitterInfo` and the member, and writing a `default(SubmitterInfo)` raises that same `JsonException`
  rather than the `InvalidOperationException` the property accessor used to let escape. The converter
  joins `DamlJsonConverters.All` beside its siblings. No member or signature changed.
- **`System.Text.Json` is a CLR round-trip contract, not a wire decoder.** The packaged `Daml.Runtime`
  README and the `DamlJsonConverters` XML doc told consumers that `Party`, `ContractId<T>`,
  `SynchronizerId`, `CommandId`, `ChoiceName` and `WorkflowId` travel as bare JSON strings "in PQS rows
  and JSON Ledger API payloads … so plain `System.Text.Json` needs no setup", and showed
  `JsonSerializer.Deserialize<IouContract>(pqsRowJson)`. The bare-string shape is real, but the conclusion
  drawn from it is not: `System.Text.Json` agrees with the Daml-LF JSON encoding for those six scalars and
  for nothing above them, because the agreement tracks which converters have been written rather than a
  boundary in the design. A `Map<K,V>` reads back `{"Entries":[…],"Count":…}` and a `NonEmpty<T>`
  `{"Hd":…,"Tl":…,"All":…}` — each a faithful CLR round trip of the value, and neither the shape the
  Daml-LF wire carries — and a generated record whose Daml field name had to be escaped in C# is read
  under the C# name rather than the `[DamlField]` label the wire carries, so a consumer following the
  readme decoded a Canton payload wrongly, silently. Both documents now state the contract that holds:
  `System.Text.Json` is a CLR round trip — a value reads back as itself — and a ledger or PQS payload is
  read with `DamlLfJsonReader`, which is given the schema. The readme's PQS example is rewritten onto that
  reader. Documentation only; no behaviour, member or signature changed.
- `LedgerClientConformanceTests<TProbe>` (`Daml.Ledger.Abstractions.Testing.Conformance`) now holds the
  `StreamTimeout` budget on the nine checks that collect a stream even when the transport's enumerator
  ignores the enumeration token. The budget was applied through `WithCancellation`, which only reaches
  `GetAsyncEnumerator` and cannot interrupt an in-flight `MoveNextAsync`, so an implementation whose
  stream never terminates — ignoring `toOffset`, the token, or both — hung the adopter's run until their
  own runner's wall-clock kill, with no message, instead of failing inside the budget the kit's README
  promises. Each `MoveNextAsync` is now raced against one whole-stream deadline, so those checks fail with
  the contract-naming `TimeoutException`; on timeout the enumeration is cancelled and the abandoned
  enumerator is disposed in the background under the same budget. The cancellation check's losing drain
  task is observed and its timer cancelled, so a timed-out check no longer leaves an unobserved faulted
  task behind. `StreamTimeout` — still `protected virtual`, 30s by default — is now the real ceiling. No
  member, type or signature changed.
- The ten `cancellationToken` parameters across `ILedgerStreamer` and the `StreamerSnapshot` extensions
  (`Daml.Ledger.Abstractions`) now state that cancellation surfaces as an `OperationCanceledException`
  rather than a gracefully-completed stream, and each carries a matching `<exception>` tag. "Cancels the
  underlying stream cleanly" said neither, and the surrounding `remarks` — which establish that transport
  faults arrive in-band as a terminal `StreamError` — led a reader toward the graceful reading, under
  which a `foreach`-and-accumulate consumer would hand back a silently partial snapshot indistinguishable
  from a complete one. The conformance kit's cancellation check now fails an implementation that ends its
  stream gracefully on cancel, rather than timing out on it. Shipped XML documentation only; no behaviour,
  member or signature changed.
- `InterfaceStreamEvent<TInterface, TView>` (`Daml.Runtime`) now documents every variant to the depth its
  template sibling `ContractStreamEvent<T>` does. The two are the same union written twice, but only one
  carried the type-level explanation — the variant list, the table of which stream shape emits which
  variant, and the warning that an `Unclassified` event's `Offset` can be `null` and must not be persisted
  as a resume offset, because substituting `LedgerOffset.Begin` re-reads the whole stream. A consumer who
  filtered a subscription to an interface got a strictly worse interface for identical code. Where the
  interface family genuinely differs it now says so rather than repeating the template wording: the
  payload is the participant-computed view record, `TView` is bound to the marker by `IHasView`, and
  `UnclassifiedKind.InterfaceViewUnavailable` is the reason only this family can report. Shipped XML
  documentation only; no behaviour, member or signature changed.
- The CHANGELOG and README described the `daml-dar-to-proto` jar and its
  `intermediate_dar-<version>.proto` schema as assets attached to each GitHub release. No release has ever
  attached them. Both documents now state the present fact — the tool is packaged for standalone
  publication and its release assets are not published — and the README's usage recipes over the missing
  assets are removed. Documentation only; no behaviour, member or signature changed.

## [0.5.0-preview.1] — 2026-09-01

### Added

- `Daml.Codegen.Intermediate` — a new package carrying the intermediate DAR contract on its
  own: the C# types generated from `intermediate_dar.proto` plus the shared Daml model
  (`DarModel`, `DamlPackage`, `DamlTemplate`, `DamlType`, `PackageVersionParser`, …).
  Producers of the intermediate representation can now depend on the contract alone instead
  of on the whole emitter. Versioned in lockstep with the other packages.
- **Typed interface/view read surface** (`Daml.Runtime`, `Daml.Ledger.Abstractions`):
  `ViewDescriptor<TInterface, TView>` is a pure type witness pairing a Daml interface marker
  with its view record, exposed by every generated marker through a static `View` property.
  `InterfaceStreamEvent<TInterface, TView>` and `InterfaceAcsSnapshotEntry<TInterface, TView>`
  are the interface-family counterparts of `ContractStreamEvent<T>` and `AcsSnapshotEntry<T>`,
  carrying the interface's server-computed view record rather than the implementing template's
  own record. `ILedgerStreamer.SubscribeAsync`, `SubscribeActiveAsync` and
  `SubscribeLedgerEffectsAsync` gain interface-family overloads of the same name, each taking
  the witness as its first argument — `client.SubscribeAsync(IHolding.View, parties)` — so both
  type parameters are inferred from one argument. A mismatched marker/view pair does not
  compile. A matching event delivered without a view is surfaced as `Unclassified` with
  `UnclassifiedKind.InterfaceViewUnavailable`, unchanged.

  Two gaps ride with them, both deliberate. There is no interface-family counterpart to
  `StreamerSnapshot.SnapshotAsync<T>`: it materializes `IReadOnlyList<Contract<T>>`, and
  `Contract<T>` is a template shape that cannot hold a view record, so draining
  `SubscribeActiveAsync(view, …)` means restating its throw-on-`StreamError`/`Unclassified`/
  missing-checkpoint policy yourself. And `LedgerClientConformanceTests<TProbe>` drives only
  the template family, so an implementation of the three new members can be wrong and still
  pass the kit; see that package's README.
- Generated interface markers now mirror their view's fields as instance get-only properties,
  and the generated view record implements its marker, so a view answers with the *interface's*
  identity in identity-keyed generic code and its fields are readable through a marker-typed
  variable. `==` between marker-typed variables is reference equality, noted on the marker's
  doc-comment; concrete view records keep record value equality. The enrichment is conditional:
  where the view record is declared in a different package than its interface, is shared by two
  interfaces, or carries a field whose C# name would not agree on both sides of the mirror, the
  interface still gets the `View` witness, but the record is not stamped with the marker and the
  marker mirrors no fields.
- **Contract keys reach the read surface.** `KeyDescriptor<TTemplate, TKey>` (`Daml.Runtime`)
  pairs a keyed template with its key type and carries the key codec as `KeyEncoder`/`KeyDecoder`,
  the way `Choice<TTemplate, TArg, TResult>` carries `ArgumentEncoder`/`ResultDecoder`. Generated
  keyed templates carry the `IHasKey<TSelf, TKey>` facet and expose the descriptor through a
  static `Key` witness, so a generic method constrained on the facet can decode a contract key
  from a template type alone and encode one back to the `DamlValue` `ExerciseByKeyCommand` takes,
  with no reflection. `TKey` is unconstrained, so a template keyed by a bare `Party`
  (`key steward : Party`) gets `KeyDescriptor<Steward, Party>` on the same footing as a
  record-keyed template's `KeyDescriptor<Account, AccountKey>`. Where a template's own payload
  field maps to the C# member name `Key`, or the template itself is named `Key`, the witness is
  emitted as an explicit interface implementation only — reachable through the facet, invisible
  on the template type — and codegen logs a warning naming the template and, where there is one,
  the field.
- `StreamerSnapshot.SnapshotAsync(T.Key, submitter)` (`Daml.Ledger.Abstractions`): a
  descriptor-taking overload returning `IReadOnlyList<Contract<T, TKey>>` with every key decoded,
  so `await client.SnapshotAsync(Account.Key, alice)` hands back contracts whose `Key.Value` is
  an `AccountKey`. Passing the template's `Key` witness fixes both type parameters from one
  argument — C# performs no partial type-argument inference, so the pair has to travel together.
  A create row that carried no key is reported as `LedgerOperationException`, consistent with the
  short-list failures the keyless overload already prevents.
- Generated code emits a by-key command builder for every choice on a key-bearing template —
  `<Choice>ByKeyCommand(key, argument)`, returning an `ExerciseByKeyCommand`, beside the existing
  `<Choice>Command(contractId, argument)`. It is a plain static on the template record rather
  than an extension method: the key's C# type may be `string` or `Party`, and extending those
  would put the method on every value of that type in the consuming project.
- `ContractKey.KeyHash` (`Daml.Runtime`): the ledger's hash of the key, the value Canton indexes
  keyed contracts by. It travels beside the key on both wire formats (`contractKeyHash` on the
  JSON encoding, `contract_key_hash` on gRPC) and is held as the base64 text the JSON encoding
  uses. An init-only property rather than a positional parameter, so `Deconstruct` keeps its two
  parameters. A `null` there is a stated absence — the created event carried no hash, or the key
  was built by a caller rather than read off the wire. Nothing in this repository computes it:
  the hash is the ledger's, and a transport that reads one off a created event populates it. On
  a decoded active contract the same hash is reached as `contract.Key.Hash` (see
  `ContractKey<TKey>` under *Changed — BREAKING*).
- **`ContractKey` equality ignores `KeyHash`.** Two keys are the same key when they name the same
  value of the same template, so a key read off the wire — which carries a hash — equals the same
  key a caller constructed to exercise by it, which does not. `Equals`/`GetHashCode` are written
  out rather than compiler-generated for this reason; the generated pair would have compared the
  hash and made by-key matching fail quietly depending on where the key came from. `KeyHash` stays
  in `ToString()`, so a diagnostic dump still shows it.
- **`DamlLfJsonReader.ReadRecord` (`Daml.Runtime`): type-directed LF-JSON decoding.** Decodes a
  JSON object against a generated record type — supplied as `T` or as a runtime `Type`, read from
  JSON text or from an already-parsed `JsonElement` — into a `DamlRecord` whose fields carry their
  true Daml types (a `Party` field arrives as `DamlParty` rather than the `DamlText` an untyped
  decode yields), ready to hand to the generated `FromRecord`. Coverage is the full observed
  LF-JSON wire encoding:
  - Scalars — `Text`, `Bool`, `Int64` as its wire string, `Numeric` at the declared scale, `Date`,
    `Timestamp` (up to seven fractional digits, with `Z`, a numeric offset, or no zone designator,
    an unzoned value read as UTC), and `ContractId<T>`.
  - Structure — nested records, lists, `TextMap`, `GenMap` (its array of key/value pairs),
    `Optional` (`Some` as the bare value, `None` as `null`), variants and enums resolved by the
    wire names the emitter writes rather than their C# identifiers, so sanitized or disambiguated
    constructors decode correctly, and `Unit`.
  - Hand-written stdlib generics — `Tuple2`/`Tuple3` from their wire record form
    (`{"_1":…,"_2":…}`, the shape a captured tuple contract key shows) and `Either` from its
    `Left`/`Right` tagged-variant form, each component decoded from the CLR generic argument, so
    `Optional` components, nested records, lists of tuples and tuples carried by an `Either` arm
    all decode in those positions. Generic variants other than `Either` are still refused with
    `NotSupportedException`.

  A wire-shape mismatch is reported as `JsonException` carrying the dotted property path. The
  reader honours `DamlJsonDeserializationLimits`; the `JsonElement` overloads apply the
  array-breadth and value-depth caps but not the input-size cap, which was the caller's to apply
  when they parsed.
- `IDamlRecord<TSelf>` (`Daml.Runtime`): self-typed record facet whose
  `static abstract TSelf FromRecord(DamlRecord)` factory lets a generated type be materialized
  through a `where T : IDamlRecord<T>` constraint instead of by reflection. Generated templates
  and non-generic records declare it, as does the hand-written `RelTime`; a non-generic record's
  base list becomes `: IDamlRecord<TheRecord>` (the plain `IDamlRecord` still holds via
  inheritance). Generic records are unchanged.
- `Optional<T>` (`Daml.Runtime.Stdlib`): the representation for a Daml `Optional` in the positions
  C# nullable syntax cannot carry — over a type parameter, as a type argument to a generated
  generic, and as a `GenMap` key. A closed `Some`/`None` record hierarchy with `Match`, `HasValue`,
  and the `GetValueOrDefault`/`GetValueOrThrow`/`TryGetValue` bridges back to idiomatic C#,
  mirroring the wire-side `DamlOptional`.
- **Nested Daml `Optional` is generated instead of refused** (`Daml.Codegen.CSharp`). A Daml
  `Optional (Optional Text)` becomes a C# `Optional<Optional<string>>` — every level of the chain
  carried by the wrapper, none of it by C# nullability — so `None`, `Some None` and `Some (Some x)`
  stay three distinct values. Where the nesting is written directly in the Daml type, codegen
  previously failed the run outright with `NotSupportedException: Codegen does not support nested
  Optional types`, so for those shapes this is new capability rather than a changed one: no package
  containing them generated before this release. Nesting that arises only by *substitution* is a
  different story and did generate before — a field typed `Crate (Optional Text)` against
  `data Crate a = Crate with item : Optional a` is nested once substituted, and it generated
  silently while writing bytes the participant rejects. Those shapes are now refused instead; see
  the breaking entry below for exactly which. A flat `Optional Text` is unchanged and still
  generates `string?`, and so does an `Optional` separated from another by an intervening type:
  `Optional (Box (Optional Text))` is still `Box<Optional<string>>?`. Only a directly nested chain
  moves.
- `DamlOptionalChain` (`Daml.Runtime.Data`): the wire node for one level of a directly nested
  Optional chain, with `Optional<T>.ToChainValue`/`Optional<T>.FromChainValue`
  (`Daml.Runtime.Stdlib`) bridging it to the typed wrapper. It is a separate node from
  `DamlOptional` because the two carry different encodings for the same shape: a flat optional
  writes JSON `null` or its bare value, while every level of a nested chain writes the array form
  — `[]` when absent, `[v]` when present — which is what a participant accepts in a nested
  position. For the same reason `DamlValueExtensions.AsOptional` now throws `InvalidCastException`
  on a chain level rather than wrapping it as a further `Some`: the level is already an Optional,
  in the other encoding. Reaching that throw requires holding a chain, and nothing produced one
  before this release.

  One asymmetry rides with it. `DamlJsonSerializer.Deserialize` and `DeserializeRecord` are
  untyped: with no schema to consult they reconstruct a chain's array form as a `DamlList`, the
  same way they already reconstruct a `TextMap` as a `DamlRecord` and a `Party` as a `DamlText`.
  So a `DeserializeRecord` result handed to a generated `FromRecord` throws `InvalidCastException:
  Cannot cast DamlList to DamlOptionalChain` on a nested-Optional field, where every other field
  type would have decoded. Read such a record with the type-directed `DamlLfJsonReader.ReadRecord<T>`
  instead, which has the schema and produces the chain.
- Generated code: every `<Choice>Async` flavour now carries a `SubmitterInfo` overload beside its
  ergonomic single-`Party` shape — the value-returning exerciser on `ContractId<T>` (including
  `Archive`), the `T.Contract` sibling of the create-bearing exerciser, and the interface-choice
  exerciser. `readAs` and multi-party `actAs` are therefore expressible everywhere the fluent path
  reaches, instead of only on the create-bearing `ContractId<T>` overload; a submission needing
  either no longer has to drop to a hand-built `ExerciseCommand`. Existing single-`Party` call
  sites bind to the same overload as before.
- `ContractIdJsonConverterFactory` and `DamlJsonConverters` (`Daml.Runtime`): the
  `System.Text.Json` converters for contract ids, with `DamlJsonConverters.All` and
  `options.AddDamlConverters()` registering `Party`, `ContractId` and `SynchronizerId` in one call.
  The factory matches the per-template `T.ContractId` record codegen derives from `ContractId<T>`
  as well as the closed generic itself, so a property declared as either takes the same wire shape.
  A property declared as `ContractId<T>` converts with no registration (`ContractId<T>` carries the
  factory as a `[JsonConverter]` attribute, as `Party` and `SynchronizerId` already did); a
  property declared as the derived `T.ContractId` — including `T.Contract.Id` — does need
  `options.AddDamlConverters()`, because `System.Text.Json` reads `[JsonConverter]` off the
  declared type and does not walk its base chain. A contract-id string the target type rejects is
  reported as `JsonException`. Consumers that hand-rolled a reflective `JsonConverterFactory` for
  contract ids can delete it.
- `ILedgerStreamer.SnapshotAsync<T>()` (`Daml.Ledger.Abstractions`): drains an active-contract-set
  snapshot into `IReadOnlyList<Contract<T>>`, decoding each row through `T.FromRecord`. Throws
  `LedgerOperationException` rather than returning a short list when the snapshot faults, carries
  an unclassified row, carries a create row that does not decode into `T`, or ends without its
  terminal checkpoint; cancelling the token surfaces as `OperationCanceledException` even when the
  transport reports the cancelled call in-band. It consumes and discards the checkpoint's
  `StakeholderResume` ticket, so callers needing the gapless snapshot-to-stream handover stay on
  `SubscribeActiveAsync`.
- `SingleCommandExtensions.TrySubmitSingleAsync` (`Daml.Ledger.Abstractions`): the single-command
  submission path shared by generated exercisers and the hand-written write-path extensions. It
  builds the `CommandsSubmission` — single command, caller's command id or a freshly minted one,
  caller's workflow id when non-empty — and submits it, so that shape lives in one versioned place
  instead of being re-emitted into every consumer's generated code.
- `commandId` parameter on the `CreateByExercise` overloads and on the void
  `ThrowingExercise.ExerciseAsync` overloads (`Daml.Ledger.Abstractions`), so a caller can reuse an
  id across a retry of a lost-but-accepted submission and have the ledger deduplicate it.
  Generated exercisers already accepted one; these did not. Note that a minted id is never reported
  back on a failed submission, so an application-level retry is deduplicable only when the caller
  supplies and retains the id itself.
- Every write-path extension that accepts a `commandId` now rejects a supplied-but-
  `default(CommandId)` with an `ArgumentException` naming the parameter, rather than forwarding it.
  `CommandId` is a struct whose `Value` throws on a default instance, so an uninitialised one
  previously travelled to the transport and surfaced there as an `InvalidOperationException` out of
  a method whose contract is to return a structured outcome instead of throwing. Omitting the
  argument still mints an id — only an explicitly passed default is rejected.
- `timeout` parameter on both `ThrowingExercise.ExerciseAsync` overloads
  (`Daml.Ledger.Abstractions`), forwarded to the underlying `TryExerciseAsync` /
  `TrySubmitSingleAsync` call. The throwing forms were the only unary write-path extensions that
  could not carry a per-call deadline, so reaching for one meant dropping down to the structured
  `Try*` form. It sits after `commandId` and before `cancellationToken`, matching the
  `CreateByExercise` overloads; every existing call site passes `cancellationToken` by name, so the
  added parameter does not rebind one. Binary compatibility breaks as it does for the other
  parameter additions this cycle; publish in lockstep.
- **`Daml.Ledger.Abstractions.Testing.Conformance`**: an opt-in command-id deduplication check.
  Override `CreateCommandIdFixture()` on `LedgerClientConformanceTests<TProbe>` to prove your
  `ILedgerWriter` implementation honours the `commandId` obligation that
  `TryExerciseAsync`/`TryCreateAsync` document — a caller-supplied id reaches the participant
  verbatim, and a fresh one is minted only when the caller omits it, never leaving `command_id`
  unset. Both directions are checked separately, since a client that mints over the supplied id and
  one that never mints are distinct faults. Leaving it at its `null` default skips the four new
  `[Fact]`s, matching the existing opt-in pattern for `CreateWriteFixture()`.
- `CommandsSubmission.WithOptionalWorkflowId(string?)` (`Daml.Runtime`): applies a workflow id when
  the caller supplies a non-empty one and returns the submission unchanged otherwise, so the
  null/empty branch lives on the submission type instead of being restated at every call site.
  Generated exercisers and the `Daml.Ledger.Abstractions` extensions both route through it.
- `CommandId.FromWire(string?)` and `SynchronizerId.FromWire(string?)` (`Daml.Runtime`): project an
  optional wire field, returning `null` for a null, empty, or whitespace value and a constructed id
  otherwise — the supported way to express "the wire carried none". Hand-rolled fallbacks returning
  `default` from a helper typed `CommandId` wrap as a non-null `CommandId?` and still throw on
  `?.Value` (`?.Id` for `SynchronizerId`); the factory's only absence value is the literal `null`,
  so replace such helpers with `FromWire`.
- `CodeGenOptions.PublishesReferencedPackages` (`Daml.Codegen.CSharp`): declares that this run also
  produces the NuGet packages for the other DAR packages the generated code references, so their
  `<PackageReference>`s are pinned to the exact version this run produces instead of floating over
  the generations published against their intrinsic version. Defaults to `false`; the CLI sets it
  from `--release-counters`.
- **Supported Daml-LF input is now declared as 2.1 through 2.3**, each carried by a conformance
  fixture. Nothing in the toolchain enforced 2.1 before; the declaration was narrower than the
  capability. C# is emitted from all three fixtures and the result pinned, so every declared
  version is proven on the emit path as well as the read one. Emitting 2.3 needs a 3.5-line Daml
  SDK — the fixtures build on 3.5.2.
- A contract-key conformance corpus ships in `Daml.Codegen.Testing.Conformance` (namespace
  `Daml.Codegen.Testing.Conformance.Contractkeys`, DAR reachable via
  `ConformanceCorpus.OpenDar(ConformancePackage.ContractKeys)`), built at Daml-LF 2.3 — the
  earliest 2.x version that can express a contract key. It covers a record key built from several
  payload fields, a record key whose field comes from a nested projection, a record key built by a
  function in another module, and a bare `Party` key. `ConformanceCorpus.OpenDar()` still returns
  the rich-types DAR.
- A default-target conformance corpus ships in `Daml.Codegen.Testing.Conformance` (namespace
  `Daml.Codegen.Testing.Conformance.Defaulttarget`, DAR reachable via
  `ConformanceCorpus.OpenDar(ConformancePackage.DefaultTarget)`), built with no `--target` so it
  carries the Daml-LF version damlc emits for a project that requests none.
- The conformance corpus (`Daml.Codegen.Testing.Conformance`, and the DAR it embeds) gains a
  `TypeCorners` template covering the type-system corners the corpus previously omitted:
  parameterized records and variants (`Box`, `Slot`) instantiated in a template payload, `GenMap`
  keyed by `Party` and by `Int`, `Either`, `Tuple2`/`Tuple3`, a recursive record (`Branch`), an
  `Optional` nested inside an `Optional` through a wrapper record, and the `Numeric` scale extremes
  0 and 37. The `Holding` interface gains choices (`Describe`, `Reissue`) alongside its view.
  Consumers round-tripping the corpus against a participant get the wider surface; the corpus
  package id changes accordingly.
- `Crate<TA>` (`Daml.Codegen.Testing.Conformance`, namespace
  `Daml.Codegen.Testing.Conformance.Richtypes`): a parameterized record whose one field is an
  `Optional` over the record's own type variable, held as `Optional<TA>`. It is the first such
  shape in the shipped corpus, so the position C# nullable syntax cannot spell is now covered end
  to end rather than only in the emitter's unit tests. `TypeCorners` carries one as `crate`.
- NuGet packages now ship a SourceLink'd symbols package (`.snupkg`) beside each `.nupkg`, embed
  repository/commit metadata for source-stepping in debuggers, and are gated by NuGet package
  validation at pack time; release builds are compiled with `ContinuousIntegrationBuild` for
  deterministic, path-normalized PDBs.
- The DAR → IntermediateDar tool (the JVM helper) is now packaged for standalone publication as
  `daml-dar-to-proto`: each GitHub release attaches `daml-dar-to-proto-<version>.jar` and the
  matching `intermediate_dar-<version>.proto`, so non-C# SDKs can decode a `.dar` to an
  Intermediate DAR without the `dpm codegen-cs` bundle. The jar gains a `--version` flag printing
  the release version it was built as (`0.0.0-dev` for non-release builds).

### Changed — BREAKING

- **The read surface's payloads are typed** (`Daml.Runtime`). `ContractStreamEvent<T>.Created`,
  `ContractStreamEvent<T>.Assigned` and `AcsSnapshotEntry<T>.Created` now carry the payload as `T`
  rather than a raw `DamlRecord`, and the two unions constrain `T` to the template family
  (`where T : ITemplate, IDamlRecord<T>`) instead of the broad `IDamlType`. Subscribing to a Daml
  interface moves to the new interface-family overloads, whose payload is the interface's view
  record. There is no deprecation period: a C# signature does not include its constraints, so the
  old and the new could not overload under one name. Template call sites recompile unchanged after
  regenerating; a marker call site takes a one-line fix — pass the marker's `View` witness instead
  of naming the marker as a type argument, and read `InterfaceStreamEvent<,>`/
  `InterfaceAcsSnapshotEntry<,>` in place of the template unions. A payload that no longer decodes
  is the projector's business now: it surfaces as `Unclassified` with
  `UnclassifiedKind.DecodeFailure` instead of throwing out of the consumer's own `FromRecord` call.
- **`ILedgerStreamer` implementations must add three members** — the interface-family
  `SubscribeAsync`, `SubscribeActiveAsync` and `SubscribeLedgerEffectsAsync` — and tighten the
  three template-family members' `where T : IDamlType` to `where T : ITemplate, IDamlRecord<T>`.
  The resume overloads keep their forwarding default implementations. A transport now decodes the
  payload before yielding it, which is what moves the decode off every consumer's `switch`.
- **`LedgerClientConformanceTests<TProbe>` constrains `TProbe` to the template family**
  (`Daml.Ledger.Abstractions.Testing.Conformance`), matching the surface it exercises. An adopter's
  probe type gains `IDamlRecord<TProbe>` — a `static FromRecord` — which a generated template
  already carries.
- **`Daml.Ledger.Abstractions.Testing.Conformance` now requires xunit.v3 `4.0.0`** (was `3.2.2`).
  The kit references `xunit.v3.extensibility.core` and `xunit.v3.assert` directly, and the `[Fact]`
  methods a consumer inherits live on its `LedgerClientConformanceTests<TProbe>` base, so the kit's
  xunit major is the consumer's — a consumer test project must move to xunit.v3 `4.0.0` in the same
  step. This also moves Microsoft.Testing.Platform `2.0.2` to `2.3.3`, which the pinned coverage
  extension already required.
- **Contract keys move off the payload and onto the active contract, typed.** `IHasKey<TKey>` and
  the `Key` member are no longer emitted on the template payload type: a payload is what a caller
  constructs locally to build a create command, so it cannot know the key of a contract that does
  not exist yet, and the accessor was a stub that threw `NotImplementedException`. Read
  `contract.Key` instead of `contract.Data.Key`. The key *type* and its serialization are
  unchanged, so caller-constructed key values and `ExerciseByKeyCommand` are untouched.
- **`Contract<T>` loses its `ContractKey? Key` slot, and keyed contracts move to a new
  `Contract<T, TKey>`** (`Daml.Runtime`). The old slot was the third *positional* parameter, so its
  removal breaks twice: `Deconstruct` drops from three parameters to two, and the
  compiler-generated equality members stop comparing the key. A template that declares no contract
  key stops offering a member that could never be populated; a keyed template's contracts are now
  `Contract<T, TKey>(ContractId<T> Id, T Data, ContractKey<TKey> Key)`, whose key is
  **non-nullable**. `Contract<T, TKey>.FromCreatedEvent` throws `InvalidOperationException` when
  the event carries no key, rather than handing back a shape whose type says the key is present.
- **The ledger's key hash is retyped, not dropped.** The new `ContractKey<TKey>(TKey Value,
  string? Hash)` pairs the decoded key with the ledger's hash of it, so `contract.Key.Value` is
  `TKey` — no `As<DamlRecord>()` hop — and `contract.Key.Hash` still reaches the hash. The hash is
  Canton-computed over the key and the template id, is not derivable client-side, and no projected
  shape exposes the raw `CreatedEvent`, so dropping the slot outright would have destroyed that
  access rather than relocating it. Callers reading `contract.Key!.KeyHash` become
  `contract.Key.Hash`; callers reading `contract.Key!.Value` as a wire `DamlValue` become
  `contract.Key.Value` typed.
- **The generated nested `Contract.Key` becomes non-nullable and gains the hash**
  (`Daml.Codegen.CSharp`). A keyed template's nested contract already carried a *typed* key, but as
  `required TKey? Key`; it is now `required ContractKey<TKey> Key`, and its `FromCreatedEvent`
  throws when the created event carries no key instead of assigning `null`. A keyless template's
  generated output is byte-identical to before. Callers become `contract.Key.Value` for the key and
  `contract.Key.Hash` for the hash, and a call site that constructed the contract with `Key = null`
  must now supply a `ContractKey<TKey>`.
- **`IHasKey<TKey>` becomes `IHasKey<TSelf, TKey>`, and its member becomes a `static abstract`
  `KeyDescriptor<TSelf, TKey> Key`** (`Daml.Runtime`). The facet previously carried an instance
  `TKey Key { get; }`, which told generic code the key's type but not how to decode one; a read
  surface holding only `TKey` had nowhere to get the decode from, so it could not admit an
  unconstrained key type. The descriptor now carries the decode and the pair of types travels
  together, so a generic call site infers both type parameters from one argument and `TKey` needs
  no `IDamlRecord<TKey>` constraint — a bare `Party` key is admitted on the same footing as a
  record key. Reached through a `static abstract` member rather than a bare static, so generic code
  constrained on the facet finds it without reflection, the same shape `ITemplate.TemplateId` uses.
  Hand-written implementors give the facet the implementing type as its first argument and replace
  the instance property with a static `Key` descriptor. Where the payload already has a member
  named `Key`, implement the descriptor explicitly
  (`static KeyDescriptor<T, TKey> IHasKey<T, TKey>.Key { get; } = new() { … }`), which leaves the
  name free on the type.
- **`AcsSnapshotEntryExtensions.ToContract<T>()` returns a keyless `Contract<T>`**
  (`Daml.Runtime`). A keyed projection goes through the new `ToContract<T, TKey>()`, which decodes
  the row's key through the template's `Key` witness and throws `InvalidOperationException` when
  the row carried none.
- **The four runtime shapes that carry a created contract gain a mandatory `ContractKey? Key`
  parameter.** `AcsSnapshotEntry<T>.Created` and `ContractStreamEvent<T>.Created` each take it
  immediately after their payload, grouping contract identity, data and key ahead of the ledger
  metadata. The parameter is positional rather than an optional init-only property, so this is a
  source break and a binary break: every projector constructing one of these shapes — every
  `ILedgerClient` implementation included — has to pass the key the created event carried, or an
  explicit `null` for a template that has none. The wire-level `ContractKey` travels rather than a
  generated key type, which `Daml.Runtime` cannot name; decode it with the hop generated code
  already performs, `TKey.FromRecord(key.Value.As<DamlRecord>())`. An optional slot would have let
  every existing projector keep compiling while silently never populating the key.
- **`ContractStreamEvent<T>.Assigned` gains a mandatory `ContractKey? Key`**, positioned
  immediately after `Payload` to match the shapes that already carry one. An assignment re-emits
  the whole created contract — Canton's assigned event wraps a full created event, key included —
  so this variant was the one create-carrying shape with nowhere to put a key, and a consumer
  rebuilding state from a single stream lost the key at every reassignment.
- **`CreatedEvent.ContractKey` loses its `= null` default** and becomes a mandatory parameter
  (`DateTimeOffset? CreatedAt` stays optional after it). This is the single entry point feeding
  every downstream key slot, so a transport could omit it entirely, compile clean, and feed `null`
  into every `required` slot below — exactly the failure the mandatory downstream slots were
  introduced to prevent. Callers now state the absence: `ContractKey: null` for a template that
  declares no key.
- **`ILedgerWriter.TryExerciseAsync<TResult>` and `ILedgerWriter.TryCreateAsync<TTemplate>` take a
  new optional `CommandId? commandId = null`** immediately after `workflowId`
  (`Daml.Ledger.Abstractions`), so a caller can reuse an id across a retry of a lost-but-accepted
  submission and have the ledger deduplicate it — the same parameter the generated exercisers
  already accept. Any external implementation of `ILedgerWriter` must add the parameter to both
  methods and honour it. Callers are unaffected at the source level unless they passed `timeout`
  positionally, which now fails to compile rather than rebinding silently — `TimeSpan` and
  `CancellationToken` cannot convert to `CommandId`, whose own conversions are explicit and
  `string`-only. Binary compatibility does break: an assembly compiled against the previous package
  bakes the old signature into its call sites, so pinning an old `Daml.Ledger.Abstractions`
  alongside newly generated code raises `MissingMethodException` at runtime. Publish in lockstep.
  The two value-returning `ThrowingExercise.ExerciseAsync<TResult>` overloads gain the same
  parameter and forward it, and the `CreateByExercise` overloads and the void
  `ThrowingExercise.ExerciseAsync` overloads take it too — there the break is positional, since
  `commandId` precedes `timeout`. Both fail to compile rather than rebinding silently.
- **`CreateAsync` no longer takes a submitter when every signatory is a payload field** — the
  emitted wrapper derives the `SubmitterInfo` from the payload's `Party` properties, so the
  parameter is removed rather than made optional. It is dropped only when the analysis yields at
  least one payload field: a template with any non-payload signatory keeps the explicit
  `SubmitterInfo` parameter, and so does one whose signatory clause resolves statically to an empty
  list. Callers that passed a submitter equal to the payload's signatories drop the argument; a
  caller that passed a different `actAs` was asserting an authority the template does not grant. A
  caller that passed a `SubmitterInfo` carrying `readAs` was over-asserting nothing, though —
  `readAs` is disclosure, not authority — and the emitted wrapper gives it no way to set one. That
  call moves to `ILedgerWriter.TryCreateAsync<TTemplate>(payload, submitter, workflowId, commandId,
  timeout, cancellationToken)`, which still takes the whole `SubmitterInfo`.
- **The `Party actAs` overloads are removed**, both the emitted ones and their six hand-written
  counterparts in `CreateByExercise` and `ThrowingExercise`. A positional `Party` call site keeps
  compiling — the implicit `Party` → `SubmitterInfo` conversion still binds — but a call passing
  `actAs:` as a named argument does not, and the change is binary-breaking either way. Pass a
  `SubmitterInfo`, or let a `Party` convert. The named-controller parameters emitted for
  create-bearing choices (`owner`, `custodian`, `provider`, `steward`) are a different family and
  are unchanged.
- **The six single-`Party` convenience overloads on `PartyOverloads`
  (`Daml.Ledger.Abstractions`) are removed**, and the class with them: the `TryExerciseAsync` /
  `TryCreateAsync` pair over `ILedgerWriter` and the four `SubscribeAsync` (offset and
  `StakeholderResume` forms), `SubscribeActiveAsync` and `SubscribeLedgerEffectsAsync` members over
  `ILedgerStreamer`. No positional call ever reached them: `SubmitterInfo` declares an implicit
  conversion from `Party`, which makes the interface member itself applicable to a `Party`
  argument, and C# considers extension methods only when no instance member is. So
  `client.SubscribeAsync<T>(alice, ...)`, `writer.TryCreateAsync(payload, alice, ...)` and every
  other positional call site compile and behave exactly as before — they always bound to the
  interface member. Only a caller who named the argument, `actAs: alice`, reached the extension,
  and that spelling no longer compiles: drop the name, or use the interface's own parameter name,
  `submitter: alice`. Binary compatibility breaks for an assembly compiled against a call to one of
  the six.
- **`SingleCommandSubmission` is renamed to `SingleCommandExtensions`**
  (`Daml.Ledger.Abstractions`), aligning it with the `<Template>Extensions` /
  `<Template>NonContractExtensions` family. Rename the type at call sites; no behaviour changed.
- **The generated `<Choice>ByKeyCommand` statics move off the extension classes onto the template
  record.** `AccountExtensions.CreditByKeyCommand(…)` becomes `Account.CreditByKeyCommand(…)`, and
  the non-contract host moves the same way. The extension classes keep everything else they emit.
- **`CommandsSubmission` gains a trailing `MinLedgerTime? MinLedgerTime` parameter**
  (`Daml.Runtime`), so a caller can finally express "do not commit this before T" — the bound the
  Ledger API carries as `Commands.min_ledger_time_abs` / `Commands.min_ledger_time_rel`. It had no
  home before: the submission type had no member for it, so a caller who needed one could not ask
  for it at all. The new `MinLedgerTime` is a closed hierarchy — an abstract record with sealed
  `Absolute(DateTimeOffset)` and `Relative(TimeSpan)` arms — rather than a pair of nullable members,
  because the two wire fields are mutually exclusive and a participant rejects a submission
  carrying both; a submission therefore cannot express that illegal state, and neither the gRPC nor
  the JSON mapper has to invent a both-set behaviour inside a serializer. `MinLedgerTime.Match`
  projects a bound by applying the handler for its arm, so a transport that adds an arm later stops
  compiling instead of falling through a default branch (a `switch` cannot offer that — C# treats a
  class hierarchy as open, so an arms-plus-`null` switch expression is still CS8509 and needs a
  discard). A negative `Relative` delay is rejected with `ArgumentOutOfRangeException`;
  `TimeSpan.Zero` is accepted. `CommandsSubmission.WithMinLedgerTime` sets the bound and clears it
  when passed `null`. `null` — the default — means no bound and preserves today's behaviour
  exactly. The parameter is positional and last, so this is a source break only for a positional
  deconstruction of `CommandsSubmission`, and a binary break regardless. Honouring the bound on the
  wire is the transport's part: a client that ignores the member silently drops the caller's
  constraint.
- **`TransactionResult.CommandId` is now a nullable `CommandId?`.** The Ledger API omits the
  command id on transactions the participant did not submit, and the parameter had no value meaning
  "absent": transports were forced to fabricate `default(CommandId)`. That instance constructs fine
  and detonates later — `CommandId.Value` throws `InvalidOperationException` on a default instance —
  so the failure surfaced at first property access, far from the projection that caused it, and a
  null check could not detect it because a default struct is not null. `null` now means the
  participant reported no command id. The parameter is positional and keeps its position, so a
  caller passing a real `CommandId` is unaffected. **Widening the slot is not on its own enough to
  defuse a transport that fabricates.** A call site passing the literal `default` at the
  `CommandId?` position now yields `null`, but an argument whose own type is `CommandId` — the
  return of a conversion helper such as `wire.Length == 0 ? default : (CommandId)wire` — is wrapped
  by the implicit `CommandId` → `CommandId?` conversion into a **non-null** `CommandId?` holding a
  default instance: `is null` reads `false` and `?.Value` still throws. Such helpers must be widened
  to return `CommandId?` as well. Reads must be updated: `result.CommandId.Value` no longer returns
  the `string` — use `result.CommandId?.Value` for the id text, and branch on
  `result.CommandId is null` for absence. `TransactionTree.ToTransactionResult()` now projects
  `null` rather than a default instance, since a tree carries no command id.
  `SubmitAndWaitResult.CommandId` is deliberately unchanged: it reports the id the client itself
  supplied or minted for that submission, so it is always present.
- **`Unclassified` on both stream unions now takes a nullable `LedgerOffset? Offset`.**
  `AcsSnapshotEntry<T>.Unclassified` and `ContractStreamEvent<T>.Unclassified` keep the offset in
  position 1 and only widen its type, so a projector that must surface a row it could not classify
  and has no offset for passes `null` instead of fabricating one. It had no honest value to pass
  before: `LedgerOffset` has no absent value and its `default` is `LedgerOffset.Begin`, a genuine
  ledger position — so a consumer following the documented resume pattern persisted that fabricated
  `Begin`, checkpointed at the beginning of the ledger and re-read the entire stream. A consumer
  persisting resume state must skip a `null` offset and keep the last one that was real, never
  checkpoint it. The parameter is positional, so this is a source break for positional patterns
  binding the offset as a non-nullable `LedgerOffset`, and a binary break regardless.
  `StreamerSnapshot.SnapshotAsync` reports an offsetless row as "with no ledger offset" rather than
  interpolating an empty position. `Daml.Ledger.Abstractions.Testing.Conformance` no longer treats
  an offsetless row as a fault: its bounded-subscription checks compare only events that carry a
  ledger position, because an event that sits nowhere cannot sit past a boundary. An implementation
  that legitimately surfaces an offsetless `Unclassified` inside a bounded window now passes those
  checks instead of failing them.
- **`AcsSnapshotEntry<T>.Unclassified` also converges on its sibling's shape** (`Daml.Runtime`),
  carrying `(LedgerOffset? Offset, UnclassifiedKind Kind, string? RawKind = null)` instead of
  `(LedgerOffset Offset, string Kind)`: a consumer handling both the snapshot and the live stream
  switches on one `UnclassifiedKind` vocabulary rather than on magic strings for one and an enum
  for the other. The same construction invariant applies — `RawKind` is non-null exactly when
  `Kind` is `UnclassifiedKind.Unknown`, and an `ArgumentException` is thrown otherwise. Projectors
  constructing this variant should pass `UnclassifiedKind.Unknown` with the previous string as
  `RawKind` to preserve today's behaviour.
- **All four stream-failure records — `AcsSnapshotEntry<T>.StreamError`,
  `ContractStreamEvent<T>.StreamError`, `InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError`
  and `InterfaceStreamEvent<TInterface, TView>.StreamError` — gain a `DamlErrorCategory? Category`
  and a trailing `Exception? SourceException`**, in the same position, name and type as the pair
  `ExerciseOutcome<T>.InfraError` carries on the write path, so a transport that classified a
  stream fault or caught the exception behind one has somewhere to put them instead of flattening
  everything to a status code and a message. Counting from the last published release the two
  template-family records go from two positional parameters to four: constructions passing two or
  three arguments still bind, but any positional pattern binding fewer than four elements no longer
  matches — use a property pattern — and the change is binary-breaking either way. The generated
  record equality and `GetHashCode` now weigh `SourceException` too, and it compares by reference,
  so two stream errors that agree on status code, message and category are unequal when they carry
  different exception instances. `StreamerSnapshot.SnapshotAsync` forwards the category onto the
  `LedgerOperationException` it throws for a faulted snapshot and the exception as its
  `InnerException`, so a classification and the stack the fault came from survive that rethrow
  instead of being dropped there. The two interface-family records declare both parameters from the
  outset.
- **`ExerciseOutcome<T>.InfraError` gains a `DamlErrorCategory? Category`, and
  `LedgerOperationException`'s two infrastructure constructors collapse into a single one carrying
  a category alongside the status code.** A transport that classifies a failure the participant
  served with no recoverable Canton error id attached — a bare HTTP 400, a gRPC `Unauthenticated` —
  can now surface that classification instead of having to discard either it or the status code.
  The new parameter sits *before* `SourceException` rather than at the end, so every
  `new InfraError(statusCode, message, exception)` call site fails to compile (an `Exception` does
  not convert to `DamlErrorCategory?`); pass the exception as `sourceException:` by name, and supply
  a category where one was determined. The placement is the point — appended last, a re-wrapping
  site that dropped the classification would keep compiling and keep dropping it silently. On the
  exception, `LedgerOperationException(message, statusCode)` and
  `LedgerOperationException(message, statusCode, innerException)` are replaced by
  `LedgerOperationException(message, statusCode, category = null, innerException = null)`: the
  two-argument form still binds, the three-argument positional form does not, so pass
  `innerException:` by name. `Category` is consequently no longer a `DamlError`-only property — it
  is also set for a classified infrastructure failure, and `null` when neither applies.
- **`CreatedContract` becomes a field-for-field mirror of `TreeEvent.Created`** (`Daml.Runtime`),
  gaining `EventId`, `WitnessParties`, `Signatories`, `Observers`, `ContractKey?` and `CreatedAt?`.
  `TransactionTree.ToTransactionResult()` forwards all six, so a create node now flattens
  losslessly — the key and the ledger-effective time survive the projection instead of being
  dropped. The first four are **required positional** parameters, in `TreeEvent.Created`'s order,
  with `ContractKey?` and `CreatedAt?` trailing and defaulted exactly as that record has them;
  `InterfaceIds` stays init-only. `init` properties were available and would have been
  non-breaking, and were rejected: the producers that build this record are the REST and gRPC
  transaction projectors, which would have kept compiling untouched while emitting empty lists,
  leaving a consumer unable to tell "this contract has no observers" from "nobody wired it up".
  The forced compile break is the point. Callers move from
  `new CreatedContract(cid, templateId, payload)` to
  `new CreatedContract(eventId, cid, templateId, payload, witnesses, signatories, observers)`;
  `Deconstruct` goes from three parameters to nine. Binary compatibility breaks for any assembly
  compiled against the old constructor or deconstruction.
- **`CreatedContract.Payload` becomes a `DamlRecord`** (`Daml.Runtime`), replacing the `string`
  slot that made the payload's encoding the transport's choice — a REST transport serialized
  Daml-JSON and a gRPC transport protobuf-JSON for the same contract, both typed `string`, so
  nothing could catch the divergence. Every sibling create-carrying shape already speaks
  `DamlRecord`, and `TransactionTree.ToTransactionResult()` now passes the tree's `CreateArguments`
  through instead of flattening them to JSON. Construction sites move from a JSON literal to a
  record (`"{}"` becomes `DamlRecord.Create()`), and equality on the slot becomes `DamlRecord`'s
  structural comparison, so assertions that compared payload strings now compare record structure.
  A consumer that needs the old JSON text calls `DamlJsonSerializer.Serialize(payload)`.
- **`CreatedContract` and `TransactionResult` compare their list members by content rather than by
  list identity** (`Daml.Runtime`). `CreatedContract.Equals`/`GetHashCode` grow element-wise
  comparison for `WitnessParties`, `Signatories`, `Observers` and `InterfaceIds`;
  `TransactionResult` gains the same pair for `CreatedContracts`, `ArchivedContractIds` and
  `ExercisedEvents`, which the record-synthesized equality compared by reference. This is a
  semantic change: two results describing the same transaction over distinct backing lists compared
  unequal before and compare equal now.
- **`ContractId<T>` (`Daml.Runtime`) now serializes through `System.Text.Json` as the bare ledger
  contract-id string** — the shape PQS rows and the JSON Ledger API use — rather than as the
  `{"Value":"..."}` object its public `Value` property yielded by default, and reads a bare string
  back. A consumer with contract ids in persisted JSON, in a stored projection, or on a wire shared
  with a non-C# peer has to migrate that data or pin the old shape with a hand-written converter
  registered ahead of the factory. Optional `ContractId<T>?` still writes and reads JSON `null`.
- **`AddDamlConverters()` (`Daml.Runtime`) now also sets `RespectNullableAnnotations` on the
  options it is given.** It is what makes the three scalar identity converters — `Party`,
  `SynchronizerId` and `ContractId<T>` — agree on what a JSON `null` means: rejected wherever the
  declared type forbids one, read as absent wherever it permits one. Previously a `null` on a
  non-nullable `ContractId<T>` field bound silently and surfaced only as a
  `NullReferenceException` at the first dereference, while the same payload against a `Party` or
  `SynchronizerId` field threw `JsonException`. `Optional (ContractId T)` — declared
  `ContractId<T>?` — still reads and writes `null` unchanged. `ContractId<T>` is a reference type
  and is therefore indistinguishable from its own nullable form at runtime, so no per-converter
  setting can draw that line; the cost is that the flag is serializer-wide and also applies to a
  host's own types in the same `JsonSerializerOptions`.
- **`DamlJsonSerializer` now writes a Daml `Int64` as a JSON string, not a JSON number**
  (`Daml.Runtime`). A participant sends `"count": "42"` and `DamlLfJsonReader` requires a string, so
  `DamlLfJsonReader.ReadRecord<T>(DamlJsonSerializer.Serialize(v.ToRecord()))` used to throw
  `JsonException` for every template with an `Int` field, and serialized payloads submitted to a
  ledger carried the wrong shape. Serialized output changes for `Int64` anywhere it appears —
  record fields, list elements, `TextMap`/`GenMap` keys and values, variant payloads. The untyped
  `DamlJsonSerializer.DeserializeRecord` still accepts both forms, so payloads written by earlier
  versions keep reading.
- **Both LF-JSON decoders now normalize a decoded `Time` to UTC instead of retaining the sender's
  wire offset.** `DamlLfJsonReader.ReadRecord` and `DamlJsonSerializer.DeserializeRecord`
  previously carried the offset through, so `"2026-01-01T12:34:56+02:00"` decoded to a
  `DamlTimestamp` whose `Value.Offset` was `+02:00` and whose `Value.Hour` was `12`, while the same
  instant sent as `"2026-01-01T10:34:56Z"` decoded to `TimeSpan.Zero` and `10` — the naive
  projections of one contract field differed by transport. A Daml `Time` is microseconds since the
  epoch and carries no zone, so both decoders now yield `Offset` `TimeSpan.Zero`. Equality, hashing,
  `MicrosecondsSinceEpoch` and re-serialization are instant-based and are unchanged; only `.Hour`,
  `.Date`, `.DateTime`, `.LocalDateTime` and `.ToString()` move. A consumer that relied on reading
  the sender's wall clock off a decoded timestamp must now convert explicitly with `TimeZoneInfo`.
- **A Daml field whose name is a C# keyword now generates a PascalCased member instead of an
  `@`-escaped lowercase one** (`Daml.Codegen.CSharp`). The keyword escape used to run before
  recasing, and `ToPascalCase` spent its capitalisation on the `@` rather than on the letter behind
  it, so the member kept the Daml name's lowercase spelling. The escape now runs last, after
  sanitising, PascalCasing and enclosing-type disambiguation. Two members in the generated corpus
  are renamed: `@operator` becomes `Operator` and `@lock` becomes `Lock`. This is source-breaking
  for code naming either member. **The Daml wire name is unchanged** — `DamlFieldAttribute("lock")`
  and `GetRequiredField("lock")` still carry the Daml spelling, so no serialized payload moves.
  Choice *parameters* are unaffected: they camelCase before escaping and so still emit `@operator`.
- **Every generated generic record and variant constrains its type parameters to `notnull`**
  (`Daml.Codegen.CSharp`). `data Box a = Box with item : a` now emits
  `public sealed record Box<TA>(...) where TA : notnull`. Without it, a field typed by the type
  parameter reports `NullabilityState.Nullable` through `NullabilityInfoContext` at every
  reference-type instantiation, so any `IDamlRecord` generic reaching `DamlLfJsonReader` decodes
  that slot as an `Optional` the wire never carried. A Daml type variable ranges only over
  serialisable Daml types, none of which is nullable — Daml spells the nullable positions
  `Optional`, which the mapper renders as an explicit `?` or as a wrapper. This is source-breaking
  for code that passed a nullable type argument, reference or value alike — both `Box<string?>` and
  `Box<long?>` are now rejected; write `Box<Optional<string>>`, which is what the emitter produces
  for a Daml `Box (Optional Text)`.
- **A Daml `Optional` passed as a type argument to a parametric stdlib generic is generated as
  `Optional<T>`, not as a C# nullable, and those generics now constrain their type parameters to
  `notnull`** (`Daml.Codegen.CSharp`, `Daml.Runtime`). A field typed `Either Text (Optional Text)`
  used to emit `Either<string, string?>`; it now emits `Either<string, Optional<string>>`, on the
  flat encoding, so the wire form is unchanged. `Either<TL, TR>`, `Tuple2<T1, T2>`,
  `Tuple3<T1, T2, T3>`, `Set<T>`, `NonEmpty<T>`, `Map<TKey, TValue>` and `Optional<T>` itself all
  carry `notnull` on their type parameters. From a consumer's seat a hand-written
  `Daml.Runtime.Stdlib.Either<TL, TR>` and a generated `Box<TA>` are the same kind of thing — a
  generic whose type argument cannot carry a C# `?` — so the wrapper rule that already covered the
  generated half now covers both. This is source-breaking for code that passed a nullable type
  argument to one of the seven, reference or value alike: write `Tuple2<long, Optional<string>>`
  rather than `Tuple2<long, string?>`.
- **A Daml `Optional` passed to a generated generic is held as `Optional<t>` rather than `t?`**
  (`Daml.Codegen.CSharp`). In the shipped corpus this changes one field — `TypeCorners.nestedNote`
  moves from `Box<string?>?` to `Box<Optional<string>>?`. The wire encoding is unchanged, so no
  ledger payload moves; consumers reading such a field switch from `?.` to `Match`, `HasValue` or
  `GetValueOrDefault()`. No implicit conversion is offered: the wrapper exists to be structurally
  distinguishable from `t?`, and a conversion would reintroduce the collapse. Every published
  `Splice.Api.Token.*` type is unaffected.
- **A Daml `Optional` in a `GenMap` key position is generated as `Optional<T>`, not as a C#
  nullable** (`Daml.Codegen.CSharp`). A field typed `GenMap (Optional Text) Int` used to emit
  `IReadOnlyDictionary<string?, long>`; it now emits `IReadOnlyDictionary<Optional<string>, long>`.
  A dictionary key is `notnull`, so the old shape could not represent Daml's `None` key at all: the
  generated `FromRecord` built it with a `ToDictionary` whose key selector evaluated to `null` on
  `None` and threw `ArgumentNullException` at decode, and the call did not even compile under
  `TreatWarningsAsErrors` because `string?` does not satisfy the `notnull` constraint (CS8714). The
  wire encoding is unchanged — the key rides the flat encoding a non-nested `Optional` always used
  — so this is a compile-time break in the generated C# only, and only for a package whose Daml
  puts an `Optional` in a `GenMap` key. An `Optional` in the `GenMap` *value* position is
  unaffected and still generates `t?`. A nested `Optional` key follows the chain rule and generates
  `Optional<Optional<string>>`.
- **Codegen refuses a Daml `Optional` passed as a type argument to a generated generic whose
  declaration wraps that same type parameter in an `Optional`** (`Daml.Codegen.CSharp`). Given
  `data Crate a = Crate with item : Optional a`, a field typed `Crate (Optional Text)` or
  `Crate (Optional (Optional Text))` now fails the run with `CodegenException: Codegen does not
  support a Daml Optional as the 'a' type argument of <Module>:<Type>`. A generic's body is emitted
  once from its declaration, so the Optional the declaration wraps around `a` has its
  chain-or-flat encoding fixed there, blind to what each use site substitutes; substituting an
  `Optional` adds a level adjacent to it, and the composed converter writes one array level short
  of the chain encoding. At three levels the participant *accepts* the short form and reads it back
  as a different Optional, so this replaces a silent wire corruption with a codegen failure.
  Passing an `Optional` to a generic that does not wrap that parameter is unaffected: with
  `data Box a = Box with item : a`, `Box (Optional (Optional Text))` still generates
  `Box<Optional<Optional<string>>>` on the chain encoding. So is an `Optional` separated from the
  parameter by another type, such as a declaration's `Optional [a]`.
- **`Set<T>`, `Map<TKey, TValue>` and `NonEmpty<T>` now compare structurally and materialize the
  collections handed to them** (`Daml.Runtime.Stdlib`). Two values built from equal contents are
  now equal and hash alike, matching every other Daml stdlib value type in the package; previously
  the record-synthesized equality compared the borrowed collection by reference, so
  `new Set<string>(["a"]) != new Set<string>(["a"])`. Each type now copies its input at
  construction and on `init`, so a producer that keeps the `HashSet<T>` or `List<T>` it supplied
  can no longer change an existing value's `Count`, `Contains`, `ToRecord` output or hash code.
  `Map<TKey, TValue>` compares entries pairwise in wire order, so two maps holding the same pairs
  in different orders stay unequal; `Set<T>` compares as an unordered set. Code that relied on
  reference equality for these three types, or on mutating a value through the collection it passed
  in, must change.
- **`Set<T>` no longer preserves the iteration order of an ordered input collection**
  (`Daml.Runtime.Stdlib`). Passing a `SortedSet<T>` used to keep that instance and its sorted
  iteration order; the constructor now copies into an unordered set, so `ToRecord` emits the `map`
  GenMap entries in hash order instead. Consumers that depend on a particular on-the-wire entry
  order must sort at the point they build the `DamlRecord`.
- **`Map<TKey, TValue>` and `NonEmpty<T>` now reject a `null` collection with
  `ArgumentNullException`** (`Daml.Runtime.Stdlib`). `new Map<string, long>(null!)` and
  `new NonEmpty<long>(1, null!)` used to store the `null` and fail later with a
  `NullReferenceException` from `Count`, `ToRecord` or an equality check. Both constructors and the
  `Entries` and `Tl` `init` accessors now throw immediately, naming the parameter, so
  `with { Entries = null! }` is rejected too. `Set<T>` already threw.
- **`DamlRecord` now copies the field list it is given and rejects a `null` one with
  `ArgumentNullException`** (`Daml.Runtime`). `DamlRecord` compares and hashes its fields by
  content but kept the caller's `IReadOnlyList<DamlField>` by reference, so a caller that mutated
  that list after construction silently changed an already-computed hash code and made the record
  unfindable in a `HashSet` or dictionary that already held it. `Fields` is now copied at
  construction and on `init`, so `with { Fields = ... }` copies too, and
  `new DamlRecord(recordId, null!)` throws `ArgumentNullException` naming `Fields` instead of
  failing later with a `NullReferenceException`. Matches what `DamlList`, `DamlTextMap` and
  `DamlGenMap` already did.
- **Interface placeholder records are no longer emitted** (`Daml.Codegen.CSharp`). Daml-LF declares
  a same-named empty record beside every `interface I where …`; the emitter used to turn it into a
  `sealed record I : ITemplate` whose every static metadata accessor threw. Nothing referenced it —
  `ContractId<IMarker>` already serves contract-id fields and the generated choice extensions — so
  regenerating drops one file per interface. A choice returning a local interface-typed
  `ContractId` now matches created contracts against the marker's `InterfaceId` static, the same
  way a foreign one already did, instead of against a string literal baked in at codegen time.
- **`IHasView<TView>.View` is removed** (`Daml.Runtime`): the interface is now a member-less
  phantom link between a marker and its view type. Views are computed by the ledger, so the
  instance property was a promise no implementation could keep, and it would have collided with the
  marker's new static `View` witness. Implementations delete the member; view payloads now
  materialize as concrete view records from the read surface.
- **The shared Daml model in `Daml.Codegen.Intermediate` moved from namespace
  `Daml.Codegen.CSharp.Model` to `Daml.Codegen.Intermediate.Model`.** All 28 hand-written types the
  package ships — `DarModel`, `IDarSource`, `DamlPackage`, `DamlTemplate`, `DamlType`,
  `DamlDataType`, `PackageVersionParser` and the rest — were named after one consumer, the C#
  emitter, rather than after the package that ships them; the generated protobuf types in the same
  assembly already sit under `Daml.Codegen.Intermediate`. No type, member or signature changed.
  Migration is one line per file: `using Daml.Codegen.CSharp.Model;` →
  `using Daml.Codegen.Intermediate.Model;`.
- **`DamlTemplate.Fields` is removed** (`Daml.Codegen.Intermediate`). Neither producer treated the
  slot as authoritative: the parser-direct path always left it empty, and the intermediate wire
  format has no slot for it at all — payload fields travel solely on the module's same-named record
  `DataType` — so a populated value could never round-trip. Read the fields from that record
  definition instead: the `module.DataTypes` entry whose `Name` equals the template's, with
  `Definition is DamlRecordDefinition`. The emitter now sources fields the same way and throws
  `CodegenException` on a template without a same-named record definition, instead of silently
  emitting an empty payload.
- **`DamlTypeApp` compares its `Arguments` by content rather than by list identity**
  (`Daml.Codegen.Intermediate`). The compiler-synthesized record equality compared the
  `IReadOnlyList<DamlType>` member by reference, so two independently built but structurally
  identical type applications were unequal and hashed differently. `Equals(DamlTypeApp?)` and
  `GetHashCode()` are now written out and compare `Base` together with the arguments element by
  element. This is a correctness fix, but it is observable: two such values now compare equal where
  they did not, and their hash codes now agree, so code keying a dictionary or set on a
  `DamlTypeApp`, or de-duplicating a sequence of them, sees different behaviour.
- **`ProjectFileGenerator.GenerateProjectFile` drops its `emittedFiles` parameter**
  (`Daml.Codegen.CSharp`). It existed only to decide whether to pin `<LangVersion>13`, which no
  emission needs any more.
- **The `richtypes` conformance corpus is rebuilt, so its published surface moves**
  (`Daml.Codegen.Testing.Conformance`). `TypeCorners` gains a `crate` field carrying the new
  `Crate<string>` and a `maybeMaybeNote` field, an `Optional<Optional<string>>` covering the
  directly nested shape end to end; `maybeMaybeNote` is declared before `crate` rather than
  appended. It is a positional record, so this breaks twice over — the constructor's arity grows
  *and* every later slot moves — and any code constructing or deconstructing a `TypeCorners`
  positionally has to be updated, as do the compiler-generated `Deconstruct` and equality members.
  Every `PackageId` and `TemplateId` literal under `Daml.Codegen.Testing.Conformance.Richtypes`
  rotates with the rebuilt DAR, so anything pinning one of those hashes breaks with it. This is a
  conformance-fixture package rather than a production dependency, so the practical reach is small,
  but it is published and the break is real.
- The void `ThrowingExercise.ExerciseAsync` overloads (`Daml.Ledger.Abstractions`) now throw
  `LedgerOperationException` on a `None` outcome from `TrySubmitSingleAsync` instead of returning
  normally. At the writer level `None` reports that the submission produced no transaction, so a
  method whose contract is to throw on failure was reporting a non-commit as success. A `Many`
  outcome stays a success: the transaction committed, and only the discarded result is ambiguous,
  so throwing there would invite a caller to resubmit work the ledger has already accepted. No
  signature changes and no binary break; hand-written code that relied on a `None` being silently
  swallowed now sees an exception, and callers wanting the outcome rather than the exception should
  use `TrySubmitSingleAsync` directly.

### Changed

- **`Daml.Codegen.CSharp` logs through `Microsoft.Extensions.Logging`; the bespoke
  `ICodegenLogger` and `ConsoleLogger` types are removed.** `CSharpCodeGenerator`'s second
  constructor parameter is now an optional `ILogger<CSharpCodeGenerator>` and defaults to
  `NullLogger`, so `new CSharpCodeGenerator(options)` is a valid, silent construction. Hosts that
  already have an `ILoggerFactory` pass `factory.CreateLogger<CSharpCodeGenerator>()` and the
  emitter's progress and warnings land in their configured sinks; hosts that implemented
  `ICodegenLogger` implement `ILogger` instead, or wrap their sink in an `ILoggerProvider`. A
  ready-made provider ships as `VerbosityConsoleLoggerProvider`, which writes the same
  severity-prefixed lines the old `ConsoleLogger` did — errors and warnings to stderr, information
  and debug to stdout — gated by the same 0–3 verbosity scale. Both command-line entry points use
  it, so their console output is unchanged. Source-breaking for anyone constructing the generator
  with a logger or implementing the interface.
- Bump `Google.Protobuf` to 3.36.1 — raises the dependency floor of the `Daml.Codegen.CSharp`
  package.
- `Daml.Codegen.CSharp` no longer declares the `Microsoft.CodeAnalysis.CSharp` (Roslyn) dependency
  — it had no usages in the emitter, so every consumer was restoring Roslyn for nothing. Consumers
  that relied on picking Roslyn up transitively must reference it themselves.
- The `DarModel` / `DamlPackage` / `DamlTemplate` / `DamlType` / `PackageVersionParser` types now
  ship in the new `Daml.Codegen.Intermediate` package rather than in `Daml.Codegen.CSharp`.
  `Daml.Codegen.CSharp` depends on the new package, so a consumer restoring the emitter gets them
  either way; only the assembly they live in changed, alongside the namespace move recorded above.
- Codegen now fails loudly at generation time with a `CodegenException` (`Daml.Codegen.CSharp`)
  when a Daml type has no deserialization mapping or a choice argument type cannot be mapped to an
  argument record. Previously the emitter wrote a `default(...)!` fallback expression (a silent
  `null` at decode time) and an empty `{Choice}Arg` stub record into the generated code; neither
  can appear in generated output anymore. Higher-kinded generic fields (e.g.
  `DA.Monoid.Types.Endo`'s `f a`) still generate compilable code and now throw via
  `GenericStub.NotImplemented` on deserialization as well as serialization.
- Generated record declarations now carry one primary-constructor parameter per line, with the
  closing parenthesis and any base list on their own line, instead of a single line that ran past
  1,000 characters on the widest templates. This applies to every parameter list built from the
  Daml model — standalone records, templates, nested choice-argument records and choice-result
  structs — so adding a field is a field-level diff a reviewer can read. Emitter-fixed parameter
  lists (`ContractId`, `Contract`, variant cases) are unchanged. Generated code is layout-only
  affected: the emitted API is identical, and byte-determinism is unaffected.
- Generated `.csproj` files no longer pin `<LangVersion>13</LangVersion>`, and codegen no longer
  writes the `.daml-langversion` state file. Both existed solely to make the emitted partial `Key`
  property parse; with no such property emitted, key-bearing packages no longer impose a .NET 9+
  SDK floor on their consumers.
- Generated choice exercisers now call `TrySubmitSingleAsync` instead of inlining the
  submission-building block at every call site. Emitted output is unchanged in behaviour and
  materially smaller; a future fix to the submission shape now ships as a package bump rather than
  requiring every consumer to regenerate.
- Generated single-`Party` `<Choice>Async` overloads now forward to their `SubmitterInfo` sibling
  instead of restating the submit-and-project body, so each choice carries one submission body
  rather than one per overload. Call sites, parameter lists and defaults are unchanged, and the
  `Party` overload still wins overload resolution for a `Party` argument. Because the forwarding
  overloads are no longer `async`, argument-validation failures now throw synchronously rather than
  surfacing on the returned task — matching the `T.Contract` overloads, which already forwarded.
- Generated `<Choice>Async` exercisers for **interface** choices return the submission task
  directly instead of awaiting it. An interface choice has no typed `<Choice>Result` to project, so
  the awaited value was handed back unchanged and the state machine bought nothing. Signatures,
  parameter lists and defaults are unchanged; as with the single-`Party` overloads,
  argument-validation failures — a null `client`, a null `contractId`, a null choice argument — now
  throw synchronously rather than surfacing on the returned task.
  `ExerciseAsync`'s single-`Party` overloads (`Daml.Ledger.Abstractions`) delegate to their
  `SubmitterInfo` overloads for the same reason, with the same sync-throw note.
- `CreateByExercise` and the void `ThrowingExercise.ExerciseAsync` now always send a command id,
  minting one when the caller supplies none. They previously left `command_id` unset for the
  participant to assign, which meant a retry could not be deduplicated. This aligns them with the
  generated exercisers, which have always minted one.
- A `workflowId` of `""` passed to `ExerciseAsync` or the create-by-exercise extensions now leaves
  `workflow_id` unset on the wire instead of sending an empty one. The emitted code already treated
  empty as absent; the hand-written extensions tested only for `null`, so the two disagreed.
- `CommandsSubmission.WithOptionalWorkflowId` (`Daml.Runtime`) now treats a whitespace-only
  workflow id as absent, matching what it already did for `""` and what its own doc always said:
  `workflow_id` is a correlation key and a blank one correlates nothing. Previously
  `WithOptionalWorkflowId("   ")` stored the value verbatim and sent it on the wire, while the
  sibling projections `CommandId.FromWire` and `SynchronizerId.FromWire` mapped the same input to
  absent. `WorkflowId` itself is unchanged and stays permissive — its constructor accepts empty and
  whitespace because the Ledger API puts no non-empty constraint on the field — so a caller who
  genuinely wants to send a blank correlation key still can, via
  `WithWorkflowId(new WorkflowId(" "))`. This reaches generated choice exercisers too, since their
  optional `workflowId:` argument is routed through this overload by `TrySubmitSingleAsync`.
  Behaviour change for anyone passing a whitespace-only workflow id through either path.
- **`ExercisedEvent` and `CaughtException` compare their collection members by content rather than
  by reference** (`Daml.Runtime`). Both gain a hand-written `Equals`/`GetHashCode`:
  `ExercisedEvent` compares `ActingParties`, `WitnessParties` and `CaughtExceptions` element by
  element, and `CaughtException` compares `Metadata` key by key independently of insertion order —
  the idiom `DamlTextMap` already uses, because ledger-supplied metadata carries no meaningful
  order. This is what makes `TransactionResult` equality structural all the way down rather than
  only at the list level: two results projected from two separately-decoded trees of the same
  transaction now compare equal even when an exercise names a party, which they did not before.
  Both types are sealed, so a hand-written `Equals` drops only the `EqualityContract` check, which
  is a no-op for a sealed record — source- and binary-compatible.
- **Every event record in `Daml.Runtime.Contracts` now rejects a `null` collection member, and the
  four whose equality reads their collections also copy them** at construction and on `init`, so
  `with` expressions are covered too. `CreatedContract`, `TransactionResult`, `ExercisedEvent` and
  `CaughtException` copy, matching what `DamlList`, `DamlTextMap` and `DamlGenMap` already do in
  `Daml.Runtime.Data`: `IReadOnlyList<T>` is a read-only view, not an immutable collection, so a
  producer that retained its backing list could previously mutate an already-constructed value's
  hash code and leave it unfindable in a set or dictionary that already held it. `CreatedEvent`,
  `ArchivedEvent`, `TreeEvent.Created`, `TreeEvent.Exercised` and `TransactionTree` keep
  record-synthesized equality, which compares the collection member by reference — no hash reads
  its contents, so nothing can be corrupted, and copying there would instead narrow equality to
  near-identity (two events built from one shared list would stop comparing equal) while allocating
  on the hot stream-read path. Those five borrow the producer's collection, and their doc-comments
  now say so: it must not be mutated after construction. The null guard is uniform across all nine,
  naming the parameter, rather than letting a `NullReferenceException` surface later from inside
  `Equals` or from a consumer, far from the producer that got it wrong.
- `DamlLfJsonReader.ReadRecord` (`Daml.Runtime`) now rejects non-canonical Int64 wire text: a
  leading `+` or leading zeros (`"+42"`, `"007"`) is refused as a malformed Daml Int64, using the
  same canonical integer grammar the serializer already enforced. Captured wire samples never carry
  either form.
- `DamlLfJsonReader.ReadRecord` (`Daml.Runtime`) now fails loud with `NotSupportedException` when a
  generated enum companion's `ToDamlEnum` throws for a member, instead of silently degrading the
  whole enum's wire constructors to C# member names. Plain enums without a companion still fall
  back to member names.
- `DamlLfJsonReader` diagnostics (`Daml.Runtime`): a TextMap wider than `MaxArrayElements` is
  reported as a TextMap entry-count overflow rather than a "JSON array length" overflow; TextMap
  keys are bracket-quoted and escaped in error paths (`attributes['a.b']`); the unknown-enum-
  constructor "expected one of" list is sorted like the variant path's; oversized-value elision no
  longer splits a surrogate pair.
- `PartyJsonConverter` and `SynchronizerIdJsonConverter` (`Daml.Runtime`) are now one
  implementation instead of two copies that had drifted apart, so `Party` picks up the diagnostic
  its sibling already had: a rejected id is echoed back in the message (`Party id cannot be empty
  or whitespace; got '   '.`) rather than reported as a bare `Party id cannot be null or
  whitespace.`. Both still serialize as a plain JSON string and still reject a `null` on a
  non-nullable field.
- **Nothing populates `Contract.Key` yet.** `CreatedEvent.ContractKey` is declared here but filled
  in by the ledger client, which does not read `contractKey` off the wire today, so the slot stays
  unpopulated until a ledger client that does ships. Two platform behaviours to plan for: contract
  keys are **not unique** — several active contracts may share one, a by-key lookup or exercise
  resolves against a first match by an order the ledger only partly guarantees, and keeping a key
  unique is the application's responsibility — and externally signing a transaction that involves a
  key requires `HASHING_SCHEME_VERSION_V3`; V2 will not work.
- The `dpm codegen-cs` OCI bundle now ships the JVM helper as `bin/daml-dar-to-proto.jar`
  (previously `bin/daml-codegen-jvm-helper.jar`). The bundle layout is a published contract, so
  tooling that reaches into the bundle's `bin/` by name must use the new filename.
- The packaged `Daml.Runtime` README gains a worked `TransactionResultExtensions` section
  (`Single<T>`/`TrySingle<T>`/`All<T>` — project a committed transaction's created contracts to
  typed `ContractId<T>` values instead of re-implementing the scan), and the packaged
  `Daml.Ledger.Abstractions` README now documents the `ILedgerClient` vs `ICantonLedgerClient`
  two-interface model (depend on the interface, never downcast to a concrete client), the
  cancellation contract (caller cancellation surfaces as `OperationCanceledException`, never as an
  `InfraError` outcome or `LedgerOperationException` — call-site guards are deletable from
  `0.4.0-preview.1`), and the `Canton.Ledger.*` minor-tracks-`Daml.*` version mapping.

### Fixed

- **A Daml `Optional` over a parameterized type's own type variable now generates code that
  compiles.** `FromRecord` emitted `… ? convert(…) : null` for such a field, and `null` does not
  target-type to `TA?` for an unconstrained `TA`, so any package declaring `Optional a` inside a
  record or variant failed to build with `error CS1503: cannot convert from 'target-typed
  conditional expression' to 'TA?'`. The field is now held as `Optional<TA>`, which carries both
  inhabitants without relying on `null`. No package in the shipped corpus declared the shape before
  this release, so the failure only ever reached consumers generating from their own DARs.
- The LF-JSON reader now decodes a field typed by the stdlib `Set`, `NonEmpty` or `Map` record.
  `DamlLfJsonReader` walked a field's CLR type through arms that none of the three matched, so
  decoding a `Set k`, `NonEmpty a` or `DA.Map.Types.Map k v` field threw `NotSupportedException`
  naming the CLR type, even though the emitter generates those fields. Each now decodes to the wire
  record its stdlib `FromRecord` reads — `map` for `Set` and `Map`, `hd`/`tl` for `NonEmpty` —
  while a bare `GenMap` or `TextMap` keeps decoding to the dictionary primitive.
- `DamlLfJsonReader` now accepts the same `Time` wire shapes as `DamlJsonSerializer`. It parsed
  with the *emit* format string, which pins a literal `Z` and at most six fractional-second digits,
  so a `Time` field arriving with a numeric UTC offset (`2026-01-01T12:34:56+02:00`), with no zone
  designator, or with seven fractional digits was rejected as malformed even though
  `DamlJsonSerializer` decoded it. Both decoders now share one parse format, and both read an
  unzoned value as UTC. Emitting is unchanged: always UTC-normalized, `Z`-suffixed, with up to six
  fractional digits and trailing zeros trimmed.
- Fix `DamlNumeric.Value` throwing `OverflowException` for every value of a high-scale `Numeric`
  field, not only values that genuinely exceed `decimal`. A participant pads a Numeric out to its
  declared scale, so a `Numeric 37` slot carrying `1.5` arrives with 36 trailing zeros, and the
  narrowing rejected it on mantissa scale before considering whether the value fits — reporting
  `DamlNumeric value '1.5' has more precision than decimal can represent exactly`. Trailing
  mantissa zeros are now stripped and the narrowing retried, but only after exact narrowing fails,
  so a mantissa `decimal` already holds keeps the scale it arrived with and `1.50` stays `1.50`. A
  value that still needs more than 28 fractional digits once stripped throws as before.
- The twelve `WitnessParties` members across `ContractStreamEvent<T>`,
  `InterfaceStreamEvent<TInterface, TView>`, `AcsSnapshotEntry<T>` and
  `InterfaceAcsSnapshotEntry<TInterface, TView>` (`Daml.Runtime.Streams`) now reject a `null` value
  with `ArgumentNullException`, at both the primary constructor and the `init` accessor, matching
  the guard `Daml.Runtime.Contracts` event records already carry. These types are what
  `ILedgerStreamer` hands a consumer for every subscription and ACS snapshot; a transport that
  produced a `null` witness list used to be accepted silently and fail later with an unattributed
  `NullReferenceException`. No member, type or signature changed, so this is not source- or
  binary-breaking — only an input previously accepted now throws at the boundary.
- `DisclosedContract` now copies the `created_event_blob` bytes it is given, both at construction
  and through a `with` expression. The record wrapped the caller's `byte[]`, so a caller that kept
  the array and mutated it could change the disclosed payload — the artifact the participant
  authorizes the submission against — after `CommandsSubmission.WithDisclosedContracts` had already
  attached the contract, and could silently change the record's equality and hash code while it sat
  in a set or dictionary. Mutating the source array now has no effect on the contract.
- Generated `T.ContractId` now carries `[JsonConverter]`, so a DTO field declared as the emitted
  nested type serializes as a bare JSON string without `AddDamlConverters()`. System.Text.Json
  reads the attribute off the declared type and does not walk the base chain, so the attribute on
  `ContractId<T>` never reached the generated derived record — the type consumers actually hold —
  leaving it on the default object contract and giving one value two wire shapes.
- Generated active-contract code now resolves every in-package name it emits into the nested
  `Contract` record with a `global::` qualifier, instead of qualifying only a rendered name that
  spelled `Contract` or `ContractId` exactly. An `Optional` key over a package type of either name
  rendered as `Contract?` / `ContractId?`, missed the exact-name check, and bound the key slot and
  its decoder to the template's own nested type — emitting code that failed to compile
  (`'T.Contract' does not contain a definition for 'FromRecord'`). A key-less template's
  `Contract.FromCreatedEvent` never qualified its payload type at all, so a template named `Id`
  emitted `Id.FromRecord(…)` inside a static member where `Id` is the contract's own instance
  property (CS0120). Key slots and their decoders now carry `global::<RootNamespace>.` on
  in-package names in every position — including inside `Optional`, list and map arguments — and
  the key-less decoder qualifies its payload type as the key-bearing one already did.
- The emitted payload-derived `SubmitterInfo` and the emitted `Observers(payload)` helper each
  referenced a payload property by its undisambiguated name, producing code that does not compile
  when a field's PascalCased name equals its template name.
- `--generate-project` now emits restorable dependency `<PackageReference>`s. A dependency's Daml
  package version was reused verbatim as its NuGet version, taking this run's generation ordinal as
  the 4th segment (default `0`) and this run's prerelease suffix (default none) — but both describe
  how that *dependency* was published, and neither is recoverable from the DAR. With CLI defaults
  every dependency was therefore pinned to `M.m.p.0`, a version nobody published, and the generated
  project failed `dotnet restore` with `NU1103` before compiling a line. This affected any DAR with
  dependencies, independently of `--include-dependencies`. Dependency references now carry
  `M.m.p.*-*`, floating over whichever generation and prerelease tag were actually published against
  that intrinsic version; `dotnet pack` resolves the float to a concrete version in the produced
  `.nuspec`. A run started with `--release-counters` is the release path that publishes a whole
  family under one ordinal, so it knows the co-produced version exactly and still pins it.
- Party analysis now resolves template-level `signatory` and `observer` clauses, and clauses that
  delegate to another value in the same package. Expressions compiled from user-written source
  carry location annotations that the structural matcher did not look through, so those clauses
  fell back to `Dynamic` while synthesized ones resolved. This is what makes the derived-submitter
  `CreateAsync` shape above reachable at all.
- Party analysis now works on LF 2.2 and 2.3 archives. Cross-package references there may be
  spelled through the package import table, and expressions may be interned; the analyzer never
  dereferenced an interned expression on an application's callee spine, and did not recognise the
  import-table spelling of a cross-package reference, so a clause reached through either read
  `Dynamic`. `damlc` targets LF 2.2 by default, so this was the common case rather than an edge
  case.
- Party analysis now reports `Dynamic` when a payload-field projection names an interned string
  index that is out of range, instead of recording a static party whose field name is the reader's
  `<unknown:N>` placeholder. Every other out-of-range index on the analyzer's path already bailed
  this way; this one did not, and the resulting verdict travelled into the intermediate DAR.
  Generated code was unaffected — the emitter independently rejects a party field the template does
  not declare — so the effect was confined to the recorded analysis.
- Interface implementations now resolve the package the interface came from on LF 2.2 and 2.3
  archives. The DALF reader had no case for the package import table when resolving a template's
  `implements` entries, so it fell through to the self-package and recorded every implemented
  interface as locally defined. Ordinary type references were unaffected — they already resolved
  the table — and an interface is normally defined in a dependency, so this hit the common shape.
  `damlc` targets LF 2.2 by default.
- Daml-LF type conversion now applies its 256-level depth bound at every type position the
  DAR-direct parser reads — record fields, variant constructors, choice arguments and results,
  template keys, and interface views. That path carried a second, unbounded copy of the conversion,
  so a pathologically nested type expression in any of those positions was converted unchecked
  instead of failing with `InvalidDataException`; only the interned type pool was bounded. Both
  entry points now share one guarded traversal. The bound applies per traversal: it does not
  account for depth composed across interned type references.
- Fix generated `{Choice}Command()`/`{Choice}Async()` on `ContractId<T>` encoding the built-in
  `Archive` choice's argument as `Unit` on the create-projecting exerciser path — Canton's command
  preprocessor rejects `Unit` against the `Archive` signature. The argument now encodes as the
  empty record `DA.Internal.Template:Archive {}`, matching the choice descriptor and the
  non-contract exerciser path; genuine `Unit`-argument choices still encode `Unit`.
- The packaged `Daml.Ledger.Abstractions` README no longer claims the gRPC transport is "(planned,
  not yet published)": `Canton.Ledger.Grpc.Client`, the REST client, and the `Canton.Ledger.Testing`
  in-memory fakes are published to NuGet.org, and the README now names them.
- Generated interface markers no longer carry the two `//` comment lines the emitter wrote for each
  interface choice. `WriteInterfaceMethod` was a placeholder that declared no member and wrote only
  `// Interface method <Name>.` and `// Choice <Name>(<Arg>) -> <Return>` into the marker's body,
  describing a member the emitter never generated, and `GenerateXmlDocs = false` suppressed only
  the first of the two lines. The marker's declared surface is unchanged — nothing was declared
  before and nothing is declared now — so regenerating removes those comment lines from a
  consumer's interface files and adds nothing.
- Generated `<Choice>Async` doc comments no longer describe the `timeout` parameter as "enforced
  server-side". The deadline is applied best-effort by the transport, and transports without a
  server-side deadline apply a client-side bound only — matching what `ILedgerWriter` already
  documented.
- Generating a Daml template named `Contract` or `ContractId` now fails with a `CodegenException`
  naming the template, instead of emitting C# that cannot compile. The emitter nests records of
  both names inside the template record, and a nested type may not share its enclosing type's name
  (CS0542) — a declaration-site rule no qualification of the references can satisfy, so the
  previous behaviour was ten cascading build errors pointing away from the cause. The check runs on
  the sanitized C# name, so a template named `contract` is caught too.

### Security

- The party-expression analyzer now bounds how deep it walks a clause. Interned expressions and
  top-level values live in flat tables that a hand-crafted DALF can chain to any length without
  nesting a single proto message, so neither the parser's own proto nesting limit nor the
  analyzer's cycle guards — which stop revisits, not long chains of distinct nodes — constrained
  the walk. Such an archive drove it to an uncatchable `StackOverflowException`, which no caller
  can recover from: the codegen process dies mid-parse with no diagnostic naming the DAR or the
  clause, and the input is an untrusted third-party DAR. A clause deeper than the bound now
  resolves to `Dynamic`, the verdict every other unsupported shape already gets.

## [0.4.1-preview.1] — 2026-07-24

### Added

- Every generated `{Choice}Async` exercise method (interface choices, template
  choices, and non-creating template choices) is now accompanied by a
  `{Choice}Command(this ContractId<T>, ...)` extension/instance method that
  builds the choice's `ExerciseCommand` without submitting it — useful for
  batching several choices into a single ledger submission. `{Choice}Async`
  now calls the builder internally; its own behavior is unchanged.
- Splice 0.6.13 support, including the Token Standard V2 (CIP-0112) package set —
  the six net-new V2 API packages (`holding-v2`, `transfer-instruction-v2`,
  `transfer-events-v2`, `allocation-v2`, `allocation-instruction-v2`,
  `allocation-request-v2`) — each generated, packed, and proven consumable from a
  fresh C# project. The `splice-token-standard-utils` helper library emits no C#
  types, so it is intentionally not published as a NuGet package. A new offline
  `samples/TokenStandardV2` console app showcases the V2 surface.
- Generated template records now declare `IImplements<TInterface>` for each Daml
  interface a template implements via `interface instance` (previously the
  `implements` clause was parsed but produced no interface conformance in the
  generated C#), so consumers can treat a contract through its implemented
  interfaces.
- Templates now generate an `ArchiveAsync` extension on their non-contract exerciser
  (previously only interfaces did). The wire argument for the synthetic `Archive` choice
  encodes as the empty record `DA.Internal.Template:Archive {}`, consistent with the fix
  in the built-in `Archive` choice below.
- The single-`Party` convenience overloads on `ILedgerStreamer` now include
  `SubscribeLedgerEffectsAsync(actAs, ...)`, mirroring the existing `SubscribeAsync`
  and `SubscribeActiveAsync` party overloads (previously ledger-effects streaming
  required constructing a `SubmitterInfo` by hand).
- **`Daml.Runtime.StakeholderResume`**: a resume ticket wrapping `LedgerOffset` (no implicit
  conversion), handed back by `AcsSnapshotEntry<T>.Checkpoint.Resume`. `ILedgerStreamer.SubscribeAsync`
  gains an overload accepting it for a gapless snapshot→stream handover; `SubscribeLedgerEffectsAsync`
  deliberately gains none, so crossing a stakeholder-based snapshot into the witness-based
  ledger-effects stream no longer compiles. The raw offset stays reachable via `.Resume.Offset`
  for a deliberate cross-basis resume.
- **`Daml.Ledger.Abstractions.Testing.Conformance`**: an opt-in submitter-authority check. Override `CreateWriteFixture()` on `LedgerClientConformanceTests<TProbe>` to prove your `ILedgerClient` implementation applies the `submitter` parameter of `SubmitAndWaitAsync`/`TrySubmitAndWaitForTransactionAsync` authoritatively via `CommandsSubmission.WithSubmitter`, rather than dispatching whatever `ActAs` the caller pre-set on the submission. Leaving it at its `null` default skips the two new `[Fact]`s, matching the existing opt-in pattern for `CreateFaultingSnapshotClient()`.

### Changed — BREAKING

- **`AcsSnapshotEntry<T>.Checkpoint` now carries a `StakeholderResume Resume` instead of a bare
  `LedgerOffset Offset`**. Callers reading the terminal checkpoint's offset now use
  `checkpoint.Resume.Offset`; callers constructing one pass `new StakeholderResume(offset)`.
- **`ILedgerWriter.SubmitAndWaitAsync` and `ILedgerWriter.TrySubmitAndWaitForTransactionAsync` now take a mandatory `SubmitterInfo submitter` parameter**, positioned right after the `CommandsSubmission submission`. Implementations must apply it via `submission.WithSubmitter(submitter)` before dispatch, which is authoritative and overwrites any `ActAs`/`ReadAs` already set on the passed-in submission. Previously, `CommandsSubmission.ActAs`/`ReadAs` were nullable and nothing stopped calling these methods with zero authorization, unlike `TryExerciseAsync`/`TryCreateAsync`, which already require a non-empty `SubmitterInfo` by construction. Callers that previously called `.WithSubmitter(submitter)` or `.WithActAs(actAs)` on the submission before dispatch now pass `submitter` directly to the method instead.

### Changed

- Interface-placeholder record XML docs now explain why every static accessor
  throws by design and clarify that interface choices are exercised directly on
  `ContractId<I>` via the generated extension methods — no coercion to a
  concrete `ContractId<TConcrete>` is needed or supported.
- Upgraded the DAR archive-reader to the stable `com.daml:daml-lf-archive` 3.5.9. Generated code now supports DARs targeting Daml-LF 2.1, as emitted by the Daml 3.4 and 3.5 SDK lines; generated output is byte-for-byte unchanged.
- Rebuilt the conformance and Quickstart fixtures with the stable Daml SDK 3.5.2 compiler (was 3.4.11), keeping the `--target=2.1` LF target. The generated C# type surface is unchanged (same types, fields, and signatures); only each fixture DAR's embedded package-hash / package-id changes.
- The Splice publish pipeline now skips DARs that emit no C# types instead of packing an empty placeholder assembly, so zero-type libraries (`Splice.Token.Standard.Utils`, `Splice.Util`) no longer produce empty NuGet packages on the feed.

### Fixed

- Parameterized generic Daml records and variants now round-trip through the
  generated `ToRecord`/`FromRecord`/`ToVariant`/`FromVariant` surface instead
  of throwing at runtime (previously any user-defined generic type outside the
  six hardcoded stdlib parametric types — `Set`, `NonEmpty`, `Either`,
  `Tuple2`, `Tuple3`, `Map` — hit an unimplemented-deserialization stub).
  - Generic records/variants now emit one extra `Func<T, DamlValue>` /
    `Func<DamlValue, T>` converter-delegate parameter per type parameter on
    `ToRecord`/`FromRecord`/`ToVariant`/`FromVariant`, and no longer implement
    `IDamlRecord`/`IDamlVariant` (whose members are parameterless) — a
    generated-code shape change for any consumer using a generic record or
    variant. Non-generic records/variants are unaffected.
- The built-in `Archive` choice now encodes its **argument** as the empty record
  `DA.Internal.Template:Archive {}` (`DamlRecord.Create()`) instead of `Unit`. Canton's
  gRPC command preprocessor type-checks the choice argument against the LF choice
  signature and rejected the `Unit` with `COMMAND_PREPROCESSING_FAILED: mismatching type:
  DA.Internal.Template:Archive and value: Unit`, so archiving over gRPC failed (the
  JSON/REST and in-memory fakes don't validate the argument shape, which is why it went
  unnoticed). The fix swaps the wire encoding at every site that ships the argument: the
  generated choice descriptor's `ArgumentEncoder`, the generated interface `ArchiveAsync`
  helper, and the runtime `IExercises<T>.ExerciseArchive()` (`Daml.Runtime`). The generated
  `Choice<T, DamlUnit, DamlUnit>` signature is unchanged (no source break), and the `Unit`
  **result** type was already correct and is untouched.
- Generated exercisers for choices returning Daml's builtin `Unit` now emit a
  fully `global::`-qualified reference to `Daml.Runtime.Stdlib.Unit` even when
  the target Daml package declares its own top-level type also named `Unit`
  (e.g. `splice-wallet-payments`'s `enum Unit`); previously the bare `Unit`
  reference bound to the package-local type and failed with `CS0117`.

## [0.4.0-preview.3] — 2026-07-19

### Fixed

- Generated exercise wrappers (`ContractId<T>.<Choice>Async`) can express `readAs`
  again. When a choice's controllers resolve statically the emitter had replaced the
  `SubmitterInfo`-accepting overload (shipped through 0.3.0-preview.1) with a single
  ergonomic `Party` overload, so a submitter could no longer supply `readAs` parties.
  A choice whose created contracts are visible to an observer but not to the submitter
  then projected no created contracts for the submitter and surfaced as
  `ExerciseOutcome.None` — a committed success that read back as a failure. The emitter
  now emits the `SubmitterInfo` overload alongside the named-`Party` overload for every
  create-bearing choice, restoring `readAs` on the generated surface while keeping the
  single-`Party` ergonomics.

## [0.4.0-preview.2] — 2026-07-18

### Added

- `AcsSnapshotEntry<T>.StreamError(int StatusCode, string Message)` (`Daml.Runtime`): a
  new variant of the active-contract-set snapshot union that surfaces a mid-snapshot
  transport fault in-band instead of throwing, mirroring
  `ContractStreamEvent<T>.StreamError` on the live subscription. It is terminal and
  mutually exclusive with `AcsSnapshotEntry<T>.Checkpoint` — a faulted snapshot ends with
  `StreamError` in place of the `Checkpoint` a successful snapshot ends with — so a caller
  draining `ILedgerStreamer.SubscribeActiveAsync` with `await foreach` handles snapshot
  faults as values under the same fault contract as `SubscribeAsync`. `StatusCode` is held
  as `int` (`(int)Grpc.Core.StatusCode` for gRPC) so the runtime type takes no
  transport-library dependency. Additive: the union stays closed (`private protected`
  constructor) and existing `Created`/`Unclassified`/`Checkpoint` handling is unaffected.
- `LedgerClientConformanceTests<TProbe>` (`Daml.Ledger.Abstractions.Testing.Conformance`)
  now asserts the snapshot fault contract: a new opt-in test
  `Active_snapshot_surfaces_a_mid_snapshot_fault_as_StreamError` verifies that a
  mid-snapshot transport fault surfaces as a terminal `AcsSnapshotEntry<T>.StreamError`
  (and yields no terminal `Checkpoint`), rather than being thrown. Adopters enable it by
  overriding the new `CreateFaultingSnapshotClient()` seam to supply a client whose
  snapshot faults mid-stream; it defaults to `null` and the check is skipped, because
  inducing a deterministic mid-snapshot fault is transport-specific.
- `DamlNumeric.ToCanonicalString()` and `DamlNumeric.TryParseCanonical(string, out DamlNumeric)`
  (`Daml.Runtime`) are now `public` (previously `internal`, and undocumented on the public
  surface despite appearing in the shipped `Daml.Runtime.xml`). They are the lossless
  canonical-wire round-trip primitives for Numeric values whose magnitude or fractional
  precision exceeds `decimal` (up to 38 significant digits, scale 0-37) — the `decimal`-based
  constructor and `Value` accessor cannot express or read back such values, so transport
  bridges now serialize via `ToCanonicalString()` and deserialize via `TryParseCanonical(...)`.
- `ILedgerStreamer.SubscribeLedgerEffectsAsync<T>` (`Daml.Ledger.Abstractions`) — a new
  streaming method exposing the **ledger-effects** transaction shape: it emits `Created` and
  `Exercised` events (a consuming `Exercised`, `Consuming == true`, is the archival signal on
  this shape) and never `Archived`, with witness-based visibility. Signature mirrors
  `SubscribeAsync<T>` (`SubmitterInfo`, exclusive `fromOffset` / inclusive `toOffset`,
  `CancellationToken`). Unlike `SubscribeAsync<T>` it must **not** be paired with a
  `SubscribeActiveAsync<T>` snapshot for a resume handover — the two use different visibility
  bases (witnesses vs stakeholders) and reconstruct different contract sets. No single-`Party`
  convenience overload is added (`SubmitterInfo`-only; a twin is additive later if wanted).
  Adding an interface member is **source-breaking for implementers**, who must implement the
  new member; non-breaking for consumers.

### Changed

- `ILedgerStreamer.SubscribeAsync<T>` is now contractually the **ACS-delta**-shaped stream:
  it emits `Created`/`Archived` (never `Exercised`) with stakeholder-based visibility, so it
  matches the `SubscribeActiveAsync<T>` snapshot — a snapshot followed by a resume rebuilds
  the same contract set, and the documented cache/checkpoint pattern (evict on `Archived`) is
  sound. Its XML contract previously described the ledger-effects shape, whose `Archived` arm
  was unreachable; that shape now lives on the new `SubscribeLedgerEffectsAsync<T>` (see
  Added). This is an XML-contract change on the transport-agnostic abstraction; concrete gRPC
  transports adopt the new stream shape in their own releases.
- **BREAKING: `ContractStreamEvent<T>.Unclassified.Kind` (`Daml.Runtime`) is now the
  strongly-typed `UnclassifiedKind` enum instead of a `string`**, and the variant gains a
  nullable `RawKind` string. Consumers `switch` on the enum — so the compiler catches a
  missing or misspelled arm — instead of comparing magic strings. The enum enumerates the
  reasons an event is surfaced unclassified (a create/archive/exercise/assign/unassign
  event whose contract is not of the subscribed marker, a missing synchronizer id, an
  unavailable interface view, a decode failure, an empty reassignment) plus `Unknown` for a
  transport-delivered variant this layer does not recognise; in the `Unknown` case the
  transport's raw descriptor is carried on `RawKind`, preserving forward-compatibility with
  server event variants added later. The `RawKind`/`Kind` relationship is a guaranteed
  invariant, not just a convention: the constructor throws `ArgumentException` unless `RawKind`
  is non-`null` exactly when `Kind` is `Unknown` (and `null` for every enumerated reason), and
  `Kind`/`RawKind` are get-only so a `with`-expression clone cannot reassign them and strand the
  invariant (only `Offset` is `with`-able) — so a consumer never sees a stale descriptor on a
  named kind. Source-breaking for code that constructs `Unclassified` with a string
  discriminator or reads `Kind` as a string; construct with an `UnclassifiedKind` value (and,
  for `Unknown`, pass the raw descriptor as `RawKind`).

## [0.4.0-preview.1] — 2026-07-17

### Added

- Unary write/read operations — `TryExerciseAsync`, `SubmitAndWaitAsync`,
  `TrySubmitAndWaitForTransactionAsync`, `TryCreateAsync` (`ILedgerWriter`),
  `GetLedgerEndAsync` (`ILedgerReader`), the create-by-exercise extensions (see the
  one/many split under Changed), and the
  `Party` convenience overloads — accept an optional `TimeSpan? timeout` applied
  best-effort by the transport (for gRPC, mapped to `CallOptions.Deadline`). Streaming
  operations deliberately take no wall-clock deadline: the `CancellationToken` remains
  the sole time bound (use `CancelAfter` for a watch window, `break` for an event
  boundary).
- Generated `<Choice>Async` exercisers (both the `ContractId<T>` receiver and the
  non-contract-returning overloads) and generated interface-choice extension methods now
  accept an optional `TimeSpan? timeout`, positioned before the trailing
  `CancellationToken`, and forward it to `ILedgerWriter.TrySubmitAndWaitForTransactionAsync`
  as the per-call best-effort deadline. The default `null` leaves the emitted call
  behaviourally identical to previous output, so regenerating against this release is a
  source- and runtime-compatible change for existing call sites.
- `ILedgerStreamer.SubscribeAsync` accepts an optional end-inclusive `LedgerOffset? toOffset`;
  when set, the stream yields the updates in `(fromOffset, toOffset]` and then completes
  normally — bounded historical replay for audits, backfills, and deterministic tests
  without cancellation plumbing. The `(fromOffset, toOffset]` window is now normative in
  the interface docs — `fromOffset` stays exclusive, so resuming from a returned
  checkpoint or completion offset never re-delivers the event at that offset — and both
  boundaries are conformance-tested.
- `ILedgerStreamer.SubscribeActiveAsync` accepts an optional `LedgerOffset? activeAtOffset`
  and now ends every snapshot with a terminal `AcsSnapshotEntry<T>.Checkpoint` carrying
  the snapshot's effective offset, so consumers resume `SubscribeAsync` from exactly the
  snapshot boundary instead of handling duplicates around a separately-fetched ledger end.
  The terminal `Checkpoint` is guaranteed even when the snapshot is empty.
- New package `Daml.Ledger.Abstractions.Testing.Conformance`: an abstract xUnit base,
  `LedgerClientConformanceTests<TProbe>`, that an `ILedgerClient` implementation
  subclasses to verify the documented behavioral contract — a cancelled subscription
  surfaces `OperationCanceledException`, an unclassifiable snapshot row surfaces as
  `AcsSnapshotEntry<T>.Unclassified` instead of being dropped, the active-contract-set
  snapshot always ends with a terminal `Checkpoint` (even when the snapshot is empty),
  seeded rows arrive before it, and bounded subscriptions honor the
  `(fromOffset, toOffset]` window on both boundaries. Streams the contract requires to
  terminate run under the overridable `StreamTimeout` budget (30 seconds by default), so
  a non-terminating implementation fails the named conformance test with a
  contract-naming `TimeoutException` instead of hanging the adopter's test run. Adopters
  whose transport rejects an active-contract-set query at offset 0 override
  `EmptySnapshotOffset` to point the empty-snapshot check at a known-empty offset.
- `ExerciseOutcome<TransactionResult>.ProjectCommitted<TProjected>(Func<TransactionResult, ExerciseOutcome<TProjected>>)`
  (`Daml.Runtime.Outcomes`): projects a transaction-level outcome onto a typed result outcome — invoking the
  projector on a committed transaction and propagating `None`/`Many`/`DamlError`/`InfraError` faithfully. The
  generated `<Choice>Async` exercisers now delegate their outcome mapping to it, so the exhaustive per-variant
  handling lives and is unit-tested in one place.

### Changed

- **BREAKING: `ILedgerClient` (`Daml.Ledger.Abstractions`) is now the composition of
  three capability interfaces** — `ILedgerWriter` (submit commands, create contracts,
  exercise choices), `ILedgerReader` (query the ledger end), and `ILedgerStreamer`
  (subscribe to contract events and active-contract-set snapshots) — instead of one flat
  interface declaring every member directly; `ILedgerClient` itself now only adds the
  `IDisposable`/`IAsyncDisposable` bridge. Derived convenience methods move out of
  `Daml.Ledger.Abstractions` entirely and into the new `Daml.Ledger.Abstractions.Extensions`
  namespace, opt in with `using Daml.Ledger.Abstractions.Extensions;`: the single-`Party`
  overloads of `TryExerciseAsync`/`TryCreateAsync`/`SubscribeAsync`/`SubscribeActiveAsync`,
  the throwing `ExerciseAsync` convenience wrappers (including the void overloads, which no
  longer route through a discarded `object` result sentinel), and the create-by-exercise
  helpers (replacing `TryExerciseForCreatedAsync` — see the one/many split entry
  below). The primitives that take an explicit
  `SubmitterInfo` remain directly on `ILedgerWriter`/`ILedgerStreamer` with no extra `using`.
  Source-breaking for implementers, who now override the split interfaces instead of one
  flat `ILedgerClient`; source-breaking for consumers who called a moved convenience method,
  since every such call site now needs a new `using Daml.Ledger.Abstractions.Extensions;`
  import that a plain `using Daml.Ledger.Abstractions;` did not previously require.
- **BREAKING: create-by-exercise is an explicit one/many split** — `TryExerciseForCreatedAsync`
  is replaced by `TryCreateOneByExerciseAsync` and `TryCreateManyByExerciseAsync`, plus the
  throwing `CreateOneByExerciseAsync`/`CreateManyByExerciseAsync` wrappers
  (`Daml.Ledger.Abstractions.Extensions`). `TryCreateOneByExerciseAsync` expects exactly one
  created contract and propagates every outcome faithfully — in particular a `Many` returned
  by the writer is never collapsed into `None`, so a committed transaction can no longer be
  misread as "created nothing" and resubmitted as a duplicate. `TryCreateManyByExerciseAsync`
  accepts any number of created contracts: success is `One` carrying the (possibly empty)
  `IReadOnlyList<ContractId<T>>`, and a single create yields a one-element list.
  `CreateOneByExerciseAsync` throws `LedgerOperationException` when the choice created none
  or many; `CreateManyByExerciseAsync` returns the list and throws only on error outcomes.
- **BREAKING: `LedgerOffset` (`Daml.Runtime`) replaces raw `long` ledger offsets across
  `Daml.Ledger.Abstractions` and `Daml.Runtime`** — `ILedgerReader.GetLedgerEndAsync`,
  `ILedgerStreamer.SubscribeAsync`'s `fromOffset`/`toOffset`, `SubscribeActiveAsync`'s
  `activeAtOffset`, every offset carried on `ContractStreamEvent<T>`/`AcsSnapshotEntry<T>`,
  and the write-path completion offsets — `SubmitAndWaitResult.CompletionOffset`,
  `TransactionResult.CompletionOffset`, and `TransactionTree.CompletionOffset` —
  now use the value type instead of a bare `long`. Construct one with `LedgerOffset.Begin`
  (start of stream) or `LedgerOffset.At(value)`; read the raw value back via `.Value`. This
  closes the same silent-transposition footgun the project has already closed for
  `ContractId`/`Party`/`ChoiceName` — an offset can no longer flow unnoticed into an
  arbitrary `long` parameter.
- **BREAKING: `ILedgerStreamer.SubscribeActiveAsync<T>` now returns
  `IAsyncEnumerable<AcsSnapshotEntry<T>>`** (was `IAsyncEnumerable<ContractStreamEvent<T>>`),
  a discriminated union scoped to the active-contract-set snapshot: `Created`,
  `Unclassified` (an unrecognized row, surfaced rather than dropped), and a terminal
  `Checkpoint` carrying the snapshot's effective offset. Source-breaking: implementations
  must return the new type, and consumers exhaustively pattern matching over the stream
  need to switch from `ContractStreamEvent<T>`'s cases to `AcsSnapshotEntry<T>`'s.
- `ContractStreamEvent<T>.Assigned` and `.Unassigned` (`Daml.Runtime`) now carry
  `ReassignmentId` (string) and `ReassignmentCounter` (long), positioned after
  `Target` and before `WitnessParties`. Canton stamps an unassignment and its
  completing assignment with the same reassignment id and counter, so a consumer
  can pair the two halves of a cross-synchronizer move, detect a missed hop, and
  dedup replays by matching them — the pairing rule the reassignment protocol
  documents. Source-breaking for code that constructs these two variants
  positionally (two new required parameters); readers gain two properties.
- The README emitted into every generated package now points consumers at `ILedgerWriter`
  for command submission (with `ILedgerReader`/`ILedgerStreamer` for reads and streams)
  instead of the pre-split `ILedgerClient`.
- Generated `<Choice>Async` exercisers (both the `ContractId<T>` receiver and the non-contract-returning
  overloads) now map a writer-level `None`/`Many` outcome to a structured `ExerciseOutcome<TResult>.None`/`.Many`
  (via `ExerciseOutcome<TransactionResult>.ProjectCommitted`) instead of throwing a generic
  `InvalidOperationException`. A `Many` carries the committed contract ids, so — as with the create-by-exercise
  one/many split — a committed transaction can no longer be misread as a submission failure and blindly
  resubmitted as a duplicate. Migration: a call site that caught `InvalidOperationException` around a generated
  `<Choice>Async` to handle these outcomes now receives them as `ExerciseOutcome<TResult>.None`/`.Many` on the
  returned outcome and should handle them in its `switch`; regenerate against this release to pick up the new shape.

### Removed

- The C# emitter machinery (`ChoiceEmitter`, `RecordEmitter`, `TemplateEmitter`,
  `EnumEmitter`, `VariantEmitter`, `InterfaceEmitter`, `RecordSerializationEmitter`,
  `SubmissionExtensionsEmitter`, `DamlTypeMapper`, `PackageEmitContext`,
  `TypeReferenceQualifier`, `ICrossPackageResolver`, `DarCrossPackageResolver`, and
  `PartyAnalysis`) is no longer public on `Daml.Codegen.CSharp`. These types are
  implementation detail used only within the codegen pipeline, not a supported
  extension point; the public entry points remain `CSharpCodeGenerator`,
  `CodeGenOptions`, and `IntermediateDarReader`.

### Fixed

- Generated bindings now compile for a record field whose type is a nested map — `Map k1 (Map k2 v)`,
  including the `TextMap`-of-`TextMap` and list-leaf variants. The deserializer left each map's
  `ToDictionary(...)` result uncast, so a map-of-map inferred a concrete
  `Dictionary<K1, Dictionary<K2,V>>` value type that is not assignable to the declared
  `IReadOnlyDictionary<K1, IReadOnlyDictionary<K2,V>>` (CS1503, because `IReadOnlyDictionary`
  is invariant in its value). Each generated map deserialization now casts to its declared
  `IReadOnlyDictionary<...>` at every nesting level, mirroring the existing list-leaf cast.
  Single-level-map consumers are behaviourally unaffected.

- Generated bindings now compile when a Daml name collides with a C# keyword. The emitter
  escaped such names once, up front, and reused that one string across grammars that
  disagree about the escape. Three defects followed. A choice whose controller field is a
  keyword (e.g. `operator : Party`) emitted `<param name="@operator">` against a parameter
  named `operator`, which fails any consumer building with XML documentation enabled
  (CS1572/CS1573) — doc `name=` attributes take the bare identifier. A Daml type variable
  named after a keyword (e.g. `event`) emitted the type parameter `T@event`, which C#
  parses as two identifiers rather than one. A payload field cased as `Operator` escaped
  nothing, then camelCased to a bare `operator`. Escaping now runs last, after casing and
  prefixing, and the doc emitter names the unescaped identifier. Generated public API is
  unchanged: parameters and properties keep their `@` escape.

- Distinct Daml type names no longer collide onto one C# type. The emitter rewrote every
  character C# forbids to `_`, which is lossy: a Daml `Foo'` (damlc-mangled to `Foo$u0027`)
  and a literal `Foo_u0027` both emitted `Foo_u0027`, producing two `Foo_u0027` declarations
  in one namespace (CS0101). Type-path sanitisation now demangles damlc's `$uXXXX`/`$$`
  mangling and escapes forbidden characters into an injective `_uXXXX` form, so distinct
  names stay distinct. This affects only type-ish names (templates, records, variant
  constructors, type names); the field/member path is unchanged, so Daml Finance's `y'`/`m'`/`d'`
  fields still emit `YU0027`/`MU0027`/`DU0027` as before. No emitted identifier in the current
  corpus changes.

- Generated bindings no longer silently drop choice-argument fields when two same-named
  data types live in different modules of the same package. The emitter's data-type
  lookup was keyed by simple name with last-wins semantics, so a choice-argument record
  could be emitted from the wrong module's type — the nested record, its
  `ToRecord`/`FromRecord`, and the `<Choice>Async` parameters all lost fields, compiling
  cleanly but failing at exercise time on the ledger. The lookup is now module-qualified,
  so each choice resolves the argument type declared in its own module.
- `DamlJsonSerializer` (`Daml.Runtime`) now rejects oversized JSON inputs and
  over-broad JSON arrays before materializing schemaless values, limiting
  shallow allocation-amplification inputs while keeping configurable limits for
  callers that need a tighter envelope.
- `DamlJsonSerializer` (`Daml.Runtime`) now rejects duplicate keys when deserializing
  GenMap-shaped JSON arrays, emits timestamps with the canonical UTC `Z` designator, and
  runtime value constructors defensively copy caller-owned collection and byte-array inputs.
- Codegen now rejects excessively deep Daml-LF type shapes with `InvalidDataException`
  before managed type conversion or value-mapping recursion can overflow the stack, and
  generated created-contract projectors now emit the leftover-copy loop without a
  redundant outer guard.
- `ThrowingExercise` (`Daml.Ledger.Abstractions.Extensions`) now preserves the original
  transport exception as `LedgerOperationException.InnerException` when an
  `ExerciseOutcome.InfraError` carries one, so throwing wrappers retain transport
  details such as trailers, retry metadata, and stack traces.
- Generated `<Choice>Async` exercise wrappers accept an optional `CommandId?`
  parameter, defaulting to a fresh id only when the caller omits one, instead of
  always minting a new `Guid`-derived command id. Retrying a lost-but-accepted
  submission with the same id now lets the ledger deduplicate the resubmission
  instead of re-executing the choice.
- The throwing `Try*` convenience wrappers on `ThrowingExercise` (`Daml.Ledger.Abstractions.Extensions`)
  no longer mask caller cancellation as an infrastructure failure: when the caller's
  `CancellationToken` is cancelled, an `ExerciseOutcome.InfraError` outcome now surfaces as
  `OperationCanceledException` instead of `LedgerOperationException`, so `catch
  (OperationCanceledException)` around a unary call behaves the same as it already does
  around the streaming methods.
- `ContractStreamEvent<T>` (`Daml.Runtime`) and `ILedgerStreamer.SubscribeAsync`
  (`Daml.Ledger.Abstractions`) XML docs now state which variants each stream shape
  actually emits. The live update subscription uses ledger-effects shape and never
  emits `Archived` — a consuming `Exercised` (`Consuming == true`) is a contract's
  archival on that shape, and `Archived` appears only on ACS-delta-shaped streams.
  The previous docs promised `Archived` on the live stream and told consumers to
  evict caches and checkpoint on it, guidance that would leave archived contracts
  cached forever (later exercises then failing `CONTRACT_NOT_FOUND`). Consumers
  maintaining a cache on the live stream must evict on the consuming `Exercised`,
  not on `Archived`.
- `DamlNumeric` (`Daml.Runtime`) now backs its value with a sign, `BigInteger` mantissa,
  and scale instead of `decimal`, so it round-trips any legal Daml-LF Numeric (up to 38
  significant digits, including magnitudes above `decimal.MaxValue`) with zero precision
  loss. `DamlJsonSerializer` no longer silently rounds excess precision on deserialize;
  `DamlNumeric.Value` now throws `OverflowException` instead of rounding when the stored
  value has more precision than `decimal` can represent exactly.
- The static party-expression analyzer now resolves a template's `signatory`/`observer`
  clause (and choice `controller`/`observer` clauses) through this SDK's generic-template
  dictionary-method indirection — a function application of the template's payload to a
  same-package top-level value, optionally type-applied — instead of unconditionally
  falling back to a dynamic submitter parameter. Templates whose party clause resolves
  through exactly this one level of indirection to a plain payload-field reference (or a
  literal empty list) now get the typed `actAs`/named-signatory codegen surface instead of
  the generic `SubmitterInfo` fallback.
- The static party-expression analyzer now also resolves a choice `controller <field>` /
  `observer <field>` clause that compiles to the per-choice `App(Val(<self>), [this, arg])`
  indirection whose body reduces to `\this -> let ds = this.<field> in \arg -> toParties ds`,
  binding the controller to a single payload-field `Party`. A generated `<Choice>Async`
  exerciser for such a choice now takes a typed `Party` parameter per controller (in
  declaration order) plus a payload-reading contract overload that reads the parties off the
  fetched contract, instead of a single explicit `SubmitterInfo submitter`. For example the
  Quickstart `Iou.Transfer` exerciser now takes `Party owner`. The one-argument
  template-`signatory` twin of this idiom stays a dynamic submitter as before.
- Generated `<Choice>Async` exercisers with statically-resolved controllers now emit an XML
  doc `<param>` tag for every controller and observer `Party` parameter, so a consumer
  project that compiles the generated code with documentation generation and
  warnings-as-errors no longer fails with CS1573.
- `DamlJsonSerializer`'s schemaless deserialize path now infers a bare integer string
  (e.g. `"1234"`, the JSON Ledger API's preferred `Int` encoding) as `DamlInt64` instead
  of `DamlText`, so generated `.As<DamlInt64>()` accessors no longer throw
  `InvalidCastException` on values round-tripped through the untyped path.
- `DamlDate.FromDaysSinceEpoch` and `DamlTimestamp.FromMicrosecondsSinceEpoch`
  (`Daml.Runtime`) now use checked arithmetic and validate the result against the
  Daml-LF Date/Timestamp bounds (`0001-01-01` to `9999-12-31`), throwing
  `ArgumentOutOfRangeException` instead of silently wrapping to a valid-looking but
  wrong date or timestamp for out-of-range or overflowing input.
- `Daml.Codegen.Testing.Conformance`'s generated `IHolding` interface now also exposes
  `public static new Identifier InterfaceId` (previously reachable only through
  the explicit `IDamlInterface.InterfaceId` implementation), matching the
  interface-identifier shape the emitter has produced for every other generated
  interface since the CS0117 interface-matching fix; the package's checked-in
  generated tree had never been regenerated to pick it up.

### Security

- The internal DAR/DALF reader (used by the codegen pipeline) no longer
  recurses without bound when a hand-crafted DALF's signatory/observer/controller expression contains a
  cyclic interned-expression reference; it now falls back to the dynamic (explicit-submitter) party path
  instead of crashing the process with an uncatchable `StackOverflowException`.
- The internal DAR/DALF reader now caps per-entry (256 MiB) and total (1 GiB) decompressed size before
  inflating a `.dalf` zip entry, and re-enables the protobuf message-size backstop it had previously
  disabled, so a zip-bomb DAR fails fast with `InvalidDataException` instead of exhausting memory.
- The `IntermediateDar` name-validation gate now anchors its identifier and package-coordinate
  regexes with `\A`/`\z` instead of `^`/`$`, closing a bypass where a name ending in a newline
  (e.g. a template or choice name of `"Foo\n"`) slipped past validation — `$` matches immediately
  before a trailing newline in .NET, `\z` does not.

## [0.3.0-preview.1] — 2026-07-04

### Added

- `Daml.Runtime.Contracts.SubmitAndWaitResult(CommandId CommandId, string UpdateId, long CompletionOffset)`
  — a new record carrying the effective command id the participant recorded for a
  fire-and-wait submission, together with the resulting transaction's update id and
  completion offset. Returned by `ILedgerClient.SubmitAndWaitAsync` (see Changed) so
  callers can correlate a completion with the command id used for deduplication — even
  when that id was assigned by the client rather than supplied by the caller.
- `TransactionResult` (`Daml.Runtime`) gains a trailing `CommandId CommandId` positional
  parameter, surfacing the effective command id of the submission that produced the
  transaction. The `TransactionTree.ToTransactionResult()` projection sets it to
  `default` because a transaction tree carries no command id to project.

### Changed

- **BREAKING: `ILedgerClient.SubmitAndWaitAsync` (`Daml.Ledger.Abstractions`) now returns
  `Task<SubmitAndWaitResult>`** (was `Task<string>`), so it surfaces the effective
  `CommandId` and `CompletionOffset` alongside the `UpdateId` it already returned rather
  than the update id alone. Source-breaking: implementations must widen the return type
  and callers that consumed the returned update-id string must read `result.UpdateId`.
  The downstream client lands in the ledger-client library.

### Fixed

- `TreeEvent.DescendantEvents()` (`Daml.Runtime`) no longer risks `StackOverflowException`
  on deeply nested transaction trees — traversal is now iterative instead of recursive.

## [0.2.0-preview.3] — 2026-07-03

### Changed

- **BREAKING: `ILedgerClient.SubscribeActiveAsync<T>` (`Daml.Ledger.Abstractions`) now returns
  `IAsyncEnumerable<ContractStreamEvent<T>>`** (was
  `IAsyncEnumerable<ContractStreamEvent<T>.Created>`), so the active-contract-set
  snapshot can surface `ContractStreamEvent<T>.Unclassified` for an entry the
  transport can't fully attribute — a missing synchronizer id, or a
  template/interface mismatch — instead of silently dropping it. This brings the
  snapshot to parity with the live `SubscribeAsync<T>` stream, which already
  returns the wide type. Source-breaking: implementations of the narrower
  `.Created`-only return type must widen it, and consumers exhaustively pattern
  matching over the stream need a new arm. Part of the v0.2.0 breaking bundle; the
  downstream fix lands in the ledger-client library.

## [0.2.0-preview.2] — 2026-07-02

### Added

- `Daml.Runtime.Commands.DisclosedContract(string ContractId, Identifier TemplateId,
  ReadOnlyMemory<byte> CreatedEventBlob)` — a new record type carrying an explicitly
  disclosed contract (Daml 3.x explicit disclosure). `CommandsSubmission` gains an
  optional trailing `IReadOnlyList<DisclosedContract>? DisclosedContracts` parameter and
  a `WithDisclosedContracts(params DisclosedContract[])` fluent method, defaulting to
  `null` so existing submissions are unaffected; calling it with no arguments, `null`,
  or an empty array clears the field back to `null`. Record equality compares `CreatedEventBlob` by content, not by
  memory reference. This repo only carries the value — mapping it onto the gRPC
  `DisclosedContract` message lives in the ledger-client repo.
- `Daml.Runtime.Commands.CommandsSubmission` gains an optional trailing
  `SynchronizerId? SynchronizerId` parameter and a `WithSynchronizerId(SynchronizerId)`
  fluent method, mirroring `WithWorkflowId`/`WithCommandId`, so callers can carry a
  submission-time synchronizer pin alongside a submission. This repo only carries the
  value — wiring it into `BuildCommands`/proto conversion lives in the ledger-client
  repo.
- `Daml.Runtime`: `ContractStreamEvent<T>.Unclassified(long Offset, string Kind)`
  — new variant surfaced when a transport delivers an event that cannot be
  mapped to any other discriminated-union case, so consumers can honour a
  no-silent-drop policy instead of the event being dropped. Code that
  exhaustively switches over `ContractStreamEvent<T>` needs a new arm.
- `Daml.Runtime.Contracts`: new `CaughtException(string ErrorId, string Message,
  IReadOnlyDictionary<string, string> Metadata)` record and an
  `ExercisedEvent.CaughtExceptions` init-only property (defaults to empty), so
  consumers can tell whether a successful exercise recovered from a Daml
  `try`/`catch`. Additive — existing positional `ExercisedEvent` constructions
  stay source-compatible. Populating `CaughtExceptions` from the ledger wire
  format is client-side and not yet implemented.
- `Daml.Runtime.Contracts.TransactionTree` and `TreeEvent` (`Created`/`Exercised`
  cases) — a transport-neutral, tree-shaped sibling of `TransactionResult` that
  preserves the parent/child hierarchy of a transaction's events (which creates
  and sub-exercises a given exercise caused), with wire-level `DamlValue`
  payloads consistent with `ExercisedEvent`. `TreeEvent.DescendantEvents()` and
  `TransactionTreeExtensions.AllEvents`/`ToTransactionResult` give depth-first
  traversal and compat-flattening to the existing `TransactionResult` shape.
  Additive — `TransactionResult` is unchanged.
- `Daml.Codegen.Testing.Conformance.Richtypes.Suit` — a new pure
  nullary-constructor Daml `enum` type in the `richtypes` conformance corpus,
  plus a `SuitExtensions` class (`ToDamlEnum()`/`FromDamlEnum()`) and a new
  `Suit Suit` field on `RichRecord` (positional constructor argument added
  after `Outcome`). Closes the enum coverage gap flagged as a follow-up in
  the bundle-level determinism gate.

### Changed

- **BREAKING: `ContractStreamEvent<T>.Created`, `.Archived`, and `.Exercised` now carry
  a `SynchronizerId SynchronizerId` parameter**, positioned right after `Offset` (before
  `WitnessParties`), matching where `Assigned`/`Unassigned` already carry
  `Source`/`Target : SynchronizerId`. Every positional construction of these three
  records must pass a `SynchronizerId` argument in the new position; regenerate/update
  call sites accordingly.
- **The 4th NuGet version segment (`M.m.p.g`) of generated Splice/Daml.Finance
  packages is now a uniform codegen-generation ordinal.** It is keyed to the
  codegen-tool version and shared by every package — and every co-produced sibling
  dependency floor — in a release, incrementing only when the codegen version
  changes rather than per DAR-content change. This replaces the former per-package,
  content-hash-driven revision counter and fixes two publish-time failures a codegen
  upgrade could trigger: a new codegen version that changed emitted C# but not the
  DAR proto hash previously froze the 4th segment, so regenerated packages collided
  at the same version as the already-published set (`CS8920` on build, `NU1605` on
  restore); and because all packages in a release now share one ordinal, co-produced
  sibling `<PackageReference>` floors can no longer diverge from the versions actually
  published together. The first post-upgrade ordinal is seeded above every published
  revision (Splice → 3, Daml.Finance → 2).

### Fixed

- Generated Daml `enum` types now carry an XML doc comment (`/// <summary>...
  enum constructor.</summary>`) above each constructor, matching every other
  generated member. Previously the emitter produced undocumented constructors,
  which built fine only because no pure nullary-constructor `enum` had ever
  been generated; the first one (`Richtypes.Suit`, added in this release) failed
  the build with `CS1591` under `TreatWarningsAsErrors`.
- A Daml template whose name equals another interface's generated `I`-prefixed
  marker name (e.g. template `IFactory` alongside interface `Factory`) no longer
  collides with it. A package's generated types all share one flat C# namespace,
  so both were previously declared as public `IFactory` types in that namespace,
  and the generated set failed to compile with `CS0101`. The interface marker
  name now appends a trailing `_` until it no longer collides with a template in
  its own package, consistently wherever the marker is referenced (declaration,
  file name, and every in-package or cross-package type reference to it).

- The `<Choice>Result` projector (`FromCreatedContracts`) now matches an
  interface-typed created slot against the created contract's `InterfaceIds`
  rather than its `TemplateId`. A choice returning `ContractId I` (where `I` is a
  Daml interface) previously emitted `IFactory.TemplateId.ModuleName` — but
  generated interface markers expose no public `TemplateId` (it is an explicit
  `IDamlType` member), so the projector failed to compile with `CS0117`. Slots to
  a concrete template are unchanged. Surfaced by the full Splice/Daml.Finance
  release build (`daml-finance-interface-holding-v4`,
  `daml-finance-interface-instrument-base-v4`).
- Generated interface markers now expose a plain `public static Identifier InterfaceId
  { get; }` alongside the existing explicit `IDamlInterface.InterfaceId`
  implementation. For a `ContractId I` choice-result slot targeting a foreign
  (cross-package) interface, the `<Choice>Result` projector's interface-matching
  branch reads `{Interface}.InterfaceId.ModuleName`/`.EntityName` off this new member
  instead of baking the interface's module/entity as string literals into the
  emitted source — robustness/consistency with the template branch, which already
  reads `{Template}.TemplateId`. Slots targeting a *local* interface ref keep
  matching via string literals baked at codegen time: the LF-mandated record
  RecordEmitter always emits alongside a local `interface I where ...` declaration
  is a throwing `ITemplate` placeholder stub with no `InterfaceId` member, so those
  slots cannot safely reference a generated symbol.


## [0.2.0-preview.1] — 2026-06-30

### Added

- Generated record properties now carry `[DamlField]` attributes recording the
  original Daml field name alongside the C# property, so consumers and tooling can
  recover the on-ledger field naming without re-deriving it from the type.

### Changed

- **Breaking:** `ILedgerClient.SubmitAsync` is renamed to `SubmitAndWaitAsync`,
  making the submit-and-wait semantics explicit at the call site. The behavior is
  unchanged; update call sites to the new name.
- **Breaking:** `IDamlType` is no longer an empty marker interface — it now
  declares a static-abstract `DamlTypeId` (`static abstract DamlTypeDescriptor
  DamlTypeId { get; }`), and `ITemplate` (which extends `IDamlType`) inherits the
  requirement. Type-identifier resolution no longer goes through runtime
  reflection. Two consequences for consumers:
  - Hand-written types implementing `IDamlType` or `ITemplate` must now provide
    `DamlTypeId`; all generated types supply it automatically.
  - Because these interfaces now carry a static-abstract member, they can no
    longer be passed as generic type *arguments* — code such as `Foo<IDamlType>`
    or `Foo<ITemplate>` no longer compiles (CS8920). Pass a concrete generated
    type, or route through a constrained type parameter (`where T : IDamlType`).
- `SubscribeActiveAsync<T>` (active-contract-set subscription) now accepts any
  `IDamlType`, widening the previous constraint so a broader set of generated
  types can be subscribed directly.

### Fixed

- The generated `Contract<T>` `<Choice>Async` exercise overload is now reachable
  when a contract is reconstructed from a created event via `FromCreatedEvent`;
  previously the nested-contract overload it resolved to was hidden, so the
  asynchronous exercise call could not be made on contracts obtained that way.

## [0.1.8-preview.5] — 2026-06-24

### Changed

- Generated code and runtime messages no longer embed internal issue-tracker
  references; limitation notes (contract-key projection, generic-type
  serialization) now read as generic, consumer-facing prose.

### Fixed

- Generated serialization no longer emits a non-compiling `.ToRecord()` for
  variant payloads or for fields the type mapper cannot name. Variant payloads
  (including recursive variants such as `DA.Logic.Types.Formula` and variant
  payloads nested in lists) now serialize via `ToVariant()`/`FromVariant()`, and
  function-typed / otherwise-unmappable fields (e.g. `DA.Action.State.Type.State`,
  `DA.Monoid.Types.Endo`) emit the `GenericStub.NotImplemented` stub instead of a
  missing-method call. Compiling an `--include-dependencies` tree containing these
  Daml stdlib types as one assembly no longer fails with CS1061.

## [0.1.8-preview.4] — 2026-06-21

### Added

- `Daml.Runtime.Contracts.CreatedContract` gains an init-only
  `IReadOnlyList<Identifier> InterfaceIds { get; init; } = Array.Empty<Identifier>()`
  member carrying the interface ids the participant computed for a created event
  (Canton gRPC `CreatedEvent.interface_views[].interface_id`). Non-breaking: it is
  not a positional parameter, so existing 3-arg construction keeps working and the
  field defaults to an empty (non-null) list. Enables interface-only consumption,
  where a contract is known only as an interface and must be matched/dispatched at
  runtime.
- **Read-path helpers now accept Daml interface markers, not just templates, and
  match created contracts by interface view.** The generic constraint on
  `TransactionResultExtensions.Single<T>`/`TrySingle<T>`/`All<T>`,
  `ILedgerClient.TryExerciseForCreatedAsync<TTemplate>` and both `SubscribeAsync<T>`
  overloads, and `ContractStreamEvent<T>` is relaxed from `ITemplate` to
  `IDamlType`. When `T` is a template the match is unchanged (created contract's
  `TemplateId`); when `T` is an interface marker (`IDamlInterface`), a created
  contract matches when its `InterfaceIds` contains `T`'s interface identifier
  (module + entity, package-id-agnostic). Constraint relaxation is
  source-compatible. `TryCreateAsync<TTemplate>` and `SubscribeActiveAsync<T>`
  intentionally stay `ITemplate`-constrained — create paths remain template-only.

### Changed

- **The contract-key `Key` accessor on generated keyed templates now emits a
  non-`partial` property that throws `NotImplementedException`**, reverting the
  body-less `partial` declaration introduced in `0.1.5`. Key-bearing packages now
  compile and publish standalone: the `partial` required a hand-rolled implementing
  partial, which the automated DAR publish pipeline has no author for, so every
  keyed package failed to build with `CS9248`. The key *type* is still generated
  and serializable for caller-constructed key-based operations, and
  `: IHasKey<TKey>` is unchanged — only the body reverts. Generated key-bearing
  packages consequently no longer pin `<LangVersion>13</LangVersion>`.

### Fixed

- Generated C# no longer fails to compile with `CS0542` when a Daml record field
  PascalCases to the same name as its enclosing type (e.g. Daml Finance `Period`
  with field `period`). The colliding C# member is now disambiguated with a
  trailing underscore while the Daml record field name used for (de)serialization
  stays unchanged.
- Generated `.csproj` files now reference co-produced sibling packages using the
  full package version, including the emitter counter and any `--version-suffix`
  prerelease tag. Previously sibling `<PackageReference>` versions dropped the
  suffix, so a prerelease set (e.g. `3.0.0-preview.3`) emitted `>= 3.0.0`
  references that NuGet could not resolve (`NU1102`).
- Generated C# no longer fails to compile when a record or template references the
  Daml stdlib enum `DA.Date.Types:DayOfWeek`. The enum now resolves to a
  runtime-provided `Daml.Runtime.Stdlib.DayOfWeek` (with serialization extensions),
  matching how other `daml-stdlib` types are handled. Because the runtime enum
  shares its simple name with `System.DayOfWeek`, which `ImplicitUsings` imports
  into every generated file, the reference is emitted fully `global::`-qualified to
  avoid an ambiguous reference (`CS0104`).

## [0.1.8-preview.3] — 2026-06-18

### Added

- Add a `--version-suffix` codegen option that appends a SemVer prerelease suffix (e.g. `preview.2`) to generated package versions, producing versions like `0.1.6.1-preview.2`. Mirrors the emitter's own prerelease tag and affects only the generated package `<Version>`; the `Daml.Runtime` reference version is unaffected.
- Generate a `README.md` for each package and emit `PackageTags` (plus `PackageProjectUrl`/`RepositoryUrl`/`RepositoryType` when `--repository-url` is supplied) in the package `<PropertyGroup>`, so published packages render on nuget.org without the missing-README warning. The README install hint adds `--prerelease` for prerelease packages.
- Generated Splice/Daml.Finance NuGet packages now ship a package icon (`PackageIcon`), so they render with the project icon on nuget.org.

### Fixed

- Generated submission-extension XML docs no longer leak an internal
  issue-tracking reference into consumer output.
- Choice-argument types that reuse the same simple name across different modules
  no longer collide: the choice-arg-to-template map is now keyed by the
  module-qualified (`Module:Name`) name, so each resolves to its own parent
  template instead of one silently overwriting the other and emitting
  unresolvable type references.
- A choice-argument type mapped by two templates in the *same package* (the
  module-qualified key collides) no longer overwrites silently: the
  choice-arg-to-template map now warns and keeps the first-seen mapping (in both
  `PackageEmitContext` and `DarCrossPackageResolver`) instead of last-wins, so the
  clash is surfaced rather than mis-resolving cross-references.

## [0.1.8-preview.2] — 2026-06-12

### Added

- Add CI-verified platform support across the full OS × architecture matrix: every shipped package builds and passes the complete test suite on Linux, Windows, and macOS, on both amd64 and arm64.
  The JVM DAR-parsing helper is verified on the same matrix minus windows-arm64, where upstream publishes no protoc binary. A .Net native DAR parser could fill the gap, but is out of scope.

### Changed

- Bump `Google.Protobuf` to 3.35.1 — raises the dependency floor of the `Daml.Codegen.CSharp` package.
- `GeneratedFile.RelativePath` is now `/`-separated on every platform (previously `\` on Windows), so codegen output layout is identical across operating systems. Paths with `/` are accepted by Windows file APIs; only callers that parsed the separator are affected.

## [0.1.8-preview.1] — 2026-06-11

### Added

- `NOTICE` file added at repo root (Apache-2.0 open-source prep).
- Add `DamlValueExtensions.AsOptional(this DamlValue)` — normalizes a value into a `DamlOptional` (an existing optional passes through, a bare value wraps as Some), recovering Optional fields from ledger JSON where Some is flattened to the inner value.
- `ILedgerClient` now implements `IAsyncDisposable`, with a default implementation that bridges to `Dispose()` — `await using var client = …` works against every implementation with no source change on the implementation side.
- `Daml.Codegen.CSharp` now ships XML documentation and a NuGet package README, so IntelliSense and the NuGet.org gallery page document the emitter API.
- XML documentation is completed across the shipped packages — `CS1591` (missing XML comment on a publicly visible member) is no longer suppressed for them.

### Changed — BREAKING

- **`IDamlValue` is now a bare marker interface; its `DamlRecord ToRecord()` member moved to a new `IDamlRecord : IDamlValue`, alongside a new `IDamlVariant : IDamlValue` carrying `DamlVariant ToVariant()`.** Generated record types and template choice-argument types now implement `IDamlRecord` instead of `IDamlValue`, and `ITemplate` / `IDamlInterface` now extend `IDamlRecord` (so every template and interface still exposes `ToRecord()`). Code that holds a value as `IDamlValue` and calls `.ToRecord()` must now hold it as `IDamlRecord` (or `ITemplate`); generic constraints `where T : IDamlValue` that relied on `ToRecord()` should be widened to `IDamlRecord`. Wire format and `ToRecord()` output are unchanged. Variant emission is unchanged in this slice (variant round-trip lands in a follow-up).
- **Removed the unused public interfaces `ITemplateCompanion<T>` and `ICreateAnd<T>` from `Daml.Runtime`.** Neither was ever implemented by generated code or referenced anywhere, so no consumer can be relying on them; they are deleted rather than frozen into the current 0.x surface.
- **`Contract<T>`, `TransactionResult`, and `CreatedContract` (in `Daml.Runtime.Contracts`) are now `sealed`.** These public result records can no longer be subclassed. Nothing was expected to derive from them, so this is a technically-breaking change only for consumers who had created their own subtypes; switch to wrapping or composing these records instead.
- **`ILedgerClient` submitter API is now strongly typed: the single-party convenience overloads of `TryExerciseAsync<TResult>`, `TryCreateAsync<TTemplate>`, `TryExerciseForCreatedAsync<TTemplate>`, `SubscribeAsync<T>`, and `SubscribeActiveAsync<T>` (and `LedgerClientExtensions.ExerciseAsync`) take `Party actAs` instead of `string actAs`.** The `SubmitterInfo` overloads remain the abstract primitives implementations override; the `Party actAs` overloads are convenience default-interface-methods that forward to them with that single `ActAs` party and empty `ReadAs`. Replace `client.ExerciseAsync(cmd, "alice")` with `client.ExerciseAsync(cmd, new Party("alice"))`.
- **Removed the implicit `string` → `SubmitterInfo` conversion.** A bare `string` no longer binds to a submitter parameter — construct the party explicitly (`new Party("alice")`), or build a `SubmitterInfo`. The implicit `Party` → `SubmitterInfo` conversion is retained, so single-party submission stays a one-liner once you hold a `Party`. This closes a transposition footgun where `actAs` and the adjacent `string? workflowId` could be swapped silently.
- **`Party` → `string` is now an explicit conversion** (was implicit). Use `party.Id` or `(string)party` where a raw string is genuinely wanted; this prevents a `Party` from silently flowing into an arbitrary `string` parameter such as `workflowId`. There is still no implicit `string` → `Party` conversion — construct with `new Party(...)`.
- The default submission path carries `ReadAs` parties and multiple `ActAs` parties straight through to the implementation instead of throwing `NotSupportedException` — a single-`actAs`-plus-`readAs` submission no longer fails at runtime.
- **`ContractId<T>` → `string` is now an explicit conversion** (was implicit). Use `.Value` or `(string)cid` to get the raw contract id; this stops a typed contract id from silently degrading into an arbitrary `string` parameter. The explicit `string` → `ContractId<T>` conversion is unchanged.
- **`SynchronizerId` → `string` is now an explicit conversion** (was implicit). Use `.Id`, `ToString()`, or `(string)sid` where a raw string is genuinely wanted, so a synchronizer id can no longer flow silently into an arbitrary `string` parameter. JSON serialization is unchanged — `SynchronizerId` still round-trips as a plain JSON string. The explicit `string` → `SynchronizerId` conversion is unchanged.
- **`CommandsSubmission.WorkflowId` and `CommandsSubmission.CommandId` are now the value types `WorkflowId?` / `CommandId?`** (was `string?`). Construct with `new WorkflowId("…")` / `new CommandId("…")` (or the explicit `(WorkflowId)"…"` cast), or pass `null` to omit them; read the raw string back via `.Value` (a nullable submission field unwraps as `submission.WorkflowId?.Value`). `WithWorkflowId(...)` / `WithCommandId(...)` now take the value types, so transposing a workflow id and a command id at a call site is a compile error rather than a silent swap. `WorkflowId` rejects only `null` — an empty or whitespace value is accepted, matching the Ledger API which documents `workflow_id` as optional with no non-empty constraint (`CommandId` still rejects empty/whitespace since `command_id` is required). Generated `<Choice>Async` convenience methods keep their `string? workflowId` parameters and stay 100% compatible with the previous string path: a `null` or empty `workflowId` omits the field, any other value (including whitespace) is forwarded verbatim, so only direct `CommandsSubmission` construction is affected. The values still project onto the Ledger API `workflow_id` / `command_id` string fields unchanged.
- **The `Choice` member on `ExerciseCommand`, `ExerciseByKeyCommand`, and `CreateAndExerciseCommand` — and the `choice` parameter of the `ExerciseCommand.For`, `ExerciseCommand.ForInterface`, and `CreateAndExerciseCommand.For` factories — are now the value type `ChoiceName`** (was `string`). Migrate by wrapping the choice name, e.g. `ExerciseCommand.For(cid, "Relabel", arg)` becomes `ExerciseCommand.For(cid, new ChoiceName("Relabel"), arg)`; read the raw string back via `.Value`. Because `Choice` and the adjacent `ContractId` no longer share the `string` type, transposing them when constructing an `ExerciseCommand` is now a compile error rather than a silent swap. The value still projects onto the Ledger API `choice` string field unchanged.
- **`ExerciseCommand.ContractId` is now the abstract `ContractId` base type** (was `string`). A new non-generic `abstract record ContractId` in `Daml.Runtime.Contracts` is the base of `ContractId<T>`, so the typed contract id flows onto the command with no unwrap/rewrap; the bare-`string` positional construction path is gone — build commands via `ExerciseCommand.For<T>` / `ForInterface<T>` (which now also reject a `null` contract id), and read the raw string back via `.Value`. The value still projects onto the Ledger API `contract_id` string field unchanged.
- **`ContractId<T>` now validates its value on construction** — `new ContractId<T>(value)` throws `ArgumentException` when `value` is null, empty, or whitespace (previously it silently accepted them). All real ledger contract ids are non-empty, so legitimate callers are unaffected; a malformed/empty id now fails loud instead of carrying `""`.
- **`ContractId<T>` is no longer a positional record, so its compiler-generated `Deconstruct(out string)` is removed.** Consumers using positional deconstruction (`var (value) = contractId;`) must read `.Value` instead.
- **`Choice<TTemplate, TArg, TResult>.Name` is now the value type `ChoiceName`** (was `string`). Generated choice metadata now constructs it explicitly — `Name = "Archive"` becomes `Name = new ChoiceName("Archive")`; read the raw string back via `.Value`. This extends the project-wide "no bare `string` choice names" pass to the choice-metadata record.
- **Generated variants now round-trip through `DamlVariant` instead of `DamlRecord`.** A generated variant's abstract base now implements `IDamlVariant` (was `IDamlValue`) and exposes `DamlVariant ToVariant()` plus a static `FromVariant(DamlVariant)` that dispatches on the constructor tag; each case overrides `ToVariant()` to produce `DamlVariant.Create("<Tag>", <payload>)` (a no-argument case uses `DamlUnit.Instance` as its value). The previous lossy `ToRecord()` / throwing `FromRecord(...)` stubs are gone, so a record or choice-result field whose type is a variant now serializes and deserializes correctly instead of throwing `NotImplementedException` at runtime. Hand-written code that called `.ToRecord()` / `.FromRecord(...)` on a generated variant must switch to `.ToVariant()` / `.FromVariant(...)`. Wire format is unchanged — the runtime `DamlVariant` JSON shape is the same.
- **Generated enum serialization extensions are renamed `ToRecord`/`FromRecord` → `ToDamlEnum`/`FromDamlEnum`** so the method name matches its `DamlEnum` return/parameter type (an enum's `ToRecord()` never returned a `DamlRecord`). For a generated `enum Status`, replace `status.ToRecord()` with `status.ToDamlEnum()` and `StatusExtensions.FromRecord(value)` with `StatusExtensions.FromDamlEnum(value)`. Generated `ToValue`/`FromValue` round-tripping is updated automatically; only hand-written code that called the enum extensions directly needs migrating. Record/variant/template `ToRecord`/`FromRecord` are unchanged.
- **Removed `CodeGenOptions.GenerateJsonSupport`, `CodeGenOptions.OutputDirectory`, `CodeGenOptions.Verbosity`, and the `--json` CLI flag.** All four were documented no-ops — setting them never changed emitter behavior or output. Delete any assignments and drop the flag; generated output is unchanged.
- **Removed the dead interface member `IDarSource.ResolveAllDependencyReferences()`.** No code path ever called it; implementations simply delete their override.
- **`DamlPackageReference.Name` and `DamlPackageReference.Version` are now init-only, and `DarArchive`'s two public resolve methods are removed**, completing the `IDarSource` cleanup: package references are immutable inputs to the emitter, and dependency resolution is no longer part of the public surface.
- **The CI versioning cluster is now internal**: `JsonReleaseCounterStore`, `NuGetVersionResolver` (née `SpliceNuGetVersion`), `ReleaseCounterEntry`, `IntermediatePackageContentHash`, and `FourPartPackageVersion` no longer appear in the public API. They exist to compute the 4th version segment for CI-driven publishing and were never a consumer integration point.
- **Model-side `DamlField` is renamed `DamlFieldDefinition`.** Only consumers of the codegen model API are affected; the runtime `DamlField` type referenced by emitted code is unchanged.
- **The conformance corpus types moved from the bare root namespace `Richtypes` to `Daml.Codegen.Testing.Conformance.Richtypes`.** Replace `using Richtypes;` with `using Daml.Codegen.Testing.Conformance.Richtypes;`.
- **CLI: `--intermediate` is now required, and the short flag `-V` is renamed `-v`.** Cancellation (Ctrl+C) is now honored and exits with code 130.

### Changed

- **`DamlNumeric` now serializes to JSON in canonical unpadded decimal form.** Trailing zeros are stripped and at least one fractional digit is always emitted, so the wire shape no longer depends on how the `decimal` was constructed: `1.5m` and `1.50m` both serialize to `"1.5"`, and an integer value such as `42m` serializes to `"42.0"`. Scientific notation is never emitted (e.g. `0.0000000001m` → `"0.0000000001"`). Previously the output preserved the `decimal`'s internal scale (`1.50m` → `"1.50"`, `42m` → `"42"`). This is the commitment-grade wire shape for the current 0.x surface; PQS-style scale-padded reading remains out of scope (deferred to a future release).
- Licensing: every source file now carries the two-line SPDX Apache-2.0 header (`Copyright (c) 2026 Peaceful Studio OÜ` + `SPDX-License-Identifier: Apache-2.0`); the central `<Copyright>` tag in `Directory.Build.props` is retained for assembly metadata. (open-source prep)
- `DamlJsonSerializer` failures now always surface as `JsonException`: a number outside `decimal` range, a non-object top level passed to `DeserializeRecord`, an unsupported `DamlValue` subtype, and value nesting beyond 64 levels all throw `JsonException` instead of leaking `FormatException`/`InvalidOperationException`/`NotSupportedException`.
- **`DamlRecord`, `DamlList`, `DamlTextMap`, and `DamlGenMap` now implement structural equality** — two values with equal contents compare equal, so two `ToRecord()` results of the same payload now satisfy `Equals`. `DamlNumeric` equality compares the numeric value only, ignoring `Scale`, so a deserialized Numeric (always reconstructed at the default scale) compares equal to the value it round-tripped from. `DamlGenMap.Create` now rejects structurally-equal duplicate keys, matching `DamlTextMap`.
- **`DamlJsonSerializer` hardening**: duplicate JSON property names are rejected; offset-less timestamp strings now parse as UTC instead of the machine's local time zone; the value-nesting bound is raised from 64 to 128 levels (above Daml-LF's own 100-level limit, so any ledger-valid value fits); and serializing a `DamlRecord` carrying duplicate field labels now throws instead of silently keeping the last value.
- **The throwing `ExerciseAsync` convenience wrappers now throw `LedgerOperationException`** (derives from `InvalidOperationException`, so existing catch blocks keep working), carrying the structured `DamlError`/`InfraError` detail — error category, error id, metadata, and status code — instead of a flattened message.
- Generated variant `Tag`/`ToVariant` overrides now carry `/// <inheritdoc />` doc comments, so consumer builds with `GenerateDocumentationFile` enabled no longer emit CS1591 for emitted code.

### Fixed

- **`DamlJsonSerializer` untyped deserialization no longer type-confuses Text values that merely look like dates or timestamps.** Inference is restricted to the exact canonical shapes the serializer emits (`yyyy-MM-dd` dates, ISO-8601 `T`-separated timestamps), so `"12:30"` or `"12/25/2023"` stay `DamlText` instead of becoming a nondeterministic `DamlTimestamp`/`DamlDate`.
- **`DamlNumeric` now round-trips through JSON.** Strings matching the canonical numeric shape the serializer emits (`-?digits.digits`, e.g. `"1.5"`, `"42.0"`) deserialize back to `DamlNumeric`; previously `DamlNumeric(1.5m)` came back as `DamlText("1.5")`.
- **`DamlJsonSerializer` no longer misreads records as variants.** Only an object with exactly the two keys `tag` (a JSON string) and `value` becomes a `DamlVariant`; objects with extra properties or a non-string `tag` deserialize as `DamlRecord` instead of silently dropping fields or throwing.
- **Optional record fields now survive deserialization from ledger JSON.** An explicit JSON `null` field deserializes to `DamlOptional.None` instead of being dropped (which made generated `FromRecord` throw on every Optional-bearing template), and generated `FromRecord`/choice-result decoders read Optional fields via `AsOptional`, accepting both the wrapped `DamlOptional` shape and the JSON-flattened Some shape.
- Generated `<summary>` doc comments now use the correct indefinite article for type names beginning with a vowel — e.g. a variant's `FromVariant` summary reads `Reconstructs an Outcome`, not `Reconstructs a Outcome`.
- **A record (or template/choice-argument) field whose type is an `enum` defined in a *dependency* package now round-trips correctly.** Previously the codegen only recognized enums declared in the package being generated, so a field referencing an enum from another package fell through to the record serialization path and the emitted code failed to compile — the TO side called `.ToRecord()` and the FROM side called `FromRecord(...)` on a bare C# `enum`, neither of which exists. Such fields now serialize and deserialize through the foreign enum's generated `…Extensions.ToDamlEnum` / `…Extensions.FromDamlEnum` helpers, exactly like a same-package enum field.
- **Generated C# for real-world Daml-LF 1.x and parametric DARs now compiles.** Archive choices on placeholder-named (pre-LF-1.8, no `PackageMetadata`) packages no longer crash or leak a `No.Package.Metadata.Archive` reference, leading-hyphen dependency names no longer produce a leading-dot package id, and stdlib-known types (`Tuple2`, `Either`, `Set`, `NonEmpty`, `Map`, `RelTime`) reached through such packages now resolve to `Daml.Runtime.Stdlib.*`. Parametric records/variants now emit a valid `using Daml.Runtime.Stdlib;` for their `GenericStub` placeholders, and a record carrying a `Numeric` field no longer emits a spurious `using Daml.Runtime.Stdlib;` for its erased scale (which renders as `decimal`).
- **`DamlJsonSerializer` now throws `JsonException` when a canonical numeric string's magnitude exceeds the `System.Decimal` range** instead of silently deserializing a ledger-valid `Numeric` as `DamlText`. The `decimal` bound — 28–29 significant digits vs Daml `Numeric`'s 38 — is now documented on `DamlNumeric`; fractional precision beyond it rounds.
- **`GetTemplateId<T>` now reads the `ITemplate` static-abstract members directly and throws on an empty `PackageName`** instead of silently falling back to the package-id hash format, which produced PQS queries that matched nothing. Its format selector is now the `TemplateIdFormat` enum (`PackageName` for read-path filters, `PackageHash` for command submission) instead of a bare boolean.
- **The default `Daml.Runtime` / `Daml.Ledger.Abstractions` package references in generated projects now pin the emitter's lockstep version** instead of `*`, which never resolves a prerelease version and failed `dotnet restore` at launch.
- **Nested choice-argument record signatures are now correctly indented** — an emitter parameter-joiner bug produced garbled signatures such as `Relabel(string NewLabel    )`.

### Security

- **`IntermediateDarReader` now rejects identifiers outside the Daml-LF name grammar**, closing the path by which a hand-crafted `IntermediateDar` proto could inject arbitrary text into generated C# source.

## [0.1.7] — 2026-06-01

### Added

- **New package `Daml.Codegen.Testing.Conformance`** (first available on NuGet.org with `0.1.8-preview.1`): compiled C# generated from the `richtypes` conformance corpus (types under namespace `Richtypes`) plus the corpus DAR embedded as a resource. Consumers call `ConformanceCorpus.OpenDar()` to obtain the DAR stream for upload to a Canton participant before running live-ledger round-trip tests. Not for production use.

- **All three `dpm-codegen-cs` entrypoints now support `--publish-nuget --nuget-config <path> --nuget-source <name>`**. When `--publish-nuget` is set, the entrypoint injects `--generate-project` into the emitter call, runs `dotnet pack`, discovers the produced `.nupkg`, and pushes it via `dotnet nuget push --skip-duplicate`. All flags are validated before any work begins; `dotnet` on PATH is also checked. Warns to stderr if `--runtime-version` is not supplied (the generated `.csproj` will reference `Daml.Runtime` with a wildcard version). Covered across `dpm-codegen-cs` (POSIX bundle entrypoint), `dpm-codegen-cs.cmd` (Windows bundle entrypoint), and `scripts/codegen-pipeline.sh` — all three ship inside the `ghcr.io/peacefulstudio/dpm-codegen-cs` OCI artifact, not in this repository.

### Changed — BREAKING

- **`ILedgerClient`: `ExerciseAsync<TResult>` and void `ExerciseAsync` overloads are removed from the interface** and replaced by `TryExerciseAsync<TResult>` returning `ExerciseOutcome<TResult>` for structured error handling (callers `switch` on the outcome instead of catching exceptions). Throwing `ExerciseAsync` convenience overloads remain available at every call site via the new `LedgerClientExtensions` static class — existing callers compile unchanged. Implementations must now override `TryExerciseAsync<TResult>` instead of `ExerciseAsync<TResult>`.

### Changed

- **JVM helper now uses `daml-lf-archive-reader` 3.4.11 stable**, replacing the previous `3.3.0-snapshot` pre-release. DARs compiled against Daml SDK 3.4.x are now parsed against a stable release of the LF archive library.

## [0.1.6] — 2026-06-01

### Added

- **`Daml.Runtime.Stdlib.Either<TL, TR>` runtime type, and codegen now maps `DA.Types.Either a b` onto it.** Previously a Daml field or choice type of `Either a b` emitted a bare `Either<TL, TR>` with no definition or `using`, so any DAR using `Either` (e.g. `canton-ping`) failed to compile (`CS0246`). `Either` is now a parametric stdlib type: `Either<TL, TR>` is an abstract record with `Left`/`Right` cases, round-tripping through `DamlVariant` via `ToValue`/`FromValue`. Generated code references it as `Daml.Runtime.Stdlib.Either<…>`.
- **`ghcr.io/peacefulstudio/dpm-codegen-cs` OCI bundle contract is now codified**. Anyone integrating with the artifact directly — not via `dpm` — gets a versioned contract for the bundle layout (top-level `component.yaml`, `bin/<exe>`, `bin/<jar>`), the per-layer OCI media type (`application/vnd.component.file`), the required `network.canton.dpm.file-{mode,modtime,name}` annotations (`file-name` is the relative path inside the bundle, not the basename), and the consumer `daml.yaml` shape (`components: ["oci://…"]`, no `sdk-version:` alongside, never the dead-code `override-components: <name>: image-tag:`). Stock `dpm ≥ 1.0.12` required on the consumer side; `dpm 1.0.16` is what our workflows pin. Public-package consumers must NOT `docker login ghcr.io` with `${{ secrets.GITHUB_TOKEN }}` — anonymous pull is the supported path.
- **`daml-codegen-csharp --release-counters <path>` resolves the 4th NuGet version segment from a `JsonReleaseCounterStore`**. When the flag is supplied the CLI computes the content hash of the `IntermediateDar` proto, opens the store at `<path>`, resolves the revision via the release-counter versioning machinery (now internal to the emitter), and uses that as `CodeGenOptions.EmitterCounter` — replacing the explicit `--emitter-counter <int>` static override for CI-driven publishing. The `Canton.Splice.*` publish workflow now wires this flag end-to-end: the counter store is the source of truth for the 4th segment, and consumers see monotonically increasing `M.m.p.r` versions across re-emissions of the same DAR-intrinsic version when emitter output content changes. The store lives in a GitHub Actions repo variable; local-dev invocations omit `--release-counters` and continue to default to `r=0`.
- **`dpm codegen-cs` is now distributable as a multi-arch OCI artifact at `ghcr.io/peacefulstudio/dpm-codegen-cs`**. The new `.github/workflows/build-oci-codegen-cs.yaml` builds a self-contained single-file C# emitter binary for `linux/amd64`, `linux/arm64`, `darwin/arm64`, and `windows/amd64`, bundles each with the JVM helper JAR + a small `dpm-codegen-cs` entrypoint script, pushes each per-RID directory as its own OCI artifact, and composes them into a multi-arch index under `ghcr.io/peacefulstudio/dpm-codegen-cs:<version>`. Stock `dpm` fetches the right RID lazily on first invocation using its `<os>/<arch>=<path>` asset-selection syntax — no host .NET runtime required on the consumer side; a host JDK is the only runtime precondition. Triggers: `workflow_dispatch` (manual) and `workflow_call` (orchestration by a release pipeline).
- **JVM helper `--schema-only` opt-out flag**. The JVM helper's default decode is now full-decode + static party-expression analysis; pass `--schema-only` to opt into the previous schema-mode decode (`SignatureErasure` runs on `signatories` / `observers` / `controllers` / `choiceObservers` expression bodies). The opt-out is patch-version-insensitive — two patch-different versions of the same package produce identical `IntermediateDar` bytes — at the cost of disabling the typed-`actAs` codegen path on the proto pipeline. `scripts/codegen-pipeline.sh` exposes the same `--schema-only` flag to chain it through to the helper.
- **4-part `M.m.p.r` NuGet versioning**, exposed as the new `Daml.Codegen.CSharp.Versioning` namespace. Segments 1–3 of a generated package's NuGet version are the DAR-intrinsic `Major.Minor.Patch`; segment 4 (`r`) is a monotonic emitter counter that disambiguates content-identical re-emissions of the same DAR-intrinsic version under different emitter versions. New consumer-facing API: `SpliceNuGetVersion.Compute(packageName, intrinsicVersion, contentHash, counterStore)` (now internal to the emitter, renamed `NuGetVersionResolver`) returns the canonical 4-part `FourPartPackageVersion`. The counter is persisted in a JSON file (`JsonReleaseCounterStore.OpenOrCreate(path)`) keyed by `{packageName}@{M.m.p}`; first emission of a (package, intrinsic-version) pair returns `r=0`, identical re-emissions hold the revision steady, and any content change bumps it. `IntermediatePackageContentHash.Compute(IntermediatePackage)` returns the stable SHA-256 over the deterministic protobuf encoding for use as the content-hash input. The NuGet packing step consumes this API; consumers see the new 4-segment versions on the wire.
- **Codegen now emits a buildable `.csproj` and packs a NuGet package per Daml package**. Generated projects carry versions per the 4-part `M.m.p.r` versioning scheme (Daml package version supplies segments 1–3; the 4th segment is the emitter counter, defaulting to `0` for the first emission), declare `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>` (configurable via the new `--package-license <SPDX>` CLI flag / `CodeGenOptions.PackageLicenseExpression` for non-Apache DARs), and reference `Daml.Runtime` and `Daml.Ledger.Abstractions` so consumers can run `dotnet add package <Pkg>` + `dotnet build` against an unmodified output tree. A new `--emitter-counter <int>` flag on the emitter CLI (validated at the boundary to reject negatives) exposes the 4th-segment override for the Splice publish pipeline; local-dev invocations leave the default in place. `CodeGenOptions.EmitterCounter` is the integration point for the per-emitter mapping table.
- `Daml.Codegen.CSharp.IntermediateDarReader.Read(IntermediateDar)` — proto-to-model adapter; the new public API surface for emitter consumers. Throws `InvalidDataException` fail-fast on malformed input (missing data-type shape, missing choice `argument_type` / `return_type`, unknown proto sort, `BUILTIN_TYPE_UNSPECIFIED`) and `NotSupportedException` on intentionally-deferred builtins; no silent fallback to `Unit` or empty record.
- `Daml.Codegen.CSharp.Model.DarModel` and `Daml.Codegen.CSharp.Model.IDarSource` — the emitter input contract. `CSharpCodeGenerator.Generate` now takes `IDarSource`, satisfied by `DarModel` (proto-direct).
- `Daml.Codegen.CSharp.ICodegenLogger` — minimal logging contract that `CSharpCodeGenerator` now depends on. `ConsoleLogger` implements it; tests and host applications can supply alternative implementations without taking a console dependency.
- `scripts/codegen-pipeline.sh` — orchestration shim that chains the JVM helper JAR + the C# CLI end-to-end; ships inside the `ghcr.io/peacefulstudio/dpm-codegen-cs` OCI artifact rather than this repository. Stands in for the `dpm codegen-cs` OCI bundle entry point.

### Changed — BREAKING

- **`Daml.Codegen.CSharp` is now a pure emitter library.** It consumes an `IntermediateDar` proto and emits `.cs`. The legacy `dotnet tool` CLI surface (`PackAsTool`, `daml-codegen-csharp` command) is removed from `Daml.Codegen.CSharp`. A new thin CLI project `Daml.Codegen.CSharp.Cli` produces the binary `daml-codegen-csharp`; it accepts `--intermediate <proto-path>`. The CLI publishes as a self-contained single-file native binary per RID — `dotnet publish src/Daml.Codegen.CSharp.Cli -c Release -r <rid> --self-contained true -p:PublishSingleFile=true`.
- **Default JVM-helper decode mode is now full-decode + static party-expression analysis**. Before this change the proto-direct pipeline produced `Dynamic` party analysis on every template and choice, so the typed-`actAs` codegen path was effectively disabled — `CreateAsync` and `<Choice>Async` wrappers always required a `SubmitterInfo` parameter. The new default invokes a Scala port of `PartyExpressionAnalyzer` against the fully-decoded `Ast.Expr` for `signatories` / `observers` / `controllers` / `choiceObservers` and emits a `Static(payload_fields…)` verdict into the `IntermediateDar` proto when every party resolves to a payload-field projection on the template parameter. Generated `*SubmissionExtensions.cs` consequently emits the `Signatories(payload)` and `Observers(payload)` helpers and derives `actAs` from the payload for templates whose signatories are payload-field projections — the dominant idiom across Splice's Amulet, Token Standard, and Synfini templates. The change is patch-version-sensitive by default; pass `--schema-only` (see Added) to opt back into patch-insensitive behavior.

### Changed

- **`IntermediateDar` proto schema gained `Template.signatories`, `Template.observers`, `Choice.controllers`, `Choice.observers`, and a new `PartyAnalysis { Static | Dynamic }` message**. All four new fields are forward-compatible (absent on older proto bytes is read as `Dynamic`). Proto consumers other than `IntermediateDarReader` that interpret the wire format should add a case for `PartyAnalysis`.
- **Generated code now emits the runtime `Party` type unqualified (bare `Party`) on packages whose namespaces don't shadow it**, matching every other runtime type's qualification policy. Previously `Party` was hard-coded as `global::Daml.Runtime.Data.Party` at every emission site, so it always carried the `global::` prefix even when nothing shadowed it — the lone runtime type that bypassed the collision-aware qualifier. It now routes through that qualifier like `ContractId`, `IDamlValue`, `Choice`, and the rest: bare on non-colliding packages, still `global::`-qualified when a generated namespace segment shadows it (e.g. a package deriving a namespace ending in `.Party`). Output for shadowing packages is unchanged; non-shadowing packages see `global::Daml.Runtime.Data.Party` become `Party` in field types, record/choice parameters, `IHasKey<>` keys, and value-decoder expressions.
- **Generated code now emits the `Daml.Runtime.Stdlib.*` types (`Either`, `Tuple2`/`Tuple3`, `Set`, `NonEmpty`, `Map`, `RelTime`, `Unit`, `GenericStub`) as bare type names under a new `using Daml.Runtime.Stdlib;` import**, qualifying with `global::` only when a generated namespace segment shadows the name. Previously these stdlib types were written fully qualified (`Daml.Runtime.Stdlib.RelTime`, `Daml.Runtime.Stdlib.Tuple2<…>`, …) at every emission site — the lone remaining family that bypassed the collision-aware qualifier. They now route through it like every other imported type: bare on non-colliding packages (e.g. `splice-api-token-holding-v1`'s `Lock.ExpiresAfter` becomes `RelTime?` under `using Daml.Runtime.Stdlib;`), still `global::`-qualified when a generated namespace segment shadows the name — fixing the `CS0118` namespace-shadowing bug class for stdlib types and completing the central-qualifier work. The using requirement is also package-gated: a user package that defines its own `DA.Types:Tuple2` (or another stdlib-named type) is rendered under its own namespace and no longer emits an unused `using Daml.Runtime.Stdlib;`, so it is not surfaced as `CS8019` under consumer `<TreatWarningsAsErrors>`.
- Generated files now emit only the `using` directives their body actually references, tracked per-file at codegen time — each namespace is required at its actual emit site (e.g. `System.Collections.Generic` only when a list/map field appears, `Daml.Runtime.Contracts` only when a template, interface, or contract-ID type is emitted, `System` only when `Version`, `DateTimeOffset`, etc. appear). The `#pragma warning disable CS8019` header that previously suppressed unused-using warnings in every file has been removed; no generated file emits an unused `using`. Consumers with `<TreatWarningsAsErrors>` no longer need a workaround, and IDEs get accurate import lists.

### Fixed

- **Generated code no longer fails to compile with `CS0118` when a Daml package or module name derives a C# namespace segment that collides with an imported runtime or BCL type name.** Previously a namespace ending in (e.g.) `Party` bound the bare `Party` identifier to the namespace rather than the runtime struct, failing the consumer build with `CS0118: 'Party' is a namespace but is used like a type` (`canton-party-replication-alpha` is the motivating real-world case). The emitter now `global::`-qualifies an imported simple type name only when a generated namespace would actually shadow it, so output for non-colliding packages (e.g. `splice-api-token-holding-v1`) is byte-identical — no churn. Coverage spans the runtime value/type family (`Party`, `ContractId`, `ITemplate`, `IHasKey`, `IDamlValue`, `Choice`, `SubmitterInfo`, `Identifier`, `ExerciseOutcome`, `TransactionResult`, `ILedgerClient`, the `Daml*` value types, …) and BCL types (`IReadOnlyList`, `IReadOnlyDictionary`, `HashSet`), across both type positions and expression/value positions (e.g. `new Identifier(...)`, `.As<DamlParty>()`, `DamlRecord.Create(...)`) as well as XML-doc crefs.
- **The key-bearing template's XML-doc `<see cref>` for `IHasKey<>` is now well-formed under `<GenerateDocumentationFile>`**, closing a `CS1584`/`CS1658` doc-build warning. The generated `Key` property doc previously embedded the rendered key type inside the cref braces (`<see cref="...IHasKey{IReadOnlyList{string}}"/>`, or `IHasKey{global::Daml.Runtime.Data.Party}` in a `Party`-shadowing namespace) — a constructed type in cref braces, which Roslyn rejects as a syntactically incorrect cref. The cref now targets the open generic by its declared type-parameter name (`<see cref="global::Daml.Runtime.Contracts.IHasKey{TKey}"/>`) and the concrete key type is rendered as prose (`Gets the contract key of type <c>…</c>`).
- **`DamlJsonSerializer.Serialize(DamlUnit.Instance)` no longer throws `ArgumentException`** and now returns `"{}"` per the Daml-LF JSON encoding for Unit. The serializer's `ValueToJsonNode` branch for `DamlUnit` wrapped a `JsonObject` in `JsonValue.Create(...)`, which only accepts primitive values and rejects any `JsonNode` — every attempt to serialize a `DamlUnit` (standalone or via the `DamlValueJsonConverter` path used by the top-level `Serialize(DamlValue)` entry point) threw at runtime.
- **Generated files now emit `using Daml.Runtime.Contracts;` for every `ContractId<T>` reference**, not only top-level record fields. Two emit sites previously skipped the required-using pass and produced files that referenced `ContractId<T>` without importing its namespace — a variant constructor whose argument type contained a `ContractId` (e.g. `Splice/Api/Token/Metadata/V1/AnyValue.cs` from `splice-api-token-metadata-v1`), and any record field whose type wrapped a `ContractId` inside a parametric stdlib or user-defined generic type (e.g. `Set (ContractId T)`, `Tuple2 (ContractId T) Int`). Both shapes appeared in 7 of 22 Splice 0.6.5 DAR families and surfaced as `CS0246: The type or namespace name 'ContractId<>' could not be found` at consumer build time.
- **`DamlJsonSerializer.Deserialize` now parses date and timestamp strings under `CultureInfo.InvariantCulture`**, closing a round-trip asymmetry with the serialize side, which already pinned `InvariantCulture` and emits ISO-8601 (`yyyy-MM-dd` for `DamlDate`, `"O"` for `DamlTimestamp`). Previously `InferStringValue` called `DateOnly.TryParse(s, out _)` and `DateTimeOffset.TryParse(s, out _)` without an explicit culture, falling back to `CurrentCulture`. Under cultures whose default calendar is non-Gregorian (`th-TH`, `fa-IR`, `ar-SA`, …) the ISO date string was reinterpreted in the host calendar — e.g. `"2026-05-26"` round-tripped to `1483-05-26` under `th-TH` — silently corrupting `DamlDate` values across the wire. `DamlTimestamp` parsing was unaffected in practice (the `"O"` shape parses universally) but is pinned for symmetry.
- **`DamlJsonSerializer.Serialize` now handles `DamlGenMap`** instead of throwing `NotSupportedException`. `DamlGenMap` is the wire-level backing for Daml `GenMap k v` and underpins the `Daml.Runtime.Stdlib.Map<K, V>` and `Set<T>` stdlib wrappers, both of which appear pervasively in the Splice Amulet and Wallet DARs (e.g. `Map Party Int` beneficiary lists, `Set Party` membership). The serialized shape is a JSON array of two-element `[key, value]` arrays, matching the Daml-LF JSON encoding for `GenMap`.
- **`DamlJsonSerializer.DeserializeRecord` and the top-level `DamlJsonSerializer.Deserialize` now both reconstruct `DamlGenMap`** from the same `[[key, value], ...]` wire shape the serializer emits, closing the round-trip asymmetry whereby `Deserialize(Serialize(genMap))` previously collapsed to a `DamlList` of two-element `DamlList`s. The `DamlValueJsonConverter` used by the top-level `Deserialize` / `Serialize` entry points now delegates to the same canonical mappers as `DeserializeRecord` / `Serialize(DamlRecord)`, removing a duplicated and divergent traversal that also disagreed on string→date inference, Variant null handling, and infinite-recursed on `Serialize(DamlValue)`. The heuristic is documented on the public `Deserialize` XML doc and is necessarily lossy for three untyped-JSON edge cases — a `List (List a)` whose inner lists all happen to be length 2 is reinterpreted as a `DamlGenMap`; an empty `[]` always resolves to an empty `DamlList` (never an empty `DamlGenMap`); and a pair with a `null` first element falls back to the list path and surfaces the original "Null array elements not supported" error rather than a misleading GenMap-key error. Callers needing exact round-trips for those shapes must deserialize against a type schema.
- **`DamlJsonSerializer` now formats `Numeric`, `Date`, and `Timestamp` values under `CultureInfo.InvariantCulture`**, so wire-format output is identical regardless of the host's `CurrentCulture`. Previously `DamlNumeric` rendered with the current-culture decimal separator (e.g. `"123,456789"` under `fr-FR`), and `DamlDate` / `DamlTimestamp` could pick up calendar-specific formatting under cultures whose default calendar is not the Gregorian one. This is required for round-tripping through PQS and the JSON Ledger API, both of which expect invariant-formatted scalars.
- **`IsArchiveChoice` filter now gates on the stdlib package id**, not just the choice name and module path. Previously a user-defined choice named `Archive` whose argument type referenced `DA.Internal.Template:Archive` would be falsely suppressed by the non-CID wrapper emitter, so the generated code was missing an `ArchiveAsync` extension on the template's contract id. The filter now mirrors the `IsParametricStdlibTypeRef` pattern: the argument type's `PackageId` must resolve through the current archive to a Daml stdlib package (`daml-prim` / `daml-stdlib` / `ghc-stdlib`); otherwise the choice flows through and a typed wrapper is emitted.
- **Choice-argument types are now emitted fully qualified when referenced by sibling records or variant constructors**. Choice-arg types (e.g. `MergeDelegation_Merge`, `DsoRules_AddSv`) are nested inside their parent template class in the generated output; any reference from outside that template — a sibling record field, a variant constructor parameter, or a cross-package variant — was previously emitted as a bare or namespace-only name that the C# compiler could not resolve (CS0246 / CS0234). The codegen now qualifies such references as `TemplateName.ChoiceArgTypeName` (same-package) or `ForeignNamespace.TemplateName.ChoiceArgTypeName` (cross-package). No consumer action required beyond re-running the codegen; the fix closes the Splice `MergeDelegationCall` and `DsoRules_ActionRequiringConfirmation` compilation failures.
- **Interface and template-extension XML-doc `<see cref>` tags are now `global::`-qualified**, closing a `CS1574` doc-build warning under `<GenerateDocumentationFile>` for packages whose namespace is rooted at `Daml.*`. Five `<see cref="Daml.Runtime.*"/>` / `<see cref="Daml.Ledger.*"/>` strings were emitted bare into generated interface-extension and template-extension class docs; Roslyn resolves `Daml.Runtime.*` relative to the enclosing `Daml.*` namespace and fails with `CS1574: XML comment has cref attribute that could not be resolved` on a package such as `daml` (namespace `Daml.*`). All five crefs now carry `global::`.
- **`FromRecord` for `TextMap`/`GenMap`-of-`List` fields no longer emits a non-compilable `Dictionary<K, List<V>>`** — the value projection lambda is now cast to `IReadOnlyList<V>` so `ToDictionary` infers `Dictionary<K, IReadOnlyList<V>>`, which does implement `IReadOnlyDictionary<K, IReadOnlyList<V>>`. Without the cast, C# generic invariance caused CS1503 in consumer builds whenever a generated record had a field of Daml type `TextMap (List a)` or `GenMap k (List v)` (surfaces in, for example, `WalletUserProxy_BatchTransferResult.SenderChangeMap`). The same cast is also emitted for top-level `List` fields and `Choice` result decoders, ensuring consistency across all deserialization paths.

## [0.1.5] — 2026-05-03

### Changed — BREAKING

- **Contract-key `Key` property is now a `partial` declaration** instead of a stub that throws `NotImplementedException` at runtime. The codegen still detects keys and emits `: IHasKey<TKey>`, but the property body is now supplied by a hand-rolled `partial` in the consuming project until the full DALF key-expression analysis (mapping the template's `key` Daml expression back to template fields) lands. This shifts the failure mode from runtime (throwing on first `Key` access) to compile time (Roslyn `CS9248` on the consumer build until the implementing partial is supplied) — impossible to ship to production unnoticed. Consumers must add an implementing partial alongside the generated template, **inside whatever namespace the generated `Foo.cs` declares**. By default that namespace is derived from the Daml package name (e.g. `My.Daml.Package`); if you override it with `--namespace` (CLI) or `CodeGenOptions.RootNamespace` (library), match the override exactly. Open the generated `Foo.cs` to confirm the namespace before writing your partial:
  ```csharp
  // In your project, alongside the generated Foo.cs.
  // Namespace MUST match whatever the generated file declares. Default is
  // package-derived, but `--namespace` / `RootNamespace` overrides it —
  // open the generated Foo.cs and copy the namespace from there.
  namespace My.Daml.Package;

  public sealed partial record Foo
  {
      public partial string Key => Owner.Id;  // or whatever your key expression is
  }
  ```
  The implementing partial's type kind must match the generated type kind: if you configure the codegen with `UseRecordTypes=false`, the generated template is a `public sealed partial class` and the implementing partial must also be a `partial class` (not `partial record`). Requires C# 13 on the consumer side, which means **.NET 9 SDK or later on the build machine** even when the consumer's `<TargetFramework>` is `net8.0` — the C# compiler is shipped with the SDK, not the target runtime, so a build host with only the .NET 8 SDK installed cannot parse the generated partial-property syntax. The codegen-emitted `.csproj` pins `<LangVersion>13</LangVersion>` only for packages that actually contain a key-bearing template, so key-less DARs continue to build with whatever LangVersion the consumer's SDK defaults supply. Lets consumers opt into typed key fetch / exercise wrappers (`Foo.FetchByKeyAsync`, `Foo.<Choice>ByKeyAsync` against `IPqsClient` / `ILedgerClient`) without inheriting a throwing default.
- **Unresolvable cross-package type references now throw at codegen time
  instead of warning and emitting unqualified names.** `ResolveTypeRefName`
  used to log a warning and return the bare sanitised name when the
  referenced foreign package was missing from the DAR (or no archive
  context was available); the consumer's `dotnet build` then surfaced a
  generic CS0246 with no pointer back to the cause. Codegen now throws
  `InvalidOperationException` naming the offending module / package id
  and suggesting a remediation (rebuild the DAR with the missing package
  included, or pass a multi-DAR input that resolves it). **Migration:**
  consumers who previously got a successful codegen run with warnings,
  then a downstream CS0246 build failure, will now get a codegen-time
  exception instead. The fix is the same — bundle the missing foreign
  package — only the failure point moves earlier. The unmapped-stdlib
  fallback (`MapStdlibType` returns null for an unknown stdlib type)
  still warns and returns unqualified.

### Added

- **Per-template `<TemplateName>SubmissionExtensions`** static class emitted
  alongside every generated template. Provides a typed `CreateAsync` extension
  that lifts the static-analyzer's signatory analysis into the C# call site.
  When every Daml signatory is a payload-field reference (the canonical
  `signatory platform, initiator, counterparty` shape against same-named
  `Party` fields), the generated `CreateAsync` takes only the payload and an
  `ILedgerClient` — the wrapper builds a `SubmitterInfo` from the payload's
  `Party` properties so the caller never restates a party that's already in
  the record. When the analyzer can't statically resolve the signatory
  expression, the wrapper takes an explicit `SubmitterInfo submitter`
  parameter (which implicitly converts from `string` / `Party`). Templates
  whose `observer` expression is statically resolvable also expose an
  `Observers(payload)` helper returning the derived observer party set from
  the payload.
- **`Daml.Runtime.Data.SynchronizerId`** — `readonly record struct` mirroring
  `Party`'s shape (null/whitespace-guarded constructor, `Id` accessor with
  default-uninitialized throw, implicit `→ string` conversion, explicit
  `string →` cast, JSON converter that round-trips as a plain string).
  Stored as an opaque string per Canton's documented guidance — does not
  decompose into name / fingerprint / protocol-version components, so the
  wrapper is safe across the Canton 3.4 (`name::fingerprint`) → 3.5
  (`name::fingerprint::protocol-version`) wire-format change.
- **`Daml.Runtime.Stdlib` stubs for `DA.Types.Tuple2` / `Tuple3`,
  `DA.Set.Types.Set`, `DA.NonEmpty.Types.NonEmpty`, and
  `DA.Map.Types.Map` / `DA.Internal.Map.Map`.** Each stub is a generic
  `record` with delegate-based `ToRecord` / `FromRecord` so the codegen
  can round-trip arbitrary CLR generic arguments (e.g. `Tuple2<Party, long>`)
  through the Daml-LF wire shape. The codegen now routes references to
  these types in `daml-prim` / `daml-stdlib` packages to the
  `Daml.Runtime.Stdlib.*` types and emits inline conversion lambdas at
  the call site. Unblocks `splice-token-test-trading-app` end-to-end and
  removes the `default! /* TODO */` decoder fallbacks for these types
  in `splice-amulet`, `splice-dso-governance`, `splice-wallet`,
  `splice-wallet-payments`, and `splice-util-featured-app-proxies`.
- **`Daml.Runtime.Contracts.ExercisedEvent`** — pure-data record describing a
  choice-exercise event observed in a transaction. Captures the subset of
  the Ledger API `ExercisedEvent` proto that the C# codegen needs to
  project typed choice results: wire-level `ChoiceArgument` and
  `ExerciseResult` (as `DamlValue`) plus `ContractId`, `TemplateId`,
  `InterfaceId?`, `ChoiceName`, `Consuming`, `ActingParties`, and
  `WitnessParties`. Other wire fields (event/node identifiers, package
  name, descendant tracking, implemented-interface lists) are intentionally
  omitted — they can be added later if a use case appears.
- **`TransactionResult.ExercisedEvents`** — new
  `IReadOnlyList<ExercisedEvent>` init-only property, defaults to an empty
  list. Lets codegen-emitted choice wrappers walk
  `ExercisedEvent.ExerciseResult` through a typed deserializer to project a
  typed `ExerciseOutcome<TResult>` for choices whose return type is not a
  contract id (e.g. `choice GetTrailingTwap : Decimal`). Additive only —
  existing 4-arg construction continues to compile and the property
  defaults to empty until a ledger-client transport implementation
  populates it.
- **`Daml.Runtime.IDamlType` marker interface** — common base for Daml-derived
  C# types. `Daml.Runtime.Contracts.ITemplate` and
  `Daml.Runtime.Contracts.IDamlInterface` both extend it. Lets generic helpers
  that don't dispatch on template-specific static metadata (`T.TemplateId`)
  constrain on the broader marker and accept either a concrete template or an
  interface marker. Additive only — existing `where T : ITemplate` constraints
  continue to compile unchanged.
- **Daml interface markers, first-class** — `ContractId<T>`'s constraint
  is relaxed from `where T : ITemplate` to `where T : IDamlType` (see above)
  so codegen-emitted interface markers (e.g. `IHolding` from the Splice
  token standard) flow through the typed contract id without the placeholder
  hack. `ContractId<T>.ToDamlValue()` resolves the embedded identifier per
  closed generic — `TemplateId` for templates, `InterfaceId` for interface
  markers — via reflection on the static virtual member.
  `ContractIdInterfaceCoercion.ToInterfaceContractId<TConcrete, TInterface>`
  extension method mirrors Daml's `toInterfaceContractId @I cid` at the
  C# type level, gated by `IImplements<TInterface>` on the source template
  so a coercion to an interface the template doesn't implement does not
  compile. `ExerciseCommand.ForInterface<TInterface>(cid, choice, arg)`
  builds an interface-typed exercise command — the wire-level `template_id`
  slot carries the interface id per Canton's `commands.proto` semantics.
- **Codegen-emitted interface choice exercisers** — for every Daml interface
  with one or more choices, the generated `IFoo.cs` file now also contains a
  sibling static `IFooExtensions` class with one `<Choice>Async`-style helper
  per choice. Callers can `cid.TransferAsync(arg)` on a `ContractId<IHolding>`
  without naming the concrete implementing template. Built via the new
  `ExerciseCommand.ForInterface<I>` runtime helper.
- **Typed `<Choice>Result` records and `FromCreatedContracts` projectors** for
  every Daml choice whose return type carries one or more `ContractId T`
  references. Choice creates a single template → single field; `Optional` →
  nullable field; `[…]` (list) → `IReadOnlyList<ContractId<T>>`; tuples are
  flattened across components. The static `FromCreatedContracts(IEnumerable<CreatedContract>)`
  projector returns `ExerciseOutcome<<Choice>Result>.One` when every required
  slot has the expected count, `.None` when a single-cardinality slot's
  template is missing, and `.Many` when a single- or optional-cardinality
  slot has more than one. Template matching is by `(ModuleName, EntityName)`
  only, so package-id drift from upgrades doesn't break projection.
- **`<Choice>Async(...)` extension methods on `ContractId<TemplateName>`** —
  one per create-bearing choice on each template, in a per-template static
  `<TemplateName>Extensions` class. The static-analyzer drives the parameter
  shape: when every controller is a payload-field reference, one named
  `Party` parameter per controller (declaration order) appears on the method,
  and the wrapper unions them into a `SubmitterInfo.actAs` set; when the
  template's `observer` clause (and/or the choice's `observer`) is also
  statically resolvable, those parties are added to `SubmitterInfo.readAs`
  so the submission carries the correct read-as set. When the controllers
  aren't statically resolvable, the wrapper falls back to a single
  `SubmitterInfo submitter` parameter. Body builds a `CommandsSubmission`,
  calls `ILedgerClient.TrySubmitAndWaitForTransactionAsync`, projects success
  via `<Choice>Result.FromCreatedContracts`. `DamlError` and `InfraError`
  outcomes pass through with all fields preserved. Workflow id has no
  default — workflow IDs are correlation keys, and a per-choice constant
  would bucket every submission of the same choice under one id and break
  observability.
- **`Daml.Ledger.Abstractions` `<PackageReference>` in generated csproj** —
  added unconditionally alongside `Daml.Runtime`. The package is
  interface-only and lockstep-versioned with the runtime, so pure-projector
  consumers absorb it at zero transitive weight. Required by the emitted
  `<Choice>Async` extension methods, which take `ILedgerClient`.
- **`PartyExpressionAnalyzer`** in `DarReader` — walks a Daml-LF expression
  rooted at a `List Party`-typed value (the shape carried by template
  `signatories` / `observers` and choice `controllers` / `observers`) and
  resolves it to an ordered list of payload-field references. Falls back to
  a single `Dynamic` marker on any unsupported shape (function calls,
  variable references, key projections), which surfaces as an explicit
  `SubmitterInfo` parameter in the generated wrapper. Recognizes `Cons`
  chains of `RecProj(template_param, fieldName)` and dereferences
  interned-expression nodes (LF 2.dev+). Distinguishes a static empty list
  (`[]`) from a `Dynamic` verdict so codegen can skip emission of helpers
  whose result would always be empty.
- **`DamlPartyAnalysis` / `DamlPartyReference`** model types on
  `DamlTemplate.Signatories`, `DamlTemplate.Observers`,
  `DamlChoice.Controllers`, and `DamlChoice.Observers`. Public so consumers
  (and tests) can inspect the analyzer's verdict before codegen runs.
  `DamlPartyPayloadField(string FieldName)` is the only resolved shape
  today; future shapes (constants, key projection) live behind their own
  records.
- **`PackageContext.GetInternedExpr(int)`** — resolves an interned-expression
  index against the package's `InternedExprs` table. Used by the static
  analyzer to dereference nodes in LF 2.dev+ packages.
- **Typed exerciser wrappers for non-contract-id choice returns**. For
  every choice whose declared return type carries no `ContractId T` slot at the
  top level (`Decimal`, `()`, records *via type-ref*, lists/optionals/tuples
  of primitives, etc.), codegen now emits a
  `<Choice>Async(this ContractId<TemplateName>, ILedgerClient, <args>, Party actAs, ...)`
  extension method on a `<TemplateName>NonContractExtensions` static class. The
  method calls `ILedgerClient.TrySubmitAndWaitForTransactionAsync`, walks the
  resulting `tx.ExercisedEvents` for
  the matching choice, runs the already-emitted
  `Choice<Choice>.ResultDecoder` over its `DamlValue` exercise result, and
  returns `Task<ExerciseOutcome<TReturn>>`. `DamlError` and `InfraError`
  outcomes pass through unchanged. Returns that expose at least one
  `ContractId T` slot at the top level — bare `ContractId T`,
  `Optional (ContractId T)`, `[ContractId T]`, and tuples with `ContractId`
  components — continue to flow through the `<TemplateName>Extensions` class
  and the slot-based projector. Records (referenced by name) whose fields
  happen to contain `ContractId`s also stay on the new wrapper path because
  the slot extractor intentionally does not unfold record types.
- **`Daml.Runtime.Stdlib.Unit`** — single-inhabitant marker (`Unit.Value`) that
  codegen surfaces at the call site for `()`-returning choices, mirroring
  `System.ValueTuple` semantics. Distinct from the wire-level
  `Daml.Runtime.Data.DamlUnit`: `Unit` is the typed return; `DamlUnit` is its
  wire encoding.
- **`Daml.Runtime.Commands.SubmitterInfo`** value type carrying the `actAs`
  (authorizing) and `readAs` (read-only visibility) party sets that propagate
  to `Commands.act_as` / `Commands.read_as` on the wire. `ActAs` is validated
  non-empty (throws `ArgumentException`); each caller-supplied party set is
  snapshotted into an immutable `FrozenSet<Party>` at construction (so caller
  mutations after the fact don't bleed in, and a consumer who casts the
  exposed `IReadOnlySet<Party>` back to a concrete type still can't mutate it),
  and any default-`Party` entry is rejected at construction time so the
  invariant fails loud rather than at later serialization.
  `Equals`/`GetHashCode` are overridden to compare by set contents
  (order-independent) rather than the record-struct-synthesized reference
  comparison on the backing fields. Implicit conversions from `string` and
  `Party` preserve the single-party ergonomic at every call site.
  Canonical home for the type: `Daml.Runtime` already owns `Party`, so command
  submitters belong here too. Foundation for the upcoming `SubmitterInfo`
  overloads on `Daml.Ledger.Abstractions.ILedgerClient` and the named-signatory
  codegen surface.
- **`CommandsSubmission.WithSubmitter(SubmitterInfo)`** helper — sets both
  `ActAs` and `ReadAs` from a typed submitter in one call. The preferred
  projection point for code-generated and library callers; mirrors the wire
  shape exactly.
- **`SubmitterInfo` overloads on `Daml.Ledger.Abstractions.ILedgerClient`**
  for `ExerciseAsync` (both result and void), `TryCreateAsync`,
  `TryExerciseForCreatedAsync`, `SubscribeAsync`, and `SubscribeActiveAsync`.
  Multi-party submitters (`ActAs.Count > 1`) and submitters carrying any
  `ReadAs` parties become expressible at the abstraction surface alongside
  the existing single-party `string actAs` overloads. Default-interface-method
  implementations preserve source compatibility with existing implementers:
  single-party submissions delegate to the legacy `string actAs` overload,
  multi-party submissions throw `NotSupportedException` until the implementation
  overrides the SubmitterInfo overload (foundation for
  named-signatories codegen).

### Changed — generated code shape

- Generated template `.cs` files now declare additional `using` directives
  unconditionally: `Daml.Ledger.Abstractions`, `Daml.Runtime.Outcomes`,
  plus the BCL set (`System`, `System.Collections.Generic`,
  `System.Threading`, `System.Threading.Tasks`) so generated source compiles
  without `<ImplicitUsings>` enabled in the consumer csproj. References to
  the new `Daml.Runtime.Stdlib` types (e.g. `Stdlib.Unit`,
  `Stdlib.Tuple2<…>`) are written fully qualified at the use site so no
  `using Daml.Runtime.Stdlib;` is emitted, avoiding spurious IDE0005 / CS8019
  failures in consumer projects with `TreatWarningsAsErrors`.

### Changed — BREAKING

- **`Daml.Runtime.Streams.ContractStreamEvent<T>.{Created, Archived, Exercised, Assigned, Unassigned}.WitnessParties`**
  changes from `IReadOnlyList<string>` to `IReadOnlyList<Party>` for sibling
  consistency with `Daml.Runtime.Contracts.{CreatedEvent, ArchivedEvent,
  ExercisedEvent}` and the broader project trend toward typed party values
  (`Party` instead of bare `string`). Consumers comparing or pattern-matching
  on these collections need to migrate `string` accesses to `Party.Id` (or
  use the `Party` value directly via `Equals`). **Implementor
  obligation**: a ledger-client transport implementation must construct
  `WitnessParties` as `Party` in its stream-projection code (rather than
  from proto `string` directly) before consuming the new `Daml.Runtime`
  version.
- **`ContractStreamEvent<T>.Assigned.{Source, Target}`** and
  **`Unassigned.{Source, Target}`** change from `string` to
  `Daml.Runtime.Data.SynchronizerId`. Same migration shape as the
  `WitnessParties` change above. Same implementor obligation:
  a ledger-client transport implementation must construct `Source` /
  `Target` as `SynchronizerId` in its reassignment-event projection
  (rather than from proto `string`) before consuming the new
  `Daml.Runtime` version.

### Fixed

- **`WriteChoiceMethod` now skips emission for choices with a fallback
  `<Choice>Arg` argument type.** Previously emitted code referenced
  `arg.ToRecord()` against a stub record with no `ToRecord()` method,
  breaking consumer compilation in those edge cases.
- **Cross-package choice argument types now resolve to their fully-qualified
  C# name instead of being silently dropped to `DamlUnit`.** Previously a choice
  whose argument was a `DamlTypeRef` pointing into a neighbouring package
  (e.g. a Splice DAR's choice taking a record imported from another splice
  package) ran through `GetChoiceArgumentInfo`'s "Other external references —
  fallback to DamlUnit as safe default" branch — the wrapper compiled, but
  the encoded payload was an empty unit and the user's record was lost on
  the wire. The defensive filters skipped emission entirely for those choices,
  so callers got a missing wrapper instead of a wrong one — better, but
  still wrong. `GetChoiceArgumentInfo` is now instance-level and routes
  non-Archive `DamlTypeRef` arguments through the same `ResolveTypeRefName`
  pipeline already used for record fields and return types. Wrappers across
  all five emit sites (`WriteSingleChoiceAsyncExerciser`,
  `WriteSingleNonContractChoiceAsyncExerciser`,
  `WriteInterfaceChoiceExtensionMethod`, `WriteChoiceArgumentType`,
  `WriteChoiceMethod`) now emit `{ResolvedNs}.{Record} argument` and
  `argument.ToRecord()` for cross-package shapes, and the defensive filters
  are gone. Consumers must run codegen on every package referenced by a
  choice argument so the resolved C# name is available at compile time —
  the standard multi-DAR codegen flow already does this. The
  companion behaviour change — `ResolveTypeRefName` now throws on
  unresolvable cross-package refs instead of warning and silently
  emitting unqualified names — is captured under `Changed — BREAKING`
  above.
- **Nested `()` in non-CID choice returns now surfaces as
  `Daml.Runtime.Stdlib.Unit` end-to-end.** Previously, a choice declared as
  `choice Foo : Optional ()`, `choice Foo : [()]`, or `choice Foo : TextMap ()`
  produced an async wrapper signed as `ExerciseOutcome<DamlUnit?>` /
  `ExerciseOutcome<IReadOnlyList<DamlUnit>>` / etc. — leaking the wire-level
  `DamlUnit` into the public API. The codegen now recurses through Optional,
  List, TextMap, and GenMap nesting and substitutes
  `Daml.Runtime.Stdlib.Unit` at every Unit slot. The projector emits a
  parallel inline decoder so the wire-typed `Choice<T,A,R>.ResultDecoder`
  doesn't type-mismatch against the public-surface signature. Limitation:
  parametric stdlib types (`Tuple2 a ()`, etc.) and user-defined parametric
  records with `()` components are not rewritten — those decode through
  `FromRecord`, which isn't pluggable per type-arg. Their public-surface
  type still names `DamlUnit` in the type-args, so consumers who pattern-
  match against `Daml.Runtime.Stdlib.Unit` for those positions will see a
  compile-time type mismatch at the call site. Very rare in practice;
  documented in `MapNonContractReturnType`'s doc-comment.
- **Generated `.cs` files no longer trip CS8019 in consumers with
  `<TreatWarningsAsErrors>`.** `WriteUsings` emits a fixed BCL set
  unconditionally so generated code compiles against consumers with
  `<ImplicitUsings>` disabled — but record-only files don't reference every
  using, and Roslyn doesn't suppress CS8019 ("unnecessary using directive")
  on `<auto-generated>` sources, so warnings-as-errors builds failed on the
  generator's own output. The file header now declares
  `#pragma warning disable CS8019` to mute the warning at source.
  Per-file conditional using emission (so the pragma can eventually be
  dropped) is non-urgent.
- **MSBuild `<LangVersion>` bump now self-clears when keys are removed.**
  Previously the codegen wrote a `.daml-needs-csharp13` sentinel only when a
  key-bearing template was present, but never deleted it on a regen that
  produced no key-bearing types — so a project that initially generated keys,
  then refactored them away, kept inheriting `<LangVersion>13</LangVersion>`
  forever. The marker is now renamed `.daml-langversion` and is **always**
  emitted: empty content means no bump, a numeric value (e.g. `13`) means the
  generated code requires that LangVersion. The MSBuild target reads the
  content via `<ReadLinesFromFile>` and only bumps `<LangVersion>` when the
  value is non-empty. Consumers who track the old
  `.daml-needs-csharp13` file directly (none expected — it was an internal
  contract between codegen and the build-time MSBuild target) should switch to
  `.daml-langversion`. The old file can be deleted from generated output dirs
  on first re-gen with the new codegen; both files are conventionally
  gitignored.

## [0.1.4] — 2026-05-01

### Added

- **`Daml.Ledger.Abstractions` (new package)** — transport-agnostic
  `ILedgerClient` interface lifted from an internal gRPC ledger bridge.
  Implementations live in their respective transport packages: a
  ledger-client transport implementation (gRPC) and a planned HTTP REST
  client.
  Generated codegen output (projector helpers, `<Choice>Async`
  extensions) will reference this package instead of the transport-specific
  one — projector-only consumers no longer transitively pull in a gRPC
  stack. Versioned in lockstep with `Daml.Runtime` and the codegen tool.
  The throwing-API variants `CreateAsync` and
  `SubmitAndWaitForTransactionAsync` (long `[Obsolete]` on the bridge's
  interface) are intentionally **not** part of the abstraction; only
  their outcome-based `Try*` replacements are surfaced. Other methods
  on the interface (`SubmitAsync`, `ExerciseAsync`, etc.) keep their
  existing names. Existing callers of the dropped methods migrate to
  `TryCreateAsync` / `TrySubmitAndWaitForTransactionAsync`.
- **`Daml.Runtime.Streams.ContractStreamEvent<T>`** — transport-agnostic discriminated
  record for typed contract subscription streams. Variants:
  `Created`, `Archived`, `Exercised`, `Assigned`, `Unassigned`, `Checkpoint`,
  `StreamError`. Lives in `Daml.Runtime` so any ledger client (gRPC, JSON,
  in-memory) can yield these without dragging the consumer into a
  transport-specific dep. `StreamError.StatusCode` is `int` (a
  `Grpc.Core.StatusCode` would be cast at the call site) — consumers stay free
  of any transport library. The counterpart in a ledger-client transport
  implementation (the prior owner of this type) is being migrated to
  consume from here.
- **`Daml.Runtime.Outcomes.ExerciseOutcome<T>`** — transport-agnostic
  discriminated record for exercise/create outcomes. Variants: `One`,
  `None`, `Many`, `DamlError`, `InfraError`. `T` is unconstrained
  (any payload shape). `InfraError.StatusCode` is `int`
  (cast `(int)Grpc.Core.StatusCode` at the gRPC client construction site)
  so this type is dep-free and any ledger client can yield it.
- **`Daml.Runtime.Outcomes.DamlErrorCategory`** — closed enum mirroring the
  Canton 3.5 documented error categories. Pre-existing canton type lifted
  here so it's reachable from generated code without a transport dep.
- **`Daml.Runtime.Contracts.TransactionResult`** and
  **`Daml.Runtime.Contracts.CreatedContract`** — pure data records for
  submitted-transaction results. Lifted from an internal gRPC ledger
  bridge; no transport deps, useful from any ledger client.
- **`Daml.Runtime.Contracts.TransactionResultExtensions`** with
  `Single<T>`, `TrySingle<T>`, `All<T>` over `TransactionResult` for
  template-typed projection of `CreatedContracts`. `(module, entity)`
  matching tolerates package-id drift.
- **`Daml.Runtime.Stdlib` namespace** with hand-coded stubs for Daml stdlib
  types that are not generated per package. Currently covers
  `DA.Time.Types.RelTime`.
- **`Daml.Runtime.Stdlib.GenericStub.NotImplemented<T>(string)`** — runtime stub
  used by generated `ToRecord`/`FromRecord` methods on records with
  type-parameter fields. Generated code compiles; calling the stub at runtime
  throws `NotImplementedException` with a pointer to the workaround.
- **Interface-placeholder record emission**. Daml-LF emits a same-named empty
  record for every `interface I where ...` declaration; the codegen now detects
  this case (record name matches an interface name in the same module) and
  emits the placeholder as `: ITemplate` with throwing static metadata. Lets
  `ContractId<I>` (which constrains `T : ITemplate`) keep compile-time safety
  while loudly failing if anyone reads `I.TemplateId` directly without first
  coercing to a concrete template type.
- **Cross-DAR type reference resolution**. Generated csprojs now emit a
  `<PackageReference>` for every type referenced from a foreign DAR, with
  fully qualified namespace prefixes in the generated code. Stdlib references
  route to `Daml.Runtime.Stdlib.*` instead of cross-package references.
- **`TextMap` and `GenMap` codec support** in both `ToValue` and `FromValue`
  conversion paths. Previously `IReadOnlyDictionary<,>.FromRecord(...)` was
  emitted, which never compiled; now generates `DamlTextMap`/`DamlGenMap`
  round-trips correctly.
- **Variant `FromRecord` stub**. Variants are emitted with a `FromRecord` that
  throws `NotImplementedException` so parent records that hold a variant field
  still compile.
- **`publish-splice.yaml` workflow** (workflow_dispatch only). Downloads a
  `hyperledger-labs/splice` release tarball, generates and packages each Splice
  DAR family in dependency order, pushes to GitHub Packages, and uploads
  per-family logs as artifacts. Inputs are validated against an explicit regex
  before flowing into `curl` URLs or MSBuild properties.

### Changed

- **BREAKING (codegen consumers): generated template files now emit a
  `<TemplateName>SubmissionExtensions` static class** with typed `CreateAsync`
  and `<Choice>Async` extensions on `ILedgerClient` / `ContractId<T>`. Method
  signatures and parameter shapes change vs. consumers using their own
  hand-rolled wrappers around the lower-level `Choice<T,A,R>` property:
  payload-derived signatories no longer require an explicit `actAs` argument,
  and per-controller named `Party` parameters appear on choice exercisers.
  Single-controller / single-signatory cases stay one-liners via
  `SubmitterInfo`'s implicit conversion from `string` / `Party`.
  `SubmitterInfo` is sourced from `Daml.Runtime.Commands` — the generated
  files do not import any transport package.
- **BREAKING:** `ContractId<T>`'s generic constraint relaxed from
  `where T : ITemplate` to `where T : IDamlType`. Source-compatible for all
  template-typed callers (`ITemplate : IDamlType`); enables the new
  interface-marker callers. Same change applied to `DamlContractId.ToTyped<T>`.
- **`ContractId<T>` typeparam doc** clarifies that `T` may be an interface or
  interface placeholder (in addition to a template), and points at the
  throwing-stub pattern.
- **Record-field deserialization expressions** simplified — redundant outer
  parens stripped (`((val).As<T>()).Value` → `val.As<T>().Value`). Generated
  code is unchanged in behavior.
- **`DamlList`/`DamlTextMap`/`DamlGenMap` materialization** in generated
  `ToRecord` methods now emits an explicit `(DamlValue)` projection cast
  followed by `.ToList()`/`.ToDictionary(...)` so the result satisfies the
  `IReadOnlyList<DamlValue>` / `IReadOnlyDictionary<string, DamlValue>`
  constructor parameter without relying on covariance.
- **Module-qualified enum dispatch** in the from-value conversion: enum
  type-refs are now keyed by `<module>:<name>` rather than just `<name>`, so
  a record and an enum sharing a simple name across different modules of the
  same package no longer route through the wrong dispatch path. Same fix
  applied to choice `ResultDecoder` emission for return-typed enums.
- **`<PackageReference>` cross-DAR list** in generated csprojs is now derived
  from types actually referenced in generated code (not from raw DAR-level
  dependency metadata, which was empty for splice DARs). Stdlib packages are
  filtered out and routed to `Daml.Runtime.Stdlib.*` instead.

### Fixed

- **`__@lock` invalid-identifier bug** when a Daml record has an `Optional`
  field whose name is a C# keyword (`lock`, `class`, `event`, ...). The
  sanitizer escapes keywords with a leading `@`; the codegen's pattern-match
  variable then concatenated `__@<keyword>` which is not a valid C# identifier.
  Fixed by stripping the `@` prefix from the local-variable name only.
- **`ToDictionary` throwing on duplicate type names** when a Daml package
  defines the same simple type name in different modules (e.g.
  `splice-amulet` defines records and enums named `Amulet` in distinct
  modules). The package-wide lookup is now built defensively as last-wins.
- **`ContractId<T>` for non-template `T`** — `splice-api-token-metadata-v1`
  uses `ContractId AnyContract` where `AnyContract` is an interface, not a
  template. The codegen's interface-placeholder emission (see Added) makes
  these contract ids compile-safe.

### Security

- **Workflow-input validation** in `publish-splice.yaml`: `splice_version`
  must match `^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$` and
  `package_version_suffix` (if non-empty) must match
  `^[A-Za-z0-9.+-]+$` before being interpolated into a release tarball URL or
  passed as an MSBuild `-p:VersionSuffix=...` property. `workflow_dispatch` is
  already gated by repo write access; this hardens the residual injection
  surface.

## [0.1.2] — 2026-04-24

### Changed — BREAKING

- **Package renamed**: `Daml.Codegen.CSharp.Runtime` → `Daml.Runtime`.
  Consumers must update their `PackageReference` and `using` directives.
  Type names are unchanged.
- **Pre-release version scheme** now uses dot separators
  (`0.1.2-<branch>.<run>.<sha>` instead of `0.1.2-<run>-<branch>-<sha>`) so that build
  numbers compare numerically under SemVer 2.0. Dev packages published under
  the old `0.1.1-*` scheme have been removed from the GitHub Packages feed
  (`nuget.pkg.github.com/peacefulstudio`); consumers pinned to them must
  upgrade to `0.1.2-*`.

### Added

- **First-class `Party` value type**. Daml `Party` now maps to a
  dedicated `Party` struct instead of `string`, giving type-safety at the
  boundary between generated code and application code.
- **`FromDamlValue<T>` helper** on `Daml.Runtime` — converts a
  `DamlValue` into strongly-typed .NET values (generated records, primitives,
  `Party`, `ContractId<T>`, and `DamlValue` subtypes) in one call, removing
  the need for manual `FromRecord` wiring in application code.

### Fixed

- **`Party` JSON serialization** is now a plain JSON string (`"Alice::1220…"`),
  matching the Ledger JSON API and PQS wire format. Previously
  `Party` was serialized as a JSON object, which broke PQS-based consumers.

## [0.1.0] — initial alpha (internal, never published to NuGet.org)

Initial release of the three-package suite:

- DAR/DALF parsing via generated protobuf stubs
- C# code generation for records, variants, enums, templates, choices,
  contract keys, interfaces, generic types, and package upgrades
- Runtime library covering all Daml primitives with JSON serialization
- CLI distributed as a `dotnet tool`

Historical pre-release dev builds (`0.1.0-*`, `0.1.1-*`) were published to
the GitHub Packages NuGet feed
(`nuget.pkg.github.com/peacefulstudio`) during development and have
since been pruned. They are not supported.

[Unreleased]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.5.0-preview.3...HEAD
[0.5.0-preview.3]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.5.0-preview.2...v0.5.0-preview.3
[0.5.0-preview.2]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.5.0-preview.1...v0.5.0-preview.2
[0.5.0-preview.1]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.4.1-preview.1...v0.5.0-preview.1
[0.4.1-preview.1]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.4.0-preview.3...v0.4.1-preview.1
[0.4.0-preview.3]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.4.0-preview.2...v0.4.0-preview.3
[0.4.0-preview.2]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.4.0-preview.1...v0.4.0-preview.2
[0.4.0-preview.1]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.3.0-preview.1...v0.4.0-preview.1
[0.3.0-preview.1]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.2.0-preview.3...v0.3.0-preview.1
[0.2.0-preview.3]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.2.0-preview.2...v0.2.0-preview.3
[0.2.0-preview.2]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.2.0-preview.1...v0.2.0-preview.2
[0.2.0-preview.1]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.8-preview.5...v0.2.0-preview.1
[0.1.8-preview.5]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.8-preview.4...v0.1.8-preview.5
[0.1.8-preview.4]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.8-preview.2...v0.1.8-preview.4
[0.1.8-preview.3]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.8-preview.2...v0.1.8-preview.3
[0.1.8-preview.2]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.8-preview.1...v0.1.8-preview.2
[0.1.8-preview.1]: https://github.com/peacefulstudio/daml-codegen-csharp/releases/tag/v0.1.8-preview.1
[0.1.7]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.6...v0.1.7
[0.1.6]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.5...v0.1.6
[0.1.5]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.2...v0.1.4
[0.1.2]: https://github.com/peacefulstudio/daml-codegen-csharp/compare/v0.1.0-alpha.3...v0.1.2
[0.1.0]: https://github.com/peacefulstudio/daml-codegen-csharp/releases/tag/v0.1.0-alpha.3
