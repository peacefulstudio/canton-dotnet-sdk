// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class RetryPipelineFactoryTests
{
    [Fact]
    public void RetryPipelineFactory_is_internal() =>
        typeof(RetryPipelineFactory).IsPublic.Should().BeFalse();

    [Fact]
    public void Create_returns_the_empty_pipeline_when_RetryOptions_disabled_by_default()
    {
        var pipeline = RetryPipelineFactory.Create(new RetryOptions());

        pipeline.Should().BeSameAs(ResiliencePipeline.Empty);
    }

    [Fact]
    public void Create_returns_the_empty_pipeline_when_enabled_with_zero_retry_attempts()
    {
        var pipeline = RetryPipelineFactory.Create(new RetryOptions { Enabled = true, MaxRetryAttempts = 0 });

        pipeline.Should().BeSameAs(ResiliencePipeline.Empty);
    }

    [Fact]
    public void Create_builds_a_pipeline_when_enabled_with_a_delay_of_exactly_one_day()
    {
        var act = () => RetryPipelineFactory.Create(
            new RetryOptions { Enabled = true, MaxRetryAttempts = 1, Delay = TimeSpan.FromDays(1), MaxDelay = TimeSpan.FromDays(1) });

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_throws_when_options_null()
    {
        var act = () => RetryPipelineFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Create_retries_the_configured_number_of_times_when_enabled()
    {
        var pipeline = RetryPipelineFactory.Create(new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = 2,
            Delay = TimeSpan.Zero
        });

        var attempts = 0;

        await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts <= 2)
                throw new InvalidOperationException("transient");

            return ValueTask.CompletedTask;
        }, TestContext.Current.CancellationToken);

        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Create_applies_jitter_so_repeated_first_retry_delays_are_not_identical()
    {
        var observedDelays = new List<TimeSpan>();

        var pipeline = RetryPipelineFactory.Create(
            new RetryOptions
            {
                Enabled = true,
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(5)
            },
            onRetry: attempt => observedDelays.Add(attempt.RetryDelay));

        for (var i = 0; i < 25; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(
                    _ => throw new InvalidOperationException("transient"),
                    TestContext.Current.CancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
        }

        observedDelays.Should().HaveCount(25);
        observedDelays.Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task Create_waits_for_the_supplied_TimeProvider_to_advance_before_retrying()
    {
        var time = new FakeTimeProvider();
        var pipeline = RetryPipelineFactory.Create(
            new RetryOptions { Enabled = true, MaxRetryAttempts = 1, Delay = TimeSpan.FromHours(1), MaxDelay = TimeSpan.FromHours(1) },
            timeProvider: time);
        var attempts = 0;
        var firstAttemptFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = pipeline.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts != 1)
                    return ValueTask.CompletedTask;

                firstAttemptFinished.SetResult();
                throw new InvalidOperationException("transient");
            },
            TestContext.Current.CancellationToken).AsTask();
        await firstAttemptFinished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        attempts.Should().Be(1);
        execution.IsCompleted.Should().BeFalse();

        using var ceiling = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!execution.IsCompleted)
        {
            ceiling.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromHours(1));
            await Task.Yield();
        }

        await execution;

        attempts.Should().Be(2);
    }

    [Fact]
    public async Task Create_caps_every_exponential_backoff_delay_at_MaxDelay()
    {
        var time = new FakeTimeProvider();
        var observedDelays = new List<TimeSpan>();
        var pipeline = RetryPipelineFactory.Create(
            new RetryOptions
            {
                Enabled = true,
                MaxRetryAttempts = 8,
                Delay = TimeSpan.FromHours(1),
                MaxDelay = TimeSpan.FromHours(2),
            },
            onRetry: attempt => observedDelays.Add(attempt.RetryDelay),
            timeProvider: time);

        var execution = pipeline.ExecuteAsync(
            _ => throw new InvalidOperationException("transient"),
            TestContext.Current.CancellationToken).AsTask();

        using var ceiling = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!execution.IsCompleted)
        {
            ceiling.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromHours(2));
            await Task.Yield();
        }

        await execution.Invoking(e => e).Should().ThrowAsync<InvalidOperationException>();
        observedDelays.Should().HaveCount(8)
            .And.OnlyContain(delay => delay <= TimeSpan.FromHours(2));
        observedDelays[^1].Should().Be(TimeSpan.FromHours(2));
    }
}
