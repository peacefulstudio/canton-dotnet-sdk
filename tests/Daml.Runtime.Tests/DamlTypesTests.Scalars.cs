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
        long expected = 42;

        DamlInt64 damlValue = expected;
        long result = damlValue;

        result.Should().Be(expected);
        damlValue.Value.Should().Be(expected);
    }

    [Fact]
    public void DamlNumeric_should_preserve_decimal_precision()
    {
        decimal expected = 123.456789m;

        var damlValue = new DamlNumeric(expected);

        damlValue.Value.Should().Be(expected);
    }

    [Fact]
    public void DamlNumeric_equality_should_compare_value_only_ignoring_trailing_zeros()
    {
        var padded = new DamlNumeric(1.50m);
        var stripped = new DamlNumeric(1.5m);

        padded.Should().Be(stripped);
        padded.GetHashCode().Should().Be(stripped.GetHashCode());
    }

    [Fact]
    public void DamlNumeric_equality_should_still_distinguish_different_values()
    {
        new DamlNumeric(1.5m).Should().NotBe(new DamlNumeric(2.5m));
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
        decimal value = 123.456m;

        DamlNumeric numeric = value;
        decimal result = numeric;

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
        string expected = "Hello, Daml!";

        DamlText damlValue = expected;
        string result = damlValue;

        result.Should().Be(expected);
    }

    [Fact]
    public void DamlBool_should_convert_to_and_from_bool()
    {
        bool expected = true;

        DamlBool damlValue = expected;
        bool result = damlValue;

        result.Should().Be(expected);
        damlValue.Value.Should().BeTrue();
    }

    [Fact]
    public void DamlBool_false_should_convert_correctly()
    {
        DamlBool damlValue = false;

        damlValue.Value.Should().BeFalse();
        ((bool)damlValue).Should().BeFalse();
    }

    [Fact]
    public void DamlUnit_should_be_singleton()
    {
        var unit1 = DamlUnit.Instance;
        var unit2 = DamlUnit.Instance;

        unit1.Should().BeSameAs(unit2);
    }

    [Fact]
    public void DamlParty_should_convert_to_and_from_string()
    {
        string partyId = "Alice::12345";

        DamlParty party = partyId;
        string result = party;

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
        var party = (Party)"alice";

        party.Value.Should().Be("alice");
    }

    [Fact]
    public void Party_should_support_equality()
    {
        var p1 = new Party("a");
        var p2 = new Party("a");
        var p3 = new Party("b");

        p1.Should().Be(p2);
        p1.Should().NotBe(p3);
    }

    [Fact]
    public void Party_should_round_trip_through_DamlParty()
    {
        var party = new Party("alice");

        var roundTripped = Party.FromDamlValue(party.ToDamlValue());

        roundTripped.Should().Be(party);
    }

    [Fact]
    public void Party_ToString_should_return_id()
    {
        var party = new Party("alice");

        party.ToString().Should().Be("alice");
    }

    [Fact]
    public void Party_constructor_should_throw_on_null()
    {
        var act = () => new Party(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_constructor_should_throw_on_empty()
    {
        var act = () => new Party("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_constructor_should_throw_on_whitespace()
    {
        var act = () => new Party("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_default_ToDamlValue_should_throw()
    {
        var party = default(Party);

        var act = () => party.ToDamlValue();

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
        var act = () => Party.FromDamlValue(new DamlParty(null!));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Party_FromDamlValue_should_throw_on_null_argument()
    {
        var act = () => Party.FromDamlValue(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Party_default_ToString_should_return_diagnostic_string()
    {
        var party = default(Party);

        party.ToString().Should().Be("<uninitialized Party>");
    }

    [Fact]
    public void Party_default_Id_should_throw()
    {
        var party = default(Party);

        var act = () => party.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DamlDate_should_convert_from_days_since_epoch()
    {
        int daysFromEpochTo20200101 = 18262;

        var damlDate = DamlDate.FromDaysSinceEpoch(daysFromEpochTo20200101);

        damlDate.Value.Should().Be(new DateOnly(2020, 1, 1));
    }

    [Fact]
    public void DamlDate_DaysSinceEpoch_should_calculate_correctly()
    {
        var date = new DamlDate(new DateOnly(2020, 1, 1));

        var days = date.DaysSinceEpoch;

        days.Should().Be(18262);
    }

    [Fact]
    public void DamlDate_should_support_implicit_conversions()
    {
        var dateOnly = new DateOnly(2023, 6, 15);

        DamlDate damlDate = dateOnly;
        DateOnly result = damlDate;

        result.Should().Be(dateOnly);
    }

    [Fact]
    public void DamlTimestamp_should_convert_from_microseconds_since_epoch()
    {
        long oneSecondInMicroseconds = 1_000_000;

        var timestamp = DamlTimestamp.FromMicrosecondsSinceEpoch(oneSecondInMicroseconds);

        timestamp.Value.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(1));
    }

    [Fact]
    public void DamlTimestamp_MicrosecondsSinceEpoch_should_calculate_correctly()
    {
        var timestamp = new DamlTimestamp(DateTimeOffset.UnixEpoch.AddSeconds(1));

        var microseconds = timestamp.MicrosecondsSinceEpoch;

        microseconds.Should().Be(1_000_000);
    }

    [Fact]
    public void DamlTimestamp_should_support_implicit_conversions()
    {
        var dto = new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero);

        DamlTimestamp timestamp = dto;
        DateTimeOffset result = timestamp;

        result.Should().Be(dto);
    }

    [Fact]
    public void DamlDate_FromDaysSinceEpoch_should_accept_daml_date_boundary_values()
    {
        var epochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
        var minValidDays = DateOnly.MinValue.DayNumber - epochDayNumber;
        var maxValidDays = DateOnly.MaxValue.DayNumber - epochDayNumber;

        var minDate = DamlDate.FromDaysSinceEpoch(minValidDays);
        var maxDate = DamlDate.FromDaysSinceEpoch(maxValidDays);

        minDate.Value.Should().Be(DateOnly.MinValue);
        maxDate.Value.Should().Be(DateOnly.MaxValue);
    }

    [Fact]
    public void DamlDate_FromDaysSinceEpoch_should_throw_when_days_precede_daml_date_lower_bound()
    {
        var epochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
        var minValidDays = DateOnly.MinValue.DayNumber - epochDayNumber;

        var act = () => DamlDate.FromDaysSinceEpoch(minValidDays - 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlDate_FromDaysSinceEpoch_should_throw_when_days_exceed_daml_date_upper_bound()
    {
        var epochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
        var maxValidDays = DateOnly.MaxValue.DayNumber - epochDayNumber;

        var act = () => DamlDate.FromDaysSinceEpoch(maxValidDays + 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlDate_FromDaysSinceEpoch_should_throw_instead_of_silently_wrapping_on_int_overflow()
    {
        var act = () => DamlDate.FromDaysSinceEpoch(int.MaxValue);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlTimestamp_FromMicrosecondsSinceEpoch_should_accept_daml_timestamp_boundary_values()
    {
        var minTicksSinceEpoch = (DateTimeOffset.MinValue - DateTimeOffset.UnixEpoch).Ticks;
        var maxTicksSinceEpoch = (DateTimeOffset.MaxValue - DateTimeOffset.UnixEpoch).Ticks;
        var minValidMicroseconds = minTicksSinceEpoch / 10;
        var maxValidMicroseconds = maxTicksSinceEpoch / 10;

        var minTimestamp = DamlTimestamp.FromMicrosecondsSinceEpoch(minValidMicroseconds);
        var maxTimestamp = DamlTimestamp.FromMicrosecondsSinceEpoch(maxValidMicroseconds);

        minTimestamp.Value.Should().Be(DateTimeOffset.MinValue);
        maxTimestamp.Value.Should().Be(DateTimeOffset.UnixEpoch.AddTicks(maxValidMicroseconds * 10));
    }

    [Fact]
    public void DamlTimestamp_FromMicrosecondsSinceEpoch_should_throw_when_microseconds_precede_daml_timestamp_lower_bound()
    {
        var minTicksSinceEpoch = (DateTimeOffset.MinValue - DateTimeOffset.UnixEpoch).Ticks;
        var minValidMicroseconds = minTicksSinceEpoch / 10;

        var act = () => DamlTimestamp.FromMicrosecondsSinceEpoch(minValidMicroseconds - 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlTimestamp_FromMicrosecondsSinceEpoch_should_throw_when_microseconds_exceed_daml_timestamp_upper_bound()
    {
        var maxTicksSinceEpoch = (DateTimeOffset.MaxValue - DateTimeOffset.UnixEpoch).Ticks;
        var maxValidMicroseconds = maxTicksSinceEpoch / 10;

        var act = () => DamlTimestamp.FromMicrosecondsSinceEpoch(maxValidMicroseconds + 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DamlTimestamp_FromMicrosecondsSinceEpoch_should_throw_instead_of_silently_wrapping_on_long_overflow()
    {
        var actMax = () => DamlTimestamp.FromMicrosecondsSinceEpoch(long.MaxValue);
        var actMin = () => DamlTimestamp.FromMicrosecondsSinceEpoch(long.MinValue);

        actMax.Should().Throw<ArgumentOutOfRangeException>();
        actMin.Should().Throw<ArgumentOutOfRangeException>();
    }
}
