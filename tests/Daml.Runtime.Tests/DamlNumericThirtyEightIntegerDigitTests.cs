// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlNumericThirtyEightIntegerDigitTests
{
    private const string MaxScaleZero = "99999999999999999999999999999999999999.";
    private const string MinScaleZero = "-99999999999999999999999999999999999999.";

    private static DamlNumeric Parse(string canonical)
    {
        DamlNumeric.TryParseCanonical(canonical, out var value).Should().BeTrue();
        return value;
    }

    [Theory]
    [InlineData(MaxScaleZero)]
    [InlineData(MinScaleZero)]
    [InlineData("10000000000000000000000000000000000000.")]
    public void ToCanonicalString_writes_a_38_digit_integer_with_the_bare_trailing_dot(string canonical)
    {
        Parse(canonical).ToCanonicalString().Should().Be(canonical);
    }

    [Theory]
    [InlineData(MaxScaleZero)]
    [InlineData(MinScaleZero)]
    public void ToCanonicalString_of_a_38_digit_integer_round_trips_through_TryParseCanonical(string canonical)
    {
        var emitted = Parse(canonical).ToCanonicalString();

        DamlNumeric.TryParseCanonical(emitted, out var reparsed).Should().BeTrue();
        reparsed.Should().Be(Parse(canonical));
    }

    [Fact]
    public void Serialize_writes_a_38_digit_integer_with_the_bare_trailing_dot()
    {
        DamlJsonSerializer.Serialize(Parse(MaxScaleZero)).Should().Be("\"99999999999999999999999999999999999999.\"");
    }

    [Theory]
    [InlineData("0.", "0.0")]
    [InlineData("42.", "42.0")]
    [InlineData("-7.", "-7.0")]
    [InlineData("9999999999999999999999999999999999999.", "9999999999999999999999999999999999999.0")]
    [InlineData("-9999999999999999999999999999999999999.", "-9999999999999999999999999999999999999.0")]
    [InlineData("1.50", "1.5")]
    [InlineData("99999999999999999999999999999999999999", "99999999999999999999999999999999999999.")]
    [InlineData("9999999999999999999999999999999999999.9", "9999999999999999999999999999999999999.9")]
    [InlineData("9.9999999999999999999999999999999999999", "9.9999999999999999999999999999999999999")]
    [InlineData("0.0000000000000000000000000000000000001", "0.0000000000000000000000000000000000001")]
    public void ToCanonicalString_keeps_every_other_shape(string input, string expected)
    {
        Parse(input).ToCanonicalString().Should().Be(expected);
    }
}
