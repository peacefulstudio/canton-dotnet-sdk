// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.DarParser.Cli;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Google.Protobuf;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Exercises <see cref="Program.Main"/>'s <c>dar2intermediate</c> subcommand end to end through
/// the CLI's argument parsing and file I/O. This is the CLI-process counterpart to
/// <c>Dar2IntermediateRoundTripTests</c>, which exercises
/// <see cref="Daml.Codegen.DarParser.IntermediateDarWriter"/> directly without going through
/// <see cref="Program.Main"/>. The <c>stdlib-runtime</c> subcommand is covered by
/// <c>StdlibRuntimeCliTests</c>.
/// </summary>
public class Dar2IntermediateCliTests : IDisposable
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private readonly string _workspace;

    public Dar2IntermediateCliTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"dar2intermediate-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Dar2IntermediateCli_dar2intermediate_writes_a_parseable_binpb()
    {
        var dar = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, $"{FixtureSnapshotName}.dar");
        File.Exists(dar).Should().BeTrue($"fixture DAR must ship at {dar}");
        var output = Path.Combine(_workspace, "nested", "intermediate.binpb");

        var exit = await Program.Main(["dar2intermediate", dar, "-o", output]);

        exit.Should().Be(0);
        File.Exists(output).Should().BeTrue("dar2intermediate must create the output file, including any missing parent directory");
        var bytes = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
        var proto = IntermediateDar.Parser.ParseFrom(bytes);
        proto.Main.PackageId.Should().NotBeNullOrEmpty("the emitted .binpb must be a valid IntermediateDar with a main package");
    }

    [Fact]
    public async Task Dar2IntermediateCli_dar2intermediate_takes_lowercase_v_for_verbosity_like_the_emitter_cli()
    {
        var dar = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, $"{FixtureSnapshotName}.dar");
        var output = Path.Combine(_workspace, "verbose.binpb");

        var exit = await Program.Main(["dar2intermediate", dar, "-o", output, "-v", "2"]);

        exit.Should().Be(0);
        File.Exists(output).Should().BeTrue();
    }

    [Fact]
    public async Task Dar2IntermediateCli_dar2intermediate_no_longer_accepts_uppercase_V_for_verbosity()
    {
        var dar = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, $"{FixtureSnapshotName}.dar");
        var output = Path.Combine(_workspace, "uppercase.binpb");

        var exit = await Program.Main(["dar2intermediate", dar, "-o", output, "-V", "2"]);

        exit.Should().NotBe(0);
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Dar2IntermediateCli_dar2intermediate_rejects_a_missing_dar_file()
    {
        var missing = Path.Combine(_workspace, "does-not-exist.dar");
        var output = Path.Combine(_workspace, "out.binpb");

        var exit = await Program.Main(["dar2intermediate", missing, "-o", output]);

        exit.Should().NotBe(0, "a missing DAR must fail validation before any work");
        File.Exists(output).Should().BeFalse("no output may be written when the input DAR is invalid");
    }

    [Fact]
    public async Task Dar2IntermediateCli_dar2intermediate_rejects_a_non_dar_extension()
    {
        var wrongExtension = Path.Combine(_workspace, "not-a-dar.txt");
        await File.WriteAllTextAsync(wrongExtension, "", TestContext.Current.CancellationToken);
        var output = Path.Combine(_workspace, "out.binpb");

        var exit = await Program.Main(["dar2intermediate", wrongExtension, "-o", output]);

        exit.Should().NotBe(0, "only .dar inputs are accepted");
        File.Exists(output).Should().BeFalse("no output may be written when the input extension is wrong");
    }

    [Fact]
    public async Task Dar2IntermediateCli_the_retired_in_process_root_invocation_shape_no_longer_parses()
    {
        var dar = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, $"{FixtureSnapshotName}.dar");
        File.Exists(dar).Should().BeTrue($"fixture DAR must ship at {dar}");

        var exit = await Program.Main(["-o", _workspace, dar]);

        exit.Should().NotBe(0,
            "the in-process DAR-direct root command (positional dar-files + -o) was retired — " +
            "Program only exposes dar2intermediate now, so this shape must fail to parse rather than generate C#");
        Directory.EnumerateFiles(_workspace, "*.cs", SearchOption.AllDirectories)
            .Should().BeEmpty("no C# may be emitted in-process; codegen now only happens through the two-step pipeline");
    }
}
