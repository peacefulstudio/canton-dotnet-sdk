# Getting started

The SDK takes a Daml `.dar` archive to strongly-typed C# bindings, then submits them to a Canton
ledger. This page is the short route; each step links to the full text.

## 1. Install the tools

You need the .NET SDK 10.0.100 or later, a JDK 17 or later (for `dpm codegen-cs`), and dpm with
Daml SDK 3.7.1. The exact install commands are under *Prerequisites* in the
[README](../../../../README.md#prerequisites).

## 2. Build a DAR and generate C#

Two small dpm projects side by side: one builds the DAR, one runs the `dpm codegen-cs` component.
The [README quick start](../../../../README.md#quick-start) walks through both with a complete
`Iou` template, and [code generation](../../../../docs/public/codegen.md) lists everything the
generator emits.

To generate during `dotnet build` instead, reference
[`Daml.Codegen.CSharp.MSBuild`](../../../../src/Daml.Codegen.CSharp.MSBuild/README.md) and add a
`<DamlArchive>` item. Its default `Standalone` mode needs no JDK.

## 3. Reference the runtime

Every consumer of generated code references `Daml.Runtime`; see its
[README](../../../../src/Daml.Runtime/README.md). How Daml types map to C# is in
[type mappings](../../../../docs/public/type-mappings.md).

## 4. Talk to a ledger

Continue with the [ledger clients quickstart](ledger-clients-quickstart.md).

## Where next

- [Architecture overview](../../../../docs/public/architecture-overview.md)
- [CLI reference](../../../../docs/public/cli-reference.md)
- [Packaging a DAR as NuGet](../../../../docs/public/dar-to-nuget.md)
- [API reference](../api/index.md)
