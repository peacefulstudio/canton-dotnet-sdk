// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

public sealed class CapturedActivitiesTests
{
    private static readonly ActivitySource HostingSource = new("Microsoft.AspNetCore");

    private static Activity StartRequestIn(ActivityTraceId traceId) =>
        HostingSource.StartActivity(
            "request",
            ActivityKind.Server,
            new ActivityContext(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded))!;

    [Fact]
    public async Task RequestActivityFailingWithAsync_waits_for_the_activity_to_stop()
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var activities = new CapturedActivities(traceId);
        var activity = StartRequestIn(traceId);
        activity.SetTag("daml.error_id", "PROBE_WAITS_FOR_STOP");

        var waiting = activities.RequestActivityFailingWithAsync("PROBE_WAITS_FOR_STOP");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        waiting.IsCompleted.Should().BeFalse();
        activity.Stop();

        var stopped = await waiting;
        stopped.Should().BeSameAs(activity);
    }

    [Fact]
    public async Task RequestActivityFailingWithAsync_returns_an_activity_that_stopped_before_the_call()
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var activities = new CapturedActivities(traceId);
        var activity = StartRequestIn(traceId);
        activity.SetTag("daml.error_id", "PROBE_STOPPED_BEFORE_CALL");
        activity.Stop();

        var stopped = await activities.RequestActivityFailingWithAsync("PROBE_STOPPED_BEFORE_CALL");

        stopped.Should().BeSameAs(activity);
    }

    [Fact]
    public async Task RequestActivityFailingWithAsync_ignores_activities_failing_with_another_error_id()
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var activities = new CapturedActivities(traceId);
        var other = StartRequestIn(traceId);
        other.SetTag("daml.error_id", "PROBE_OTHER_ERROR");
        other.Stop();
        var wanted = StartRequestIn(traceId);
        wanted.SetTag("daml.error_id", "PROBE_WANTED_ERROR");
        wanted.Stop();

        var stopped = await activities.RequestActivityFailingWithAsync("PROBE_WANTED_ERROR");

        stopped.Should().BeSameAs(wanted);
    }

    [Fact]
    public async Task RequestActivityFailingWithAsync_ignores_the_activity_of_another_trace_failing_with_the_same_error_id()
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var activities = new CapturedActivities(traceId);
        var foreign = StartRequestIn(ActivityTraceId.CreateRandom());
        foreign.SetTag("daml.error_id", "PROBE_SHARED_ERROR");
        foreign.Stop();
        var own = StartRequestIn(traceId);
        own.SetTag("daml.error_id", "PROBE_SHARED_ERROR");
        own.Stop();

        var stopped = await activities.RequestActivityFailingWithAsync("PROBE_SHARED_ERROR");

        stopped.Should().BeSameAs(own);
    }
}
