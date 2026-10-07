# Canton.Ledger.Client.Parity.SpliceBindings

Splice C# bindings for the LocalNet parity suite (`Canton.Ledger.Client.Parity.Tests`), generated
at build time by this checkout's own emitter instead of restored from the published `Splice.*`
NuGet packages. A change to the runtime or the emitter is therefore exercised end to end against a
live LocalNet by the same pull request that makes it.

## What is vendored

| File | What it is |
|---|---|
| `Fixtures/splice-wallet-<version>.binpb` | The IntermediateDar proto decoded from the `splice-wallet` DAR. The emitter reads this, so building needs neither a JDK nor the DAR decoder. |

## Provenance

The proto is decoded from `splice-wallet-0.1.24.dar` (sha256 `50e5ce2e35f3e7428bca43df9b3ce2b9372966e83aa3c891b0c4284a7bd6097b`), taken from the `dars/` directory of the Splice 0.8.4 `splice-app` image the LocalNet runs. A DAR bundles its dependencies, so it carries `splice-amulet`, `splice-wallet-payments` and the token-standard interface packages, with the package ids the LocalNet participant has uploaded. The DAR itself is not part of this package.

## How the bindings are built

`Canton.Ledger.Client.Parity.SpliceBindings.csproj` builds `src/Daml.Codegen.CSharp.Cli` and runs
it as `--intermediate Fixtures/splice-wallet-<version>.binpb --include-dependencies true` into
`obj/`, then compiles the output. Dependency packages land in their own Daml module namespaces
(`Splice.Api.Token.HoldingV1`, `Splice.ValidatorLicense`, `Splice.Wallet.TransferOffer`, ...).
There is no manual step: restoring and building any project that references this one regenerates
the bindings when the proto or the emitter changed.

`Canton.Ledger.Client.Parity.Tests` asserts the package ids of the generated bindings, so a DAR
that does not match what the LocalNet deploys fails without needing a LocalNet.
