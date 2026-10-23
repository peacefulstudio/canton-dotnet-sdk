# Daml.Codegen.CSharp.MSBuild

Generates C# bindings for Daml templates and data types at `dotnet build` time, part of the
[Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk). The generated code
compiles against `Daml.Runtime`, so reference that package too.

## Installation

```bash
dotnet add package Daml.Codegen.CSharp.MSBuild
dotnet add package Daml.Runtime
```

## Usage

Point a `<DamlArchive>` item at a DAR, or a `<DamlProject>` item at the directory holding a
Daml project's `daml.yaml` (the package builds it with `dpm build` first):

```xml
<ItemGroup>
  <DamlArchive Include="daml/contracts.dar" />
</ItemGroup>
```

```xml
<ItemGroup>
  <DamlProject Include="daml" />
</ItemGroup>
```

The bindings land under `obj/` and compile into the project.

## Modes

| `DamlCodegenMode` | Runs | Needs |
|---|---|---|
| `Standalone` (default) | the .NET DAR decoder and emitter bundled in this package | the .NET runtime only |
| `Dpm` | `dpm codegen-cs`, pinned to this package's version and bundle digest | dpm 1.0.20 or later and a JDK on `PATH` |

Standalone reads Daml-LF 2.1 to 2.4 and 2.5-rc1. A DAR built for Daml-LF 1.x (Daml Finance, for
example) or for another 2.x minor fails the build with a message pointing at `Dpm`, which decodes
them.

```xml
<PropertyGroup>
  <DamlCodegenMode>Dpm</DamlCodegenMode>
</PropertyGroup>
```

Dpm mode pins the `ghcr.io/peacefulstudio/dpm-codegen-cs` bundle by version and index digest,
both stamped into this package when it was packed, and runs dpm with `DPM_AUTO_INSTALL=true` under
a dpm home of its own per bundle version, `$DPM_HOME/dpm-codegen-cs/<version>` (`~/.dpm/dpm-codegen-cs/<version>`
when `DPM_HOME` is unset). A bundle version other than the package's needs its digest too:

```xml
<PropertyGroup>
  <DamlCodegenMode>Dpm</DamlCodegenMode>
  <DamlCodegenBundleVersion>0.6.0</DamlCodegenBundleVersion>
  <DamlCodegenBundleDigest>sha256:9f040219936298f085e2ade77fbbc37829df065b879633282aa4628c9a1b6548</DamlCodegenBundleDigest>
</PropertyGroup>
```

`oras resolve ghcr.io/peacefulstudio/dpm-codegen-cs:<version>` prints a version's digest. Without
one the build fails with `DAML0006`, and with a value that is not `sha256:` and 64 lowercase hex
digits with `DAML0007`.

A `<DamlProject>` item needs dpm in both modes, to build the DAR.

## Properties

| Property | Default | Effect |
|---|---|---|
| `DamlNamespace` | none | namespace prefix for the main package's modules |
| `DamlIncludeDependencies` | `false` | also generate the DAR's dependency packages |
| `DamlNullable` | `true` | emit nullable reference type annotations |
| `DamlGeneratedDir` | `$(IntermediateOutputPath)DamlGenerated/` | where the bindings are written |

## License

Apache-2.0. See the repository's LICENSE file.
