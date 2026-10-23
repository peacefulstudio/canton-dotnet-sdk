// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

/// <summary>
/// Drives the root command the way a host does, through <c>Parse(...).InvokeAsync(token)</c>,
/// to pin the behaviour that depends on the token and on the <c>--root</c> filter rather than
/// on <c>Program.Main</c>'s default token.
/// </summary>
[Collection("ConsoleRedirection")]
public class CliRunCodegenTests : IDisposable
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private const string InterfaceFileName = "IHolding.cs";
    private const string RecordFileName = "InstrumentId.cs";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"cli-run-{Guid.NewGuid():N}");

    public CliRunCodegenTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string FixtureIntermediate() =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, "intermediate.binpb");

    private static string[] EmittedFileNames(string directory) =>
        [.. Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).Select(Path.GetFileName).Cast<string>()];

    [Fact]
    public async Task CliRunCodegen_cancelled_token_exits_130_and_writes_no_bindings()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exit = await Program.BuildRootCommand()
            .Parse(["--intermediate", FixtureIntermediate(), "-o", _workspace])
            .InvokeAsync(cancellationToken: cancellation.Token);

        exit.Should().Be(130, "a cancelled run reports the conventional SIGINT exit code instead of a failure");
        EmittedFileNames(_workspace).Should().BeEmpty("cancellation is observed before any binding is generated");
    }

    [Fact]
    public async Task CliRunCodegen_root_filter_keeps_only_the_matching_interface_bindings()
    {
        var unfiltered = Path.Combine(_workspace, "unfiltered");
        var matching = Path.Combine(_workspace, "matching");
        var nonMatching = Path.Combine(_workspace, "non-matching");

        (await Program.Main(["--intermediate", FixtureIntermediate(), "-o", unfiltered])).Should().Be(0);
        (await Program.Main(["--intermediate", FixtureIntermediate(), "-o", matching, "--root", @"^Splice\.Api\.Token\.HoldingV1:Holding$"])).Should().Be(0);
        (await Program.Main(["--intermediate", FixtureIntermediate(), "-o", nonMatching, "--root", "^NoSuchModule:"])).Should().Be(0);

        EmittedFileNames(unfiltered).Should().Contain(InterfaceFileName);
        EmittedFileNames(matching).Should().Contain(InterfaceFileName);
        EmittedFileNames(nonMatching).Should().NotContain(InterfaceFileName,
            "an interface whose Module:Name the --root pattern does not match is not emitted");
        EmittedFileNames(nonMatching).Should().Contain(RecordFileName,
            "the root filter selects templates and interfaces; plain records are still emitted");
    }
}
