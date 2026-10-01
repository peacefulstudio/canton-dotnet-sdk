# Daml.Codegen.Intermediate

The intermediate DAR contract shared by every Daml C# codegen path in the [Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk). It carries
two things:

- the C# types generated from `intermediate_dar.proto`, the versioned wire
  format a DAR decoder produces, and
- the language-neutral Daml model (`DarModel`, `DamlPackage`, `DamlTemplate`,
  `DamlType`, …) that both producers and the emitter speak.

Producers write the protobuf; `Daml.Codegen.CSharp` reads it and emits C#.
Reference this package directly only when you write your own producer or your
own consumer of the intermediate representation.

## Installation

```bash
dotnet add package Daml.Codegen.Intermediate --version 0.6.0-preview.3
```

## Usage

```csharp
using Daml.Codegen.Intermediate;

IntermediateDar proto;
await using (var stream = File.OpenRead("intermediate.binpb"))
{
    proto = IntermediateDar.Parser.ParseFrom(stream);
}
```

## License

Apache-2.0. See the repository's LICENSE file.
