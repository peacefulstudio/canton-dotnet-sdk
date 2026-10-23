// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class PackageVersionParserTests
{
    [Fact]
    public void PackageVersionParser_overflowing_segment_falls_back_to_zero_zero_zero()
    {
        var result = PackageVersionParser.Parse("2147483648.0.0");

        result.Should().Be(new Version(0, 0, 0));
    }

    [Theory]
    [InlineData("1.0", 1, 0, 0)]
    [InlineData("2", 2, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("1.2.3.4", 1, 2, 3)]
    public void PackageVersionParser_pads_short_versions_to_three_parts(string raw, int major, int minor, int build)
    {
        var result = PackageVersionParser.Parse(raw);

        result.Should().Be(new Version(major, minor, build));
    }

    [Theory]
    [InlineData("v1.2.3")]
    [InlineData("")]
    [InlineData("abc")]
    public void PackageVersionParser_non_numeric_version_falls_back_to_zero_zero_zero(string raw)
    {
        var result = PackageVersionParser.Parse(raw);

        result.Should().Be(new Version(0, 0, 0));
    }
}
