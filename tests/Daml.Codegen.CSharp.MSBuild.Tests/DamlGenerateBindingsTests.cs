// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public class DamlGenerateBindingsTests : DamlGenerateBindingsAdapterContractTests
{
    private readonly MsBuildIntegrationFixture _fx;
    private readonly IReadOnlyDictionary<string, string?> _buildEnvironment;

    public DamlGenerateBindingsTests(MsBuildIntegrationFixture fx) : base(fx)
    {
        _fx = fx;
        var toolsDir = Path.Combine(ProjectDir, "fake-tools");
        _fx.SeedFakeToolsDir(toolsDir);
        _buildEnvironment = new Dictionary<string, string?> { ["PATH"] = MsBuildProcessRunner.BuildPath(toolsDir) };
    }

    protected override IReadOnlyDictionary<string, string?>? BuildEnvironment => _buildEnvironment;

    private const string BundleDigest = "sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432";
    private const string OtherBundleDigest = "sha256:9f040219936298f085e2ade77fbbc37829df065b879633282aa4628c9a1b6548";

    protected override void WriteConsumerProject(string generatedDir, IReadOnlyList<string> darPaths) =>
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, generatedDir, darPaths, damlCodegenMode: "Dpm",
            damlCodegenBundleDigest: BundleDigest);

    private string GeneratedDir => Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;

    private string PinFile => Path.Combine(ProjectDir, "obj", "Debug", "net10.0", "daml-codegen-cs-pin", "daml.yaml");

    private IReadOnlyDictionary<string, string?> EnvironmentWith(params (string Key, string? Value)[] variables)
    {
        var environment = new Dictionary<string, string?>(_buildEnvironment);
        foreach (var (key, value) in variables)
        {
            environment[key] = value;
        }
        return environment;
    }

    [Fact]
    public void DamlWriteDpmPin_pins_the_bundle_by_version_and_index_digest()
    {
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, GeneratedDir, [_fx.FixtureDar],
            damlCodegenMode: "Dpm", damlCodegenBundleVersion: "1.2.3", damlCodegenBundleDigest: BundleDigest);

        var (exit, stdout, stderr) = Build("--target DamlWriteDpmPin --verbosity minimal");

        exit.Should().Be(0, $"DamlWriteDpmPin must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.ReadAllText(PinFile).ReplaceLineEndings("\n").Should().Be(
            "components:\n" +
            "- \"oci://ghcr.io/peacefulstudio/dpm-codegen-cs:1.2.3@sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432\"\n",
            "a tag-only pin lets dpm's name-keyed component cache decide which emitter runs");
    }

    [Fact]
    public void DamlWriteDpmPin_without_a_digest_fails_with_DAML0006_and_writes_no_pin()
    {
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, GeneratedDir, [_fx.FixtureDar],
            damlCodegenMode: "Dpm", damlCodegenBundleVersion: "1.2.3");

        var (exit, stdout, stderr) = Build("--target DamlWriteDpmPin --verbosity minimal");

        exit.Should().NotBe(0, $"a digest-less pin must never be written.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("error DAML0006");
        (stdout + stderr).Should().Contain("<DamlCodegenBundleDigest>");
        (stdout + stderr).Should().Contain("oras resolve ghcr.io/peacefulstudio/dpm-codegen-cs:1.2.3");
        File.Exists(PinFile).Should().BeFalse();
    }

    [Fact]
    public void DamlWriteDpmPin_with_a_digest_that_is_not_sha256_fails_with_DAML0007()
    {
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, GeneratedDir, [_fx.FixtureDar],
            damlCodegenMode: "Dpm", damlCodegenBundleVersion: "1.2.3",
            damlCodegenBundleDigest: "sha256:9D056E899E4D1FF071D2B438785650A1E81BDD6C26F613E42D3AFD1081211432");

        var (exit, stdout, stderr) = Build("--target DamlWriteDpmPin --verbosity minimal");

        exit.Should().NotBe(0, $"an OCI digest is lowercase hex.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("error DAML0007");
        File.Exists(PinFile).Should().BeFalse();
    }

    [Fact]
    public void DamlWriteDpmPin_leaves_an_unchanged_pin_untouched_on_the_next_build()
    {
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, GeneratedDir, [_fx.FixtureDar],
            damlCodegenMode: "Dpm", damlCodegenBundleVersion: "1.2.3", damlCodegenBundleDigest: BundleDigest);
        var (firstExit, firstOut, firstErr) = Build("--target DamlWriteDpmPin --verbosity minimal");
        firstExit.Should().Be(0, $"first build must succeed.\nSTDOUT:\n{firstOut}\nSTDERR:\n{firstErr}");
        var earlier = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(PinFile, earlier);

        var (secondExit, secondOut, secondErr) = Build("--target DamlWriteDpmPin --verbosity minimal");

        secondExit.Should().Be(0, $"second build must succeed.\nSTDOUT:\n{secondOut}\nSTDERR:\n{secondErr}");
        File.GetLastWriteTimeUtc(PinFile).Should().Be(earlier,
            "rewriting an unchanged pin on every build would discard whatever dpm records beside it");
    }

    private (string PropsFile, string TargetsFile) WritePackedBuildDirectory(string stampedVersion, string? stampedDigest)
    {
        var packedBuildDir = Path.Combine(ProjectDir, "packed", "build");
        Directory.CreateDirectory(packedBuildDir);
        var sourceBuildDir = Path.GetDirectoryName(_fx.PropsFile)!;
        foreach (var msbuildFile in Directory.GetFiles(sourceBuildDir, "*.props").Concat(Directory.GetFiles(sourceBuildDir, "*.targets")))
        {
            File.Copy(msbuildFile, Path.Combine(packedBuildDir, Path.GetFileName(msbuildFile)));
        }
        File.WriteAllText(Path.Combine(packedBuildDir, "Daml.Codegen.CSharp.MSBuild.BundleVersion.g.txt"), stampedVersion + "\n");
        File.WriteAllText(Path.Combine(packedBuildDir, "Daml.Codegen.CSharp.MSBuild.DpmFloor.g.txt"), "1.0.20\n");
        if (stampedDigest is not null)
        {
            File.WriteAllText(Path.Combine(packedBuildDir, "Daml.Codegen.CSharp.MSBuild.BundleDigest.g.txt"), stampedDigest + "\n");
        }
        return (Path.Combine(packedBuildDir, "Daml.Codegen.CSharp.MSBuild.props"),
            Path.Combine(packedBuildDir, "Daml.Codegen.CSharp.MSBuild.targets"));
    }

    [Fact]
    public void DamlWriteDpmPin_defaults_to_the_digest_stamped_into_the_package_with_its_version()
    {
        var (propsFile, targetsFile) = WritePackedBuildDirectory("1.2.3", BundleDigest);
        ConsumerProjectWriter.Write(ProjectDir, propsFile, targetsFile, GeneratedDir, [_fx.FixtureDar], damlCodegenMode: "Dpm");

        var (exit, stdout, stderr) = Build("--target DamlWriteDpmPin --verbosity minimal");

        exit.Should().Be(0, $"DamlWriteDpmPin must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.ReadAllText(PinFile).Should().Contain(
            "\"oci://ghcr.io/peacefulstudio/dpm-codegen-cs:1.2.3@sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432\"");
    }

    [Fact]
    public void DamlWriteDpmPin_does_not_pair_the_stamped_digest_with_an_overridden_version()
    {
        var (propsFile, targetsFile) = WritePackedBuildDirectory("1.2.3", BundleDigest);
        ConsumerProjectWriter.Write(ProjectDir, propsFile, targetsFile, GeneratedDir, [_fx.FixtureDar],
            damlCodegenMode: "Dpm", damlCodegenBundleVersion: "1.2.4");

        var (exit, stdout, stderr) = Build("--target DamlWriteDpmPin --verbosity minimal");

        exit.Should().NotBe(0,
            $"the stamped digest names the 1.2.3 bundle; pairing it with 1.2.4 would run 1.2.3 under a 1.2.4 pin.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("error DAML0006");
        File.Exists(PinFile).Should().BeFalse();
    }

    [Fact]
    public void DamlWriteDpmPin_in_a_package_packed_without_a_digest_fails_with_DAML0006()
    {
        var (propsFile, targetsFile) = WritePackedBuildDirectory("1.2.3", stampedDigest: null);
        ConsumerProjectWriter.Write(ProjectDir, propsFile, targetsFile, GeneratedDir, [_fx.FixtureDar], damlCodegenMode: "Dpm");

        var (exit, stdout, stderr) = Build("--target DamlWriteDpmPin --verbosity minimal");

        exit.Should().NotBe(0, $"a package packed without a digest must not fall back to a tag pin.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        (stdout + stderr).Should().Contain("error DAML0006");
        File.Exists(PinFile).Should().BeFalse();
    }

    [Fact]
    public void DamlGenerateBindings_runs_dpm_under_a_dpm_home_per_bundle_version()
    {
        var dpmHome = Path.Combine(ProjectDir, "dpm-home");
        var codegenLog = Path.Combine(ProjectDir, "codegen.log");
        var environment = EnvironmentWith(("DPM_HOME", dpmHome), ("FAKE_DPM_CODEGEN_LOG", codegenLog));
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, GeneratedDir, [_fx.FixtureDar], damlCodegenMode: "Dpm");

        var (firstExit, firstOut, firstErr) = MsBuildProcessRunner.RunDotnetBuild(ProjectDir,
            $"--target DamlGenerateBindings -p:DamlCodegenBundleVersion=1.2.3 -p:DamlCodegenBundleDigest={BundleDigest} --verbosity minimal",
            environment);
        firstExit.Should().Be(0, $"first build must succeed.\nSTDOUT:\n{firstOut}\nSTDERR:\n{firstErr}");
        var (secondExit, secondOut, secondErr) = MsBuildProcessRunner.RunDotnetBuild(ProjectDir,
            $"--target DamlGenerateBindings -p:DamlCodegenBundleVersion=1.2.4 -p:DamlCodegenBundleDigest={OtherBundleDigest} --verbosity minimal",
            environment);
        secondExit.Should().Be(0, $"second build must succeed.\nSTDOUT:\n{secondOut}\nSTDERR:\n{secondErr}");

        File.ReadAllLines(codegenLog).Should().Equal(
            [
                $"DPM_HOME={Path.Combine(dpmHome, "dpm-codegen-cs", "1.2.3")}",
                $"DPM_HOME={Path.Combine(dpmHome, "dpm-codegen-cs", "1.2.4")}",
            ],
            "dpm caches a component under its name alone, so two bundle versions sharing one dpm home run whichever emitter was cached first");
    }

    [Fact]
    public void DamlGenerateBindings_without_DPM_HOME_keeps_the_per_version_dpm_home_under_the_user_dpm_directory()
    {
        var codegenLog = Path.Combine(ProjectDir, "codegen.log");
        var environment = EnvironmentWith(("DPM_HOME", null), ("FAKE_DPM_CODEGEN_LOG", codegenLog));
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, GeneratedDir, [_fx.FixtureDar],
            damlCodegenMode: "Dpm", damlCodegenBundleVersion: "1.2.3", damlCodegenBundleDigest: BundleDigest);

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(ProjectDir,
            "--target DamlGenerateBindings --verbosity minimal", environment);

        exit.Should().Be(0, $"build must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        File.ReadAllLines(codegenLog).Should().Equal(
            $"DPM_HOME={Path.Combine(userProfile, ".dpm", "dpm-codegen-cs", "1.2.3")}");
    }

    [Fact]
    public void DamlGenerateBindings_stamp_records_the_bundle_digest()
    {
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, GeneratedDir, [_fx.FixtureDar],
            damlCodegenMode: "Dpm", damlCodegenBundleVersion: "1.2.3", damlCodegenBundleDigest: BundleDigest);

        var (exit, stdout, stderr) = Build("--target DamlGenerateBindings --verbosity minimal");

        exit.Should().Be(0, $"build must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.ReadAllLines(Path.Combine(GeneratedDir, ".codegen-version")).Should().Contain(
            "bundle-digest=sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432",
            "a new digest for the same version must rerun the emitter");
    }

    [Fact]
    public void DamlGenerateBindings_generation_with_default_generateddir_lands_under_the_project_directory_not_the_pin_directory()
    {
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, darPaths: [_fx.FixtureDar], damlCodegenMode: "Dpm",
            damlCodegenBundleDigest: BundleDigest);

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            ProjectDir, "--target DamlGenerateBindings --verbosity minimal", BuildEnvironment);

        exit.Should().Be(0, $"DamlGenerateBindings must succeed with the default DamlGeneratedDir.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var expectedGeneratedDir = Path.Combine(ProjectDir, "obj", "Debug", "net10.0", "DamlGenerated");
        Directory.GetFiles(expectedGeneratedDir, "*.cs", SearchOption.AllDirectories).Should().NotBeEmpty(
            $"the default DamlGeneratedDir must resolve under the project's own intermediate output ({expectedGeneratedDir}), " +
            "not under the dpm pin directory that DamlCodegenToolWorkingDirectory points $(DamlCodegenTool) at");

        Directory.Exists(Path.Combine(ProjectDir, "obj", "Debug", "net10.0", "daml-codegen-cs-pin", "DamlGenerated")).Should().BeFalse(
            "the generated directory must not be nested inside the pin directory");
    }

    [Fact]
    public void DamlGenerateBindings_generation_runs_dpm_from_the_pin_directory_containing_daml_yaml()
    {
        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, generatedDir, [_fx.FixtureDar],
            damlCodegenBundleVersion: "1.2.3", damlCodegenBundleDigest: BundleDigest, damlCodegenMode: "Dpm");

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            ProjectDir, "--target DamlGenerateBindings --verbosity minimal", BuildEnvironment);

        exit.Should().Be(0,
            $"fake-dpm asserts a daml.yaml pin exists in its working directory; a nonzero exit means DamlCodegenToolWorkingDirectory " +
            $"was not wired to where DamlWriteDpmPin wrote it.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var pinFiles = Directory.GetFiles(ProjectDir, "daml.yaml", SearchOption.AllDirectories);
        pinFiles.Should().ContainSingle("DamlWriteDpmPin must write exactly one daml.yaml under the project's intermediate output");
        File.ReadAllText(pinFiles[0]).Should().Contain(
            "oci://ghcr.io/peacefulstudio/dpm-codegen-cs:1.2.3",
            "the pin must reference the default OCI repository at the requested bundle version");
    }

    [Fact]
    public void DamlGenerateBindings_stamp_records_the_bundle_version()
    {
        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, generatedDir, [_fx.FixtureDar],
            damlCodegenBundleVersion: "1.2.3", damlCodegenBundleDigest: BundleDigest, damlCodegenMode: "Dpm");

        var (exit, stdout, stderr) = Build("--target DamlGenerateBindings --verbosity minimal");

        exit.Should().Be(0, $"build must succeed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        File.ReadAllLines(Path.Combine(generatedDir, ".codegen-version")).Should().Contain("bundle-version=1.2.3");
    }

    [Fact]
    public void DamlGenerateBindings_changing_the_bundle_version_reruns_generation()
    {
        var generatedDir = Path.Combine(ProjectDir, "DamlGenerated") + Path.DirectorySeparatorChar;
        ConsumerProjectWriter.Write(ProjectDir, _fx.PropsFile, _fx.TargetsFile, generatedDir, [_fx.FixtureDar], damlCodegenMode: "Dpm", damlCodegenBundleDigest: BundleDigest);

        var (firstExit, firstOut, firstErr) = Build("--target DamlGenerateBindings -p:DamlCodegenBundleVersion=1.2.3 --verbosity minimal");
        firstExit.Should().Be(0, $"first build must succeed.\nSTDOUT:\n{firstOut}\nSTDERR:\n{firstErr}");

        var (secondExit, secondOut, secondErr) = Build("--target DamlGenerateBindings -p:DamlCodegenBundleVersion=1.2.4 --verbosity minimal");
        secondExit.Should().Be(0, $"second build must succeed.\nSTDOUT:\n{secondOut}\nSTDERR:\n{secondErr}");
        secondOut.Should().Contain(
            "Generating Daml C# bindings...",
            "bumping DamlCodegenBundleVersion without changing the DAR must rerun the emitter");
    }
}
