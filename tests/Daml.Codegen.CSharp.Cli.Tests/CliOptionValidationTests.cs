// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

/// <summary>
/// A blank value for an option that feeds generated project metadata is rejected at the CLI
/// boundary even when every other argument is valid, so the failure is reported up front
/// instead of surfacing as a broken <c>.csproj</c> later.
/// </summary>
[Collection("ConsoleRedirection")]
public class CliOptionValidationTests : IDisposable
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"cli-validation-{Guid.NewGuid():N}");

    public CliOptionValidationTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string FixtureIntermediate() =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, "intermediate.binpb");

    [Theory]
    [InlineData("--target-framework", "")]
    [InlineData("--target-framework", "   ")]
    [InlineData("--runtime-version", "")]
    [InlineData("--runtime-version", "   ")]
    [InlineData("--package-license", "")]
    [InlineData("--package-license", "   ")]
    [InlineData("--repository-url", "")]
    [InlineData("--repository-url", "   ")]
    public async Task CliOptionValidation_blank_value_is_rejected_when_the_other_arguments_are_valid(string option, string blankValue)
    {
        var exit = await Program.Main(
            ["--intermediate", FixtureIntermediate(), "-o", _workspace, option, blankValue]);

        exit.Should().NotBe(0, $"{option} must be a non-empty value, so a blank one fails before any file is generated");
        Directory.GetFiles(_workspace, "*.cs", SearchOption.AllDirectories).Should().BeEmpty(
            "validation fails before generation starts");
    }
}
