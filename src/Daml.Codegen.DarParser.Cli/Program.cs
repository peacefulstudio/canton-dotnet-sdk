// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;

namespace Daml.Codegen.DarParser.Cli;

/// <summary>
/// Entry point for the pure-.NET DAR tooling, with two subcommands. <c>dar2intermediate</c> reads
/// a <c>.dar</c> archive with <c>Daml.Codegen.DarParser</c> and writes the corresponding
/// <c>IntermediateDar</c> protobuf; the <c>Daml.Codegen.CSharp.MSBuild</c> package bundles this
/// binary as the first step of its <c>DamlCodegenMode=Standalone</c> chain —
/// <c>dar2intermediate</c> followed by <c>Daml.Codegen.CSharp.Cli --intermediate</c> — so
/// <c>&lt;DamlArchive&gt;</c> codegen runs without a JDK. <c>stdlib-runtime</c> writes the
/// <c>Daml.Runtime.Stdlib</c> bindings from the vendored Daml stable packages.
/// </summary>
public static class Program
{
    /// <summary>Parses <paramref name="args"/> and runs the requested subcommand.</summary>
    /// <param name="args">Command-line arguments as supplied by the host.</param>
    /// <returns>The process exit code: <c>0</c> on success.</returns>
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Translate DAR archives into IntermediateDar protobufs");
        rootCommand.Subcommands.Add(Dar2IntermediateCommand.Create());
        rootCommand.Subcommands.Add(StdlibRuntimeCommand.Create());

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync().ConfigureAwait(false);
    }
}
