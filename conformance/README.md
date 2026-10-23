# Conformance corpus

DAML models that define the type shapes codegen claims to support. Three corpora are
shipped (compiled + embedded) in `Daml.Codegen.Testing.Conformance`: `richtypes` below,
plus the contract-key and default-target corpora described further down.

The committed `.dar` is the source of truth for the package build. `richtypes` and
`contractkeys` carry a content-addressed package name (`<base>-h<hash12>`, the hash
covering the Daml sources and `daml.yaml`) because Canton's upgrade check works per
package name: two builds with different content need different names, or the second
build is rejected as `KNOWN_PACKAGE_VERSION` by a participant already holding the
first. To grow the corpus (new type shapes), edit `RichTypes.daml`, then run one
command, which re-derives the name, rebuilds the DAR and regenerates the shipped
bindings and the pinned SHAs the determinism gate compares against (it reads this
DAR):

    scripts/regenerate.sh rebuild-dar --package richtypes --base richtypes

Commit the changed `daml.yaml`, DAR, `Generated/` tree and `tests/determinism/shas/`
together.
A drift test reads the shipped DAR fresh and fails if a step was skipped.

`RichRecord` covers the primitive, collection and nominal shapes; `TypeCorners`
covers the harder corners — parameterized records and variants (`Box`, `Slot`)
instantiated in a template payload, `GenMap` keyed by `Party` and by `Int`,
`Either`, `Tuple2`/`Tuple3`, the recursive record `Branch`, an `Optional` nested
directly inside an `Optional` (`maybeMaybeNote`) and one separated from its outer
`Optional` by an intervening record (`nestedNote`), an `Optional` over a record's
own type variable (`Crate`) and one carried by an `Either` arm (`noteOrRank`) —
positions C# nullable syntax cannot spell, so they emit the `Optional<T>` wrapper
instead — and the `Numeric` scale extremes 0 and 37. `ThreeDeepOptionals` carries an
`Optional (Optional (Optional Text))` in `deepNote` and again in the trailing `tailDeepNote`,
so a chain three levels deep is emitted and round-tripped end to end. The `Holding` interface carries choices (`Describe`, `Reissue`, `Split`) as well as a
view, so the interface-choice emitter path is exercised through a real DAR rather
than a synthetic package; `Split` is nonconsuming and returns `[ContractId Holding]`,
so an interface choice whose result is itself a generic family is covered too.
`GenericResults` covers the same ground for a template's own choices, one choice per
top-level generic family: `ReturnContractIds` returns `[ContractId GenericResults]`;
`ReturnOptionalText` returns `Optional Text` driven by its own `wantSome` argument;
`ReturnTextMap` returns `TextMap Int`; `ReturnGenMap` returns `Map Text Int`;
`ReturnTuple` returns `(Text, Int)`; `ReturnEither` returns `Either Text Int` driven
by its own `wantRight` argument; `ReturnSet` returns `Set Int`; `ReturnNonEmpty`
returns `NonEmpty Int`; and `ReturnNestedOptional` returns `Optional (Optional Text)`
driven by its own `outer`/`inner` arguments. `ReturnGenMap`'s result is written with
`DA.Map`'s `Map` rather than the bare `GenMap` primitive on purpose: `Map` is a type
synonym for `GenMap`, not a distinct Daml-LF shape — `TypeCorners.quotaByParty` and
`.labelByRank` above already pin that erasure for `Map`-typed fields, and
a characterization test pins it again for this choice's result — so
one choice proves both the `GenMap` and the `Map` family from this corpus's
top-level-generic-family choice-return conformance requirement; a second choice under a
different Daml spelling would characterize the same `DamlTypeApp(GenMap, ...)` shape a
second time, not a different one. Every one of these choices renders as a C# constructed
generic type (`Either<long, string>`, `IReadOnlyDictionary<string, long>`, `Set<long>`,
`NonEmpty<long>`, `Optional<Optional<string>>`, ...); before it was fixed, that tripped a
defect in `ChoiceEmitter.NonContractExercisers.cs`'s
`WriteSingleNonContractChoiceAsyncExerciser`, which interpolated the raw return-type name
into an XML `<c>...</c>` doc-comment tag without escaping `<`/`>`/`,`, turning
`GenerateDocumentationFile` + `TreatWarningsAsErrors` (both repo-wide via
`Directory.Build.props`) into a `CS1570` build break for every one of them. Escaping the
interpolated type name fixed it, which is what unblocked this template-choice half of the
corpus; see the typed exercise-path choice-descriptor work (choice descriptors as witnesses,
`ArgumentDecoder` + interface-choice descriptors) for the wider follow-up.

`OptionalTails` and `NestedOptionalTails` place a `None` at every position the JSON
Ledger API may encode differently: `OptionalTails` carries a mid-record
`Optional Text` (`midNote`), a record whose own last field is an `Optional Text`
(`inner : TrailingNote`, trailing `remark`) and a trailing `Optional Text` (`tailNote`);
`NestedOptionalTails` carries an `Optional (Optional Text)` mid-record (`midMaybe`) and
trailing (`tailMaybe`). Each template's nonconsuming `Echo…` choice returns the contract's
own payload, so the same shapes are read back both as a create argument and as an exercise
result.

`richtypes/daml.yaml` carries `-Wno-upgrade-interfaces` in its `build-options`:
at `--target=2.1`, a smart-contract-upgrade-eligible target, `damlc` refuses a
module that mixes interface definitions with the templates implementing them
(as `RichTypes.daml` does — `Holding` plus its implementers) unless that
warning is explicitly silenced. `contractkeys/daml.yaml` carries the same flag
for the same reason: `ContractKeys.daml` mixes the `Stewardship` interface
with its implementing template (`Steward`) too.

Contract keys are deliberately absent: they need a Daml-LF version above the
`--target` this corpus pins, and retargeting it would remove the 2.1 emit-path and
main-package read coverage *this corpus* pins — the vendored Splice snapshots under
`tests/Daml.Codegen.CSharp.Tests/Snapshots/` keep their own 2.1 main packages either way. The
2.1 dependency read floor stays regardless: at the SDK version pinned here, it is held by the
twenty-seven per-module `daml-prim`/`daml-stdlib` component packages a DAR bundles whatever
its target, not by this corpus. So keys live in their own package below.
A characterization test asserts both the pin and the absence of
keyed templates, so moving the pin or adding a keyed template here has to be a
deliberate act.

The `contractkeys` package carries the contract-key shapes, and pins
`--target=2.3` because neither 2.1 nor 2.2 can express a contract key at all. It
is compiled and shipped by `Daml.Codegen.Testing.Conformance` alongside
`richtypes`, and its DAR is reachable from
`ConformanceCorpus.OpenDar(ConformancePackage.ContractKeys)`. The six key shapes
it covers are the ones real key-bearing Daml packages use: a record built from
several payload fields (`Account`), a record whose field comes from a projection
nested inside a payload record (`Holiday`), a record built by a function declared
in another module, sharing no field name with the payload (`Schedule`), a
bare `Party` key whose maintainer clause names the key binder itself
(`Steward`), a tuple key `(Party, Text)` (`Membership`), and a tuple key with an
Optional-family component, `(Party, Optional Text)` (`Enrollment`) — both derive
their maintainer from the key's first component (`key._1`). `Steward` also
implements the `Stewardship` interface, which declares both a `viewtype`
(`StewardshipView`) and a non-`Unit` nonconsuming choice (`DescribeCharter`),
so a keyed template backing an interface with a real choice is covered too.
The package also carries one unkeyed template, `Offer` (module `Disclosure`),
whose only signatory is its issuer and whose nonconsuming `Inspect` choice is
controlled by whichever `reader` party the exercise names, so a party that is no
stakeholder of the contract can exercise it only by disclosing it explicitly.
A characterization test reads the DAR and
asserts each of them, so a fixture edit that flattens a shape fails rather than
quietly narrowing the evidence. It also carries a content-addressed package name
(see above); rebuild its DAR, and refresh the `Generated/ContractKeys/` sources the
package compiles, with one command:

    scripts/regenerate.sh rebuild-dar --package contractkeys --base contractkeys

The determinism gate reads only `richtypes`, so the determinism pins do not
need refreshing for a contract-key-only change.

The `crossmodulecollision` package is a codegen-drift regression
fixture rather than a type-shape corpus: two modules (`CollisionA`,
`CollisionB`) each declare a same-named `Retag` choice-argument record with a
different field list, guarding against a cross-module simple-name collision in
the emitter. Its generated output is pinned by the `crossmodulecollision`
drift snapshot under `tests/Daml.Codegen.CSharp.Tests/Snapshots`; rebuild its
DAR the same way:

    cd conformance/crossmodulecollision && dpm build && \
      cp .daml/dist/crossmodulecollision-*.dar ./crossmodulecollision.dar

That snapshot decodes this DAR directly, so run `scripts/regenerate.sh write`
after a rebuild to refresh it.

The `nonserializable` package declares a data type whose only field is a
function arrow, `data Fn = Fn with f : Int -> Int`, beside a template that uses
it in a choice body. Daml-LF marks `Fn` non-serializable, so the decoder drops it
and the emitter never sees an `Arrow` field it cannot spell. It builds with
`--target=2.2`, the one fixture that keeps Daml-LF 2.2 covered while the compiler
default is 2.3. Its generated output is pinned by the `nonserializable` drift
snapshot under `tests/Daml.Codegen.CSharp.Tests/Snapshots`; rebuild its DAR as
described in `conformance/nonserializable/README.md`.

The `defaulttarget` package is a single boring template whose only job is
to be compiled with **no `build-options:` block at all**. `damlc` picks a
Daml-LF target when a project does not request one, and it announces that
choice nowhere, so a scaffolded project hands the toolchain a version the rest
of this corpus never exercises. The omission is load-bearing: adding
`--target=` here — to any version, including the one currently emitted — turns
the fixture into a restatement of the pin and stops it tracking the default.
A characterization test reads this DAR off disk and asserts the
version it actually carries, so a moved default surfaces as a failing
assertion rather than as silence. It is a shipped package too: the emitter runs
over it and `Daml.Codegen.Testing.Conformance` compiles and ships the result, so
the default target is covered on the emit path and not only on the read one.
Rebuild its DAR the same way as the other shipped packages, which gives it a
content-addressed package name (`defaulttarget-h<hash12>`) and refreshes the
generated tree in one step:

    scripts/regenerate.sh rebuild-dar --package defaulttarget --base defaulttarget

The determinism gate reads only `richtypes`, so the determinism pins do not need
refreshing for a default-target-only change.

## Wire captures

Raw responses from a live Canton participant, kept next to the corpus whose shapes they
pin, so that decoder work is built against observed encodings rather than against a
specification or a remembered table. Each file under `conformance/<corpus>/wire/` is a
verbatim server response plus the Daml type it came from and the request that produced
it. The `richtypes` captures sit in `conformance/richtypes/wire/` and the one key-bearing
capture in `conformance/contractkeys/wire/`; both directories ship with their corpus.

`tests/Daml.Codegen.Testing.Conformance.Tests` decodes every capture into the generated
`RichTypes` / `ContractKeys` types (`WireCaptureDecodeTests`) and holds the serializer
against each one (`SerializedWireShapeMatchesCaptureTests`, and the REST submission path in
`Canton.Ledger.Rest.Client.Tests`). A capture no test consumes fails the build, so a new
file needs a row in `WireCaptures.SupportedWireSamplePayloads` (or the fact-consumed list in
`WireCaptureDecodeTests`).

Captured 2026-07-31 against canton-localnet (Splice 0.6.14, Canton **3.5.10**),
`a-validator-1` slot, from a DAR built on Daml SDK **3.5.2** targeting **Daml-LF 2.3**.
That throwaway capture model (`WireTypes`) was folded into `RichTypes.daml`: `RichRecord`
grew `nestedNote`, `genMap` and `unitField` plus the `DescribeOutcome` and `Ping` choices
so the captured payloads decode into it unedited. The `damlType` strings inside the capture
files still name `WireTypes:...`, and `exercise_describe.json` records the choice as
`Describe` where the corpus calls it `DescribeOutcome` (the `Holding` interface already owns
`Describe`); the decoders read the payload only, so neither matters to them. The key-bearing capture's `contractKey`
is a `(Party, Text)` tuple, identical in shape to `Membership`'s key, so it is decoded with
`Membership.Key`.

### Why LF 2.3

The capture model targeted LF 2.3 rather than the 2.1 the corpus builds with, because
contract keys require LF >= 2.3 - on 2.1 the compiler rejects them with `Contract Keys not
supported on current lf version (2.1), feature supported in from 2.3`. Capturing at 2.3
covered the key-bearing shapes as well; it does not change the `--target=2.1` pin that
`conformance/` and `samples/` build with.

The table below is nonetheless authoritative for the LF 2.1 code this repo ships. The whole
model was rebuilt at `--target=2.1` (minus the keyed template, which 2.1 cannot express),
recaptured against the same participant, and diffed field by field against the 2.3 set:
**every encoding is identical**. The only difference across the two capture sets was the
keyed contract's presence in the 2.3 ACS. `contractKey` and `contractKeyHash` appear only on
the key-bearing contract, not on unkeyed ones, in either version. Scope: this covers the
shapes the capture model expressed; it is not a proof that no 2.3-only encoding exists for a
type the model omitted.

### Observed encodings

| Daml type | Wire form | Sample |
|---|---|---|
| `Int64` | JSON **string** | `"42"`, `"-1"` |
| `Numeric n` | string at the **declared** scale | `Numeric 10` -> `"42.5000000000"`; `Numeric 2` -> `"1.25"` |
| `Text` | string | `"hello"`, `""` |
| `Bool` | JSON boolean | `true` |
| `Date` | string | `"2026-07-31"` |
| `Time` | string, **6 fractional digits or none** | `"2026-07-31T12:34:56.123456Z"`, `"2026-07-31T12:34:56Z"` |
| `Party`, `ContractId a` | string | `"wire87…::1220…"` |
| `Optional a` (`a` not optional) | `Some` is the **bare value**, `None` is `null` | `"present"` / `null` |
| `Optional (Optional a)` | one array level **per** `Optional` | `None` -> `[]`, `Some None` -> `[[]]`, `Some (Some "deep")` -> `[["deep"]]` |
| `[a]` | array | `["x","y"]`, `[]` |
| `TextMap a` | object | `{"a":"1"}`, `{}` |
| `Map k v` (GenMap) | array of `[key, value]` pairs | `[["party::1220…","7"]]`, `[]` |
| `()` | empty object | `{}` |
| record | object keyed by field name | `{"nickname":"nick","level":"3"}` |
| variant, tagged arm | `{"tag":…,"value":…}` | `{"tag":"Win","value":{"prize":"1.25","tier":"gold"}}` |
| variant, nullary arm | `value` is an **empty object** | `{"tag":"Pending","value":{}}` |
| enum | **bare string** | `"Hearts"` |

Interface views arrive as `CreatedEvent.interfaceViews[]`, each entry carrying
`interfaceId`, `viewStatus`, `implementationPackageId`, and the record itself
under `viewValue` (`richtypes/wire/acs_interface_view_holding.json`).

Contract keys arrive as `CreatedEvent.contractKey` alongside a base64
`contractKeyHash`. A tuple key encodes as a **record**, not an array:
`{"_1":"party::1220…","_2":"alpha"}` (`contractkeys/wire/create_keyed_contract_key.json`).

On the exercise path, `ExercisedEvent.choiceArgument` is the argument record
with no wrapper, and `exerciseResult` follows the ordinary encoding for its
type — a `ContractId` result is a string, a variant result is `{"tag":…}`, and a
`Unit` result is `{}`. A no-argument choice sends `{}`, an **empty record**
rather than a unit value.

One finding that removes work rather than adds it, and one the codegen now
depends on:

- **`verbose` does not change the payload.** Requesting the same ACS with
  `verbose: true` and `verbose: false` returns byte-identical events; only the
  opaque `streamContinuationToken` differs. There is no verbose-form variant for
  a reader to handle. (`richtypes/wire/acs_wildcard_verbose_{true,false}.json`)
- **Nested `Optional` reaches this repo's codegen, and the array encoding is
  what makes it representable.** A Daml `Optional (Optional a)` generates an
  `Optional<Optional<a>>`, and every level *of that chain* is written and read
  in the array form the table records. The rule is scoped to the chain, not to
  the runtime at large: a single-level `Optional a` keeps the flat
  `null`-or-bare-value form, and so does an `Optional` separated from another by
  an intervening type, such as `Optional (Box (Optional Text))`. The two travel
  as different wire nodes for that reason.
  `richtypes/wire/probe_nested_optional_matrix.json` is the pin: one create per candidate
  encoding, showing the participant accept `[]`, `[[]]` and `[["deep"]]` and
  reject `null`, `[null]` and `["deep"]` with HTTP 500. `[null]` is the one to
  watch — it is the mixed form, array at the outer level and flat at the inner,
  which is what an encoder taught the chain encoding at only one level emits for
  `Some None`. It looks plausible, and the participant refuses it.

### Recapture

The scripted rig that produced these files is no longer in the tree. A recapture allocates
a fresh party, so contract ids, party ids and offsets differ from the ones recorded here -
the encodings are what these files pin, not the identifiers. Captures are refreshed from the
corpus DAR itself (`richtypes` or `contractkeys`), not from a separate model.

To redo the LF 2.1 comparison, build a copy of the corpus with `--target=2.1` in `daml.yaml`
and a distinct `name:`, then capture the same requests against the 2.1 artifacts into a
scratch output directory. Compare the two sets by walking both JSON trees and asserting leaf
values under `createArgument`, `choiceArgument`, `exerciseResult`, `contractKey` and
`viewValue` - party ids and contract ids aside - while comparing only shape elsewhere, since
offsets, update ids, record times and package hashes differ by construction. Group
active-contract entries by qualified template name first; ACS ordering is not guaranteed.
Validate any such comparison against a deliberately mutated copy before trusting a clean
run, or a silently vacuous walk will report agreement it never checked.
