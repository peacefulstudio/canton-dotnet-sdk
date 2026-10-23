// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.MSBuild.TestSupport;

/// <summary>
/// The outcomes the <c>DamlProject</c> item must give in every codegen mode: <c>dotnet build</c>
/// runs <c>dpm build</c> on the Daml project, feeds the DAR to the mode's adapter, and skips the
/// step when none of its inputs changed. An adapter's test class derives from this and supplies
/// how its consumer project is written and which fake tools its build finds on PATH.
/// </summary>
public abstract class DamlProjectStepContractTests : IDisposable
{
    private const string BeforeBuildTarget = "--target BeforeBuild --verbosity minimal";

    private readonly IMsBuildAdapterFixture _adapterFixture;
    private readonly string _toolsDirectory;
    private readonly string _buildLog;

    protected DamlProjectStepContractTests(IMsBuildAdapterFixture adapterFixture)
    {
        _adapterFixture = adapterFixture;
        RootDirectory = Path.Combine(Path.GetTempPath(), $"msbuild-damlproject-test-{Guid.NewGuid():N}");
        ConsumerDirectory = Path.Combine(RootDirectory, "consumer project");
        Directory.CreateDirectory(ConsumerDirectory);
        _toolsDirectory = Path.Combine(RootDirectory, "fake-tools");
        _buildLog = Path.Combine(RootDirectory, "dpm-build.log");
    }

    protected string RootDirectory { get; }

    protected string ConsumerDirectory { get; }

    private string DamlDirectory => Path.Combine(ConsumerDirectory, "daml");

    private string BuiltDar => Path.Combine(ConsumerDirectory, "obj", "Debug", "net10.0", "DamlBuild", "daml.dar");

    private string GeneratedDirectory => Path.Combine(ConsumerDirectory, "obj", "Debug", "net10.0", "DamlGenerated");

    protected abstract void SeedFakeTools(string toolsDirectory);

    private protected abstract void WriteConsumerProject(string projectDirectory, IReadOnlyList<DamlProjectItem> damlProjects);

    public void Dispose()
    {
        try { Directory.Delete(RootDirectory, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {RootDirectory}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {RootDirectory}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    private Dictionary<string, string?> BuildEnvironment(string path) => new()
    {
        ["PATH"] = path,
        ["FAKE_DPM_BUILD_DAR"] = _adapterFixture.FixtureDar,
        ["FAKE_DPM_BUILD_LOG"] = _buildLog,
    };

    private Dictionary<string, string?> EnvironmentWithFakeTools()
    {
        SeedFakeTools(_toolsDirectory);
        return BuildEnvironment(MsBuildProcessRunner.BuildPath(_toolsDirectory));
    }

    private Dictionary<string, string?> EnvironmentWithoutDpm() =>
        BuildEnvironment(MsBuildProcessRunner.BuildPath(null, "dpm"));

    private (int ExitCode, string StdOut, string StdErr) Build(IReadOnlyDictionary<string, string?> environment, string arguments = BeforeBuildTarget) =>
        MsBuildProcessRunner.RunDotnetBuild(ConsumerDirectory, arguments, environment);

    private void WriteDamlProject(params string[] extraFiles)
    {
        Directory.CreateDirectory(DamlDirectory);
        WriteAged(Path.Combine(DamlDirectory, "daml.yaml"), "name: fake\n");
        WriteAged(Path.Combine(DamlDirectory, "Main.daml"), "module Main where\n");
        foreach (var relativePath in extraFiles)
        {
            var path = Path.Combine(DamlDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAged(path, "placeholder\n");
        }
    }

    private static void WriteAged(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
    }

    private static void Touch(string path) => File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

    private int DpmBuildCount => File.Exists(_buildLog) ? File.ReadAllLines(_buildLog).Length : 0;

    private void WriteDefaultConsumerProject() =>
        WriteConsumerProject(ConsumerDirectory, [new DamlProjectItem("daml")]);

    [Fact]
    public void DamlProject_build_runs_dpm_build_and_feeds_the_dar_to_codegen()
    {
        WriteDamlProject();
        WriteDefaultConsumerProject();

        var (exit, stdout, stderr) = Build(EnvironmentWithFakeTools());

        exit.Should().Be(0, $"dotnet build must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.ReadAllBytes(BuiltDar).Should().Equal(File.ReadAllBytes(_adapterFixture.FixtureDar),
            "dpm build writes the project's DAR to obj/<tfm>/DamlBuild/<directory name>.dar");
        DpmBuildCount.Should().Be(1);
        Directory.GetFiles(GeneratedDirectory, "*.cs", SearchOption.AllDirectories).Should().NotBeEmpty(
            "the adapter generates typed C# from the DAR the step built");
    }

    [Fact]
    public void DamlProject_no_op_rebuild_skips_dpm_build()
    {
        WriteDamlProject();
        WriteDefaultConsumerProject();
        var environment = EnvironmentWithFakeTools();
        Build(environment).ExitCode.Should().Be(0);

        var (exit, stdout, stderr) = Build(environment);

        exit.Should().Be(0, $"the second build must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        DpmBuildCount.Should().Be(1, "nothing changed, so the incremental build must not run dpm build again");
    }

    [Theory]
    [InlineData("daml/Main.daml")]
    [InlineData("daml/daml.yaml")]
    [InlineData("daml/vendor/prebuilt.dar")]
    [InlineData("dependencies/lib.dar")]
    public void DamlProject_changing_an_input_reruns_dpm_build(string changedFile)
    {
        WriteDamlProject("vendor/prebuilt.dar");
        Directory.CreateDirectory(Path.Combine(ConsumerDirectory, "dependencies"));
        WriteAged(Path.Combine(ConsumerDirectory, "dependencies", "lib.dar"), "dependency\n");
        WriteConsumerProject(ConsumerDirectory, [new DamlProjectItem("daml", ["dependencies/lib.dar"])]);
        var environment = EnvironmentWithFakeTools();
        Build(environment).ExitCode.Should().Be(0);

        Touch(Path.Combine(ConsumerDirectory, changedFile));
        var (exit, stdout, stderr) = Build(environment);

        exit.Should().Be(0, $"the rebuild must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        DpmBuildCount.Should().Be(2, $"touching {changedFile} must rerun dpm build");
    }

    [Theory]
    [InlineData("daml/.daml/dist/cache.dar")]
    [InlineData("daml/.daml/scratch/Cached.daml")]
    public void DamlProject_changing_a_file_under_the_dpm_build_directory_does_not_rerun_dpm_build(string changedFile)
    {
        WriteDamlProject(".daml/dist/cache.dar", ".daml/scratch/Cached.daml");
        WriteDefaultConsumerProject();
        var environment = EnvironmentWithFakeTools();
        Build(environment).ExitCode.Should().Be(0);

        Touch(Path.Combine(ConsumerDirectory, changedFile));
        var (exit, stdout, stderr) = Build(environment);

        exit.Should().Be(0, $"the rebuild must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        DpmBuildCount.Should().Be(1, $"{changedFile} is dpm's own scratch space, not a project input");
    }

    [Fact]
    public void DamlProject_missing_dpm_fails_with_DAML0001_naming_the_project_directory()
    {
        WriteDamlProject();
        WriteDefaultConsumerProject();

        var (exit, stdout, stderr) = Build(EnvironmentWithoutDpm());

        exit.Should().NotBe(0, $"the build must fail when dpm is not on PATH.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        var output = stdout + stderr;
        output.Should().Contain("error DAML0001");
        output.Should().Contain(DamlDirectory, "the message must name the Daml project directory");
        DpmBuildCount.Should().Be(0);
    }

    [Fact]
    public void DamlProject_failed_dpm_build_fails_with_DAML0002_naming_the_project_directory_and_exit_code()
    {
        WriteDamlProject();
        WriteDefaultConsumerProject();
        var environment = EnvironmentWithFakeTools();
        environment["FAKE_DPM_BUILD_EXIT"] = "3";

        var (exit, stdout, stderr) = Build(environment);

        exit.Should().NotBe(0, $"the build must fail when dpm build fails.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        var output = stdout + stderr;
        output.Should().Contain("error DAML0002");
        output.Should().Contain(DamlDirectory, "the message must name the Daml project directory");
        output.Should().Contain("exit code 3");
    }

    [Fact]
    public void DamlProject_directory_without_daml_yaml_fails_with_DAML0004()
    {
        Directory.CreateDirectory(DamlDirectory);
        WriteDefaultConsumerProject();

        var (exit, stdout, stderr) = Build(EnvironmentWithFakeTools());

        exit.Should().NotBe(0, $"the build must fail without a daml.yaml.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        var output = stdout + stderr;
        output.Should().Contain("error DAML0004");
        output.Should().Contain(DamlDirectory);
        DpmBuildCount.Should().Be(0);
    }

    [Fact]
    public void DamlProject_more_than_one_item_fails_with_DAML0003()
    {
        WriteDamlProject();
        WriteConsumerProject(ConsumerDirectory, [new DamlProjectItem("daml"), new DamlProjectItem("other")]);

        var (exit, stdout, stderr) = Build(EnvironmentWithFakeTools());

        exit.Should().NotBe(0, $"the build must fail with two Daml projects.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("error DAML0003");
        DpmBuildCount.Should().Be(0);
    }

    [Fact]
    public void DamlProject_building_the_project_needs_dpm_but_no_java()
    {
        WriteDamlProject();
        WriteDefaultConsumerProject();
        var dpmOnlyToolsDirectory = Path.Combine(RootDirectory, "dpm-only-tools");
        _adapterFixture.SeedFakeDpm(dpmOnlyToolsDirectory);
        var environment = BuildEnvironment(MsBuildProcessRunner.BuildPath(dpmOnlyToolsDirectory, "java"));

        var (exit, stdout, stderr) = Build(environment, "--target DamlBuildProject --verbosity minimal");

        exit.Should().Be(0, $"building a Daml project must not require a JDK.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.Exists(BuiltDar).Should().BeTrue();
    }

    [Fact]
    public void DamlProject_clean_removes_the_built_dar_and_the_generated_bindings()
    {
        WriteDamlProject();
        WriteDefaultConsumerProject();
        var environment = EnvironmentWithFakeTools();
        Build(environment).ExitCode.Should().Be(0);

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnet("clean", ConsumerDirectory, environment);

        exit.Should().Be(0, $"dotnet clean must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Directory.Exists(Path.GetDirectoryName(BuiltDar)).Should().BeFalse("dotnet clean removes obj/<tfm>/DamlBuild");
        Directory.Exists(GeneratedDirectory).Should().BeFalse("dotnet clean removes the generated bindings of a Daml project too");
    }
}
