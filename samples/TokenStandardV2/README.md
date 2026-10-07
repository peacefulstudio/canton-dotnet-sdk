# Token Standard V2 sample

An offline console app showing the Splice Token Standard V2 (CIP-0112) C#
packages composing with `Daml.Runtime` and `Daml.Ledger.Abstractions`. It
constructs a real V2 workflow — an `IHolding` ACS query, a `TransferInstruction`
accept exercised through its typed interface choice, and an `Allocation`
withdraw — assembled into an unsent command submission, all without contacting
a ledger.

## How it is built

Unlike `samples/QuickstartExample` (which references the `src` projects), this
sample references the **published NuGet surface**: the V2 API packages plus
`Daml.Runtime` / `Daml.Ledger.Abstractions`. It is deliberately excluded from
`Daml.Codegen.CSharp.slnx` and from central package management
(`ManagePackageVersionsCentrally=false`) so the tool's own build never tries to
restore not-yet-published packages, and so package versions can float on the
release-time counter segment.

By default, `dotnet restore samples/TokenStandardV2` uses `NuGet.config` and
resolves `Daml.*` / `Splice.*` from nuget.org like any other consumer — no
local feed required. To test against packages that have not been published
yet, populate `local-feed/` with the packed V2 `.nupkg` files and the packed
`Daml.Runtime` / `Daml.Ledger.Abstractions`, then restore with the opt-in
config: `dotnet restore samples/TokenStandardV2 --configfile
samples/TokenStandardV2/NuGet.local-feed.config`, followed by `dotnet build
--no-restore`. CI does this automatically, building the sample against the
freshly packed feed as a focused V2 compile-gate before publishing.

## Isolated package cache

`NuGet.config` pins `globalPackagesFolder` to `.nuget-packages/`, so restores
here never write to the machine-wide `~/.nuget/packages`. `local-feed/` carries
locally packed builds of versions that also exist on nuget.org, and NuGet never
re-fetches a version whose folder already exists — without that pin, one sample
build would leave local packs permanently shadowing the real published packages
for every other project on the machine. Keep the pin, and keep
`.nuget-packages/` out of the feed.

## Package version floats

The `Splice.*` references float `1.*-*` and the `Daml.*` references float
`0.*-*` — the widest prerelease pattern within each package's current major —
rather than pinning a specific minor/patch. Against the default
`NuGet.config`, the float resolves the newest matching prerelease already
published to nuget.org. Against `NuGet.local-feed.config`, CI packs the in-progress build into a
private `local-feed`, and that config's
package-source mapping resolves every `Splice.*` / `Daml.*` package **only**
from that feed — so the float resolves whatever this repo just packed — which keeps the V2
compile-gate stable across the repo's own version bumps instead of breaking
each time `Directory.Build.props` moves to a new minor.

## Why it references the whole V2 family

The sample references the full V2 API family — including
`transfer-events-v2`, `allocation-instruction-v2`, and `allocation-request-v2`,
which its walkthrough does not directly exercise — so the compile-gate proves the
whole family restores and composes together, not only the packages this sample
touches. `splice-token-standard-utils` is deliberately absent: it emits no C#
types and is not published as a package.
