// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Text;
using Daml.Codegen.Tooling;
using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.DarParser.Cli;

/// <summary>
/// The <c>stdlib-runtime</c> entry point: decodes the vendored Daml stable packages and writes the
/// <c>Daml.Runtime.Stdlib</c> bindings the emitter references for the standard library types, one
/// <c>.cs</c> file per type. The output is deterministic, so a checked-in copy can be compared
/// byte for byte against a fresh run.
/// </summary>
public static class StdlibRuntimeCommand
{
    /// <summary>Builds the <c>stdlib-runtime</c> subcommand.</summary>
    public static Command Create()
    {
        var command = new Command(
            "stdlib-runtime",
            "Generate the Daml.Runtime.Stdlib bindings from a directory of Daml stable-package .dalf files");

        var stablePackagesArgument = new Argument<DirectoryInfo>("stable-packages-dir")
        {
            Description = "Directory holding the stable-package .dalf files (searched recursively)"
        };
        stablePackagesArgument.Validators.Add(result =>
        {
            var directory = result.GetValueOrDefault<DirectoryInfo>();
            if (directory is null || !directory.Exists)
            {
                result.AddError($"Stable packages directory not found: {directory?.FullName}");
            }
        });

        var outputOption = new Option<DirectoryInfo>("-o")
        {
            Description = "Directory the generated .cs files are written to; existing .cs files in it are removed first",
            Required = true
        };
        outputOption.Aliases.Add("--output");

        command.Arguments.Add(stablePackagesArgument);
        command.Options.Add(outputOption);
        command.SetAction(parseResult => Run(
            parseResult.GetValue(stablePackagesArgument)!,
            parseResult.GetValue(outputOption)!));

        return command;
    }

    private static int Run(DirectoryInfo stablePackagesDirectory, DirectoryInfo output)
    {
        var packages = stablePackagesDirectory
            .EnumerateFiles("*.dalf", SearchOption.AllDirectories)
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .Select(file => DalfReader.Parse(File.ReadAllBytes(file.FullName)))
            .ToList();

        var files = StdlibRuntimeBindings.Generate(packages);

        output.Create();
        foreach (var stale in output.EnumerateFiles("*.cs"))
        {
            stale.Delete();
        }
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(output.FullName, file.RelativePath), file.Content, new UTF8Encoding(false));
        }

        Console.Out.WriteLine($"Wrote {files.Count} files to {output.FullName}");
        return 0;
    }
}
