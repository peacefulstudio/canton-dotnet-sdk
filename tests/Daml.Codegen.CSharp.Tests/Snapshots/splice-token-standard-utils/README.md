# `splice-token-standard-utils` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree.

This package emits no data types, only a `Constants` class holding the two Text values the
helper library defines as literals, so the `expected/` tree pins exactly that file.

## Why this DAR

The Token Standard V2 helper library — a non-`vN` package (version 2.0.0). It
is a library of Daml functions and re-exports of types that are owned by the V2
API packages, so it defines no serializable data types of its own. Its only
codegen-visible surface is the literal top-level values of
`Splice.TokenStandard.Utils.Internal.Conversions`, which the C# codegen emits as
`public const` fields. Pinning it puts that surface under drift detection: if a
future Splice release adds a type or a literal value to the library, or an
emitter change alters what is emitted, the drift test flags it instead of
letting the new surface ship unnoticed.

## Refreshing the snapshot

`scripts/regenerate.sh write --only snapshots` regenerates `intermediate.binpb` from the vendored
DAR with a JVM helper assembled from the working tree's sources, then
regenerates the `expected/` tree from that proto with the current codegen
source. Pass `--keep-protos` to keep the vendored proto and refresh only
`expected/`. Run it from the repo root (POSIX shell; on Windows, use WSL or Git
Bash):

```bash
scripts/regenerate.sh write --only snapshots --snapshot splice-token-standard-utils
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-token-standard-utils.dar` is the upstream Splice archive it was
derived from (`splice-token-standard-utils-2.0.0.dar` from the Splice `0.9.1`
`splice-node` release tarball), kept alongside as the frozen upstream artifact
and used as the regeneration input. Do not hand-edit `intermediate.binpb`: the
refresh script regenerates it from this DAR on every run, and CI regenerates
it the same way and fails on any byte delta. If the upstream Splice package
genuinely needs to advance, replace the files in place, refresh the snapshot
per the procedure above, and call out the version bump in the pull request
description.
