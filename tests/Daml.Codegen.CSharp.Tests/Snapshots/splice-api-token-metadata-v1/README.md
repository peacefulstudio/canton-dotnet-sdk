# `splice-api-token-metadata-v1` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

This package is published to nuget.org by the existing pipeline:
the public twin's `publish-splice.yaml` pulls the Splice `splice-node` release
tarball, takes every DAR under `*/dars/*.dar` whose basename matches `splice-*`,
and packs each one. `splice-api-token-metadata-v1` has always been in that set,
but it had no snapshot family, so its emitted C# shipped to consumers with
nothing pinning it — the publish-time drift guard covered every family that
*depends* on this package without covering the package itself.

The dependency is not incidental: every other Splice DAR vendored under
`Snapshots/` carries `splice-api-token-metadata-v1` as a dependency dalf, and
the types it defines (`Metadata`, `ChoiceContext`, `ExtraArgs`) appear in the
choice signatures those families emit. A change to how this package's types are
emitted moves output across the whole Token Standard set, so pinning it here
puts the shared base under the same byte-equality gate as its dependents.

The DAR contains one Daml module, `Splice.Api.Token.MetadataV1`, and no
templates, no contract keys and no choices. Its codegen-visible surface is the
`AnyValue` variant, the `AnyContract` interface with its empty view
`AnyContractView`, and the records `ChoiceContext`, `Metadata`, `ExtraArgs` and
`ChoiceExecutionMetadata` — seven emitted files. Two shapes in `AnyValue` are
worth naming: its `AV_ContractId` arm carries a `ContractId AnyContract`, a
contract id of an interface that no template implements, and its `AV_List` and
`AV_Map` arms make the variant self-referential through `[AnyValue]` and
`TextMap AnyValue`.

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
scripts/refresh-snapshot.sh splice-api-token-metadata-v1
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-api-token-metadata-v1.dar` is the upstream Splice archive it was
derived from (`splice-api-token-metadata-v1-1.0.0.dar` from the Splice `0.8.2`
`splice-node` release tarball), kept alongside as the frozen upstream artifact
and used as the regeneration input. Do not hand-edit `intermediate.binpb`: the
refresh script regenerates it from this DAR on every run, and CI regenerates it
the same way and fails on any byte delta. If the upstream Splice package
genuinely needs to advance, replace the files in place, refresh the snapshot per
the procedure above, and call out the version bump in the pull request
description.
