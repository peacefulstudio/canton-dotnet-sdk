// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.MSBuild.TestSupport;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

public sealed class MsBuildIntegrationFixture : IAsyncLifetime, IMsBuildAdapterFixture
{
    private const string AdapterProjectDirectoryName = "Daml.Codegen.CSharp.MSBuild";

    public string RepoRoot { get; } = MsBuildProcessRunner.LocateRepoRoot(AdapterProjectDirectoryName);
    public string PropsFile => Path.Combine(RepoRoot, "src", AdapterProjectDirectoryName,
        "build", "Daml.Codegen.CSharp.MSBuild.props");
    public string TargetsFile => Path.Combine(RepoRoot, "src", AdapterProjectDirectoryName,
        "build", "Daml.Codegen.CSharp.MSBuild.targets");
    public string FixtureDar => Path.Combine(AppContext.BaseDirectory, "Fixtures", "splice-api-token-holding-v1.dar");
    private string FakeDpmProject => Path.Combine(RepoRoot, "tests", "Fixtures", "FakeDpm");
    public string FakeJavaProject => Path.Combine(RepoRoot, "tests", "Fixtures", "FakeJava");
    public string AdapterProjectDirectory => Path.Combine(RepoRoot, "src", AdapterProjectDirectoryName);
    public string BundledDarParserCliDll => Path.Combine(AdapterProjectDirectory, "tools", "net10.0", "Daml.Codegen.DarParser.Cli.dll");
    public string DarParserCliProject => Path.Combine(RepoRoot, "src", "Daml.Codegen.DarParser.Cli",
        "Daml.Codegen.DarParser.Cli.csproj");
    public string EmitterCliProject => Path.Combine(RepoRoot, "src", "Daml.Codegen.CSharp.Cli",
        "Daml.Codegen.CSharp.Cli.csproj");

    public async ValueTask InitializeAsync()
    {
        await BuildAsync(AdapterProjectDirectory,
            "the umbrella adapter, so it stamps a fresh bundle-version file and bundles both Standalone CLIs");
        await BuildAsync(FakeDpmProject, "the fake dpm executable");
        await BuildAsync(FakeJavaProject, "the fake java executable");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Populates <paramref name="dir"/> with fake tools named exactly as the real
    /// executables ("dpm", "java", plus the platform's executable extension) so PATH-based
    /// resolution of $(DamlCodegenTool)'s default ("dpm codegen-cs") and the JDK-presence
    /// check find them unmodified. Each fake is a .NET console application, so the same
    /// executable and the same contract are exercised on every platform.
    /// </summary>
    public void SeedFakeToolsDir(string dir, bool includeDpm = true, bool includeJava = true)
    {
        Directory.CreateDirectory(dir);
        if (includeDpm)
        {
            FakeToolSeeder.Seed(FakeDpmProject, dir, "dpm");
        }
        if (includeJava)
        {
            FakeToolSeeder.Seed(FakeJavaProject, dir, "java");
        }
    }

    /// <summary>
    /// The path a tool seeded by <see cref="SeedFakeToolsDir"/> occupies in
    /// <paramref name="toolsDir"/>, under the file name the platform requires.
    /// </summary>
    public static string SeededToolPath(string toolsDir, string tool) =>
        FakeToolSeeder.SeededToolPath(toolsDir, tool);

    public void SeedFakeDpm(string toolsDirectory) => SeedFakeToolsDir(toolsDirectory, includeDpm: true, includeJava: false);

    private async Task BuildAsync(string projectDirectory, string description)
    {
        var result = await MsBuildProcessRunner.RunProcessAsync(
            "dotnet", $"build \"{projectDirectory}\" -c Debug", RepoRoot);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to build {description} before integration tests.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        }
    }
}
