// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Backs the published "Codegen: supported Daml-LF input" table: each Daml-LF version the codegen
/// accepts is the version of some conformance fixture's main package, so the read path decodes
/// it from a DAR the Daml SDK built at that target.
/// </summary>
public sealed class SupportedInputFixtureTests
{
    [Theory]
    [InlineData("richtypes", "2.1")]
    [InlineData("nonserializable", "2.2")]
    [InlineData("defaulttarget", "2.3")]
    [InlineData("contractkeys", "2.3")]
    [InlineData("lf24", "2.4")]
    [InlineData("lf25rc1", "2.5-rc1")]
    public async Task SupportedInputFixture_main_package_is_decoded_at_the_daml_lf_version_the_published_table_names(
        string fixtureName, string expectedLfVersion)
    {
        var archive = await DarArchive.ReadAsync(
            ParitySnapshots.DarPath(fixtureName), TestContext.Current.CancellationToken);

        archive.MainPackage.LfVersion.Should().Be(expectedLfVersion);
    }

    [Theory]
    [InlineData("richtypes")]
    [InlineData("nonserializable")]
    [InlineData("defaulttarget")]
    [InlineData("contractkeys")]
    [InlineData("lf24")]
    [InlineData("lf25rc1")]
    public async Task SupportedInputFixture_carries_the_daml_lf_2_1_stdlib_component_packages(string fixtureName)
    {
        var archive = await DarArchive.ReadAsync(
            ParitySnapshots.DarPath(fixtureName), TestContext.Current.CancellationToken);

        archive.Dependencies.Count(dependency => dependency.LfVersion == "2.1")
            .Should().BeGreaterThanOrEqualTo(27);
    }
}
