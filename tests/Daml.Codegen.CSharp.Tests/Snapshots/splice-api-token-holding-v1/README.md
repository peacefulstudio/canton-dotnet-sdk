# `splice-api-token-holding-v1` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

Interface-only fixture: one Daml interface (`Holding`) plus a handful of
records (`HoldingView`, `InstrumentId`, `Lock`); no concrete templates, no
contract keys. Small enough to keep snapshot diffs reviewable, no
cross-family cycles. This DAR is **not** a comprehensive feature-coverage
fixture — paths such as the partial-property contract `Key`, typed
`WitnessParties`, and typed `SynchronizerId` reassignment fields are exercised
by `EmittedCodeCompilesTests` and the per-feature shape tests, not by this
snapshot.

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
scripts/refresh-snapshot.sh splice-api-token-holding-v1
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-api-token-holding-v1.dar` is the upstream Splice archive it was
derived from (`splice-api-token-holding-v1-1.0.0.dar` from the Splice `0.8.4`
`splice-node` release tarball), kept alongside as the frozen upstream artifact
and used as the regeneration input. Do not hand-edit `intermediate.binpb`: the
refresh script regenerates it from this DAR on every run, and CI regenerates it
the same way and fails on any byte delta. If the upstream Splice package
genuinely needs to advance, replace the files in place, refresh the snapshot per
the procedure above, and call out the version bump in the pull request
description.
