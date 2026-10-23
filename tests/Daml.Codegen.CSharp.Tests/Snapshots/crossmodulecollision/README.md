# `crossmodulecollision` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

Cross-module collision regression fixture. The package has two modules,
`CollisionA` and `CollisionB`, that each declare a choice named `Retag` — so
both generate a choice-argument record with the same simple name `Retag` but a
different field list (`CollisionA` carries `newOperator : Party`; `CollisionB`
carries `label : Text` and `count : Int`). The emitter keys its package-wide
data-type table by module-qualified name; this snapshot pins that the two
`Retag` records keep their own distinct fields. Regressing to a simple-name key
makes one record inherit the other's fields — a silent field-drop that
compiles but sends the wrong argument to the ledger — and this snapshot fails.

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test. It is
generated from `conformance/crossmodulecollision/crossmodulecollision.dar` (Daml
source in `conformance/crossmodulecollision`), which `scripts/regenerate.sh`
decodes in place. After a corpus change, rebuild the DAR (`cd
conformance/crossmodulecollision && dpm build && cp
.daml/dist/crossmodulecollision-*.dar ./crossmodulecollision.dar`) and run
`scripts/regenerate.sh write`.

## Refreshing the snapshot

`scripts/regenerate.sh write --only snapshots` regenerates `intermediate.binpb` from the
conformance DAR with a JVM helper assembled from the working tree's sources, then
regenerates the `expected/` tree from that proto with the current codegen
source. Run it from the repo root (POSIX shell; on Windows, use WSL or Git
Bash):

```bash
scripts/regenerate.sh write --only snapshots --snapshot crossmodulecollision
```
