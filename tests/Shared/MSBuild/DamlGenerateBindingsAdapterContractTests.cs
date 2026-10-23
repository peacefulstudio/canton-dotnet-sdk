// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.MSBuild.TestSupport;

/// <summary>
/// The outcomes every MSBuild adapter must give through the shared targets
/// (<c>Daml.Codegen.CSharp.MSBuild.Shared.targets</c>), whichever emitter it drives.
/// An adapter's test class derives from this and supplies how its consumer project is
/// written and which environment its build runs in.
/// </summary>
public abstract class DamlGenerateBindingsAdapterContractTests : IDisposable
{
    private readonly IMsBuildAdapterFixture _adapterFixture;

    protected DamlGenerateBindingsAdapterContractTests(IMsBuildAdapterFixture adapterFixture)
    {
        _adapterFixture = adapterFixture;
        ProjectDir = Path.Combine(Path.GetTempPath(), $"msbuild-adapter-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ProjectDir);
    }

    protected string ProjectDir { get; }

    protected virtual IReadOnlyDictionary<string, string?>? BuildEnvironment => null;

    protected abstract void WriteConsumerProject(string generatedDir, IReadOnlyList<string> darPaths);

    protected virtual void AssertEmitterIsBuilt()
    {
    }

    public void Dispose()
    {
        try { Directory.Delete(ProjectDir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {ProjectDir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {ProjectDir}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    protected (int ExitCode, string StdOut, string StdErr) Build(string extraArgs) =>
        MsBuildProcessRunner.RunDotnetBuild(ProjectDir, extraArgs, BuildEnvironment);

    private string WriteFixtureConsumerProject()
    {
        File.Exists(_adapterFixture.FixtureDar).Should().BeTrue(
            $"DAR fixture must be present at {_adapterFixture.FixtureDar}");
        AssertEmitterIsBuilt();

        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        WriteConsumerProject(generatedDir, [_adapterFixture.FixtureDar]);
        return generatedDir;
    }

    [Fact]
    public void DamlGenerateBindings_generating_bindings_produces_cs_files_and_marker_under_generated_dir()
    {
        var generatedDir = WriteFixtureConsumerProject();

        var (exit, stdout, stderr) = Build("--target DamlGenerateBindings --verbosity minimal");

        exit.Should().Be(0,
            $"DamlGenerateBindings target must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var csFiles = Directory.GetFiles(generatedDir, "*.cs", SearchOption.AllDirectories);
        csFiles.Should().NotBeEmpty(
            "the MSBuild target must invoke the emitter and produce at least one .cs file");

        var marker = Path.Combine(generatedDir, ".daml-generated");
        File.Exists(marker).Should().BeTrue(
            "the MSBuild target must touch the .daml-generated incremental marker after a successful run");
    }

    [Fact]
    public void DamlGenerateBindings_generated_cs_files_are_included_in_compile_via_daml_include_generated_files_target()
    {
        WriteFixtureConsumerProject();

        var (exit, stdout, _) = Build("--target DamlGenerateBindings;DamlIncludeGeneratedFiles --verbosity normal");

        exit.Should().Be(0,
            $"build must succeed before checking log output\nSTDOUT:\n{stdout}");

        stdout.Should().Contain(
            "Added generated Daml files to compilation",
            "the DamlIncludeGeneratedFiles target must log its message confirming the .cs files were added to the Compile item group");
    }

    [Fact]
    public void DamlGenerateBindings_second_build_with_no_input_change_does_not_update_marker_mtime()
    {
        var generatedDir = WriteFixtureConsumerProject();

        var incrementalArgs = "--target DamlGenerateBindings --verbosity minimal";

        var (firstExit, firstOut, firstErr) = Build(incrementalArgs);
        firstExit.Should().Be(0,
            $"first build must succeed.\nSTDOUT:\n{firstOut}\nSTDERR:\n{firstErr}");

        var marker = Path.Combine(generatedDir, ".daml-generated");
        File.Exists(marker).Should().BeTrue("marker must exist after first build");
        var mtimeAfterFirstBuild = File.GetLastWriteTimeUtc(marker);
        mtimeAfterFirstBuild.Should().NotBe(default(DateTime), "marker must have a real mtime confirming it was created");

        var (secondExit, secondOut, secondErr) = Build(incrementalArgs);
        secondExit.Should().Be(0,
            $"second build must succeed.\nSTDOUT:\n{secondOut}\nSTDERR:\n{secondErr}");

        var mtimeAfterSecondBuild = File.GetLastWriteTimeUtc(marker);
        mtimeAfterSecondBuild.Should().Be(mtimeAfterFirstBuild,
            "the DamlGenerateBindings target declares Inputs/Outputs — with unchanged inputs MSBuild must skip it " +
            "and must not update the marker mtime");
    }

    [Fact]
    public void DamlGenerateBindings_failing_emitter_invocation_fails_the_build_with_nonzero_exit()
    {
        var nonExistentDar = Path.Combine(ProjectDir, "does-not-exist.dar");
        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        WriteConsumerProject(generatedDir, [nonExistentDar]);

        var (exit, stdout, stderr) = Build("--target DamlGenerateBindings --verbosity minimal");

        exit.Should().NotBe(0,
            $"dotnet build must fail when the declared DamlArchive does not exist.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain(
            "Daml code generation failed with exit code",
            $"the build must fail specifically because DamlGenerateBindings ran the emitter on a missing DAR, not because the harness is misconfigured.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Fact]
    public void DamlGenerateBindings_clean_target_removes_the_generated_directory()
    {
        var generatedDir = WriteFixtureConsumerProject();

        var (generateExit, generateOut, generateErr) = Build("--target DamlGenerateBindings --verbosity minimal");
        generateExit.Should().Be(0,
            $"DamlGenerateBindings must succeed before testing Clean.\nSTDOUT:\n{generateOut}\nSTDERR:\n{generateErr}");
        Directory.Exists(generatedDir).Should().BeTrue("generated directory must exist after DamlGenerateBindings");
        Directory.GetFiles(generatedDir, "*.cs", SearchOption.AllDirectories).Should().NotBeEmpty(
            "at least one .cs file must be present before cleaning");

        var (cleanExit, cleanOut, cleanErr) = Build("--target Clean --verbosity minimal");
        cleanExit.Should().Be(0,
            $"Clean target must succeed.\nSTDOUT:\n{cleanOut}\nSTDERR:\n{cleanErr}");

        Directory.Exists(generatedDir).Should().BeFalse(
            "DamlClean must RemoveDir the generated directory after Clean");
    }

    [Fact]
    public void DamlGenerateBindings_cli_build_with_no_input_change_skips_generation()
    {
        WriteFixtureConsumerProject();

        var buildArgs = "--target DamlGenerateBindings --verbosity minimal";

        var (firstExit, firstOut, firstErr) = Build(buildArgs);
        firstExit.Should().Be(0,
            $"first DamlGenerateBindings must succeed.\nSTDOUT:\n{firstOut}\nSTDERR:\n{firstErr}");
        firstOut.Should().Contain("Generating Daml C# bindings...");

        var (secondExit, secondOut, secondErr) = Build(buildArgs);
        secondExit.Should().Be(0,
            $"second DamlGenerateBindings must succeed.\nSTDOUT:\n{secondOut}\nSTDERR:\n{secondErr}");
        secondOut.Should().NotContain(
            "Generating Daml C# bindings...",
            "a CLI build with an unchanged DAR and an unchanged codegen version must skip the emitter; " +
            $"the high-importance message appears at minimal verbosity whenever the target runs.\nSTDOUT:\n{secondOut}");
    }

    [Fact]
    public void DamlGenerateBindings_writes_the_codegen_package_version_to_a_stamp_under_the_generated_dir()
    {
        var generatedDir = WriteFixtureConsumerProject();

        var (exit, stdout, stderr) = Build("--target DamlGenerateBindings -p:_DamlCodegenPackageVersion=1.2.3 --verbosity minimal");

        exit.Should().Be(0, $"build must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        var stampLines = File.ReadAllLines(Path.Combine(generatedDir, ".codegen-version"));
        stampLines.Should().Contain("package-version=1.2.3");
    }

    [Theory]
    [InlineData("DamlNamespace", "Acme.One", "Acme.Two")]
    [InlineData("DamlIncludeDependencies", "false", "true")]
    [InlineData("DamlNullable", "true", "false")]
    public void DamlGenerateBindings_changing_an_output_affecting_option_reruns_generation(string property, string first, string second)
    {
        WriteFixtureConsumerProject();

        var (firstExit, firstOut, firstErr) = Build($"--target DamlGenerateBindings -p:{property}={first} --verbosity minimal");
        firstExit.Should().Be(0, $"first build must succeed.\nSTDOUT:\n{firstOut}\nSTDERR:\n{firstErr}");

        var (secondExit, secondOut, secondErr) = Build($"--target DamlGenerateBindings -p:{property}={second} --verbosity minimal");
        secondExit.Should().Be(0, $"second build must succeed.\nSTDOUT:\n{secondOut}\nSTDERR:\n{secondErr}");
        secondOut.Should().Contain(
            "Generating Daml C# bindings...",
            $"changing {property} changes the emitted bindings, so it must invalidate the stamp");
    }

    [Fact]
    public void DamlGenerateBindings_changing_the_codegen_package_version_reruns_generation()
    {
        WriteFixtureConsumerProject();

        var (firstExit, firstOut, firstErr) = Build("--target DamlGenerateBindings -p:_DamlCodegenPackageVersion=1.2.3 --verbosity minimal");
        firstExit.Should().Be(0, $"first build must succeed.\nSTDOUT:\n{firstOut}\nSTDERR:\n{firstErr}");

        var (secondExit, secondOut, secondErr) = Build("--target DamlGenerateBindings -p:_DamlCodegenPackageVersion=1.2.4 --verbosity minimal");
        secondExit.Should().Be(0, $"second build must succeed.\nSTDOUT:\n{secondOut}\nSTDERR:\n{secondErr}");
        secondOut.Should().Contain(
            "Generating Daml C# bindings...",
            "a different codegen package version must invalidate the stamp and rerun the emitter");
    }

    [Fact]
    public void DamlGenerateBindings_touching_the_damlarchive_reruns_generation()
    {
        var darCopy = Path.Combine(ProjectDir, "touched.dar");
        File.Copy(_adapterFixture.FixtureDar, darCopy);
        AssertEmitterIsBuilt();
        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        WriteConsumerProject(generatedDir, [darCopy]);

        var buildArgs = "--target DamlGenerateBindings --verbosity minimal";
        var (firstExit, firstOut, firstErr) = Build(buildArgs);
        firstExit.Should().Be(0, $"first build must succeed.\nSTDOUT:\n{firstOut}\nSTDERR:\n{firstErr}");

        File.SetLastWriteTimeUtc(darCopy, DateTime.UtcNow.AddMinutes(1));

        var (secondExit, secondOut, secondErr) = Build(buildArgs);
        secondExit.Should().Be(0, $"second build must succeed.\nSTDOUT:\n{secondOut}\nSTDERR:\n{secondErr}");
        secondOut.Should().Contain(
            "Generating Daml C# bindings...",
            "a DamlArchive newer than the marker must rerun the emitter");
    }
}
