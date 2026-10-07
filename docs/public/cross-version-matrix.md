# Canton cross-version compatibility matrix

The Canton ledger clients are exercised against more than one Canton release. The
`Cross-version matrix` workflow (`.github/workflows/cross-version-matrix.yaml`) boots a
LocalNet per Canton release and runs the live-ledger suites against it. It runs weekly and on
demand (`workflow_dispatch`), on GitHub-hosted `ubuntu-latest` runners, one matrix cell per job.

## Supported Canton versions

The clients target Canton Ledger API v2. `Canton.Ledger.Grpc` downloads the Canton Ledger API proto files from Maven Central during its build.

| SDK Version | Canton Version |
|-----------------|----------------|
| 0.6.0 and later | 3.5.x |

Before `0.6.0` the `Canton.Ledger.*` packages had their own version line:

| `Canton.Ledger.*` Version | Canton Version |
|-----------------|----------------|
| 0.4.1 – 0.5.x | 3.5.x |
| 0.4.0 | 3.4.x |
| 0.2.x | 3.4.x |
| 0.1.x | 3.4.x |

The clients support Canton 3.5 only — running them against a 3.4.x participant is untested and unsupported. This release's vendored protos and JSON Ledger API spec are pinned at Canton `3.5.19`. Any `3.5.x` patch release is fine for the gRPC client: the vendored surface is stable within the minor. The REST client needs Canton `3.5.10` or later, the first patch that serves `POST /v2/state/active-contracts-page`, which every REST active-contract-set read pages over.

A weekly cross-version matrix, described below, runs the live-ledger suites against the baseline Canton release, other 3.5 patches and a non-gating LocalNet unreleased (`dev`) cell, and publishes a results table per run.

## Cells

The workflow's `include:` list is the single source of truth for which cells run. There are
three kinds:

- **Baseline.** The Canton release the SDK's vendored protos and JSON Ledger API spec are
  pinned at (`<CantonVersion>` in `Directory.Build.props`), on the matching
  [canton-localnet](https://github.com/peacefulstudio/canton-localnet) release tag.
- **Other 3.5 patches.** Canton 3.5.18, the previous baseline, and Canton 3.5.17. New 3.5
  patches are added as canton-localnet releases ship them. A cell takes a canton-localnet
  release tag and, when no release carries that patch, overrides the Splice image tag on a
  release whose compose tree is unchanged across those Splice releases.
- **LocalNet unreleased (`dev`).** Runs canton-localnet's unreleased `dev` branch with no
  version expectation, as an early warning for the next LocalNet release. It is non-gating, so
  a red result never fails the run.

Each cell:

1. checks out canton-localnet at its ref and brings the stack up with PQS enabled;
2. reads the participant's `GET /v2/version` and fails unless it reports the cell's expected
   Canton version, so a cell cannot silently run a different patch than it claims;
3. runs the gRPC integration suite, the PQS round-trip test, the REST (JSON Ledger API)
   conformance suite and the cross-transport parity suite. The REST suites are gated
   fail-closed by `LedgerApiVersionSkewGuard`: they hard-fail unless the participant's Canton
   minor is one the vendored spec accepts, with no environment override.

Adding a cell is one line in `include:`, for example
`- {order: 3, cell: 'Canton 3.5.N', localnet-ref: vX.Y.Z-N, image-tag: '', expected-canton: 3.5.N, gating: true}`.
`order` sets the row order in the results table; an empty `image-tag` keeps the release's own
Splice images.

## Results

- **Job summary.** The run's `Matrix results` job writes a cell table (LocalNet ref, Splice
  images, expected and reported Canton version, gating, result) and a per-suite table
  (succeeded, failed, skipped, total) to its summary.
- **Artifact.** The same job uploads the raw results as the `cross-version-matrix` artifact,
  kept for 90 days.
- **Release notes.** `scripts/cross-version-matrix-table.sh fetch` renders the latest completed
  `main` run's tables as Markdown, with a link to that run; pass `--run <run-id>` to render a
  specific run.

## Synchronizer id formats

Canton 3.4 reports synchronizer ids as `name::fingerprint`; Canton 3.5 appends the protocol
version (`name::fingerprint::35-0`). No 3.4 participant is in the matrix, so compatibility with
both formats is pinned offline instead: the `SynchronizerIdWireFormatTests` classes in
`tests/Canton.Ledger.Kernel.Tests`, `tests/Canton.Ledger.Grpc.Client.Tests` and
`tests/Canton.Ledger.Rest.Client.Tests` feed both formats through every transport seam that
reads or writes a synchronizer id (stream projection, active-contract reads, reassignment
source and target, command and disclosed-contract submission, vetted-package reads) and
assert each id comes through verbatim.
