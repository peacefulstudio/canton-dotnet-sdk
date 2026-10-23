// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.Tests.Shared;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Guards the committed corpus DARs under <c>conformance/</c> against being swapped or rebuilt
/// unnoticed, on three axes:
/// <list type="bullet">
/// <item>every <c>.dar</c> on disk has a <b>literal</b> main package id pinned in
/// <c>PinnedMainPackageIds</c> (and no pin names a missing DAR), so a swapped DAR shows up as a
/// reviewed diff of the pin; no id is derived at test time;</item>
/// <item>for the shipped conformance packages, the generated bindings' package <b>id</b> equals the
/// id read fresh from the DAR through <see cref="DarArchive"/> (the same DALF-hash computation damlc
/// uses), which catches a rebuild that skipped <c>scripts/regenerate.sh write --only conformance</c>;</item>
/// <item>the main package <b>name</b> equals the content-addressed name
/// (<c>scripts/set-conformance-package-name.sh</c>), which catches a rebuild that skipped
/// re-deriving the name and so reintroduces the fixed-name collision content addressing prevents.</item>
/// </list>
/// </summary>
public sealed class ConformancePackageIdentityDriftTests
{
    private static async Task<DamlPackage> BuiltDarMainPackageAsync(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{fixtureName}.dar");
        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);
        return archive.MainPackage;
    }

    private static string ExpectedContentAddressedName(string package, string baseName)
    {
        var repoRoot = LocateRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "set-conformance-package-name.sh");
        var psi = new ProcessStartInfo(BashExecutable.Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(scriptPath.Replace('\\', '/'));
        psi.ArgumentList.Add("--package");
        psi.ArgumentList.Add(package);
        psi.ArgumentList.Add("--base");
        psi.ArgumentList.Add(baseName);
        psi.ArgumentList.Add("--print");

        using var proc = Process.Start(psi)!;
        var stdOut = proc.StandardOutput.ReadToEnd();
        var stdErr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException($"set-conformance-package-name.sh --print did not exit within 60 seconds (package: {package}).");
        }
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"set-conformance-package-name.sh --print exited {proc.ExitCode} for package '{package}': {stdErr}");
        }
        return stdOut.Trim();
    }

    private static string LocateRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(current.FullName, "scripts")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException(
            $"Cannot locate repo root from {AppContext.BaseDirectory}. Expected a Directory.Build.props alongside scripts/.");
    }

    private static readonly IReadOnlyDictionary<string, string> PinnedMainPackageIds =
        new Dictionary<string, string>
        {
            ["contractkeys/contractkeys.dar"] = "2bf1bcfec97eb3d0466d5fc85bc58d65c7854a5d4e9d5b88401d9c33833d86e2",
            ["crossmodulecollision/crossmodulecollision.dar"] = "3dd99d252736a4342a35f264ecc67c45d2f5f90a4a6d1fa63c094ef6245f7ae4",
            ["defaulttarget/defaulttarget.dar"] = "7897bdaaa6fd92d791907246106dad01515e99cd3be473e404c455b47373eb21",
            ["nonserializable/nonserializable.dar"] = "9335f002ce4daef5836dd88ff655503429e9cfff718ec8c9f3a1082494cbd966",
            ["richtypes/richtypes.dar"] = "2f0d55f302337c8a8a0497a00bc255f8e7a30aa52c783719dd5a3bdd486904d4",
        };

    public static TheoryData<string, string> PinnedDars()
    {
        var data = new TheoryData<string, string>();
        foreach (var (relativePath, packageId) in PinnedMainPackageIds)
        {
            data.Add(relativePath, packageId);
        }
        return data;
    }

    private static SortedSet<string> CommittedDarsAmong(IEnumerable<string> relativePaths) =>
        new(
            relativePaths.Where(path => !path.Split('/').Contains(".daml", StringComparer.Ordinal)),
            StringComparer.Ordinal);

    private static SortedSet<string> ConformanceDarsOnDisk()
    {
        var conformanceRoot = Path.Combine(LocateRepoRoot(), "conformance");
        return CommittedDarsAmong(
            Directory.EnumerateFiles(conformanceRoot, "*.dar", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(conformanceRoot, path).Replace('\\', '/')));
    }

    [Fact]
    public void Dpm_build_output_under_a_dot_daml_directory_is_not_a_committed_corpus_dar()
    {
        var relativePaths = new[]
        {
            "richtypes/richtypes.dar",
            "richtypes/.daml/dist/richtypes-0.0.1.dar",
            "nested/pkg/.daml/dist/pkg.dar",
            "contractkeys/contractkeys.dar",
        };

        CommittedDarsAmong(relativePaths).Should().Equal("contractkeys/contractkeys.dar", "richtypes/richtypes.dar");
    }

    [Fact]
    public void Every_conformance_dar_on_disk_has_a_pinned_package_id()
    {
        var unpinned = ConformanceDarsOnDisk().Except(PinnedMainPackageIds.Keys, StringComparer.Ordinal).ToList();

        unpinned.Should().BeEmpty(
            "every committed corpus DAR needs a literal main package id in PinnedMainPackageIds, so that "
            + "swapping a DAR against untouched Daml sources shows up as a reviewed diff");
    }

    [Fact]
    public void Every_pinned_package_id_names_a_conformance_dar_still_on_disk()
    {
        var stale = PinnedMainPackageIds.Keys.Except(ConformanceDarsOnDisk(), StringComparer.Ordinal).ToList();

        stale.Should().BeEmpty(
            "a pin whose DAR was deleted or renamed under conformance/ must be removed or moved with it");
    }

    [Theory]
    [MemberData(nameof(PinnedDars))]
    public async Task Conformance_dar_main_package_id_equals_its_pinned_literal(string relativePath, string pinnedPackageId)
    {
        var path = Path.Combine(LocateRepoRoot(), "conformance", relativePath);
        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.MainPackage.PackageId.Should().Be(
            pinnedPackageId,
            $"conformance/{relativePath} no longer carries the package id pinned in PinnedMainPackageIds; "
            + "if the DAR was rebuilt on purpose, update the pin in the same commit");
    }

    [Fact]
    public async Task RichTypes_generated_package_id_matches_the_built_dar()
    {
        var builtPackage = await BuiltDarMainPackageAsync("richtypes");

        Daml.Codegen.Testing.Conformance.RichTypes.Marker.PackageId.Should().Be(
            builtPackage.PackageId,
            "conformance/richtypes/richtypes.dar was rebuilt (e.g. a template edit) without "
            + "re-running scripts/regenerate.sh write --only conformance, so the generated bindings still carry "
            + "the previous build's package id");
    }

    [Fact]
    public async Task ContractKeys_generated_package_id_matches_the_built_dar()
    {
        var builtPackage = await BuiltDarMainPackageAsync("contractkeys");

        Daml.Codegen.Testing.Conformance.ContractKeys.Account.PackageId.Should().Be(
            builtPackage.PackageId,
            "conformance/contractkeys/contractkeys.dar was rebuilt (e.g. a template edit) without "
            + "re-running scripts/regenerate.sh write --only conformance, so the generated bindings still carry "
            + "the previous build's package id");
    }

    [Fact]
    public async Task RichTypes_built_dar_carries_the_package_name_its_current_sources_hash_to()
    {
        var builtPackage = await BuiltDarMainPackageAsync("richtypes");
        var expectedName = ExpectedContentAddressedName("richtypes", "richtypes");

        builtPackage.Name.Should().Be(
            expectedName,
            "conformance/richtypes/richtypes.dar was rebuilt from sources without first re-running "
            + "scripts/set-conformance-package-name.sh, so its package name no longer matches its "
            + "own content — the exact fixed-name collision content-addressing exists to prevent");
    }

    [Fact]
    public async Task ContractKeys_built_dar_carries_the_package_name_its_current_sources_hash_to()
    {
        var builtPackage = await BuiltDarMainPackageAsync("contractkeys");
        var expectedName = ExpectedContentAddressedName("contractkeys", "contractkeys");

        builtPackage.Name.Should().Be(
            expectedName,
            "conformance/contractkeys/contractkeys.dar was rebuilt from sources without first "
            + "re-running scripts/set-conformance-package-name.sh, so its package name no longer "
            + "matches its own content — the exact fixed-name collision content-addressing exists "
            + "to prevent");
    }

    [Fact]
    public async Task DefaultTarget_generated_package_id_matches_the_built_dar()
    {
        var builtPackage = await BuiltDarMainPackageAsync("defaulttarget");

        Daml.Codegen.Testing.Conformance.DefaultTarget.Note.PackageId.Should().Be(
            builtPackage.PackageId,
            "conformance/defaulttarget/defaulttarget.dar was rebuilt (e.g. a template edit) without "
            + "re-running scripts/regenerate.sh write --only conformance, so the generated bindings still carry "
            + "the previous build's package id");
    }

    [Fact]
    public async Task DefaultTarget_built_dar_carries_the_package_name_its_current_sources_hash_to()
    {
        var builtPackage = await BuiltDarMainPackageAsync("defaulttarget");
        var expectedName = ExpectedContentAddressedName("defaulttarget", "defaulttarget");

        builtPackage.Name.Should().Be(
            expectedName,
            "conformance/defaulttarget/defaulttarget.dar was rebuilt from sources without first "
            + "re-running scripts/set-conformance-package-name.sh, so its package name no longer "
            + "matches its own content — the exact fixed-name collision content-addressing exists "
            + "to prevent");
    }
}
