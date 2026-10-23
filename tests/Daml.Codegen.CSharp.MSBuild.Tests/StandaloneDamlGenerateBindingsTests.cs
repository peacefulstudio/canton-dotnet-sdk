// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public class StandaloneDamlGenerateBindingsTests : DamlGenerateBindingsAdapterContractTests
{
    private readonly MsBuildIntegrationFixture _fx;

    public StandaloneDamlGenerateBindingsTests(MsBuildIntegrationFixture fx) : base(fx)
    {
        _fx = fx;
    }

    protected override void WriteConsumerProject(string generatedDir, IReadOnlyList<string> darPaths) =>
        ConsumerProjectWriter.Write(
            ProjectDir,
            _fx.PropsFile,
            _fx.TargetsFile,
            generatedDir: generatedDir,
            darPaths: darPaths,
            damlCodegenMode: "Standalone");

    protected override void AssertEmitterIsBuilt() =>
        File.Exists(_fx.BundledDarParserCliDll).Should().BeTrue($"the umbrella build must bundle dar2intermediate at {_fx.BundledDarParserCliDll}");

    [Fact]
    public async Task DamlGenerateBindings_two_step_pipeline_writes_a_throwaway_intermediate_binpb_ahead_of_the_emitter()
    {
        File.Exists(_fx.FixtureDar).Should().BeTrue(
            $"DAR fixture must be present at {_fx.FixtureDar}");

        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        WriteConsumerProject(generatedDir, [_fx.FixtureDar]);

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            ProjectDir, "--target DamlGenerateBindings --verbosity minimal");

        exit.Should().Be(0,
            $"the two-step pipeline must succeed end to end.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var intermediateFiles = Directory.GetFiles(ProjectDir, "*.binpb", SearchOption.AllDirectories);
        intermediateFiles.Should().ContainSingle(
            "dar2intermediate must write exactly one throwaway IntermediateDar protobuf under the project's " +
            "intermediate output, proving it ran as a process distinct from the emitter rather than an in-process shortcut");

        var bytes = await File.ReadAllBytesAsync(intermediateFiles[0], TestContext.Current.CancellationToken);
        bytes.Should().NotBeEmpty(
            "the throwaway artifact must carry real protobuf bytes — the build already succeeded above, so the " +
            "emitter parsed this exact file as a valid IntermediateDar");
    }

    [Fact]
    public void DamlGenerateBindings_without_a_codegen_mode_runs_the_standalone_adapter_with_neither_dpm_nor_a_jdk_on_path()
    {
        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, generatedDir: generatedDir, darPaths: [_fx.FixtureDar]);
        var environment = new Dictionary<string, string?> { ["PATH"] = MsBuildProcessRunner.BuildPath(null, "dpm", "java") };

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            ProjectDir, "--target DamlGenerateBindings --verbosity minimal", environment);

        exit.Should().Be(0,
            $"a consumer that sets no DamlCodegenMode gets Standalone, which needs neither dpm nor java.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Directory.GetFiles(ProjectDir, "*.binpb", SearchOption.AllDirectories).Should().ContainSingle(
            "only the Standalone adapter writes an intermediate protobuf ahead of the emitter");
        Directory.GetFiles(generatedDir, "*.cs", SearchOption.AllDirectories).Should().NotBeEmpty();
    }

    [Fact]
    public void DamlGenerateBindings_two_step_pipeline_rejects_more_than_one_damlarchive_item()
    {
        File.Exists(_fx.FixtureDar).Should().BeTrue(
            $"DAR fixture must be present at {_fx.FixtureDar}");

        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        WriteConsumerProject(generatedDir, [_fx.FixtureDar, _fx.FixtureDar]);

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            ProjectDir, "--target DamlGenerateBindings --verbosity minimal");

        exit.Should().NotBe(0,
            $"the two-step pipeline takes a single DAR per project, like the Dpm adapter.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain(
            "exactly one",
            $"the failure must name the single-DAR constraint, not fail for an unrelated reason.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Fact]
    public void DamlGenerateBindings_item_level_namespace_and_includeDependencies_metadata_has_no_effect()
    {
        File.Exists(_fx.FixtureDar).Should().BeTrue(
            $"DAR fixture must be present at {_fx.FixtureDar}");

        var defaultGeneratedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        WriteConsumerProject(defaultGeneratedDir, [_fx.FixtureDar]);
        var (defaultExit, defaultOut, defaultErr) = MsBuildProcessRunner.RunDotnetBuild(
            ProjectDir, "--target DamlGenerateBindings --verbosity minimal");
        defaultExit.Should().Be(0,
            $"the baseline build with no item metadata must succeed.\nSTDOUT:\n{defaultOut}\nSTDERR:\n{defaultErr}");

        var withMetadataDir = Path.Combine(ProjectDir, "with-item-metadata");
        Directory.CreateDirectory(withMetadataDir);
        var withMetadataGeneratedDir = Path.Combine(withMetadataDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        ConsumerProjectWriter.Write(
            withMetadataDir,
            _fx.PropsFile,
            _fx.TargetsFile,
            generatedDir: withMetadataGeneratedDir,
            darPaths: [_fx.FixtureDar],
            damlCodegenMode: "Standalone",
            itemNamespace: "Some.Bogus.Item.Level.Namespace",
            itemIncludeDependencies: true);
        var (withMetadataExit, withMetadataOut, withMetadataErr) = MsBuildProcessRunner.RunDotnetBuild(
            withMetadataDir, "--target DamlGenerateBindings --verbosity minimal");
        withMetadataExit.Should().Be(0,
            $"an unread item metadata attribute must not fail the build.\nSTDOUT:\n{withMetadataOut}\nSTDERR:\n{withMetadataErr}");

        var defaultFiles = Directory.GetFiles(defaultGeneratedDir, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(defaultGeneratedDir, path))
            .OrderBy(relativePath => relativePath, StringComparer.Ordinal)
            .ToList();
        var withMetadataFiles = Directory.GetFiles(withMetadataGeneratedDir, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(withMetadataGeneratedDir, path))
            .OrderBy(relativePath => relativePath, StringComparer.Ordinal)
            .ToList();

        withMetadataFiles.Should().Equal(defaultFiles,
            "item-level Namespace/IncludeDependencies metadata on the DamlArchive item is never read, " +
            "so it must not change which files are emitted");

        foreach (var relativePath in defaultFiles)
        {
            var defaultContent = File.ReadAllText(Path.Combine(defaultGeneratedDir, relativePath));
            var withMetadataContent = File.ReadAllText(Path.Combine(withMetadataGeneratedDir, relativePath));
            withMetadataContent.Should().Be(defaultContent,
                $"{relativePath} must be byte-identical whether or not the DamlArchive item carries " +
                "Namespace/IncludeDependencies metadata, since only the project-level " +
                "$(DamlNamespace)/$(DamlIncludeDependencies) properties are ever read");
        }
    }
}
