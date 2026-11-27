// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.MSBuild.TestSupport;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.ProjectTemplates.Tests;

public sealed partial class CantonAppTemplateTests(PackedTemplateFixture fixture) : IClassFixture<PackedTemplateFixture>
{
    private const string ProjectName = "Acme.Ledger";

    private void SkipWithoutDpm()
    {
        if (fixture.UnavailableReason is { } reason)
        {
            Assert.Skip(reason);
        }
    }

    [Fact]
    public async Task CantonApp_instantiated_from_the_packed_template_builds_both_projects_in_standalone_mode()
    {
        SkipWithoutDpm();
        var project = await fixture.InstantiateAsync(ProjectName);

        var build = await MsBuildProcessRunner.RunProcessAsync("dotnet", "build", project);

        PackedTemplateFixture.RequireSuccess("dotnet build of the instantiated template", build);
        Directory.GetFiles(Path.Combine(project, "App", "bin"), "*.dar", SearchOption.AllDirectories)
            .Should().NotBeEmpty("the app uploads the DAR it finds beside its assembly");
    }

    [Fact]
    public async Task CantonApp_instantiated_from_the_packed_template_pins_every_sdk_package_to_the_packed_version()
    {
        SkipWithoutDpm();
        var project = await fixture.InstantiateAsync(ProjectName);

        var pinned = XDocument.Load(Path.Combine(project, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .ToDictionary(item => (string)item.Attribute("Include")!, item => (string)item.Attribute("Version")!);

        pinned.Should().Contain("Daml.Codegen.CSharp.MSBuild", fixture.SdkVersion);
        pinned.Should().Contain("Daml.Runtime", fixture.SdkVersion);
        pinned.Should().Contain("Canton.Ledger.Grpc.Client", fixture.SdkVersion);
    }

    [Fact]
    public async Task CantonApp_instantiated_from_the_packed_template_names_the_solution_after_the_chosen_name()
    {
        SkipWithoutDpm();
        var project = await fixture.InstantiateAsync(ProjectName);

        Directory.EnumerateFileSystemEntries(project, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(project, path).Replace('\\', '/'))
            .Should().Contain([
                "Acme.Ledger.slnx",
                "global.json",
                "daml/daml.yaml",
                "Contracts/Contracts.csproj",
                "App/App.csproj",
                "App/Program.cs",
                "App/appsettings.json",
            ]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CantonApp_run_against_localnet_creates_a_greeting_and_reads_it_back_from_the_active_contract_set()
    {
        SkipWithoutDpm();
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL")))
        {
            Assert.Skip("no LocalNet is reachable: CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL is not set");
        }
        var project = await fixture.InstantiateAsync(ProjectName);

        var run = await MsBuildProcessRunner.RunProcessAsync("dotnet", "run --project App", project);

        await RevokeActAsRightOfAuthorAsync(run.StdOut);
        PackedTemplateFixture.RequireSuccess("dotnet run of the instantiated template against LocalNet", run);
        run.StdOut.Should().Contain("Hello from Canton");
    }

    private static async Task RevokeActAsRightOfAuthorAsync(string appOutput)
    {
        var author = AuthorLine().Match(appOutput);
        if (!author.Success)
        {
            return;
        }

        await using var lease = ActAsRightsLease.ForValidator(LocalnetFixture.FromEnvironment());
        await lease.GrantAsync(author.Groups["party"].Value);
    }

    [GeneratedRegex(@"^(?<party>\S+) says: Hello from Canton", RegexOptions.Multiline)]
    private static partial Regex AuthorLine();
}
