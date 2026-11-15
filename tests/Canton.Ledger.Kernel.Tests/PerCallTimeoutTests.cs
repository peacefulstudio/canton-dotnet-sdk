// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class PerCallTimeoutTests
{
    private static readonly TimeSpan ThirtySeconds = TimeSpan.FromSeconds(30);

    [Fact]
    public void Resolve_maps_an_infinite_timeout_to_no_deadline()
    {
        PerCallTimeout.Resolve(TimeSpan.FromMilliseconds(-1), ThirtySeconds).Should().BeNull();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-50_000L)]
    [InlineData(-20_000L)]
    [InlineData(long.MinValue)]
    public void Resolve_rejects_a_timeout_that_is_zero_or_negative_and_not_infinite(long ticks)
    {
        var act = () => PerCallTimeout.Resolve(TimeSpan.FromTicks(ticks), ThirtySeconds);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("timeout");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-50_000L)]
    [InlineData(-20_000L)]
    [InlineData(long.MinValue)]
    public void ThrowIfInvalid_rejects_a_timeout_that_is_zero_or_negative_and_not_infinite(long ticks)
    {
        var act = () => PerCallTimeout.ThrowIfInvalid(TimeSpan.FromTicks(ticks));

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("timeout");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-10_000L)]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void ThrowIfInvalid_accepts_no_timeout_an_infinite_one_and_any_positive_one(long? ticks)
    {
        var act = () => PerCallTimeout.ThrowIfInvalid(ticks is { } value ? TimeSpan.FromTicks(value) : null);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(4_294_967_295L)]
    [InlineData(5_184_000_000L)]
    public void Resolve_shortens_a_timeout_beyond_the_longest_timer_wait_to_that_wait(long milliseconds)
    {
        PerCallTimeout.Resolve(TimeSpan.FromMilliseconds(milliseconds), ThirtySeconds)
            .Should().Be(TimeSpan.FromMilliseconds(4_294_967_294));
    }

    [Fact]
    public void Resolve_shortens_the_largest_timeout_to_the_longest_timer_wait()
    {
        PerCallTimeout.Resolve(TimeSpan.MaxValue, ThirtySeconds).Should().Be(TimeSpan.FromMilliseconds(4_294_967_294));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(200L)]
    [InlineData(4_294_967_294L)]
    public void Resolve_keeps_a_positive_timeout_within_the_longest_timer_wait(long milliseconds)
    {
        PerCallTimeout.Resolve(TimeSpan.FromMilliseconds(milliseconds), ThirtySeconds)
            .Should().Be(TimeSpan.FromMilliseconds(milliseconds));
    }

    [Fact]
    public void Resolve_falls_back_to_the_client_timeout_when_the_call_names_none()
    {
        PerCallTimeout.Resolve(null, ThirtySeconds).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Resolve_applies_no_deadline_when_neither_the_call_nor_the_client_names_one()
    {
        PerCallTimeout.Resolve(null, null).Should().BeNull();
    }

    [Fact]
    public void Resolve_maps_an_infinite_client_timeout_to_no_deadline()
    {
        PerCallTimeout.Resolve(null, TimeSpan.FromMilliseconds(-1)).Should().BeNull();
    }

    [Fact]
    public void Resolve_shortens_a_client_timeout_beyond_the_longest_timer_wait_to_that_wait()
    {
        PerCallTimeout.Resolve(null, TimeSpan.MaxValue).Should().Be(TimeSpan.FromMilliseconds(4_294_967_294));
    }
}
