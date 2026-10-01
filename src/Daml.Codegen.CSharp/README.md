# Daml.Codegen.CSharp

C# code generator library for Daml smart contracts, part of the [Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk). Consumes an
`IntermediateDar` protobuf (produced by the JVM helper bundled in
`dpm codegen-cs`) and emits strongly-typed C# bindings for Daml templates,
data types, and interfaces.

## Installation

```bash
dotnet add package Daml.Codegen.CSharp --version 0.6.0-preview.3
```

## Usage

Most users run codegen through `dpm codegen-cs` rather than this library
directly. Reference the library when you need programmatic code generation,
for example inside a build tool:

```csharp
using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate;
using Microsoft.Extensions.Logging;

IntermediateDar proto;
await using (var stream = File.OpenRead("intermediate.binpb"))
{
    proto = IntermediateDar.Parser.ParseFrom(stream);
}

var dar = IntermediateDarReader.Read(proto);

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddProvider(new VerbosityConsoleLoggerProvider(verbosity: 1))
           .SetMinimumLevel(LogLevel.Trace));

var generator = new CSharpCodeGenerator(
    new CodeGenOptions(),
    loggerFactory.CreateLogger<CSharpCodeGenerator>());
var files = generator.Generate(dar);

foreach (var file in files)
{
    // file.RelativePath, file.Content — the caller owns writing to disk.
}
```

## Logging

`CSharpCodeGenerator` reports progress and warnings through
`Microsoft.Extensions.Logging`. The `logger` parameter is optional: omit it and
the generator stays silent, so a host already wired for logging simply hands it
an `ILogger<CSharpCodeGenerator>` from its own factory.

`VerbosityConsoleLoggerProvider` is included for hosts that just want
severity-prefixed console output — errors and warnings on stderr, everything
else on stdout. Its verbosity maps to a minimum level: `0` errors only, `1`
warnings, `2` information, `3` or more debug. Building a factory around it, as
the sample above does, needs the `Microsoft.Extensions.Logging` package; the
library itself depends only on `Microsoft.Extensions.Logging.Abstractions`.

Generated code targets the lockstep-versioned `Daml.Runtime` and
`Daml.Ledger.Abstractions` packages.

## Support

For questions, bug reports and feature requests, open a
[GitHub issue](https://github.com/peacefulstudio/canton-dotnet-sdk/issues).
This package is maintained by [Peaceful Studio](https://peaceful.studio).
For consulting beyond this SDK (Daml development, .NET integration with
Canton, validator operations), please write to
[info@peaceful.studio](mailto:info@peaceful.studio).

## License

Apache-2.0. See the repository's LICENSE file.
