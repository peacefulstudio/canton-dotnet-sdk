// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class PqsLeafValuesTests
{
    [Theory]
    [InlineData("42.5", "42.5000000000")]
    [InlineData("0", "0.0000000000")]
    [InlineData("-0", "0")]
    [InlineData("100", "1E2")]
    [InlineData("100", "100.00")]
    public void Cast_Numeric_compares_by_value_not_by_scale(string left, string right)
    {
        PqsLeafValues.Cast(PqsLeafKind.Numeric, left).CompareTo(PqsLeafValues.Cast(PqsLeafKind.Numeric, right)).Should().Be(0);
        PqsLeafValues.Cast(PqsLeafKind.Numeric, left).Should().Be(PqsLeafValues.Cast(PqsLeafKind.Numeric, right));
    }

    [Fact]
    public void Cast_Numeric_distinguishes_values_beyond_decimal_precision()
    {
        var wide = PqsLeafValues.Cast(PqsLeafKind.Numeric, "0.1234567890123456789012345678901234567");
        var wider = PqsLeafValues.Cast(PqsLeafKind.Numeric, "0.1234567890123456789012345678901234568");

        wide.CompareTo(wider).Should().BeNegative();
        wide.Should().NotBe(wider);
    }

    [Fact]
    public void Cast_Numeric_orders_values_of_different_magnitude()
    {
        PqsLeafValues.Cast(PqsLeafKind.Numeric, "9.99").CompareTo(PqsLeafValues.Cast(PqsLeafKind.Numeric, "10")).Should().BeNegative();
        PqsLeafValues.Cast(PqsLeafKind.Numeric, "-10").CompareTo(PqsLeafValues.Cast(PqsLeafKind.Numeric, "-9.99")).Should().BeNegative();
    }

    [Fact]
    public void FromOperand_Numeric_decimal_keeps_its_exact_digits()
    {
        var operand = new PqsOperand(PqsLeafKind.Numeric, 12.340m);

        PqsLeafValues.FromOperand(operand).Should().Be(PqsLeafValues.Cast(PqsLeafKind.Numeric, "12.34"));
    }

    [Fact]
    public void FromOperand_Numeric_literal_is_parsed_exactly()
    {
        var operand = new PqsOperand(PqsLeafKind.Numeric, "10.5");

        PqsLeafValues.FromOperand(operand).Should().Be(PqsLeafValues.Cast(PqsLeafKind.Numeric, "10.50"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.2.3")]
    [InlineData(".")]
    public void Cast_Numeric_rejects_text_Postgres_would_reject(string text)
    {
        var act = () => PqsLeafValues.Cast(PqsLeafKind.Numeric, text);

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("12.0")]
    [InlineData("abc")]
    [InlineData("9223372036854775808")]
    public void Cast_Int64_rejects_text_Postgres_would_reject(string text)
    {
        var act = () => PqsLeafValues.Cast(PqsLeafKind.Int64, text);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Cast_Int64_accepts_signed_digits()
    {
        PqsLeafValues.Cast(PqsLeafKind.Int64, "-42").Should().Be(-42L);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("t", true)]
    [InlineData("YES", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("f", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public void Cast_Bool_accepts_the_Postgres_boolean_spellings(string text, bool expected)
    {
        PqsLeafValues.Cast(PqsLeafKind.Bool, text).Should().Be(expected);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("o")]
    [InlineData("")]
    public void Cast_Bool_rejects_text_Postgres_would_reject(string text)
    {
        var act = () => PqsLeafValues.Cast(PqsLeafKind.Bool, text);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Cast_Date_orders_by_calendar_day()
    {
        PqsLeafValues.Cast(PqsLeafKind.Date, "2026-05-29").CompareTo(PqsLeafValues.Cast(PqsLeafKind.Date, "2026-06-01")).Should().BeNegative();
    }

    [Fact]
    public void Cast_Date_rejects_a_non_date()
    {
        var act = () => PqsLeafValues.Cast(PqsLeafKind.Date, "29/05/2026");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Cast_Date_reads_infinity_beyond_every_calendar_day()
    {
        PqsLeafValues.Cast(PqsLeafKind.Date, "infinity").CompareTo(PqsLeafValues.Cast(PqsLeafKind.Date, "9999-12-31")).Should().BePositive();
        PqsLeafValues.Cast(PqsLeafKind.Date, "-infinity").CompareTo(PqsLeafValues.Cast(PqsLeafKind.Date, "0001-01-01")).Should().BeNegative();
    }

    [Fact]
    public void FromOperand_Date_extremes_become_infinities()
    {
        PqsLeafValues.FromOperand(new PqsOperand(PqsLeafKind.Date, DateOnly.MaxValue))
            .Should().Be(PqsLeafValues.Cast(PqsLeafKind.Date, "infinity"));
        PqsLeafValues.FromOperand(new PqsOperand(PqsLeafKind.Date, DateOnly.MinValue))
            .Should().Be(PqsLeafValues.Cast(PqsLeafKind.Date, "-infinity"));
    }

    [Fact]
    public void FromOperand_Time_extremes_become_infinities()
    {
        PqsLeafValues.FromOperand(new PqsOperand(PqsLeafKind.Time, DateTimeOffset.MaxValue))
            .Should().Be(PqsLeafValues.Cast(PqsLeafKind.Time, "infinity"));
        PqsLeafValues.FromOperand(new PqsOperand(PqsLeafKind.Time, DateTimeOffset.MinValue))
            .Should().Be(PqsLeafValues.Cast(PqsLeafKind.Time, "-infinity"));
    }

    [Fact]
    public void FromOperand_Time_truncates_sub_microsecond_ticks()
    {
        var operand = new PqsOperand(PqsLeafKind.Time, new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero).AddTicks(19));

        PqsLeafValues.FromOperand(operand).Should().Be(PqsLeafValues.Cast(PqsLeafKind.Time, "2026-05-29T13:30:00.000001Z"));
    }

    [Fact]
    public void FromOperand_Time_truncates_toward_the_past_before_the_epoch()
    {
        var operand = new PqsOperand(PqsLeafKind.Time, new DateTimeOffset(1960, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-1));

        PqsLeafValues.FromOperand(operand).Should().Be(PqsLeafValues.Cast(PqsLeafKind.Time, "1959-12-31T23:59:59.999999Z"));
    }

    [Fact]
    public void Cast_Time_reads_offsets_as_the_same_instant()
    {
        PqsLeafValues.Cast(PqsLeafKind.Time, "2026-05-29T15:30:00+02:00").Should().Be(PqsLeafValues.Cast(PqsLeafKind.Time, "2026-05-29T13:30:00Z"));
    }

    [Fact]
    public void Cast_Time_orders_by_instant()
    {
        PqsLeafValues.Cast(PqsLeafKind.Time, "2026-05-29T13:30:00.000001Z")
            .CompareTo(PqsLeafValues.Cast(PqsLeafKind.Time, "2026-05-29T13:30:00.000002Z")).Should().BeNegative();
    }

    [Fact]
    public void Cast_Time_rejects_a_non_timestamp()
    {
        var act = () => PqsLeafValues.Cast(PqsLeafKind.Time, "yesterday-ish");

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("Party")]
    [InlineData("ContractId")]
    [InlineData("Enum")]
    public void Cast_leaves_textual_kinds_untouched(string kindName)
    {
        PqsLeafValues.Cast(Enum.Parse<PqsLeafKind>(kindName), " Mixed Case ").Should().Be(" Mixed Case ");
    }
}
