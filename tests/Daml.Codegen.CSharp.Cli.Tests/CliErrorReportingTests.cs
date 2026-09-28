// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Cli;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

[Collection("ConsoleRedirection")]
public class CliErrorReportingTests : IDisposable
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private readonly string _workspace;

    public CliErrorReportingTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"cli-errors-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string FixtureIntermediatePath =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, "intermediate.binpb");

    [Fact]
    public async Task CliErrorReporting_failure_at_default_verbosity_also_logs_the_root_cause_when_it_differs_from_the_top_message()
    {
        var corruptCounters = Path.Combine(_workspace, "release-counters.json");
        await File.WriteAllTextAsync(corruptCounters, "{not json", TestContext.Current.CancellationToken);

        var (exit, stderr) = await RunCapturingStdErr(() => Program.Main(
        [
            "--intermediate", FixtureIntermediatePath,
            "-o", _workspace,
            "--release-counters", corruptCounters
        ]));

        exit.Should().Be(1);
        stderr.Should().Contain("Release-counter store",
            "the top-level message must surface as before");
        stderr.Should().Contain("Root cause:",
            "a wrapped JsonException's message must surface at default verbosity, not only at -v 3");
    }

    [Fact]
    public async Task CliErrorReporting_cancellation_warns_that_partially_written_files_may_remain_in_the_output_directory()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var args = new CodegenArgs(
            new FileInfo(FixtureIntermediatePath),
            new DirectoryInfo(_workspace),
            NamespacePrefix: null,
            Verbosity: 1,
            RootFilter: null,
            EnableNullable: true,
            GenerateProjectFile: false,
            IncludeDependencies: false,
            TargetFramework: "net10.0",
            RuntimePackageVersion: null,
            EmitterCounter: 0,
            ReleaseCountersFile: null,
            CodegenVersion: null,
            PackageLicenseExpression: "Apache-2.0",
            VersionSuffix: null,
            RepositoryUrl: null);

        var (exit, stderr) = await RunCapturingStdErr(() => Program.RunCodegen(args, cts.Token));

        exit.Should().Be(130);
        stderr.Should().Contain("canceled");
        stderr.Should().Contain("Partially written files may remain",
            "an interrupted run can leave a half-emitted output tree and the operator must know to clean it");
    }

    [Fact]
    public async Task CliErrorReporting_invalid_version_suffix_is_rejected_at_the_cli_boundary_with_a_clean_error()
    {
        var (exit, stderr) = await RunCapturingStdErr(() => Program.Main(
        [
            "--intermediate", FixtureIntermediatePath,
            "-o", _workspace,
            "--version-suffix", "bad suffix"
        ]));

        exit.Should().NotBe(0,
            "an unvalidated suffix would otherwise reach <Version> and only fail late at dotnet pack");
        stderr.Should().Contain("--version-suffix");
        stderr.Should().Contain("bad suffix",
            "the error must name the rejected value so the operator can see what was wrong");
    }

    [Fact]
    public async Task CliErrorReporting_the_removed_contract_identifiers_flag_is_an_unrecognized_option()
    {
        var (exit, stderr) = await RunCapturingStdErr(() => Program.Main(
        [
            "--intermediate", FixtureIntermediatePath,
            "-o", _workspace,
            "--contract-identifiers", "true"
        ]));

        exit.Should().NotBe(0,
            "a build script still passing the flag must fail loudly rather than silently lose the helper class");
        stderr.Should().Contain("--contract-identifiers");
    }

    private static async Task<(int Exit, string StdErr)> RunCapturingStdErr(Func<Task<int>> run)
    {
        var originalError = Console.Error;
        using var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            var exit = await run();
            return (exit, capture.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
