// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public class PerCallTimeoutRuleTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-50_000L)]
    [InlineData(-20_000L)]
    [InlineData(long.MinValue)]
    public void ThrowIfInvalid_rejects_a_timeout_that_is_zero_or_negative_and_not_infinite(long ticks)
    {
        var act = () => PerCallTimeoutRule.ThrowIfInvalid(TimeSpan.FromTicks(ticks));

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("timeout");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-10_000L)]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void ThrowIfInvalid_accepts_no_timeout_an_infinite_one_and_any_positive_one(long? ticks)
    {
        var act = () => PerCallTimeoutRule.ThrowIfInvalid(ticks is { } value ? TimeSpan.FromTicks(value) : null);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-300_000_000L)]
    [InlineData(long.MinValue)]
    [InlineData(42_949_672_950_000L)]
    [InlineData(51_840_000_000_000L)]
    [InlineData(long.MaxValue)]
    public void ThrowIfInvalidDrainWindow_rejects_a_negative_timeout_that_is_not_infinite_or_one_past_the_timer_limit(long ticks)
    {
        var act = () => PerCallTimeoutRule.ThrowIfInvalidDrainWindow(TimeSpan.FromTicks(ticks));

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("timeout");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-10_000L)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(42_949_672_940_000L)]
    public void ThrowIfInvalidDrainWindow_accepts_no_timeout_an_infinite_one_zero_and_a_positive_one_within_the_timer_limit(long? ticks)
    {
        var act = () => PerCallTimeoutRule.ThrowIfInvalidDrainWindow(ticks is { } value ? TimeSpan.FromTicks(value) : null);

        act.Should().NotThrow();
    }
}
