// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

public sealed class CapturedActivitiesTests
{
    private static readonly ActivitySource HostingSource = new("Microsoft.AspNetCore");

    [Fact]
    public async Task RequestActivityFailingWithAsync_waits_for_the_activity_to_stop()
    {
        using var activities = new CapturedActivities();
        var activity = HostingSource.StartActivity("request")!;
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
        using var activities = new CapturedActivities();
        var activity = HostingSource.StartActivity("request")!;
        activity.SetTag("daml.error_id", "PROBE_STOPPED_BEFORE_CALL");
        activity.Stop();

        var stopped = await activities.RequestActivityFailingWithAsync("PROBE_STOPPED_BEFORE_CALL");

        stopped.Should().BeSameAs(activity);
    }

    [Fact]
    public async Task RequestActivityFailingWithAsync_ignores_activities_failing_with_another_error_id()
    {
        using var activities = new CapturedActivities();
        var other = HostingSource.StartActivity("other")!;
        other.SetTag("daml.error_id", "PROBE_OTHER_ERROR");
        other.Stop();
        var wanted = HostingSource.StartActivity("request")!;
        wanted.SetTag("daml.error_id", "PROBE_WANTED_ERROR");
        wanted.Stop();

        var stopped = await activities.RequestActivityFailingWithAsync("PROBE_WANTED_ERROR");

        stopped.Should().BeSameAs(wanted);
    }
}
