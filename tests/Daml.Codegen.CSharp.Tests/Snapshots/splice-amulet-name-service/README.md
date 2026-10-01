# `splice-amulet-name-service` codegen snapshot

`DriftDetectionTests` regenerates C# from the vendored `intermediate.binpb`
(the `IntermediateDar` proto for this package) and asserts byte-equal output
against the `expected/` tree. When a codegen change legitimately alters the
generated output, refresh the `expected/` snapshot.

## Why this DAR

Concrete-template fixture, complementing the interface-only
`splice-api-token-holding-v1` snapshot. It is the smallest amulet-family DAR
and exercises the codegen paths the interface-only fixture cannot reach:

- Concrete `template ... where` declarations (`AnsEntry`, `AnsEntryContext`,
  `AnsRules`, `AmuletConversionRateFeed`).
- Non-Unit choices whose typed `<Choice>Result` records carry contract IDs
  (e.g. `AnsRules_RequestEntry`, `AnsEntryContext_CollectInitialEntryPayment`),
  driving the `<Choice>Result` projector, the `FromCreatedContracts` factory,
  and the `<Choice>Async(this ContractId<T>, ...)` extension.
- A `Numeric n` field mapped to `decimal` (`AnsRulesConfig.EntryFee`).
- Cross-family references to the `splice-amulet` package, exercising the
  qualified-reference emission path without emitting the referenced package.

It also exercises a parameterized generic type applied across a package
boundary: a few `<Choice>Result` records (e.g.
`AnsRules_RejectEntryInitialPaymentResult`) carry a
`Splice.Amulet.AmuletCreateSummary<...>` field. `ToRecord`/`FromRecord`
serialize and deserialize it for real, via a converter delegate per type
argument threaded through the general parameterized-`DamlTypeApp` path in
`DamlTypeMapper` — not specific to this fixture, the larger `splice-amulet`
DAR exercises the same pattern. Pinning it here puts the path under drift
detection.

Together with the holding snapshot this guards the concrete-template,
typed-choice-result, and decimal-mapping surfaces against formatting,
member-ordering, XML-doc, and `using`-directive drift that the does-it-compile
`EmittedCodeCompilesTests` would let through.

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
scripts/refresh-snapshot.sh splice-amulet-name-service
```

## The vendored inputs

`intermediate.binpb` is the canonical codegen input for the drift test;
`splice-amulet-name-service.dar` is the upstream Splice archive it was derived
from (`splice-amulet-name-service-0.1.24.dar` from the Splice `0.8.4`
`splice-node` release tarball), kept alongside as the frozen upstream artifact
and used as the regeneration input. Do not hand-edit `intermediate.binpb`: the
refresh script regenerates it from this DAR on every run, and CI regenerates
it the same way and fails on any byte delta. If the upstream Splice package
genuinely needs to advance, replace the files in place, refresh the snapshot
per the procedure above, and call out the version bump in the pull request
description.
