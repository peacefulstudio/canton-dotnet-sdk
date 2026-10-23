// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

public class MsBuildIntegrationCoverageTests
{
    private const string GuardSourceFileName = "MsBuildIntegrationCoverageTests.cs";
    private const int SmallestHonestSourceCount = 5;
    private const int DamlGenerateBindingsFactCount = 24;
    private const int DamlCheckDpmAvailableFactCount = 12;
    private const int StandaloneDamlGenerateBindingsFactCount = 14;

    [Fact]
    public void MsBuildIntegrationCoverage_no_test_in_this_project_skips_itself()
    {
        var sources = ProjectSources();

        sources.Should().HaveCountGreaterThanOrEqualTo(SmallestHonestSourceCount,
            "a lint that reads no sources passes vacuously; this project has at least this many");
        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            text.Should().NotContain("Assert.Skip",
                $"{Path.GetFileName(source)} must not opt a platform out at runtime — a suite that skips itself on Windows " +
                "reports green while proving nothing; make the fixture work there instead");
            text.Should().NotContain("(Skip",
                $"{Path.GetFileName(source)} must not carry a statically skipped fact for the same reason");
        }
    }

    [Fact]
    public void MsBuildIntegrationCoverage_the_integration_suite_declares_the_expected_number_of_facts()
    {
        DeclaredFactCount(typeof(DamlGenerateBindingsTests)).Should().Be(DamlGenerateBindingsFactCount,
            "the count is a ratchet against a platform being made green by deleting coverage rather than by fixing it — " +
            "raise it when you add a fact, never lower it");
        DeclaredFactCount(typeof(DamlCheckDpmAvailableTests)).Should().Be(DamlCheckDpmAvailableFactCount,
            "the count is a ratchet against a platform being made green by deleting coverage rather than by fixing it — " +
            "raise it when you add a fact, never lower it");
        DeclaredFactCount(typeof(StandaloneDamlGenerateBindingsTests)).Should().Be(StandaloneDamlGenerateBindingsFactCount,
            "the count is a ratchet against a platform being made green by deleting coverage rather than by fixing it — " +
            "raise it when you add a fact, never lower it");
    }

    private static int DeclaredFactCount(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Count(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any());

    private static IReadOnlyList<string> ProjectSources()
    {
        var testsDir = Path.Combine(MsBuildProcessRunner.LocateRepoRoot("Daml.Codegen.CSharp.MSBuild"), "tests");
        var projectDir = Path.Combine(testsDir, "Daml.Codegen.CSharp.MSBuild.Tests");
        var sharedDir = Path.Combine(testsDir, "Shared", "MSBuild");
        return Directory.GetFiles(projectDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(sharedDir, "*.cs", SearchOption.TopDirectoryOnly))
            .Where(file => !string.Equals(Path.GetFileName(file), GuardSourceFileName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
