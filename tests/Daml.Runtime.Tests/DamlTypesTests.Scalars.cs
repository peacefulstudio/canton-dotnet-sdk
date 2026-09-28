// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class DamlTypesTests
{
    [Fact]
    public void DamlInt64_should_convert_to_and_from_long()
    {
        // Arrange
        long expected = 42;

        // Act
        DamlInt64 damlValue = expected;
        long result = damlValue;

        // Assert
        result.Should().Be(expected);
        damlValue.Value.Should().Be(expected);
    }

    [Fact]
    public void DamlNumeric_should_preserve_decimal_precision()
    {
        // Arrange
        decimal expected = 123.456789m;

        // Act
        var damlValue = new DamlNumeric(expected, 6);

        // Assert
        damlValue.Value.Should().Be(expected);
        damlValue.Scale.Should().Be(6);
    }

    [Fact]
    public void DamlNumeric_equality_should_compare_value_only_ignoring_scale()
    {
        var nonDefaultScale = new DamlNumeric(1.5m, 38);
        var defaultScale = new DamlNumeric(1.5m);

        nonDefaultScale.Should().Be(defaultScale,
            "Scale is not serialized and deserialization reconstructs the default, so it must not participate in equality");
        nonDefaultScale.GetHashCode().Should().Be(defaultScale.GetHashCode());
    }

    [Fact]
    public void DamlNumeric_equality_should_still_distinguish_different_values()
    {
        new DamlNumeric(1.5m, 38).Should().NotBe(new DamlNumeric(2.5m, 38));
    }

    [Fact]
    public void DamlNumeric_equality_should_hold_beyond_decimal_range_across_differing_scales()
    {
        var padded = DamlJsonSerializer.DeserializeRecord("""{"amount":"100000000000000000000000000000.00"}""")
            .GetRequiredField("amount").As<DamlNumeric>();
        var stripped = DamlJsonSerializer.DeserializeRecord("""{"amount":"100000000000000000000000000000.0"}""")
            .GetRequiredField("amount").As<DamlNumeric>();

        padded.Should().Be(stripped);
        padded.GetHashCode().Should().Be(stripped.GetHashCode());
    }

    [Fact]
    public void DamlNumeric_should_support_implicit_conversions()
    {
        // Arrange
        decimal value = 123.456m;

        // Act
        DamlNumeric numeric = value;
        decimal result = numeric;

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public void ToCanonicalString_formats_a_within_decimal_value()
    {
        new DamlNumeric(123.45m).ToCanonicalString().Should().Be("123.45");
    }

    [Fact]
    public void ToCanonicalString_round_trips_a_magnitude_beyond_decimal_range()
    {
        const string canonical = "1000000000000000000000000000000.5";

        DamlNumeric.TryParseCanonical(canonical, out var value).Should().BeTrue();
        value.ToCanonicalString().Should().Be(canonical);
        value.Invoking(v => v.Value).Should().Throw<OverflowException>(
            "the magnitude exceeds decimal.MaxValue and must not silently narrow");
    }

    [Fact]
    public void ToCanonicalString_round_trips_more_fractional_digits_than_decimal_holds()
    {
        const string canonical = "0.12345678901234567890123456789";

        DamlNumeric.TryParseCanonical(canonical, out var value).Should().BeTrue();
        value.ToCanonicalString().Should().Be(canonical);
        value.Invoking(v => v.Value).Should().Throw<OverflowException>(
            "29 fractional digits exceed decimal's 28-digit limit and must not silently round");
    }

    [Fact]
    public void Value_narrows_a_scale_padded_value_that_fits_decimal()
    {
        const string paddedToScale37 = "1.5000000000000000000000000000000000000";

        DamlNumeric.TryParseCanonical(paddedToScale37, out var value).Should().BeTrue();
        value.Value.Should().Be(1.5m,
            "a participant pads every Numeric 37 field out to its declared scale, so the padding must not decide representability");
    }

    [Fact]
    public void Value_narrows_a_scale_padded_zero()
    {
        const string paddedZero = "0.0000000000000000000000000000000000000";

        DamlNumeric.TryParseCanonical(paddedZero, out var value).Should().BeTrue();
        value.Value.Should().Be(0m);
    }

    [Fact]
    public void Value_keeps_the_trailing_zeros_of_a_mantissa_decimal_already_holds()
    {
        DamlNumeric.TryParseCanonical("1.50", out var value).Should().BeTrue();
        value.Value.Scale.Should().Be(2,
            "stripping is a fallback for mantissas decimal cannot otherwise hold, not a normalisation of every value");
    }

    [Theory]
    [InlineData("42.", 42)]
    [InlineData("-7.", -7)]
    [InlineData("0.", 0)]
    public void TryParseCanonical_reads_the_participant_scale_zero_form_with_a_bare_trailing_dot(string participantText, int expected)
    {
        DamlNumeric.TryParseCanonical(participantText, out var value).Should().BeTrue(
            "Daml-LF 3.5.9's Numeric.toString writes a Numeric 0 as toPlainString plus a bare '.', as the participant sent for TypeCorners.whole in run 36402548934");
        value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("1e5")]
    [InlineData(".")]
    [InlineData("-.")]
    [InlineData(".5")]
    [InlineData("")]
    [InlineData("+42.")]
    [InlineData("+1.5")]
    [InlineData(" 42.")]
    [InlineData("42. ")]
    [InlineData("4.2e1")]
    [InlineData("42.e1")]
    [InlineData("42..")]
    [InlineData("4 2.")]
    public void TryParseCanonical_rejects_non_canonical_text(string text)
    {
        DamlNumeric.TryParseCanonical(text, out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryParseCanonical_returns_false_for_null()
    {
        DamlNumeric.TryParseCanonical(null!, out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void DamlText_should_convert_to_and_from_string()
    {
        // Arrange
        string expected = "Hello, Daml!";

        // Act
        DamlText damlValue = expected;
        string result = damlValue;

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void DamlBool_should_convert_to_and_from_bool()
    {
        // Arrange
        bool expected = true;

        // Act
        DamlBool damlValue = expected;
        bool result = damlValue;

        // Assert
        result.Should().Be(expected);
        damlValue.Value.Should().BeTrue();
    }

    [Fact]
    public void DamlBool_false_should_convert_correctly()
    {
        // Arrange & Act
        DamlBool damlValue = false;

        // Assert
        damlValue.Value.Should().BeFalse();
        ((bool)damlValue).Should().BeFalse();
    }

    [Fact]
    public void DamlUnit_should_be_singleton()
    {
        // Act
        var unit1 = DamlUnit.Instance;
        var unit2 = DamlUnit.Instance;

        // Assert
        unit1.Should().BeSameAs(unit2);
    }

    [Fact]
    public void DamlParty_should_convert_to_and_from_string()
    {
        // Arrange
        string partyId = "Alice::12345";

        // Act
        DamlParty party = partyId;
        string result = party;

        // Assert
        result.Should().Be(partyId);
        party.Value.Should().Be(partyId);
        party.ToString().Should().Be(partyId);
    }

    [Fact]
    public void Party_should_convert_to_string_explicitly()
    {
        var party = new Party("alice");

        var s = (string)party;

        s.Should().Be("alice");
    }

    [Fact]
    public void Party_should_convert_from_string_explicitly()
    {
        // Arrange & Act
        var party = (Party)"alice";

        // Assert
        party.Value.Should().Be("alice");
    }

    [Fact]
    public void Party_should_support_equality()
    {
        // Arrange
        var p1 = new Party("a");
        var p2 = new Party("a");
        var p3 = new Party("b");

        // Assert
        p1.Should().Be(p2);
        p1.Should().NotBe(p3);
    }

    [Fact]
    public void Party_should_round_trip_through_DamlParty()
    {
        // Arrange
        var party = new Party("alice");

        // Act
        var roundTripped = Party.FromDamlValue(party.ToDamlValue());

        // Assert
        roundTripped.Should().Be(party);
    }

    [Fact]
    public void Party_ToString_should_return_id()
    {
        // Arrange
        var party = new Party("alice");

        // Act & Assert
        party.ToString().Should().Be("alice");
    }

    [Fact]
    public void Party_constructor_should_throw_on_null()
    {
        // Act
        var act = () => new Party(null!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_constructor_should_throw_on_empty()
    {
        // Act
        var act = () => new Party("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_constructor_should_throw_on_whitespace()
    {
        // Act
        var act = () => new Party("  ");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_default_ToDamlValue_should_throw()
    {
        // Arrange
        var party = default(Party);

        // Act
        var act = () => party.ToDamlValue();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Party_default_explicit_string_conversion_should_throw()
    {
        var party = default(Party);

        var act = () => { var _ = (string)party; };

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Party_FromDamlValue_should_throw_on_null_value()
    {
        // Act
        var act = () => Party.FromDamlValue(new DamlParty(null!));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Party_FromDamlValue_should_throw_on_null_argument()
    {
        // Act
        var act = () => Party.FromDamlValue(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Party_default_ToString_should_return_diagnostic_string()
    {
        // Arrange
        var party = default(Party);

        // Act & Assert
        party.ToString().Should().Be("<uninitialized Party>");
    }

    [Fact]
    public void Party_default_Id_should_throw()
    {
        // Arrange
        var party = default(Party);

        // Act
        var act = () => party.Value;

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DamlDate_should_convert_from_days_since_epoch()
    {
        // Arrange - January 1, 2020 is 18262 days since Unix epoch
        int days = 18262;

        // Act
        var damlDate = DamlDate.FromDaysSinceEpoch(days);

        // Assert
        damlDate.Value.Should().Be(new DateOnly(2020, 1, 1));
    }

    [Fact]
    public void DamlDate_DaysSinceEpoch_should_calculate_correctly()
    {
        // Arrange - January 1, 2020 is 18262 days since Unix epoch
        var date = new DamlDate(new DateOnly(2020, 1, 1));

        // Act
        var days = date.DaysSinceEpoch;

        // Assert
        days.Should().Be(18262);
    }

    [Fact]
    public void DamlDate_should_support_implicit_conversions()
    {
        // Arrange
        var dateOnly = new DateOnly(2023, 6, 15);

        // Act
        DamlDate damlDate = dateOnly;
        DateOnly result = damlDate;

        // Assert
        result.Should().Be(dateOnly);
    }

    [Fact]
    public void DamlTimestamp_should_convert_from_microseconds_since_epoch()
    {
        // Arrange - 1000000 microseconds = 1 second after epoch
        long microseconds = 1_000_000;

        // Act
        var timestamp = DamlTimestamp.FromMicrosecondsSinceEpoch(microseconds);

        // Assert
        timestamp.Value.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(1));
    }

    [Fact]
    public void DamlTimestamp_MicrosecondsSinceEpoch_should_calculate_correctly()
    {
        // Arrange - 1 second after epoch = 1,000,000 microseconds
        var timestamp = new DamlTimestamp(DateTimeOffset.UnixEpoch.AddSeconds(1));

        // Act
        var microseconds = timestamp.MicrosecondsSinceEpoch;

        // Assert
        microseconds.Should().Be(1_000_000);
    }

    [Fact]
    public void DamlTimestamp_should_support_implicit_conversions()
    {
        // Arrange
        var dto = new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero);

        // Act
        DamlTimestamp timestamp = dto;
        DateTimeOffset result = timestamp;

        // Assert
        result.Should().Be(dto);
    }

    [Fact]
    public void DamlDate_FromDaysSinceEpoch_should_accept_daml_date_boundary_values()
    {
        // Arrange
        var epochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
        var minValidDays = DateOnly.MinValue.DayNumber - epochDayNumber;
        var maxValidDays = DateOnly.MaxValue.DayNumber - epochDayNumber;

        // Act
        var minDate = DamlDate.FromDaysSinceEpoch(minValidDays);
        var maxDate = DamlDate.FromDaysSinceEpoch(maxValidDays);

        // Assert
        minDate.Value.Should().Be(DateOnly.MinValue);
        maxDate.Value.Should().Be(DateOnly.MaxValue);
    }

    [Fact]
    public void DamlDate_FromDaysSinceEpoch_should_throw_when_days_precede_daml_date_lower_bound()
    {
        // Arrange
        var epochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
        var minValidDays = DateOnly.MinValue.DayNumber - epochDayNumber;

        // Act
        var act = () => DamlDate.FromDaysSinceEpoch(minValidDays - 1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlDate_FromDaysSinceEpoch_should_throw_when_days_exceed_daml_date_upper_bound()
    {
        // Arrange
        var epochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
        var maxValidDays = DateOnly.MaxValue.DayNumber - epochDayNumber;

        // Act
        var act = () => DamlDate.FromDaysSinceEpoch(maxValidDays + 1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlDate_FromDaysSinceEpoch_should_throw_instead_of_silently_wrapping_on_int_overflow()
    {
        // Act
        var act = () => DamlDate.FromDaysSinceEpoch(int.MaxValue);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlTimestamp_FromMicrosecondsSinceEpoch_should_accept_daml_timestamp_boundary_values()
    {
        // Arrange
        var minTicksSinceEpoch = (DateTimeOffset.MinValue - DateTimeOffset.UnixEpoch).Ticks;
        var maxTicksSinceEpoch = (DateTimeOffset.MaxValue - DateTimeOffset.UnixEpoch).Ticks;
        var minValidMicroseconds = minTicksSinceEpoch / 10;
        var maxValidMicroseconds = maxTicksSinceEpoch / 10;

        // Act
        var minTimestamp = DamlTimestamp.FromMicrosecondsSinceEpoch(minValidMicroseconds);
        var maxTimestamp = DamlTimestamp.FromMicrosecondsSinceEpoch(maxValidMicroseconds);

        // Assert
        minTimestamp.Value.Should().Be(DateTimeOffset.MinValue);
        maxTimestamp.Value.Should().Be(DateTimeOffset.UnixEpoch.AddTicks(maxValidMicroseconds * 10));
    }

    [Fact]
    public void DamlTimestamp_FromMicrosecondsSinceEpoch_should_throw_when_microseconds_precede_daml_timestamp_lower_bound()
    {
        // Arrange
        var minTicksSinceEpoch = (DateTimeOffset.MinValue - DateTimeOffset.UnixEpoch).Ticks;
        var minValidMicroseconds = minTicksSinceEpoch / 10;

        // Act
        var act = () => DamlTimestamp.FromMicrosecondsSinceEpoch(minValidMicroseconds - 1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlTimestamp_FromMicrosecondsSinceEpoch_should_throw_when_microseconds_exceed_daml_timestamp_upper_bound()
    {
        // Arrange
        var maxTicksSinceEpoch = (DateTimeOffset.MaxValue - DateTimeOffset.UnixEpoch).Ticks;
        var maxValidMicroseconds = maxTicksSinceEpoch / 10;

        // Act
        var act = () => DamlTimestamp.FromMicrosecondsSinceEpoch(maxValidMicroseconds + 1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlTimestamp_FromMicrosecondsSinceEpoch_should_throw_instead_of_silently_wrapping_on_long_overflow()
    {
        // Act
        var actMax = () => DamlTimestamp.FromMicrosecondsSinceEpoch(long.MaxValue);
        var actMin = () => DamlTimestamp.FromMicrosecondsSinceEpoch(long.MinValue);

        // Assert
        actMax.Should().Throw<ArgumentOutOfRangeException>();
        actMin.Should().Throw<ArgumentOutOfRangeException>();
    }
}
