// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

public sealed class GeneratedOutputWriterTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"writer-{Guid.NewGuid():N}");
    private readonly DirectoryInfo _output;

    public GeneratedOutputWriterTests()
    {
        _output = Directory.CreateDirectory(Path.Combine(_workspace, "out"));
    }

    public void Dispose()
    {
        Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    private Task Write(params GeneratedFile[] files) =>
        GeneratedOutputWriter.WriteAsync(files, _output, NullLogger.Instance, TestContext.Current.CancellationToken);

    private string InOutput(string relativePath) => Path.Combine(_output.FullName, relativePath);

    [Fact]
    public async Task GeneratedOutputWriter_records_every_written_file_in_the_manifest()
    {
        await Write(
            GeneratedFile.Text("Pkg/Mod/Payload.cs", "payload"),
            GeneratedFile.Text("Pkg.csproj", "<Project/>"));

        File.ReadAllLines(InOutput(".codegen-manifest")).Should().Equal("Pkg.csproj", "Pkg/Mod/Payload.cs");
    }

    [Fact]
    public async Task GeneratedOutputWriter_deletes_files_the_previous_run_wrote_and_this_run_did_not()
    {
        await Write(
            GeneratedFile.Text("Pkg/Kept.cs", "kept"),
            GeneratedFile.Text("Pkg/Removed.cs", "removed"));

        await Write(GeneratedFile.Text("Pkg/Kept.cs", "kept again"));

        File.Exists(InOutput("Pkg/Removed.cs")).Should().BeFalse();
        File.ReadAllText(InOutput("Pkg/Kept.cs")).Should().Be("kept again");
        File.ReadAllLines(InOutput(".codegen-manifest")).Should().Equal("Pkg/Kept.cs");
    }

    [Fact]
    public async Task GeneratedOutputWriter_removes_directories_left_empty_by_a_deleted_file()
    {
        await Write(
            GeneratedFile.Text("Pkg/Kept.cs", "kept"),
            GeneratedFile.Text("Pkg/Gone/Deep/Removed.cs", "removed"));

        await Write(GeneratedFile.Text("Pkg/Kept.cs", "kept"));

        Directory.Exists(InOutput("Pkg/Gone")).Should().BeFalse();
        Directory.Exists(InOutput("Pkg")).Should().BeTrue();
        _output.Exists.Should().BeTrue();
    }

    [Fact]
    public async Task GeneratedOutputWriter_keeps_files_it_never_wrote()
    {
        await File.WriteAllTextAsync(InOutput("HandWritten.cs"), "mine", TestContext.Current.CancellationToken);
        await Write(GeneratedFile.Text("Pkg/Old.cs", "old"));

        await Write(GeneratedFile.Text("Pkg/New.cs", "new"));

        File.ReadAllText(InOutput("HandWritten.cs")).Should().Be("mine");
        File.Exists(InOutput("Pkg/Old.cs")).Should().BeFalse();
    }

    [Fact]
    public async Task GeneratedOutputWriter_deletes_nothing_on_the_first_run_without_a_manifest()
    {
        await File.WriteAllTextAsync(InOutput("Legacy.cs"), "from an older emitter", TestContext.Current.CancellationToken);

        await Write(GeneratedFile.Text("Pkg/New.cs", "new"));

        File.Exists(InOutput("Legacy.cs")).Should().BeTrue();
    }

    [Theory]
    [InlineData("/Escaped.cs")]
    [InlineData("../Escaped.cs")]
    [InlineData("Pkg/../../Escaped.cs")]
    public async Task GeneratedOutputWriter_rejects_a_path_that_leaves_the_output_directory_before_writing_anything(string relativePath)
    {
        var act = () => Write(
            GeneratedFile.Text("Pkg/Fine.cs", "fine"),
            GeneratedFile.Text(relativePath, "escaped"));

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*outside the output directory*");
        File.Exists(InOutput("Pkg/Fine.cs")).Should().BeFalse();
        File.Exists(Path.Combine(_workspace, "Escaped.cs")).Should().BeFalse();
        File.Exists("/Escaped.cs").Should().BeFalse();
    }

    [Fact]
    public async Task GeneratedOutputWriter_never_deletes_outside_the_output_directory_for_a_manifest_entry_that_climbs_out()
    {
        var victim = Path.Combine(_workspace, "Victim.cs");
        await File.WriteAllTextAsync(victim, "precious", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(InOutput(".codegen-manifest"), "../Victim.cs\n/etc/hosts\n", TestContext.Current.CancellationToken);

        await Write(GeneratedFile.Text("Pkg/New.cs", "new"));

        File.ReadAllText(victim).Should().Be("precious");
        File.ReadAllLines(InOutput(".codegen-manifest")).Should().Equal("Pkg/New.cs");
    }

    [Fact]
    public async Task GeneratedOutputWriter_never_deletes_through_a_directory_symlink_inside_the_output_directory()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_workspace, "outside"));
        var victim = Path.Combine(outside.FullName, "Victim.cs");
        await File.WriteAllTextAsync(victim, "precious", TestContext.Current.CancellationToken);
        Directory.CreateSymbolicLink(InOutput("Pkg"), outside.FullName);
        await File.WriteAllTextAsync(InOutput(".codegen-manifest"), "Pkg/Victim.cs\n", TestContext.Current.CancellationToken);

        await Write(GeneratedFile.Text("Other/New.cs", "new"));

        File.ReadAllText(victim).Should().Be("precious");
    }

    [Fact]
    public async Task GeneratedOutputWriter_deletes_stale_files_when_the_output_path_has_a_trailing_separator_under_a_symlinked_ancestor()
    {
        var realParent = Directory.CreateDirectory(Path.Combine(_workspace, "real-parent"));
        var linkedParent = Path.Combine(_workspace, "linked-parent");
        Directory.CreateSymbolicLink(linkedParent, realParent.FullName);
        var output = new DirectoryInfo(Path.Combine(linkedParent, "Generated") + Path.DirectorySeparatorChar);
        Directory.CreateDirectory(output.FullName);

        await GeneratedOutputWriter.WriteAsync(
            [GeneratedFile.Text("Pkg/A.cs", "a"), GeneratedFile.Text("Pkg/B.cs", "b")],
            output, NullLogger.Instance, TestContext.Current.CancellationToken);
        await GeneratedOutputWriter.WriteAsync(
            [GeneratedFile.Text("Pkg/A.cs", "a")],
            output, NullLogger.Instance, TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(output.FullName, "Pkg/B.cs")).Should().BeFalse();
        File.Exists(Path.Combine(output.FullName, "Pkg/A.cs")).Should().BeTrue();
    }

    [Fact]
    public async Task GeneratedOutputWriter_writes_binary_files_and_lists_them_in_the_manifest()
    {
        await Write(GeneratedFile.Binary("icon.png", [1, 2, 3]));

        File.ReadAllBytes(InOutput("icon.png")).Should().Equal(1, 2, 3);
        File.ReadAllLines(InOutput(".codegen-manifest")).Should().Equal("icon.png");
    }
}
