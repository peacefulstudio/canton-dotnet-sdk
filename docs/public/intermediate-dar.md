# Intermediate DAR

The Intermediate DAR is the protobuf message that sits between a Daml `.dar` archive and
any downstream code generator: a JVM tool decodes a DAR into this format, and a consumer
(today, this repository's C# emitter) reads the format instead of the DAR. If you are
building your own Daml decoder for another language and want a way to check it against a
trustworthy reference without carrying a JVM into your production runtime, this page is
for you.

## What it is

`proto/intermediate_dar.proto` (package `daml.codegen.intermediate`) describes a decoded
Daml package set: modules, data types (records, variants, enums), templates with their
choices, keys, signatories and observers, and interfaces with their methods and view
types. It is a snapshot of a DAR's *type* surface, not its logic: choice and update
bodies are erased. Produced with `--schema-only` (the `shape` tier, see
[Choosing a tier](#choosing-a-tier)), the message does not depend on the Daml-LF patch
version of the DAR.

**The reference producer is the JVM tool, `daml-dar-to-proto`.** It wraps Digital Asset's
`daml-lf-archive` library, the same decoder the official Daml SDK tooling uses, so its
output is the closest thing to ground truth this project can offer. When you want an
oracle to check your own decoder against, diff against `daml-dar-to-proto`.

## Release assets

From `0.6.0-preview.1` of
[`peacefulstudio/canton-dotnet-sdk`](https://github.com/peacefulstudio/canton-dotnet-sdk)
onward, every `v*` GitHub release attaches:

| Asset | Contents |
|---|---|
| `daml-dar-to-proto-<version>.jar` | The reference producer, a self-contained JVM jar. |
| `intermediate_dar-<version>.proto` | The exact schema that jar writes against. |
| `SHA256SUMS` | Checksums for the jar, the proto and the fixtures archive. |
| `intermediate-fixtures-<version>.tar.gz` | For each DAR in this project's conformance corpus, a `.binpb` plus its canonical protobuf-JSON rendering in two tiers: `full/` is the plain decode and `shape/` is the `--schema-only` decode. Each fixture is named after its corpus DAR (`conformance/<name>/<name>.dar`). `manifest.json` indexes every fixture by that name and records the SHA-256 of each source DAR and each output. Releases before this layout carry flat schema-only files and no tiers. |

Fixtures are per release, with no stability across releases: the answers change whenever
the oracle does, and every Daml SDK bump changes package ids. Pin a release and compare
against that release's tarball.

The tarball unpacks to one subtree per tier, `full/` and `shape/`. Each holds, per corpus
DAR, `<name>.binpb` (the exact bytes the jar writes) and `<name>.json` (the canonical JSON
form: `buf convert`, then `jq -S`, as in [Diffing the binary output](#diffing-the-binary-output)).
`manifest.json` has a top-level `version` and one `fixtures` entry per DAR: its `name`, the
SHA-256 of the source DAR under `source.darSha256`, and, under `full` and `shape`, the
paths of that tier's `.binpb` and `.json` with their SHA-256 values.

### Older releases

For a release cut before these assets existed, extract the jar from the platform bundle
instead. Every release attaches a `dpm-codegen-cs-<version>-<os>-<arch>.tar.gz` per
platform built for the `dpm codegen-cs` OCI bundle (`linux-amd64`, `linux-arm64`,
`darwin-arm64`, or `windows-amd64`), and that bundle carries the same jar, at
`bin/daml-dar-to-proto.jar` from `0.5.0-preview.1` onward. For `0.4.1-preview.1` and
earlier, the file is at `bin/daml-codegen-jvm-helper.jar` instead (the jar's pre-rename
name):

```bash
VERSION=X.Y.Z  # a release that predates the assets in the table above
curl -sSLO "https://github.com/peacefulstudio/canton-dotnet-sdk/releases/download/v${VERSION}/dpm-codegen-cs-${VERSION}-linux-amd64.tar.gz"
mkdir -p bundle
tar -xzf "dpm-codegen-cs-${VERSION}-linux-amd64.tar.gz" -C bundle
# the jar is now at bundle/bin/daml-dar-to-proto.jar
```

The proto schema for any given release is always reachable at that tag in this
repository, at `proto/intermediate_dar.proto`.

## Quickstart: diff your decoder in CI

The jar needs only a JVM to run; nothing else in this recipe does. A minimal CI job:

```yaml
env:
  VERSION: X.Y.Z  # pin this to the release you validated against

steps:
  - uses: actions/setup-java@v6
    with:
      distribution: temurin
      java-version: '17'

  - name: Download and verify the reference producer
    env:
      GH_TOKEN: ${{ github.token }}
    run: |
      set -euo pipefail
      gh release download "v${VERSION}" \
        --repo peacefulstudio/canton-dotnet-sdk \
        -p "daml-dar-to-proto-${VERSION}.jar" \
        -p "intermediate_dar-${VERSION}.proto" \
        -p "SHA256SUMS"
      sha256sum -c --ignore-missing SHA256SUMS

  - name: Decode every DAR to Intermediate DAR
    run: |
      set -euo pipefail
      mkdir -p out
      for dar in dars/*.dar; do
        name="$(basename "${dar%.dar}")"
        java -jar "daml-dar-to-proto-${VERSION}.jar" \
          --dar "${dar}" \
          --out "out/${name}.binpb"
      done

  - name: Run your oracle test
    run: ./run-oracle-test.sh out/
```

`run-oracle-test.sh` is yours: decode the same DARs with your own tooling and assert your
output matches what `daml-dar-to-proto` produced. This job decodes in the `full` tier; add
`--schema-only` to the `java` command to check the `shape` tier instead.

### Diffing the binary output

`.binpb` is protobuf wire format, not something you want to `diff` directly. Convert each
side to JSON first, with [`buf convert`](https://buf.build/docs/reference/cli/buf/convert/)
against the schema you downloaded above:

```bash
buf convert "intermediate_dar-${VERSION}.proto" \
  --type daml.codegen.intermediate.IntermediateDar \
  --from out/richtypes.binpb \
  --to out/richtypes.json
```

`protoc --decode=<TYPE>` is a common alternative, but it prints protobuf **text format**,
not JSON, useful for an eyeballed diff, but not something `jq` can parse. For the
normalized-JSON comparison below, use `buf convert` or your own language's protobuf-JSON
mapper (most protobuf runtimes ship one), not `protoc --decode`.

Then normalize both JSON files before diffing, because protobuf-JSON serializers do not commit to
stable field ordering or whitespace, so a byte diff between two semantically identical
messages routinely fails for the wrong reason:

```bash
jq -S . out/richtypes.json > out/richtypes.normalized.json
```

Compare the sorted-key, normalized files, not the raw protojson.

One further wrinkle: the reference producer always sets an explicit verdict on every
`signatories`/`observers`/`controllers` field rather than leaving it unset, even under
`--schema-only`: a `Dynamic` verdict renders as `{"dynamic": {}}`, not as an absent key.
The proto's own
comments document plain field-absence as an equally valid way to say `Dynamic`, so a
spec-compliant decoder that omits the field instead will diff clean against the schema but
not against this producer's literal JSON. Normalize both sides to one shape (for example,
treat a missing `signatories`/`observers`/`controllers` key the same as `{"dynamic": {}}`)
before asserting equality, rather than comparing the raw normalized JSON byte for byte.

### Choosing a tier

The fixtures and the Action below come in two tiers.

- **`full`** is the plain decode, with every field. That includes the static
  party-expression analysis, which annotates each template and choice with a
  `Static`/`Dynamic` verdict on its signatories, observers and controllers. It is the
  oracle check proper: only this tier can catch a wrong verdict.
- **`shape`** is the `--schema-only` decode. It skips the party-expression analysis and
  sets every verdict to `Dynamic`, so it does not depend on the Daml-LF patch version of the
  DAR. It is an entry tier for an SDK that only decodes types.

## Oracle check Action

The repository ships a composite GitHub Action, `actions/intermediate-dar-oracle-check`,
that does the quickstart's work for you. It runs your producer once per DAR in the
conformance corpus, converts your output and the oracle's to canonical JSON, and fails when
they differ. The verdict is on the JSON, not on bytes, so a difference that only comes from
the protobuf encoder does not count. On every difference the log carries the JSON diff for
that DAR, and the step summary lists each DAR with its tier and result. The Action runs on
Linux x64 runners. It is available in releases after `0.7.0`: a tag up to `v0.7.0` does not
contain it.

```yaml
steps:
  - uses: actions/checkout@v7

  - uses: peacefulstudio/canton-dotnet-sdk/actions/intermediate-dar-oracle-check@vX.Y.Z
    with:
      version: X.Y.Z
      tier: full
      producer: ./my-decoder --dar "$DAR" --out "$OUT"
```

Pin the Action to the release tag `vX.Y.Z` and set `version` to the same `X.Y.Z`. The
Action takes its corpus DARs and `intermediate_dar.proto` from its own checkout. Unless you
set `oracle-jar`, it fails when that proto differs from the one attached to release
`X.Y.Z`, and it checks every downloaded release asset against the release's `SHA256SUMS`.

| Input | Meaning |
|---|---|
| `tier` | Required: `full` or `shape`, as in [Choosing a tier](#choosing-a-tier). |
| `producer` | Required: a shell command that writes your decoder's Intermediate DAR (`.binpb`) to `$OUT` for the DAR at `$DAR`. It runs once per DAR, with `DAR`, `OUT` and `TIER` in its environment. A non-zero exit, an empty `$OUT` or an `$OUT` that is not an `IntermediateDar` fails that DAR. |
| `version` | The release whose `intermediate-fixtures-<version>.tar.gz` holds the expected answers. Required unless `oracle-jar` is set. |
| `oracle-jar` | Optional path to a local `daml-dar-to-proto` jar. When set, the expected answers come from this jar instead of the fixtures tarball. |
| `splice` | Optional, off when empty. Adds the Splice DARs to the run: `latest`, a Splice version such as `0.9.1`, or a 40-character commit of `canton-network/splice`. |

With `splice` set, the Action fetches the highest-versioned DAR of each `splice-*` family
(the `-current` DARs excluded) from `daml/dars/` of `canton-network/splice` at that commit,
and decodes them with the oracle jar to get the expected answers, so the run needs JDK 17.
The Action sets that up itself when `splice` or `oracle-jar` is set. It logs the commit it
resolved, so you can reproduce the run.

The Action exposes two outputs: `mismatches`, the number of DARs whose canonical JSON
differs from the oracle's, and `splice-commit`, the Splice commit the run resolved to
(empty when `splice` is off). The step fails when `mismatches` is not zero.

## Compatibility policy

This schema follows a deliberate versioning and distribution policy. As a consumer, the
parts of it that matter to you are:

- **The release version is the contract version.** `daml-dar-to-proto` has no independent
  version of its own: the jar and proto attached to release `X.Y.Z` speak contract
  version `X.Y.Z`, reportable by running the jar with `--version`.
- **Field numbers are append-only.** A field is never reused or renumbered; a retired slot
  is reserved, not recycled.
- **Byte-identical output.** For a given `(DAR, tool version)` pair, the emitted
  Intermediate DAR is byte-identical across runs and across operating systems, so you can
  cache and hash Intermediate DARs keyed on that pair.
- **Ordering and normalization are contract, not incidental.** The orderings documented in
  the proto's own comments (dependencies by package ID, modules by qualified name, data
  types/templates/interfaces by name, and so on) are guaranteed, not merely today's
  implementation detail, as are a few orderings the proto doesn't comment on but the
  reference producer always applies: a template's `choices` and `implements`, and an
  interface's `methods` and `choices`, are each sorted by name (`implements` by the full
  `(package_id, module, name)` tuple). `jq -S` sorts object keys, not array elements, so
  don't rely on it to paper over an array your own decoder emits in a different order.
  Normalizations such as flattening curried type application are equally guaranteed.
- **This project is pre-1.0.** Under SemVer 0.x, a release may still change this schema in
  a way that breaks a deployed consumer. Any such change is called out in this
  repository's `CHANGELOG.md` under the release that ships it, so read that entry before
  upgrading the pinned version in your CI job.
- **The package name is stable.** The proto package is `daml.codegen.intermediate` with no
  `v1`/`v2` suffix: the release version you pin *is* the schema version, so the package
  name does not need to encode one too.

## Daml-LF support

The reference producer decodes contract keys (Daml-LF 2.3 and later) as well as the rest
of the type surface: a keyed template carries its key type on the wire (`Template.key_type`).
Like every other expression body, the maintainer expression itself is erased (it has no
slot in this schema), so your decoder only needs to reproduce which templates are keyed and
each key's type, never how a key's maintainers are computed. If your own decoder does not
yet model contract keys at all, expect the oracle to disagree on any DAR that declares one
until it does.
