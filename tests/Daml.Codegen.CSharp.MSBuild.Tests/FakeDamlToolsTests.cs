// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public class FakeDamlToolsTests : IDisposable
{
    private const string MarkerSource = "namespace FakeDpmGenerated { public sealed class Marker { } }\n";

    private readonly MsBuildIntegrationFixture _fx;
    private readonly string _tempDir;
    private readonly string _toolsDir;

    public FakeDamlToolsTests(MsBuildIntegrationFixture fx)
    {
        _fx = fx;
        _tempDir = Path.Combine(Path.GetTempPath(), $"msbuild-umbrella-faketools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _toolsDir = Path.Combine(_tempDir, "fake-tools");
        _fx.SeedFakeToolsDir(_toolsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {_tempDir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {_tempDir}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    private Task<(int ExitCode, string StdOut, string StdErr)> RunDpmAsync(
        string args, IReadOnlyDictionary<string, string?>? environment = null) =>
        MsBuildProcessRunner.RunProcessAsync(
            MsBuildIntegrationFixture.SeededToolPath(_toolsDir, "dpm"), args, _tempDir, environment);

    private Task<(int ExitCode, string StdOut, string StdErr)> RunJavaAsync(string args) =>
        MsBuildProcessRunner.RunProcessAsync(
            MsBuildIntegrationFixture.SeededToolPath(_toolsDir, "java"), args, _tempDir);

    private Task<(int ExitCode, string StdOut, string StdErr)> RunCodegenAsync(
        string args, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var autoInstallEnvironment = new Dictionary<string, string?> { ["DPM_AUTO_INSTALL"] = "true" };
        foreach (var (key, value) in environment ?? new Dictionary<string, string?>())
        {
            autoInstallEnvironment[key] = value;
        }
        return RunDpmAsync(args, autoInstallEnvironment);
    }

    private void WritePin() => WritePin(
        "oci://ghcr.io/peacefulstudio/dpm-codegen-cs:1.2.3@sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432");

    private void WritePin(string component) =>
        File.WriteAllText(Path.Combine(_tempDir, "daml.yaml"), $"components:\n  - \"{component}\"\n");

    [Fact]
    public async Task FakeDamlTools_dpm_reports_the_default_version()
    {
        var (exit, stdout, stderr) = await RunDpmAsync("--version");

        exit.Should().Be(0, $"`dpm --version` must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        stdout.ReplaceLineEndings("\n").Should().Be("version: 1.0.22\nbuild: b65a815\nbuildDate: b65a815\n",
            "an unset FAKE_DPM_VERSION must print byte for byte what dpm 1.0.22 prints for `dpm --version`");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_prints_the_banner_before_the_version_line()
    {
        var env = new Dictionary<string, string?>
        {
            ["FAKE_DPM_VERSION"] = "1.0.10",
            ["FAKE_DPM_BANNER"] = "dpm nightly build 0.0.1",
        };

        var (exit, stdout, stderr) = await RunDpmAsync("--version", env);

        exit.Should().Be(0, $"`dpm --version` must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        var lines = stdout.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(4).And.HaveElementAt(0, "dpm nightly build 0.0.1")
            .And.HaveElementAt(1, "version: 1.0.10",
                "the banner must precede the version line so the adapter's line-anchored parse has something to reject");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_codegen_cs_without_a_pin_in_the_working_directory_exits_5()
    {
        var (exit, _, stderr) = await RunCodegenAsync(
            $"codegen-cs --dar \"{_fx.FixtureDar}\" --out \"{Path.Combine(_tempDir, "out")}\"");

        exit.Should().Be(5, "a missing daml.yaml component pin is how the fake proves the adapter set its working directory");
        stderr.Should().Contain("no daml.yaml component pin found");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_codegen_cs_with_a_missing_dar_exits_1()
    {
        WritePin();

        var (exit, _, stderr) = await RunCodegenAsync(
            $"codegen-cs --dar \"{Path.Combine(_tempDir, "does-not-exist.dar")}\" --out \"{Path.Combine(_tempDir, "out")}\"");

        exit.Should().Be(1, "a DAR that does not exist must fail the codegen invocation");
        stderr.Should().Contain("DAR not found");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_codegen_cs_without_an_out_flag_exits_2()
    {
        WritePin();

        var (exit, _, stderr) = await RunCodegenAsync($"codegen-cs --dar \"{_fx.FixtureDar}\"");

        exit.Should().Be(2, "--out is required by `dpm codegen-cs`");
        stderr.Should().Contain("--out is required");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_codegen_cs_writes_the_marker_source_under_the_out_directory()
    {
        WritePin();
        var outputDir = Path.Combine(_tempDir, "out");

        var (exit, stdout, stderr) = await RunCodegenAsync(
            $"codegen-cs --dar \"{_fx.FixtureDar}\" --out \"{outputDir}\" -- -v 1 --nullable false");

        exit.Should().Be(0, $"a well-formed invocation must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.ReadAllText(Path.Combine(outputDir, "Marker.cs")).Should().Be(MarkerSource,
            "the emitted marker is what the adapter's generated-file assertions look for, byte for byte on every platform");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_codegen_cs_refuses_a_pin_without_a_sha256_digest_with_exit_6()
    {
        WritePin("oci://ghcr.io/peacefulstudio/dpm-codegen-cs:1.2.3");

        var (exit, _, stderr) = await RunCodegenAsync(
            $"codegen-cs --dar \"{_fx.FixtureDar}\" --out \"{Path.Combine(_tempDir, "out")}\"");

        exit.Should().Be(6, "a tag-only pin lets dpm's name-keyed cache pick the emitter, so the fake refuses it");
        stderr.Should().Contain("fake-dpm: the daml.yaml component pin carries no @sha256: digest");
        File.Exists(Path.Combine(_tempDir, "out", "Marker.cs")).Should().BeFalse();
    }

    [Fact]
    public async Task FakeDamlTools_dpm_codegen_cs_without_auto_install_refuses_the_uninstalled_component_with_exit_7()
    {
        WritePin();

        var (exit, _, stderr) = await RunDpmAsync(
            $"codegen-cs --dar \"{_fx.FixtureDar}\" --out \"{Path.Combine(_tempDir, "out")}\"",
            new Dictionary<string, string?> { ["DPM_AUTO_INSTALL"] = null });

        exit.Should().Be(7, "dpm 1.0.20 and later refuse a component that is not installed unless DPM_AUTO_INSTALL is true");
        stderr.Should().Contain("is currently not installed");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_codegen_cs_logs_the_dpm_home_it_ran_under()
    {
        WritePin();
        var codegenLog = Path.Combine(_tempDir, "codegen.log");
        var env = new Dictionary<string, string?>
        {
            ["DPM_HOME"] = "/dpm-homes/1.2.3",
            ["FAKE_DPM_CODEGEN_LOG"] = codegenLog,
        };

        var (exit, stdout, stderr) = await RunCodegenAsync(
            $"codegen-cs --dar \"{_fx.FixtureDar}\" --out \"{Path.Combine(_tempDir, "out")}\"", env);

        exit.Should().Be(0, $"a well-formed invocation must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.ReadAllLines(codegenLog).Should().Equal("DPM_HOME=/dpm-homes/1.2.3");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_version_subcommand_lists_the_active_sdk_as_dpm_does()
    {
        var (exit, stdout, stderr) = await RunDpmAsync("version");

        exit.Should().Be(0, $"`dpm version` must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        stdout.Should().Contain("* 3.6.0",
            "`dpm version` lists SDK versions, with `*` marking the active one, not the dpm version");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_build_writes_the_configured_dar_to_the_output_path_and_logs_the_invocation()
    {
        File.WriteAllText(Path.Combine(_tempDir, "daml.yaml"), "name: fake\n");
        var outputDirectory = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDirectory);
        var outputDar = Path.Combine(outputDirectory, "fake.dar");
        var buildLog = Path.Combine(_tempDir, "build.log");
        var env = new Dictionary<string, string?>
        {
            ["FAKE_DPM_BUILD_DAR"] = _fx.FixtureDar,
            ["FAKE_DPM_BUILD_LOG"] = buildLog,
        };

        var (exit, stdout, stderr) = await RunDpmAsync($"build -o \"{outputDar}\"", env);

        exit.Should().Be(0, $"a well-formed `dpm build` must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.ReadAllBytes(outputDar).Should().Equal(File.ReadAllBytes(_fx.FixtureDar));
        File.ReadAllLines(buildLog).Should().Equal("build");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_build_without_a_daml_yaml_in_the_working_directory_exits_5()
    {
        var env = new Dictionary<string, string?> { ["FAKE_DPM_BUILD_DAR"] = _fx.FixtureDar };

        var (exit, _, stderr) = await RunDpmAsync($"build -o \"{Path.Combine(_tempDir, "x.dar")}\"", env);

        exit.Should().Be(5, "a missing daml.yaml is how the fake proves the target ran dpm build from the Daml project directory");
        stderr.Should().Contain("no daml.yaml found");
    }

    [Fact]
    public async Task FakeDamlTools_dpm_build_exits_with_the_configured_failure_code_and_writes_no_dar()
    {
        File.WriteAllText(Path.Combine(_tempDir, "daml.yaml"), "name: fake\n");
        var outputDar = Path.Combine(_tempDir, "failed.dar");
        var env = new Dictionary<string, string?>
        {
            ["FAKE_DPM_BUILD_DAR"] = _fx.FixtureDar,
            ["FAKE_DPM_BUILD_EXIT"] = "3",
        };

        var (exit, _, stderr) = await RunDpmAsync($"build -o \"{outputDar}\"", env);

        exit.Should().Be(3);
        stderr.Should().Contain("fake-dpm: build failed");
        File.Exists(outputDar).Should().BeFalse();
    }

    [Fact]
    public async Task FakeDamlTools_dpm_rejects_an_unrecognized_invocation_with_exit_64()
    {
        var (exit, _, stderr) = await RunDpmAsync("publish --everything");

        exit.Should().Be(64, "an invocation the fake does not implement must fail loudly rather than silently succeed");
        stderr.Should().Contain("fake-dpm: unrecognized invocation: publish --everything");
    }

    [Fact]
    public async Task FakeDamlTools_java_writes_its_version_to_stderr_and_exits_zero()
    {
        var (exit, stdout, stderr) = await RunJavaAsync("-version");

        exit.Should().Be(0, "the adapter's JDK-presence check reads the exit code of `java -version`");
        stderr.Should().Contain("fake-java version", "a real java writes its version banner to stderr, not stdout");
        stdout.Should().BeEmpty("nothing must reach stdout, matching a real java");
    }

    [Fact]
    public async Task FakeDamlTools_java_rejects_an_unrecognized_invocation_with_exit_64()
    {
        var (exit, _, stderr) = await RunJavaAsync("-jar helper.jar");

        exit.Should().Be(64, "an invocation the fake does not implement must fail loudly rather than silently succeed");
        stderr.Should().Contain("fake-java: unrecognized invocation: -jar helper.jar");
    }

    [Fact]
    public void FakeDamlTools_seeded_tools_are_visible_to_the_path_resolution_predicate()
    {
        MsBuildProcessRunner.ResolvesFrom(_toolsDir, "dpm").Should().BeTrue(
            "the seeded dpm must carry a name this platform resolves from PATH, or the tests that hide dpm would hide nothing");
        MsBuildProcessRunner.ResolvesFrom(_toolsDir, "java").Should().BeTrue(
            "the seeded java must carry a name this platform resolves from PATH");
        MsBuildProcessRunner.ResolvesFrom(_toolsDir, "javac").Should().BeFalse(
            "a tool that was never seeded must not resolve, or the predicate would report every name as present");
    }

    [Fact]
    public async Task FakeDamlTools_resolve_from_PATH_by_bare_name_through_the_platform_shell()
    {
        var env = new Dictionary<string, string?> { ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir) };

        var (dpmExit, dpmOut, dpmErr) = await MsBuildProcessRunner.RunThroughPlatformShellAsync(
            "dpm --version", _tempDir, env);
        dpmExit.Should().Be(0,
            $"the adapter runs `dpm --version` as a bare command, so the seeded fake must resolve from PATH unmodified.\nSTDOUT:\n{dpmOut}\nSTDERR:\n{dpmErr}");
        dpmOut.Should().Contain("version: 1.0.22");

        var (javaExit, javaOut, javaErr) = await MsBuildProcessRunner.RunThroughPlatformShellAsync(
            "java -version", _tempDir, env);
        javaExit.Should().Be(0,
            $"the adapter runs `java -version` as a bare command.\nSTDOUT:\n{javaOut}\nSTDERR:\n{javaErr}");
        javaErr.Should().Contain("fake-java version");
    }
}
