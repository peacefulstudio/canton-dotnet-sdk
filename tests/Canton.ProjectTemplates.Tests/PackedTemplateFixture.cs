// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using Daml.Codegen.MSBuild.TestSupport;
using Xunit;

namespace Canton.ProjectTemplates.Tests;

public sealed class PackedTemplateFixture : IAsyncLifetime
{
    private const string AnchorProjectDirectoryName = "Canton.ProjectTemplates";
    private const string TemplatePackageId = "Canton.ProjectTemplates";
    private const string DpmTool = "dpm";

    private readonly string _scratchDirectory =
        Path.Combine(Path.GetTempPath(), $"canton-project-templates-{Guid.NewGuid():N}");

    public string RepoRoot { get; } = MsBuildProcessRunner.LocateRepoRoot(AnchorProjectDirectoryName);

    public string FeedDirectory => Path.Combine(_scratchDirectory, "feed");

    public string HiveDirectory => Path.Combine(_scratchDirectory, "hive");

    public string SdkVersion => XDocument
        .Load(Path.Combine(RepoRoot, "Directory.Build.props"))
        .Descendants("Version")
        .Single()
        .Value;

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!DpmIsOnPath())
        {
            UnavailableReason = "dpm is not on PATH, and building the template's Daml project needs it";
            return;
        }

        Directory.CreateDirectory(FeedDirectory);
        foreach (var packageId in PublishedPackageIds())
        {
            var csproj = Path.Combine(RepoRoot, "src", packageId, $"{packageId}.csproj");
            var artifactsPath = Path.Combine(_scratchDirectory, "artifacts");
            var pack = await MsBuildProcessRunner.RunProcessAsync(
                "dotnet",
                $"pack \"{csproj}\" -c Release --artifacts-path \"{artifactsPath}\" -o \"{FeedDirectory}\"",
                RepoRoot);
            RequireSuccess($"dotnet pack of {packageId}", pack);
        }

        var install = await RunDotnetNewAsync($"install \"{TemplatePackagePath()}\"");
        RequireSuccess("dotnet new install of the packed template", install);
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_scratchDirectory, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[project-templates cleanup] Could not delete {_scratchDirectory}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[project-templates cleanup] Could not delete {_scratchDirectory}: {ex.Message}"); }
        return ValueTask.CompletedTask;
    }

    public string TemplatePackagePath() =>
        Directory.GetFiles(FeedDirectory, $"{TemplatePackageId}.{SdkVersion}.nupkg").Single();

    public Task<(int ExitCode, string StdOut, string StdErr)> RunDotnetNewAsync(string arguments, string? workingDirectory = null)
    {
        Directory.CreateDirectory(_scratchDirectory);
        return MsBuildProcessRunner.RunProcessAsync(
            "dotnet",
            $"new {arguments} --debug:custom-hive \"{HiveDirectory}\"",
            workingDirectory ?? _scratchDirectory,
            new Dictionary<string, string?> { ["DOTNET_NOLOGO"] = "1" });
    }

    public async Task<string> InstantiateAsync(string projectName)
    {
        var outputDirectory = Path.Combine(_scratchDirectory, $"instance-{Guid.NewGuid():N}", projectName);
        var instantiate = await RunDotnetNewAsync($"canton-app --name {projectName} --output \"{outputDirectory}\"");
        RequireSuccess($"dotnet new canton-app {projectName}", instantiate);
        File.WriteAllText(Path.Combine(outputDirectory, "NuGet.config"), NuGetConfigWithLocalFeed());
        return outputDirectory;
    }

    private string NuGetConfigWithLocalFeed() =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="local-sdk" value="{FeedDirectory}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
        </configuration>
        """;

    private static IReadOnlyList<string> PublishedPackageIds() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "published-package-ids.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    private static bool DpmIsOnPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => MsBuildProcessRunner.ResolvesFrom(directory, DpmTool));

    internal static void RequireSuccess(string step, (int ExitCode, string StdOut, string StdErr) result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{step} failed with exit code {result.ExitCode}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        }
    }
}
