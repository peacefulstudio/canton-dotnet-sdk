// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlNumericBoundaryTests
{
    private const string ThirtySevenFractionalDigits = "1234567890123456789012345678901234567";
    private const string ThirtyEightNines = "99999999999999999999999999999999999999";

    [Theory]
    [InlineData("+0")]
    [InlineData("+0.")]
    [InlineData("+-1.5")]
    [InlineData("-+1.5")]
    [InlineData("--1.5")]
    [InlineData("++1.5")]
    public void TryParseCanonical_rejects_a_sign_that_is_not_a_single_leading_minus(string text)
    {
        DamlNumeric.TryParseCanonical(text, out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Theory]
    [InlineData("-0.0")]
    [InlineData("-0.")]
    [InlineData("-0.000")]
    [InlineData("-0.0000000000000000000000000000000000000")]
    public void TryParseCanonical_normalises_a_negative_zero_to_the_unsigned_zero(string negativeZero)
    {
        DamlNumeric.TryParseCanonical(negativeZero, out var parsed).Should().BeTrue();
        DamlNumeric.TryParseCanonical("0.0", out var zero).Should().BeTrue();

        parsed.ToCanonicalString().Should().Be("0.0");
        parsed.Should().Be(zero);
        parsed.GetHashCode().Should().Be(zero.GetHashCode());
    }

    [Fact]
    public void Constructor_normalises_a_negative_zero_decimal_to_the_unsigned_zero()
    {
        var negativeZero = new decimal(0, 0, 0, isNegative: true, scale: 1);

        var numeric = new DamlNumeric(negativeZero);

        numeric.ToCanonicalString().Should().Be("0.0");
        numeric.Should().Be(new DamlNumeric(0m));
        numeric.GetHashCode().Should().Be(new DamlNumeric(0m).GetHashCode());
    }

    [Fact]
    public void TryParseCanonical_keeps_the_minus_sign_of_a_tiny_negative_value()
    {
        DamlNumeric.TryParseCanonical("-0.0000000000000000000000000000000000001", out var value).Should().BeTrue();

        value.ToCanonicalString().Should().Be("-0.0000000000000000000000000000000000001");
        value.Should().NotBe(new DamlNumeric(0m));
    }

    [Fact]
    public void TryParseCanonical_accepts_thirty_seven_fractional_digits_the_maximum_scale()
    {
        var canonical = "0." + ThirtySevenFractionalDigits;

        DamlNumeric.TryParseCanonical(canonical, out var value).Should().BeTrue();

        value.ToCanonicalString().Should().Be("0.1234567890123456789012345678901234567");
    }

    [Fact]
    public void TryParseCanonical_rejects_thirty_eight_fractional_digits_one_beyond_the_maximum_scale()
    {
        DamlNumeric.TryParseCanonical("0." + ThirtySevenFractionalDigits + "8", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryParseCanonical_accepts_thirty_eight_significant_digits_split_across_the_point()
    {
        DamlNumeric.TryParseCanonical("12345678901234567890123456789.012345678", out var value).Should().BeTrue();

        value.ToCanonicalString().Should().Be("12345678901234567890123456789.012345678");
    }

    [Fact]
    public void TryParseCanonical_rejects_thirty_nine_significant_digits_with_a_scale_within_range()
    {
        DamlNumeric.TryParseCanonical("123456789012345678901234567890.123456789", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryParseCanonical_accepts_thirty_eight_integer_digits_at_scale_zero()
    {
        DamlNumeric.TryParseCanonical(ThirtyEightNines + ".", out var value).Should().BeTrue();

        value.Invoking(v => v.Value).Should().Throw<OverflowException>();
    }

    [Fact]
    public void TryParseCanonical_rejects_thirty_nine_integer_digits_at_scale_zero()
    {
        DamlNumeric.TryParseCanonical("1" + ThirtyEightNines + ".", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryParseCanonical_round_trips_the_negative_extreme_of_the_thirty_eight_digit_bound()
    {
        const string canonical = "-9999999999999999999999999999999999999.9";

        DamlNumeric.TryParseCanonical(canonical, out var value).Should().BeTrue();

        value.ToCanonicalString().Should().Be(canonical);
    }

    [Fact]
    public void Value_throws_for_the_smallest_scale_thirty_seven_fraction_because_decimal_holds_twenty_eight_places()
    {
        DamlNumeric.TryParseCanonical("0.0000000000000000000000000000000000001", out var value).Should().BeTrue();

        value.Invoking(v => v.Value).Should().Throw<OverflowException>();
    }

    [Fact]
    public void Constructor_round_trips_decimal_MaxValue_through_the_canonical_string()
    {
        var numeric = new DamlNumeric(decimal.MaxValue);

        numeric.ToCanonicalString().Should().Be("79228162514264337593543950335.0");
        numeric.Value.Should().Be(79228162514264337593543950335m);
    }

    [Fact]
    public void Constructor_round_trips_decimal_MinValue_through_the_canonical_string()
    {
        var numeric = new DamlNumeric(decimal.MinValue);

        numeric.ToCanonicalString().Should().Be("-79228162514264337593543950335.0");
        numeric.Value.Should().Be(-79228162514264337593543950335m);
    }
}
