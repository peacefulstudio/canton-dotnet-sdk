// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Cli.Versioning;
using Daml.Codegen.CSharp.Versioning;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

public class NuGetVersionResolverTests : IDisposable
{
    private readonly string _storePath;

    public NuGetVersionResolverTests()
    {
        _storePath = Path.Combine(
            Path.GetTempPath(),
            $"nuget-version-resolver-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_storePath)) File.Delete(_storePath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Compute_stamps_generation_ordinal_from_store()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        var version = NuGetVersionResolver.Compute(
            intrinsicVersion: new Version(0, 1, 17),
            codegenVersion: "0.2.0-preview.3",
            counterStore: store);

        version.Should().Be(new FourPartPackageVersion(0, 1, 17, 0));
        version.ToString().Should().Be("0.1.17.0");
    }

    [Fact]
    public void Compute_yields_same_ordinal_for_different_packages_under_one_codegen_version()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        var amulet = NuGetVersionResolver.Compute(new Version(0, 1, 17), "0.2.0-preview.3", store);
        var holding = NuGetVersionResolver.Compute(new Version(3, 4, 5), "0.2.0-preview.3", store);

        amulet.Should().Be(new FourPartPackageVersion(0, 1, 17, 0));
        holding.Should().Be(new FourPartPackageVersion(3, 4, 5, 0));
    }

    [Fact]
    public void Compute_increments_ordinal_when_codegen_version_changes()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        NuGetVersionResolver.Compute(new Version(0, 1, 17), "0.2.0-preview.3", store)
            .Should().Be(new FourPartPackageVersion(0, 1, 17, 0));
        NuGetVersionResolver.Compute(new Version(0, 1, 17), "0.2.0-preview.4", store)
            .Should().Be(new FourPartPackageVersion(0, 1, 17, 1));
    }
}
