// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Daml.Codegen.CSharp.Cli;
using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

/// <summary>
/// CLI integration tests for the <c>--release-counters &lt;path&gt;</c> wire-up.
/// The publish workflow points the CLI at a JSON store of
/// <see cref="Daml.Codegen.CSharp.Cli.Versioning.JsonReleaseCounterStore"/> generation
/// ordinals and the CLI stamps the 4th NuGet version segment from the store — a
/// codegen-generation ordinal keyed by <c>--codegen-version</c> — rather than from
/// the static <c>--emitter-counter</c> override.
/// </summary>
[Collection("ConsoleRedirection")]
public class CliReleaseCountersTests : IDisposable
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private readonly string _workspace;

    public CliReleaseCountersTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"cli-counters-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string FixtureIntermediate() =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, "intermediate.binpb");

    [Fact]
    public async Task CliReleaseCounters_release_counters_flag_stamps_generation_ordinal_into_generated_csproj()
    {
        var intermediate = FixtureIntermediate();
        File.Exists(intermediate).Should().BeTrue($"fixture proto must ship at {intermediate}");
        var counters = Path.Combine(_workspace, "release-counters.json");

        var exit = await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--codegen-version", "0.2.0-preview.3",
            "--generate-project"
        ]);

        exit.Should().Be(0);
        File.Exists(counters).Should().BeTrue(
            "the CLI must persist the resolved generation ordinal back to the store path");

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(counters, TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("codegen_generations")
            .GetProperty("0.2.0-preview.3").GetInt32().Should().Be(0,
                "a first-seen codegen version in an empty store mints generation ordinal 0");

        FourthSegmentOfGeneratedVersion(_workspace).Should().Be("0",
            "the generated package version's 4th segment is the codegen-generation ordinal");
    }

    [Fact]
    public async Task CliReleaseCounters_release_counters_flag_holds_generation_ordinal_steady_on_re_emission_of_the_same_codegen_version()
    {
        var intermediate = FixtureIntermediate();
        var counters = Path.Combine(_workspace, "release-counters.json");

        (await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--codegen-version", "0.2.0-preview.3",
            "--generate-project"
        ])).Should().Be(0);

        var secondWorkspace = Path.Combine(_workspace, "rerun");
        Directory.CreateDirectory(secondWorkspace);
        (await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", secondWorkspace,
            "--release-counters", counters,
            "--codegen-version", "0.2.0-preview.3",
            "--generate-project"
        ])).Should().Be(0);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(counters, TestContext.Current.CancellationToken));
        var generations = document.RootElement.GetProperty("codegen_generations").EnumerateObject().ToList();
        generations.Should().ContainSingle(
            "re-emitting under the same codegen version must not mint a new ordinal");
        generations[0].Value.GetInt32().Should().Be(0);

        FourthSegmentOfGeneratedVersion(secondWorkspace).Should().Be("0");
    }

    [Fact]
    public async Task CliReleaseCounters_codegen_version_flag_drives_the_generation_key()
    {
        var intermediate = FixtureIntermediate();
        var counters = Path.Combine(_workspace, "release-counters.json");

        (await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--codegen-version", "0.2.0-preview.3",
            "--generate-project"
        ])).Should().Be(0);

        var secondWorkspace = Path.Combine(_workspace, "next-version");
        Directory.CreateDirectory(secondWorkspace);
        (await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", secondWorkspace,
            "--release-counters", counters,
            "--codegen-version", "0.2.0-preview.4",
            "--generate-project"
        ])).Should().Be(0);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(counters, TestContext.Current.CancellationToken));
        var generations = document.RootElement.GetProperty("codegen_generations");
        generations.GetProperty("0.2.0-preview.3").GetInt32().Should().Be(0);
        generations.GetProperty("0.2.0-preview.4").GetInt32().Should().Be(1);

        FourthSegmentOfGeneratedVersion(secondWorkspace).Should().Be("1",
            "a newly-seen codegen version mints the next ordinal and stamps it into the package version");
    }

    [Fact]
    public async Task CliReleaseCounters_release_counters_flag_defaults_the_generation_key_to_the_emitter_version_when_codegen_version_is_omitted()
    {
        var intermediate = FixtureIntermediate();
        var counters = Path.Combine(_workspace, "release-counters.json");

        (await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--generate-project"
        ])).Should().Be(0);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(counters, TestContext.Current.CancellationToken));
        var generations = document.RootElement.GetProperty("codegen_generations").EnumerateObject().ToList();
        generations.Should().ContainSingle(
            "omitting --codegen-version keys the store by the emitter's own version");
        generations[0].Name.Should().Be(ExpectedEmitterGenerationKey(),
            "the fallback keys the generation ordinal by ProjectFileGenerator.EmitterLockstepVersion");
        generations[0].Value.GetInt32().Should().Be(0);

        FourthSegmentOfGeneratedVersion(_workspace).Should().Be("0");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CliReleaseCounters_codegen_version_flag_fails_loudly_when_blank(string blankCodegenVersion)
    {
        var intermediate = FixtureIntermediate();
        var counters = Path.Combine(_workspace, "release-counters.json");

        var exit = await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--codegen-version", blankCodegenVersion
        ]);

        exit.Should().NotBe(0,
            "a blank --codegen-version is rejected at validation rather than keying the store by an empty generation");
    }

    [Fact]
    public async Task CliReleaseCounters_release_counters_flag_fails_loudly_when_intermediate_is_not_provided()
    {
        var counters = Path.Combine(_workspace, "release-counters.json");

        var exit = await Program.Main(
        [
            "--release-counters", counters,
            "-o", _workspace
        ]);

        exit.Should().NotBe(0,
            "--intermediate is a required option, so invoking the CLI without it fails with a non-zero exit code even when --release-counters is supplied");
    }

    [Fact]
    public async Task CliReleaseCounters_codegen_version_flag_fails_loudly_when_release_counters_is_not_provided()
    {
        var intermediate = FixtureIntermediate();

        var exit = await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--codegen-version", "0.2.0-preview.3"
        ]);

        exit.Should().NotBe(0,
            "--codegen-version only keys the release-counter store, so supplying it without --release-counters fails loudly rather than being silently ignored");
    }

    [Fact]
    public async Task CliReleaseCounters_release_counters_flag_pins_dependency_references_to_the_co_produced_version()
    {
        var intermediate = FixtureIntermediate();
        var counters = Path.Combine(_workspace, "release-counters.json");

        (await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--codegen-version", "0.2.0-preview.3",
            "--generate-project"
        ])).Should().Be(0);

        DependencyReferenceVersion(_workspace, "Splice.Api.Token.Metadata.V1").Should().Be("1.0.0.0",
            "--release-counters keys one generation ordinal across a family the release publishes together, so the dependency is co-produced by this run at a version it knows exactly");
    }

    [Fact]
    public async Task CliReleaseCounters_omitting_release_counters_floats_dependency_references_over_published_generations()
    {
        var intermediate = FixtureIntermediate();

        (await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--generate-project"
        ])).Should().Be(0);

        DependencyReferenceVersion(_workspace, "Splice.Api.Token.Metadata.V1").Should().Be("1.0.0.*-*",
            "without --release-counters this run publishes nothing, so it cannot know the dependency's generation ordinal or prerelease tag; pinning the default 1.0.0.0 named a version nobody published and failed restore with NU1103");
    }

    private static string DependencyReferenceVersion(string workspace, string packageId)
    {
        var csproj = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly).Single();
        return XDocument.Load(csproj)
            .Descendants("PackageReference")
            .Single(reference => reference.Attribute("Include")?.Value == packageId)
            .Attribute("Version")!
            .Value;
    }

    private static string FourthSegmentOfGeneratedVersion(string workspace)
    {
        var csproj = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly).Single();
        var version = XDocument.Load(csproj)
            .Descendants("Version")
            .Single()
            .Value;
        var segments = version.Split('.');
        segments.Should().HaveCount(4);
        return segments[3];
    }

    private static string ExpectedEmitterGenerationKey()
    {
        var informational = typeof(ProjectFileGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var metadataSeparator = informational.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator >= 0 ? informational[..metadataSeparator] : informational;
    }
}
