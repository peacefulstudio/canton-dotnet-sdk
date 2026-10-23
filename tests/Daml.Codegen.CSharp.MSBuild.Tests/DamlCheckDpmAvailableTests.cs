// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public class DamlCheckDpmAvailableTests : IDisposable
{
    private readonly MsBuildIntegrationFixture _fx;
    private readonly string _tempDir;
    private readonly string _toolsDir;

    public DamlCheckDpmAvailableTests(MsBuildIntegrationFixture fx)
    {
        _fx = fx;
        _tempDir = Path.Combine(Path.GetTempPath(), $"msbuild-umbrella-dpmcheck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _toolsDir = Path.Combine(_tempDir, "fake-tools");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {_tempDir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {_tempDir}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    private string WriteConsumer(int archiveCount = 1) =>
        ConsumerProjectWriter.Write(_tempDir, _fx.PropsFile, _fx.TargetsFile,
            Path.Combine(_tempDir, "DamlGenerated") + Path.DirectorySeparatorChar,
            Enumerable.Repeat(_fx.FixtureDar, archiveCount).ToList(),
            damlCodegenMode: "Dpm",
            damlCodegenBundleDigest: "sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432");

    [Fact]
    public void DamlCheckDpmAvailable_dpm_missing_fails_the_build_with_actionable_guidance()
    {
        _fx.SeedFakeToolsDir(_toolsDir, includeDpm: false, includeJava: true);
        WriteConsumer();

        var env = new Dictionary<string, string?> { ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir, "dpm") };
        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckDpmAvailable --verbosity minimal", env);

        exit.Should().NotBe(0, $"the build must fail when dpm is not on PATH.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("dpm was not found on PATH",
            $"the error must name the actual problem so a consumer knows what to fix.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("1.0.20", "the error must state the version floor");
        (stdout + stderr).Should().Contain("<DamlCodegenMode>Standalone</DamlCodegenMode>",
            "the error must point at the JDK-free mode this same package ships");
        (stdout + stderr).Should().NotContain("Daml.Codegen.CSharp.MSBuild.Standalone",
            "no separate Standalone package exists to install");
    }

    [Fact]
    public void DamlCheckDpmAvailable_dpm_too_old_fails_the_build_with_actionable_guidance()
    {
        _fx.SeedFakeToolsDir(_toolsDir);
        WriteConsumer();

        var env = new Dictionary<string, string?>
        {
            ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir),
            ["FAKE_DPM_VERSION"] = "1.0.10",
        };
        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckDpmAvailable --verbosity minimal", env);

        exit.Should().NotBe(0, $"the build must fail when dpm is below the version floor.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("dpm 1.0.10 is too old",
            $"the error must name the detected version.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Fact]
    public void DamlCheckDpmAvailable_dpm_one_patch_below_the_floor_fails_naming_the_floor()
    {
        _fx.SeedFakeToolsDir(_toolsDir);
        WriteConsumer();

        var env = new Dictionary<string, string?>
        {
            ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir),
            ["FAKE_DPM_VERSION"] = "1.0.19",
        };
        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckDpmAvailable --verbosity minimal", env);

        exit.Should().NotBe(0, $"1.0.19 predates digest pins.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("dpm 1.0.19 is too old");
        (stdout + stderr).Should().Contain("requires dpm >= 1.0.20");
    }

    [Fact]
    public void DamlCheckDpmAvailable_dpm_exactly_at_the_floor_passes()
    {
        _fx.SeedFakeToolsDir(_toolsDir);
        WriteConsumer();

        var env = new Dictionary<string, string?>
        {
            ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir),
            ["FAKE_DPM_VERSION"] = "1.0.20",
        };
        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckDpmAvailable --verbosity minimal", env);

        exit.Should().Be(0, $"the floor itself is supported.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Fact]
    public void DamlCheckDpmAvailable_floor_is_the_dpm_key_of_versions_yaml_and_the_readme_states_it()
    {
        var floor = System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText(Path.Combine(_fx.RepoRoot, "versions.yaml")),
            @"^dpm:[ \t]*(\d+\.\d+\.\d+)", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value;
        var adapter = _fx.AdapterProjectDirectory;
        var stamp = File.ReadAllText(Path.Combine(adapter, "build", "Daml.Codegen.CSharp.MSBuild.DpmFloor.g.txt")).Trim();

        floor.Should().Be("1.0.20");
        stamp.Should().Be("1.0.20", "the build stamps versions.yaml's dpm key for the targets to read");
        File.ReadAllText(Path.Combine(adapter, "README.md")).Should().Contain("dpm 1.0.20 or later");
    }

    [Fact]
    public void DamlCheckDpmAvailable_dpm_version_is_read_from_the_version_line_not_a_banner_token()
    {
        _fx.SeedFakeToolsDir(_toolsDir);
        WriteConsumer();

        var env = new Dictionary<string, string?>
        {
            ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir),
            ["FAKE_DPM_VERSION"] = "1.0.22",
            ["FAKE_DPM_BANNER"] = "dpm nightly build 0.0.1",
        };
        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckDpmAvailable --verbosity minimal", env);

        exit.Should().Be(0,
            $"the version must be read from the `version:` line (1.0.22), not the first semver in a preceding banner (0.0.1).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().NotContain("is too old",
            $"a version-like token in banner output must not be mistaken for the reported dpm version.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Fact]
    public void DamlCheckDpmAvailable_jdk_missing_fails_the_build_with_actionable_guidance()
    {
        _fx.SeedFakeToolsDir(_toolsDir, includeDpm: true, includeJava: false);
        WriteConsumer();

        var env = new Dictionary<string, string?> { ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir, "java") };
        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckDpmAvailable --verbosity minimal", env);

        exit.Should().NotBe(0, $"the build must fail when no JDK is on PATH.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("No JDK was found on PATH",
            $"the error must name the actual problem.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("<DamlCodegenMode>Standalone</DamlCodegenMode>",
            "the error must point at the JDK-free mode this same package ships");
        (stdout + stderr).Should().NotContain("Daml.Codegen.CSharp.MSBuild.Standalone",
            "no separate Standalone package exists to install");
    }

    [Fact]
    public void DamlCheckDpmAvailable_jdk_missing_is_still_diagnosed_when_the_jdk_shared_a_directory_with_the_shell()
    {
        _fx.SeedFakeToolsDir(_toolsDir, includeDpm: true, includeJava: false);
        WriteConsumer();

        var env = new Dictionary<string, string?> { ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir, "java", "sh") };
        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckDpmAvailable --verbosity minimal", env);

        exit.Should().NotBe(0, $"the build must fail when no JDK is on PATH.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("No JDK was found on PATH",
            "on a merged-/usr Linux the JDK and sh are both /usr/bin entries, so hiding java hides the interpreter " +
            $"Exec starts; the adapter must still reach its own diagnostic rather than report MSB6003.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Fact]
    public void DamlCheckDpmAvailable_dpm_and_jdk_available_passes_the_check()
    {
        _fx.SeedFakeToolsDir(_toolsDir);
        WriteConsumer();

        var env = new Dictionary<string, string?> { ["PATH"] = MsBuildProcessRunner.BuildPath(_toolsDir) };
        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckDpmAvailable --verbosity minimal", env);

        exit.Should().Be(0, $"dpm >= 1.0.20 and a JDK are both on PATH; the check must pass.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Fact]
    public void DamlCheckDpmAvailable_dpm_mode_rejects_more_than_one_damlarchive_item()
    {
        WriteConsumer(archiveCount: 2);

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlBuildDpmArgs --verbosity minimal");

        exit.Should().NotBe(0, $"dpm codegen-cs accepts exactly one --dar.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("exactly one",
            $"the error must explain the single-DAR limitation of the Dpm adapter.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Fact]
    public void DamlCheckDpmAvailable_bundle_version_defaults_to_the_lockstep_package_version()
    {
        var expected = File.ReadAllText(
            Path.Combine(_fx.RepoRoot, "src", "Daml.Codegen.CSharp.MSBuild", "build", "Daml.Codegen.CSharp.MSBuild.BundleVersion.g.txt"))
            .Trim();

        WriteConsumer();

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlWriteDpmPin --verbosity minimal");
        exit.Should().Be(0, $"DamlWriteDpmPin must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var pinFiles = Directory.GetFiles(_tempDir, "daml.yaml", SearchOption.AllDirectories);
        pinFiles.Should().ContainSingle();
        File.ReadAllText(pinFiles[0]).Should().Contain(
            $"oci://ghcr.io/peacefulstudio/dpm-codegen-cs:{expected}",
            "an unset DamlCodegenBundleVersion must default to the lockstep <Version> this package was built with");
    }

    [Fact]
    public void DamlCheckDpmAvailable_bundle_version_explicit_override_is_respected()
    {
        ConsumerProjectWriter.Write(_tempDir, _fx.PropsFile, _fx.TargetsFile,
            Path.Combine(_tempDir, "DamlGenerated") + Path.DirectorySeparatorChar,
            [_fx.FixtureDar],
            damlCodegenBundleVersion: "9.9.9-custom",
            damlCodegenBundleDigest: "sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432",
            damlCodegenMode: "Dpm");

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlWriteDpmPin --verbosity minimal");
        exit.Should().Be(0, $"DamlWriteDpmPin must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var pinFiles = Directory.GetFiles(_tempDir, "daml.yaml", SearchOption.AllDirectories);
        pinFiles.Should().ContainSingle();
        File.ReadAllText(pinFiles[0]).Should().Contain(
            "oci://ghcr.io/peacefulstudio/dpm-codegen-cs:9.9.9-custom",
            "an explicit DamlCodegenBundleVersion override must be respected");
    }
}
