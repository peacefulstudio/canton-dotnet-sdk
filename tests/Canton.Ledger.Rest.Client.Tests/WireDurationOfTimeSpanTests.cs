// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class WireDurationOfTimeSpanTests
{
    [Theory]
    [InlineData(0, "0s")]
    [InlineData(500, "0.500s")]
    [InlineData(1_500, "1.500s")]
    [InlineData(5_000, "5s")]
    public void CanonicalOf_renders_a_time_span_in_the_proto3_canonical_form(int milliseconds, string expected)
    {
        WireDuration.CanonicalOf(TimeSpan.FromMilliseconds(milliseconds)).Should().Be(expected);
    }

    [Fact]
    public void CanonicalOf_renders_a_sub_millisecond_time_span_to_nine_digit_precision()
    {
        WireDuration.CanonicalOf(TimeSpan.FromTicks(1)).Should().Be("0.000000100s");
    }

    [Fact]
    public void CanonicalOf_renders_a_time_span_that_reads_back_as_the_parts_proto3_requires()
    {
        var rendered = WireDuration.CanonicalOf(TimeSpan.FromMilliseconds(1_500));

        WireDuration.PartsOf(rendered).Should().Be((1L, 500_000_000),
            "the participant reads the whole seconds and the fraction together");
    }

    [Fact]
    public void CanonicalOf_refuses_a_strictly_negative_time_span()
    {
        var act = () => WireDuration.CanonicalOf(TimeSpan.FromMilliseconds(-1));

        act.Should().Throw<ArgumentOutOfRangeException>(
            "a delay that has already elapsed has no wire representation");
    }

    [Fact]
    public void CanonicalOf_accepts_a_zero_time_span()
    {
        var act = () => WireDuration.CanonicalOf(TimeSpan.Zero);

        act.Should().NotThrow().Which.Should().Be("0s");
    }
}
