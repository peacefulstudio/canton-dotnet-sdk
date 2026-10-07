# CLI reference

The `dpm codegen-cs` bundle, the emitter CLI and the standalone DAR-to-proto tool, with every flag.

Back to the [README](../../README.md).

Two layers share one flag surface:

- **`dpm codegen-cs`** — the OCI bundle. Takes a `.dar`, decodes it to an
  IntermediateDar proto with its bundled JVM helper, then runs the emitter
  CLI below on the proto. This is the only place the DAR → IntermediateDar
  decode ships.
- **`Daml.Codegen.CSharp.Cli`** — the emitter CLI in this repo. Takes the
  IntermediateDar proto via `--intermediate`; it does not read `.dar` files.

## Bundle usage

```
dpm codegen-cs --dar <path-to-dar> --out <output-dir> [--publish-nuget --nuget-config <path> --nuget-source <name>] [--] [emitter-options...]
```

Options other than `--dar`/`--out` and the three publishing options are forwarded to the
emitter CLI. `--publish-nuget` packs the generated project and pushes the package after
emission; it implies `--generate-project` and requires `--nuget-config` (a `NuGet.config`
path) and `--nuget-source` (a source name in it).

## Emitter CLI

Output of `dotnet run --project src/Daml.Codegen.CSharp.Cli -- --help`
(the `--output-directory` default is the invoking directory):

```
Description:
  Generate C# code from an IntermediateDar proto

Usage:
  Daml.Codegen.CSharp.Cli [options]

Options:
  --intermediate <intermediate> (REQUIRED)  Path to an IntermediateDar proto file produced by the JVM helper.
  -o, --output-directory <o>                Output directory for generated sources [default: the invoking directory]
  -n, --namespace <n>                       Namespace prefix for the main package's generated code: each Daml module is emitted under <prefix>.<Module>, the prefix is elided when the module name already starts with it, and dependency packages are never prefixed (default: no prefix, the namespace is the module name)
  -v, --verbosity <v>                       Verbosity level: 0=errors only, 1=warnings, 2=info, 3=debug [default: 1]
  -r, --root <r>                            Regular expression to filter which templates to generate (default: .*)
  --nullable                                Enable nullable reference types in generated code
  --generate-project                        Generate a .csproj file for the generated code
  --include-dependencies                    Generate code for dependency packages as well
  --target-framework <target-framework>     Target framework for the generated project (e.g., net10.0) [default: net10.0]
  --runtime-version <runtime-version>       Version of Daml.Runtime package to reference
  --emitter-counter <emitter-counter>       4th segment of the generated NuGet version (Major.Minor.Patch.Generation). Defaults to 0; set a monotonic counter to distinguish republished builds of the same source. Overridden by --release-counters, which resolves the segment as a codegen-generation ordinal. [default: 0]
  --release-counters <release-counters>     Path to a JSON release-counter store. When set, the 4th NuGet version segment is resolved from this store as a codegen-generation ordinal keyed by --codegen-version, overriding --emitter-counter. The store is created on first use and atomically updated when a new codegen version is first seen.
  --codegen-version <codegen-version>       Codegen-tool version that keys the release-counter generation ordinal (the 4th NuGet version segment). Every package produced by one codegen version shares the ordinal, which increments when the version changes. Defaults to this emitter build's informational version (AssemblyInformationalVersionAttribute) with any '+' build metadata stripped.
  --package-license <package-license>       SPDX license expression emitted in the generated .csproj's <PackageLicenseExpression>. Defaults to Apache-2.0. [default: Apache-2.0]
  --version-suffix <version-suffix>         SemVer prerelease suffix appended to generated package versions, e.g. 'preview.2'. Mirrors the emitter prerelease tag. No leading dash.
  --repository-url <repository-url>         Repository URL emitted in the generated .csproj's <PackageProjectUrl>/<RepositoryUrl>/<RepositoryType>. When omitted, those elements are not emitted.
  -?, -h, --help                            Show help and usage information
  --version                                 Show version information
```

## Standalone DAR-to-proto tool

The DAR to IntermediateDar decode is packaged for standalone publication as
`daml-dar-to-proto`, a runnable jar emitting the schema defined in
`proto/intermediate_dar.proto`.

From `0.6.0-preview.1`, each `v*` release attaches `daml-dar-to-proto-<version>.jar`,
`intermediate_dar-<version>.proto`, a `SHA256SUMS` checksum file and
`intermediate-fixtures-<version>.tar.gz` (a schema-only `.binpb` plus a canonical-JSON
rendering per conformance-corpus DAR, with a `manifest.json` recording each DAR's and
each output's SHA-256) to the GitHub release.

```
java -jar daml-dar-to-proto-<version>.jar --dar contracts.dar --out intermediate.binpb
```

The jar's own interface, for reference: `--dar` and `--out` select input and
output, `--schema-only` opts into the patch-version-insensitive schema-mode
decode (the default full decode is patch-version-sensitive and additionally
captures the party expressions that enable typed-actAs codegen), and `--version`
prints the tool's version. Exit codes: 0 on success (and `--help`/`--version`),
1 on runtime failure, 2 on usage error.
