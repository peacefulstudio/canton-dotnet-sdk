// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Exercises the <c>stdlib-runtime</c> subcommand end to end through <see cref="Program.Main"/>:
/// regenerating into a directory that already holds bindings must replace them, because the
/// runtime project compiles every <c>.cs</c> file under that directory.
/// </summary>
public class StdlibRuntimeCliTests : IDisposable
{
    private const string StaleFileName = "RemovedFromInventory.cs";
    private const string UnrelatedFileName = "notes.txt";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"stdlib-runtime-cli-{Guid.NewGuid():N}");

    public StdlibRuntimeCliTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string StablePackagesDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "stable-packages");

    [Fact]
    public async Task StdlibRuntimeCli_regenerating_into_a_populated_directory_removes_stale_bindings_and_keeps_other_files()
    {
        var output = Path.Combine(_workspace, "out");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, StaleFileName), "// stale", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, UnrelatedFileName), "keep me", TestContext.Current.CancellationToken);

        var exit = await Program.Main(["stdlib-runtime", StablePackagesDirectory(), "-o", output]);

        exit.Should().Be(0);
        File.Exists(Path.Combine(output, StaleFileName)).Should().BeFalse(
            "a binding the generator no longer emits must not survive regeneration");
        File.Exists(Path.Combine(output, UnrelatedFileName)).Should().BeTrue(
            "only .cs files are swept from the output directory");
        Directory.GetFiles(output, "*.cs").Should().NotBeEmpty("the stable packages yield the stdlib bindings");
    }
}
