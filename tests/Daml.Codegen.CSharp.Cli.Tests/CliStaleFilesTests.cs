// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Google.Protobuf;
using Daml.Codegen.Intermediate;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

/// <summary>
/// Regenerating into an output directory from a DAR that dropped a type must not leave the
/// old binding behind, because the MSBuild adapters and the bundle compile and pack every
/// <c>.cs</c> file under that directory.
/// </summary>
[Collection("ConsoleRedirection")]
public class CliStaleFilesTests : IDisposable
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private const string DroppedTypeName = "Lock";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"cli-stale-{Guid.NewGuid():N}");

    public CliStaleFilesTests()
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

    private string WriteIntermediateWithoutDroppedType()
    {
        var dar = IntermediateDar.Parser.ParseFrom(File.ReadAllBytes(FixtureIntermediate()));
        foreach (var module in dar.Main.Modules)
        {
            var dropped = module.DataTypes.Where(dt => dt.Name == DroppedTypeName).ToList();
            foreach (var dataType in dropped)
            {
                module.DataTypes.Remove(dataType);
            }
        }
        var path = Path.Combine(_workspace, "smaller.binpb");
        File.WriteAllBytes(path, dar.ToByteArray());
        return path;
    }

    [Fact]
    public async Task CliStaleFiles_regenerating_from_a_smaller_dar_removes_the_binding_of_the_dropped_type()
    {
        var output = Path.Combine(_workspace, "out");
        (await Program.Main(["--intermediate", FixtureIntermediate(), "-o", output])).Should().Be(0);
        var droppedFile = Directory.GetFiles(output, $"{DroppedTypeName}.cs", SearchOption.AllDirectories);
        droppedFile.Should().HaveCount(1, "the full fixture emits a binding for the type the smaller DAR drops");
        var keptFiles = Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories).Length - 1;

        (await Program.Main(["--intermediate", WriteIntermediateWithoutDroppedType(), "-o", output])).Should().Be(0);

        File.Exists(droppedFile[0]).Should().BeFalse("the dropped type's binding must not survive regeneration");
        Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories).Should().HaveCount(keptFiles);
    }

    [Fact]
    public async Task CliStaleFiles_a_run_for_another_package_exits_1_naming_both_packages_and_touches_nothing()
    {
        var output = Path.Combine(_workspace, "out");
        (await Program.Main(["--intermediate", FixtureIntermediate(), "-o", output])).Should().Be(0);
        var filesBefore = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Order().ToList();
        var manifestBefore = File.ReadAllText(Path.Combine(output, ".codegen-manifest"));

        var (exit, stderr) = await RunCapturingStdErr(() => Program.Main(
            ["--intermediate", WriteIntermediateRenamedTo("another-package"), "-o", output]));

        exit.Should().Be(1);
        stderr.Should().Contain("already holds the generated output of package 'splice-api-token-holding-v1'");
        stderr.Should().Contain("this run generates package 'another-package'");
        stderr.Should().Contain("Give each DAR its own output directory.");
        stderr.Should().Contain("If this directory holds an earlier build of the same project, run 'dotnet clean' or delete the directory.");
        Directory.GetFiles(output, "*", SearchOption.AllDirectories).Order().Should().Equal(filesBefore);
        File.ReadAllText(Path.Combine(output, ".codegen-manifest")).Should().Be(manifestBefore);
    }

    private string WriteIntermediateRenamedTo(string packageName)
    {
        var dar = IntermediateDar.Parser.ParseFrom(File.ReadAllBytes(FixtureIntermediate()));
        dar.Main.PackageName = packageName;
        var path = Path.Combine(_workspace, "renamed.binpb");
        File.WriteAllBytes(path, dar.ToByteArray());
        return path;
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
