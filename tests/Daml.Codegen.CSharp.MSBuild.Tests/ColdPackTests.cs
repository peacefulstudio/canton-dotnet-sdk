// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO.Compression;
using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

/// <summary>
/// Regression coverage for a cold, one-shot <c>dotnet pack</c> of the umbrella
/// package: with the gitignored <c>tools/</c> bundling directory absent (a fresh clone, or
/// after <c>dotnet clean</c>), packing must still publish and bundle both CLIs rather than
/// silently shipping a nupkg with no <c>tools/</c> content.
/// </summary>
[Collection("MSBuild")]
public sealed class ColdPackTests : IDisposable
{
    private readonly MsBuildIntegrationFixture _fx;
    private readonly string _scratchDir;
    private readonly string _packOutputDir;
    private readonly string _toolPublishDir;
    private readonly string _isolatedOutputArgs;

    public ColdPackTests(MsBuildIntegrationFixture fx)
    {
        _fx = fx;
        _scratchDir = Path.Combine(Path.GetTempPath(), $"umbrella-cold-pack-test-{Guid.NewGuid():N}");
        _packOutputDir = Path.Combine(_scratchDir, "nupkg");
        _toolPublishDir = Path.Combine(_scratchDir, "tools");
        Directory.CreateDirectory(_packOutputDir);
        _isolatedOutputArgs =
            $"--artifacts-path \"{Path.Combine(_scratchDir, "artifacts")}\" -p:ToolPublishDir=\"{_toolPublishDir}/\"";
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[cold-pack test cleanup] Could not delete {_scratchDir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[cold-pack test cleanup] Could not delete {_scratchDir}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    private static readonly string[] BundledAssembliesBuiltFromPublishedPackages =
        ["Daml.Runtime.dll", "Daml.Codegen.CSharp.Cli.dll", "Daml.Codegen.DarParser.Cli.dll"];

    private const string BundleDigestArg =
        "-p:DamlCodegenBundleDigest=sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432";

    private string UmbrellaCsproj => Path.Combine(_fx.AdapterProjectDirectory, "Daml.Codegen.CSharp.MSBuild.csproj");

    private string SinglePackedUmbrella() =>
        Directory.GetFiles(_packOutputDir, "Daml.Codegen.CSharp.MSBuild.*.nupkg")
            .Single(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void NoBuildPack_stamps_the_bundle_digest_it_is_given_into_the_package()
    {
        var build = MsBuildProcessRunner.RunDotnet(
            $"build \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} -p:Version=0.0.5-built", _fx.RepoRoot);
        build.ExitCode.Should().Be(0, $"the umbrella must build.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");

        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release --no-build {_isolatedOutputArgs} {BundleDigestArg} -p:PackageVersion=0.0.5-built -o \"{_packOutputDir}\"", _fx.RepoRoot);
        pack.ExitCode.Should().Be(0, $"the release pack runs with --no-build.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");

        using var archive = ZipFile.OpenRead(SinglePackedUmbrella());
        var stamp = archive.GetEntry("build/Daml.Codegen.CSharp.MSBuild.BundleDigest.g.txt");
        stamp.Should().NotBeNull(
            $"Dpm mode pins the bundle by the stamped digest, but the nupkg contained: {string.Join(", ", archive.Entries.Select(e => e.FullName))}");
        using var reader = new StreamReader(stamp!.Open());
        reader.ReadToEnd().Trim().Should().Be("sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432");
    }

    [Fact]
    public void ReleasePack_without_a_bundle_digest_fails_naming_the_property()
    {
        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} -p:ContinuousIntegrationBuild=true -o \"{_packOutputDir}\"", _fx.RepoRoot);

        pack.ExitCode.Should().NotBe(0,
            $"the release pack must not ship a package whose Dpm mode has no digest to pin.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");
        (pack.StdOut + pack.StdErr).Should().Contain("-p:DamlCodegenBundleDigest=sha256:");
        (pack.StdOut + pack.StdErr).Should().Contain("ContinuousIntegrationBuild=true");
        Directory.GetFiles(_packOutputDir, "*.nupkg").Should().BeEmpty();
    }

    [Fact]
    public void Pack_with_a_bundle_digest_that_is_not_sha256_fails()
    {
        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} -p:DamlCodegenBundleDigest=0.7.0 -o \"{_packOutputDir}\"", _fx.RepoRoot);

        pack.ExitCode.Should().NotBe(0, $"a tag is not a digest.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");
        (pack.StdOut + pack.StdErr).Should().Contain("DamlCodegenBundleDigest '0.7.0' is not an OCI digest");
        Directory.GetFiles(_packOutputDir, "*.nupkg").Should().BeEmpty();
    }

    [Fact]
    public void Pack_outside_a_release_ships_no_bundle_digest()
    {
        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} -o \"{_packOutputDir}\"", _fx.RepoRoot);

        pack.ExitCode.Should().Be(0, $"only a ContinuousIntegrationBuild pack is a release pack.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");
        using var archive = ZipFile.OpenRead(SinglePackedUmbrella());
        archive.GetEntry("build/Daml.Codegen.CSharp.MSBuild.BundleDigest.g.txt").Should().BeNull(
            "a CI or local pack's Dpm mode fails with DAML0006 until the consumer sets a digest, rather than pinning by tag");
    }

    [Fact]
    public void ColdPack_one_shot_dotnet_pack_with_tools_dir_absent_bundles_both_clis()
    {
        Directory.Exists(_toolPublishDir).Should().BeFalse("the bundling directory must start absent");

        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} {BundleDigestArg} -o \"{_packOutputDir}\"", _fx.RepoRoot);
        pack.ExitCode.Should().Be(0,
            $"a cold one-shot `dotnet pack` with tools/ absent must publish both bundled CLIs before packing, " +
            $"not glob an empty tools/ directory that was never created.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");

        var nupkg = Directory.GetFiles(_packOutputDir, "Daml.Codegen.CSharp.MSBuild.*.nupkg")
            .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .ToList();
        nupkg.Should().ContainSingle("the cold pack must produce exactly one umbrella .nupkg");

        var snupkg = Directory.GetFiles(_packOutputDir, "Daml.Codegen.CSharp.MSBuild.*.snupkg");
        snupkg.Should().ContainSingle(
            "the cold pack must also produce a non-empty .snupkg — an empty tools/ tree previously left no " +
            "symbols to pack and failed the whole pack with NU5017");

        using var archive = ZipFile.OpenRead(nupkg[0]);
        var toolEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("tools/net10.0/", StringComparison.Ordinal))
            .ToList();
        toolEntries.Should().NotBeEmpty(
            "the packed nupkg must bundle both CLIs under tools/net10.0/ even on a cold, one-shot pack — " +
            $"but it contained: {string.Join(", ", archive.Entries.Select(e => e.FullName))}");
        toolEntries.Should().Contain(entry => entry.FullName.EndsWith("Daml.Codegen.DarParser.Cli.dll", StringComparison.OrdinalIgnoreCase),
            "the dar2intermediate producer must be bundled");
        toolEntries.Should().Contain(entry => entry.FullName.EndsWith("Daml.Codegen.CSharp.Cli.dll", StringComparison.OrdinalIgnoreCase),
            "the emitter CLI must be bundled");
    }

    [Fact]
    public void ColdPack_umbrella_nupkg_carries_the_props_and_targets_both_modes_import()
    {
        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} {BundleDigestArg} -o \"{_packOutputDir}\"", _fx.RepoRoot);
        pack.ExitCode.Should().Be(0, $"the umbrella must pack.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");

        var nupkg = Directory.GetFiles(_packOutputDir, "Daml.Codegen.CSharp.MSBuild.*.nupkg")
            .Single(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));
        using var archive = ZipFile.OpenRead(nupkg);
        archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith("build/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Should().Equal(
                "build/Daml.Codegen.CSharp.MSBuild.BundleDigest.g.txt",
                "build/Daml.Codegen.CSharp.MSBuild.BundleVersion.g.txt",
                "build/Daml.Codegen.CSharp.MSBuild.DpmFloor.g.txt",
                "build/Daml.Codegen.CSharp.MSBuild.Shared.props",
                "build/Daml.Codegen.CSharp.MSBuild.Shared.targets",
                "build/Daml.Codegen.CSharp.MSBuild.props",
                "build/Daml.Codegen.CSharp.MSBuild.targets");
    }

    [Fact]
    public void NoBuildPack_stamps_the_package_version_it_packs_into_the_bundle_version_file()
    {
        var build = MsBuildProcessRunner.RunDotnet(
            $"build \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} -p:Version=0.0.1-built", _fx.RepoRoot);
        build.ExitCode.Should().Be(0, $"the umbrella must build.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");

        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release --no-build {_isolatedOutputArgs} {BundleDigestArg} -p:PackageVersion=0.0.2-packed -o \"{_packOutputDir}\"", _fx.RepoRoot);
        pack.ExitCode.Should().Be(0, $"the release pack runs with --no-build.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");

        var nupkg = Directory.GetFiles(_packOutputDir, "Daml.Codegen.CSharp.MSBuild.0.0.2-packed.nupkg").Single();
        using var archive = ZipFile.OpenRead(nupkg);
        var stamp = archive.GetEntry("build/Daml.Codegen.CSharp.MSBuild.BundleVersion.g.txt");
        stamp.Should().NotBeNull(
            $"Dpm mode reads its bundle pin from the stamp, but the nupkg contained: {string.Join(", ", archive.Entries.Select(e => e.FullName))}");
        using var reader = new StreamReader(stamp!.Open());
        reader.ReadToEnd().Trim().Should().Be("0.0.2-packed");
    }

    [Fact]
    public void NoBuildPack_stamps_the_dpm_floor_from_versions_yaml_into_the_package()
    {
        var build = MsBuildProcessRunner.RunDotnet(
            $"build \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} -p:Version=0.0.4-built", _fx.RepoRoot);
        build.ExitCode.Should().Be(0, $"the umbrella must build.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");

        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release --no-build {_isolatedOutputArgs} {BundleDigestArg} -p:PackageVersion=0.0.4-built -o \"{_packOutputDir}\"", _fx.RepoRoot);
        pack.ExitCode.Should().Be(0, $"the release pack runs with --no-build.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");

        var nupkg = Directory.GetFiles(_packOutputDir, "Daml.Codegen.CSharp.MSBuild.0.0.4-built.nupkg").Single();
        using var archive = ZipFile.OpenRead(nupkg);
        var stamp = archive.GetEntry("build/Daml.Codegen.CSharp.MSBuild.DpmFloor.g.txt");
        stamp.Should().NotBeNull("the Dpm preflight reads its floor from the stamp");
        using var reader = new StreamReader(stamp!.Open());
        reader.ReadToEnd().Trim().Should().Be("1.0.20");
    }

    [Fact]
    public void NoBuildPack_bundles_the_tool_binaries_the_release_build_compiled()
    {
        var build = MsBuildProcessRunner.RunDotnet(
            $"build \"{UmbrellaCsproj}\" -c Release {_isolatedOutputArgs} -p:Version=0.0.3-built", _fx.RepoRoot);
        build.ExitCode.Should().Be(0, $"the umbrella must build.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");

        var pack = MsBuildProcessRunner.RunDotnet(
            $"pack \"{UmbrellaCsproj}\" -c Release --no-build {_isolatedOutputArgs} {BundleDigestArg} -p:PackageVersion=0.0.3-built -o \"{_packOutputDir}\"", _fx.RepoRoot);
        pack.ExitCode.Should().Be(0, $"the release pack runs with --no-build.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");

        var extractDir = Path.Combine(_scratchDir, "extracted");
        ZipFile.ExtractToDirectory(Path.Combine(_packOutputDir, "Daml.Codegen.CSharp.MSBuild.0.0.3-built.nupkg"), extractDir);
        var bundledProductVersions = BundledAssembliesBuiltFromPublishedPackages
            .Select(dll => FileVersionInfo.GetVersionInfo(Path.Combine(extractDir, "tools", "net10.0", dll)).ProductVersion);
        bundledProductVersions.Should().AllSatisfy(version => version.Should().StartWith("0.0.3-built"));
    }
}
