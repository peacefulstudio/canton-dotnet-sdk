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
}
