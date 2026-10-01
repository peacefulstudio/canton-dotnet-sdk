<!-- Copyright 2026 Peaceful Studio OÜ -->
<!-- SPDX-License-Identifier: Apache-2.0 -->

# LF-JSON wire samples

Raw responses from a live Canton participant, captured so that decoder work is
built against observed encodings rather than against a specification or a
remembered table. Every file under `data/` is a verbatim server response plus
the Daml type it came from and the request that produced it.

Captured 2026-07-31 against canton-localnet (Splice 0.6.14, Canton **3.5.10**),
`a-validator-1` slot, from a DAR built on Daml SDK **3.5.2** targeting
**Daml-LF 2.3**.

## Why LF 2.3

The capture model targets LF 2.3 rather than the 2.1 the shipped fixtures use,
because contract keys require LF >= 2.3 — on 2.1 the compiler rejects them with
`Contract Keys not supported on current lf version (2.1), feature supported in
from 2.3`. Capturing at 2.3 covers the key-bearing shapes as well; it does not
change the `--target=2.1` pin that `conformance/` and `samples/` build with.

The table below is nonetheless authoritative for the LF 2.1 code this repo
ships. The whole model was rebuilt at `--target=2.1` (minus `Keyed`, which 2.1
cannot express), recaptured against the same participant, and diffed field by
field against the 2.3 set: **every encoding is identical**. The only difference
across the two capture sets is the `Keyed` contract's presence in the 2.3 ACS.
`contractKey` and `contractKeyHash` appear only on the key-bearing contract, not
on unkeyed ones, in either version. Scope: this covers the shapes the capture
model expresses; it is not a proof that no 2.3-only encoding exists for a type
the model omits.

## Layout

| Path | What |
|---|---|
| `daml/` | The capture model — one template per shape family |
| `data/` | The captures |

## Observed encodings

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
under `viewValue` (`data/acs_interface_view_holding.json`).

Contract keys arrive as `CreatedEvent.contractKey` alongside a base64
`contractKeyHash`. A tuple key encodes as a **record**, not an array:
`{"_1":"party::1220…","_2":"alpha"}` (`data/create_keyed_contract_key.json`).

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
  a reader to handle. (`data/acs_wildcard_verbose_{true,false}.json`)
- **Nested `Optional` reaches this repo's codegen, and the array encoding is
  what makes it representable.** A Daml `Optional (Optional a)` generates an
  `Optional<Optional<a>>`, and every level *of that chain* is written and read
  in the array form the table records. The rule is scoped to the chain, not to
  the runtime at large: a single-level `Optional a` keeps the flat
  `null`-or-bare-value form, and so does an `Optional` separated from another by
  an intervening type, such as `Optional (Box (Optional Text))`. The two travel
  as different wire nodes for that reason.
  `data/probe_nested_optional_matrix.json` is the pin: one create per candidate
  encoding, showing the participant accept `[]`, `[[]]` and `[["deep"]]` and
  reject `null`, `[null]` and `["deep"]` with HTTP 500. `[null]` is the one to
  watch — it is the mixed form, array at the outer level and flat at the inner,
  which is what an encoder taught the chain encoding at only one level emits for
  `Some None`. It looks plausible, and the participant refuses it.

## Recapture

The scripted rig that produced these files is no longer in the tree; the captures and the
`daml/` model they came from are what remain.

A recapture allocates a fresh party, so contract ids, party ids and offsets
differ from the ones recorded here — the encodings are what these files pin,
not the identifiers.

To redo the LF 2.1 comparison, copy `daml/` with `--target=2.1` in `daml.yaml`,
a distinct `name:`, and the `Keyed` template deleted; build it; then capture the
same requests against the 2.1 artifacts into a scratch output directory. Compare the two sets by walking both JSON trees
and asserting leaf values under `createArgument`, `choiceArgument`,
`exerciseResult`, `contractKey` and `viewValue` — party ids and contract ids
aside — while comparing only shape elsewhere, since offsets, update ids, record
times and package hashes differ by construction. Group active-contract entries
by qualified template name first; ACS ordering is not guaranteed. Validate any
such comparison against a deliberately mutated copy before trusting a clean run,
or a silently vacuous walk will report agreement it never checked.
