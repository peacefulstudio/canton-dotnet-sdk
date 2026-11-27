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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1Acme")]
    [InlineData("Acme Corp")]
    [InlineData("Acme.")]
    [InlineData(".Acme")]
    [InlineData("Acme..Ledger")]
    [InlineData("Acme.class")]
    [InlineData("Acme-Ledger")]
    [InlineData("Acme.@")]
    [InlineData("@class.Acme")]
    public async Task CliOptionValidation_namespace_that_is_not_a_dotted_csharp_identifier_is_rejected(string prefix)
    {
        var exit = await Program.Main(["--intermediate", FixtureIntermediate(), "-o", _workspace, "--namespace", prefix]);

        exit.Should().NotBe(0, "a namespace prefix that is not a dotted C# identifier would emit a namespace declaration that does not compile");
        Directory.GetFiles(_workspace, "*.cs", SearchOption.AllDirectories).Should().BeEmpty(
            "validation fails before generation starts");
    }

    [Theory]
    [InlineData("Acme.Ledger", "Acme/Ledger/Splice/Api/Token/HoldingV1/IHolding.cs")]
    [InlineData("_Acme", "_Acme/Splice/Api/Token/HoldingV1/IHolding.cs")]
    [InlineData("Société", "Société/Splice/Api/Token/HoldingV1/IHolding.cs")]
    public async Task CliOptionValidation_namespace_that_is_a_dotted_csharp_identifier_prefixes_the_main_package(string prefix, string interfacePath)
    {
        var exit = await Program.Main(
            ["--intermediate", FixtureIntermediate(), "-o", _workspace, "--namespace", prefix, "--root", @"^Splice\.Api\.Token\.HoldingV1:Holding$"]);

        exit.Should().Be(0);
        File.Exists(Path.Combine(_workspace, interfacePath)).Should().BeTrue(
            "the --namespace value prefixes each module namespace and the --root value filters, never the other way round");
    }
}
