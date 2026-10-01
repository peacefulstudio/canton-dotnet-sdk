# `splice-test-token-v1` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

First fixture pinning `interface instance` emission — templates that
`interface instance`-implement one or more foreign, cross-package
token-standard interfaces. Every template in this package carries at least
one such block:

- `Token` implements `Splice.Api.Token.HoldingV1.Holding` (1 instance).
- `TokenAllocation` implements `Holding` and
  `Splice.Api.Token.AllocationV1.Allocation` (2 instances).
- `TokenRules` implements `Splice.Api.Token.AllocationInstructionV1.AllocationFactory`
  and `Splice.Api.Token.TransferInstructionV1.TransferFactory` (2 instances).
- `TokenTransferOffer` implements `Splice.Api.Token.TransferInstructionV1.TransferInstruction`
  and `Holding` (2 instances).

That is 7 `interface instance` blocks total. A sibling `splice-test-token-v2`
and `token-test-trading-app-v2` DAR carry the same shape with fewer
instances; `-v1` is the richest single representative, so only this one is
vendored.

The emitter now emits an `IImplements<TInterface>` facet in each template
record's base list — one entry per interface the template implements, resolved
to that interface's C# marker type. The DAR decoder captures each template's
implemented interfaces into `DamlTemplate.Implements` (via
`IntermediateDarReader`), the field is carried all the way into the
`IntermediateDar` proto, and `TemplateEmitter` now consults it. The 7
`interface instance` blocks above surface as 7 `IImplements<...>` entries — one
on `Token`, two on `TokenAllocation`, two on `TokenRules`, and two on
`TokenTransferOffer`.

`IImplements<TInterface>` (`where TInterface : IDamlInterface`) is a bare marker
interface, so membership is all that is emitted: no interface-view accessor and
no interface-choice surface land on the template record. Pinning the base lists
here means a change to what the emitter emits for `interface instance` shows up
as a diff in this snapshot instead of landing silently.

Beyond the `interface instance` angle, this DAR also exercises:

- Concrete `template ... where` declarations (`Token`, `TokenAllocation`,
  `TokenRules`, `TokenTransferOffer`), each with a single payload field and
  only the synthetic `Archive` choice — the templates' own business choices
  live on the foreign interfaces they implement, not on the template, so
  there is no template-native choice surface to speak of.
- Cross-family record-field references that resolve to a **sibling snapshot
  already vendored in this repo**: `Token.Holding` is typed
  `Splice.Api.Token.Holding.V1.HoldingView`, the exact record pinned by the
  `splice-api-token-holding-v1` snapshot. `FromRecord` emits a real
  `Splice.Api.Token.Holding.V1.HoldingView.FromRecord(...)` call (not a
  placeholder — see below).
- Cross-family record-field references to packages **not** vendored anywhere
  in this repo (`TokenAllocation.Allocation` :
  `Splice.Api.Token.Allocation.V1.AllocationSpecification`;
  `TokenTransferOffer.Transfer` :
  `Splice.Api.Token.Transfer.Instruction.V1.Transfer`), exercising the same
  qualified-reference-without-emitting-the-referenced-package path documented
  by the `splice-amulet-name-service` snapshot.

### The `default(...)!` placeholder pattern does not apply here

`splice-amulet-name-service`'s README documents a pre-existing emitter
limitation: `FromRecord` emits a `default(...)!` placeholder with a `TODO:
Implement deserialization` comment when a field's type is a **generic type
application** across a package boundary (e.g.
`Splice.Amulet.AmuletCreateSummary<ContractId<Splice.Amulet.Amulet>>`).
None of this DAR's cross-package fields are generic applications — `Holding`,
`AllocationSpecification`, and `Transfer` are all plain (non-generic) record
references — so every one of them takes the normal
`<Type>.FromRecord(record.GetRequiredField(...).As<DamlRecord>())` path.
There is no `default(...)!` anywhere in this snapshot.

### Cross-package references and standalone compilation

The `expected/` tree names several sibling packages that are not vendored
anywhere in this repo, so a standalone Roslyn compile of `expected/` against
the real `Daml.Runtime` and `Daml.Ledger.Abstractions` assemblies reports
`CS0234` ("type or namespace does not exist") for each of them. That is the
same class of error `splice-api-token-holding-v1` itself produces standalone
(its own `HoldingView.Meta` field types `Splice.Api.Token.Metadata.V1.Metadata`,
likewise never vendored) — the pre-existing, by-design consequence of vendoring
one family's DAR without its dependencies, not anything specific to
`interface instance` handling.

The record-field references account for two of them
(`Splice.Api.Token.Allocation.V1.AllocationSpecification`,
`Splice.Api.Token.Transfer.Instruction.V1.Transfer`). The `IImplements<...>`
base-list entries add the interface markers as further cross-package
references:

- `Splice.Api.Token.Holding.V1.IHolding` — resolves into the already-vendored
  `splice-api-token-holding-v1` sibling snapshot, exactly as `HoldingView`
  does, so this one is available rather than unvendored.
- `Splice.Api.Token.Allocation.V1.IAllocation`
- `Splice.Api.Token.Allocation.Instruction.V1.IAllocationFactory`
- `Splice.Api.Token.Transfer.Instruction.V1.ITransferFactory`
- `Splice.Api.Token.Transfer.Instruction.V1.ITransferInstruction`

The latter four live in packages not vendored anywhere here and so join the
`CS0234` set above.

## About `using` directives

Each generated file emits only the namespaces its body actually references,
tracked at codegen time. No generated file emits an unused `using`, so the
generated headers carry no `#pragma warning disable CS8019`.

## Refreshing the snapshot

`scripts/refresh-snapshot.sh` regenerates `intermediate.binpb` from the vendored
DAR with a JVM helper assembled from the working tree's sources, then
regenerates the `expected/` tree from that proto with the current codegen
source. Pass `--skip-binpb` to keep the vendored proto and refresh only
`expected/`. Run it from the repo root (POSIX shell; on Windows, use WSL or Git
Bash):

```bash
scripts/refresh-snapshot.sh splice-test-token-v1
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-test-token-v1.dar` is the upstream Splice archive it was derived from
(`splice-test-token-v1-1.0.1.dar` from the Splice `0.8.4` `splice-node`
release tarball), kept alongside as the frozen upstream artifact and used as
the regeneration input. Do not hand-edit `intermediate.binpb`: the refresh
script regenerates it from this DAR on every run, and CI regenerates it the
same way and fails on any byte delta. If the upstream Splice package genuinely
needs to advance, replace the files in place, refresh the snapshot per the
procedure above, and call out the version bump in the pull request
description.
