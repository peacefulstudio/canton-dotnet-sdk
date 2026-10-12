# Canton cross-version compatibility matrix

The Canton ledger clients are exercised against more than one Canton release. The
`Cross-version matrix` workflow (`.github/workflows/cross-version-matrix.yaml`) boots a
LocalNet per Canton release and runs the live-ledger suites against it. It runs weekly and on
demand (`workflow_dispatch`), on GitHub-hosted `ubuntu-latest` runners, one matrix cell per job.

## Supported Canton versions

The clients target Canton Ledger API v2. `Canton.Ledger.Grpc` downloads the Canton Ledger API proto files from Maven Central during its build.

| SDK Version | Canton Version |
|-----------------|----------------|
| 0.7.0 and later | 3.6.x |
| 0.6.x | 3.5.x |

Before `0.6.0` the `Canton.Ledger.*` packages had their own version line:

| `Canton.Ledger.*` Version | Canton Version |
|-----------------|----------------|
| 0.4.1 – 0.5.x | 3.5.x |
| 0.4.0 | 3.4.x |
| 0.2.x | 3.4.x |
| 0.1.x | 3.4.x |

The clients support Canton 3.6 only. Running them against a 3.5.x or older participant is untested and unsupported. If you're still on 3.5, use 0.6.x. This release's vendored protos and JSON Ledger API spec are pinned at Canton `3.6.1`. Any `3.6.x` patch works for both clients, because the vendored surface is stable within the minor.

A weekly cross-version matrix, described below, runs the live-ledger suites against every Canton 3.6 patch that's live on a Canton Network (MainNet, TestNet or DevNet), plus a non-gating LocalNet unreleased (`dev`) cell. It publishes a results table per run.

## Cells

The workflow's `include:` list is the single source of truth for which cells run. There are
two kinds:

- **Live releases.** One gating cell per Canton 3.6 patch running on a Canton Network, each on
  the matching [canton-localnet](https://github.com/peacefulstudio/canton-localnet) release tag.
  A cell is added when a patch goes live on a network, and removed when no network runs it any
  more. The baseline (`<CantonVersion>` in `Directory.Build.props`) is one of these.
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
`- {order: 2, cell: 'Canton 3.6.N', localnet-ref: vX.Y.Z-N, image-tag: '', expected-canton: 3.6.N, gating: true}`.
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
