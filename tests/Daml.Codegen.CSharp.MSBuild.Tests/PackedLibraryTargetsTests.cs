// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Xml.Linq;
using AwesomeAssertions;
using Daml.Codegen.MSBuild.TestSupport;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

public sealed class PackedLibraryTargetsFixture : IAsyncLifetime
{
    private const string AnchorProjectDirectoryName = "Daml.Codegen.CSharp.MSBuild";
    private const string UmbrellaPackageId = "Daml.Codegen.CSharp.MSBuild";
    private const string TemplatePackageId = "Canton.ProjectTemplates";

    private readonly string _scratchDirectory =
        Path.Combine(Path.GetTempPath(), $"packed-library-targets-{Guid.NewGuid():N}");

    public string RepoRoot { get; } = MsBuildProcessRunner.LocateRepoRoot(AnchorProjectDirectoryName);

    public string PackDirectory => Path.Combine(_scratchDirectory, "nupkg");

    public static IReadOnlyList<string> PublishedLibraryIds() =>
        File.ReadAllLines(Path.Combine(
                MsBuildProcessRunner.LocateRepoRoot(AnchorProjectDirectoryName), "scripts", "published-package-ids.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line != UmbrellaPackageId && line != TemplatePackageId)
            .ToList();

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(PackDirectory);
        foreach (var packageId in PublishedLibraryIds())
        {
            var csproj = Path.Combine(RepoRoot, "src", packageId, $"{packageId}.csproj");
            var artifactsPath = Path.Combine(_scratchDirectory, "artifacts");
            var pack = await MsBuildProcessRunner.RunProcessAsync(
                "dotnet",
                $"pack \"{csproj}\" -c Release --artifacts-path \"{artifactsPath}\" -o \"{PackDirectory}\"",
                RepoRoot);
            if (pack.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"dotnet pack of {packageId} failed.\nSTDOUT:\n{pack.StdOut}\nSTDERR:\n{pack.StdErr}");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_scratchDirectory, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[packed-library-targets cleanup] Could not delete {_scratchDirectory}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[packed-library-targets cleanup] Could not delete {_scratchDirectory}: {ex.Message}"); }
        return ValueTask.CompletedTask;
    }

    public ZipArchive OpenPackage(string packageId)
    {
        var nupkg = Directory.GetFiles(PackDirectory, $"{packageId}.*.nupkg")
            .Single(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).Substring(packageId.Length + 1).Split('.')[0].All(char.IsDigit));
        return ZipFile.OpenRead(nupkg);
    }
}

/// <summary>
/// Packs every shipped library and inspects the packages a consumer restores: each carries an asset
/// for <c>net10.0</c> and one for <c>net11.0</c>, and each target's dependency group names the
/// Microsoft packages of its own line.
/// </summary>
public sealed class PackedLibraryTargetsTests(PackedLibraryTargetsFixture fixture) : IClassFixture<PackedLibraryTargetsFixture>
{
    public static TheoryData<string> PublishedLibraryIds() => new(PackedLibraryTargetsFixture.PublishedLibraryIds());

    [Theory]
    [MemberData(nameof(PublishedLibraryIds))]
    public void PackedLibrary_carries_one_assembly_per_target_framework(string packageId)
    {
        using var archive = fixture.OpenPackage(packageId);

        archive.Entries.Select(entry => entry.FullName)
            .Where(name => name.EndsWith($"/{packageId}.dll", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Should().Equal($"lib/net10.0/{packageId}.dll", $"lib/net11.0/{packageId}.dll");
    }

    [Theory]
    [MemberData(nameof(PublishedLibraryIds))]
    public void PackedLibrary_declares_a_dependency_group_per_target_framework_and_net11_adds_no_package_net10_lacks(string packageId)
    {
        using var archive = fixture.OpenPackage(packageId);
        var groups = DependencyGroups(archive);

        groups.Keys.Order(StringComparer.Ordinal).Should().Equal("net10.0", "net11.0");
        groups["net11.0"].Keys.Should().BeSubsetOf(groups["net10.0"].Keys,
            "the net11.0 group only drops packages the .NET 11 framework already carries");
    }

    [Theory]
    [MemberData(nameof(PublishedLibraryIds))]
    public void PackedLibrary_depends_on_the_microsoft_packages_of_each_targets_own_line(string packageId)
    {
        using var archive = fixture.OpenPackage(packageId);
        var groups = DependencyGroups(archive);

        MicrosoftVersions(groups["net10.0"]).Should().AllSatisfy(
            version => version.Should().StartWith("10.0."), "the net10.0 asset must restore only 10.0 Microsoft packages");
        MicrosoftVersions(groups["net11.0"]).Should().AllSatisfy(
            version => version.Should().StartWith("11.0."), "the net11.0 asset must restore only 11.0 Microsoft packages");
    }

    [Fact]
    public void PackedLibraries_together_depend_on_microsoft_packages_in_both_lines()
    {
        var microsoftVersionsByTarget = PackedLibraryTargetsFixture.PublishedLibraryIds()
            .SelectMany(packageId =>
            {
                using var archive = fixture.OpenPackage(packageId);
                return DependencyGroups(archive)
                    .SelectMany(group => MicrosoftVersions(group.Value).Select(version => (group.Key, version)));
            })
            .ToList();

        microsoftVersionsByTarget.Should().Contain(pair => pair.Key == "net10.0");
        microsoftVersionsByTarget.Should().Contain(pair => pair.Key == "net11.0");
    }

    private static Dictionary<string, Dictionary<string, string>> DependencyGroups(ZipArchive archive)
    {
        var nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);
        return nuspec.Descendants()
            .Where(element => element.Name.LocalName == "group" && element.Parent?.Name.LocalName == "dependencies")
            .ToDictionary(
                group => (string?)group.Attribute("targetFramework") ?? string.Empty,
                group => group.Elements()
                    .Where(dependency => dependency.Name.LocalName == "dependency")
                    .ToDictionary(
                        dependency => (string)dependency.Attribute("id")!,
                        dependency => ((string)dependency.Attribute("version")!).TrimStart('[', '(').Split(',')[0].Trim()));
    }

    private static IEnumerable<string> MicrosoftVersions(Dictionary<string, string> group) =>
        group.Where(dependency => dependency.Key.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                || dependency.Key.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
            .Select(dependency => dependency.Value);
}
