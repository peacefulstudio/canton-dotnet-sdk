// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Regression coverage for the mirror-promotion overlay's internal-only-MSBuild-package
/// leak-check: pins its pattern against representative lines so a future edit to the gate
/// can't silently regress to matching the pre-fix state. The "should be blocked" fixtures
/// are built at runtime (never as a contiguous literal in this file's source) so this
/// shipped test file can never itself trip the gate it verifies. The internal-reference
/// sweep is pinned by <c>scripts/check-promoted-refs.sh --self-test</c> instead, since
/// the gate runs that script rather than an inline pattern.
/// </summary>
public class MirrorPromotionGateTests
{
    private static readonly Regex InternalOnlyMsBuildPackage = new(@"Daml\.Codegen\.CSharp\.MSBuild");

    [Theory]
    [InlineData("New `", "` package: add it and declare a DamlArchive item")]
    [InlineData("dotnet add package ", "")]
    public void MirrorPromotionGate_internal_only_msbuild_package_is_blocked_by_the_530_guard(string prefix, string suffix)
    {
        var packageName = string.Concat("Daml.Codegen.CSharp", ".", "MSBuild");
        var line = string.Concat(prefix, packageName, suffix);

        InternalOnlyMsBuildPackage.IsMatch(line).Should().BeTrue();
    }

    [Fact]
    public void MirrorPromotionGate_the_already_public_emitter_package_passes_the_530_guard()
    {
        InternalOnlyMsBuildPackage.IsMatch("dotnet add package Daml.Codegen.CSharp").Should().BeFalse();
    }
}
