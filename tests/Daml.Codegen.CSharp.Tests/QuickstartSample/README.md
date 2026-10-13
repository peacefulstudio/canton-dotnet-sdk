# Quickstart sample codegen input

`QuickstartSampleDriftTests` regenerates C# from the vendored
`intermediate.binpb` (the `IntermediateDar` proto for the `quickstart`
package) and asserts byte-equal output against the committed showcase sample
at `samples/QuickstartExample/Generated`. This puts the showcase sample under
the same drift detection as the snapshot fixtures, so it fails CI the day it
falls behind emitter output instead of silently rotting.

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift gate;
`quickstart-0.0.1.dar` is the archive it was derived from, built from
`samples/QuickstartExample/daml` (SDK 3.6.0, LF target 2.1), kept alongside
for provenance.

## Refreshing the sample

When an emitter change legitimately alters the generated output, refresh the
committed sample (not this fixture) from the repo root:

```bash
scripts/regenerate.sh write --only quickstart
```

## Rebuilding the proto

The proto only changes when the Quickstart `.daml` source or the JVM helper
changes. To rebuild it, build the DAR, copy it over the vendored one and let the
regeneration script decode it, from the repo root:

```bash
(cd samples/QuickstartExample/daml && dpm build)
cp samples/QuickstartExample/daml/.daml/dist/quickstart-0.0.1.dar \
  tests/Daml.Codegen.CSharp.Tests/QuickstartSample/quickstart-0.0.1.dar
scripts/regenerate.sh write --only quickstart
```

Call out the change in the pull request description.
