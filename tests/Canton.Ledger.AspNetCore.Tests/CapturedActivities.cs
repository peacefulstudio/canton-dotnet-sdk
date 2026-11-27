// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

internal sealed class CapturedActivities : IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly List<Activity> _stopped = [];
    private readonly List<(string Tag, string Value, TaskCompletionSource<Activity> Stopped)> _waiters = [];
    private readonly ActivityTraceId _traceId;
    private readonly ActivityListener _listener;

    internal CapturedActivities(ActivityTraceId traceId)
    {
        _traceId = traceId;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    internal Task<Activity> RequestActivityFailingWithAsync(string errorId) =>
        RequestActivityTaggedAsync("daml.error_id", errorId);

    internal Task<Activity> RequestActivityFailingInCategoryAsync(string category) =>
        RequestActivityTaggedAsync("daml.error_category", category);

    private async Task<Activity> RequestActivityTaggedAsync(string tag, string value)
    {
        Task<Activity> stopped;
        lock (_stopped)
        {
            var alreadyStopped = _stopped.SingleOrDefault(activity => IsTagged(activity, tag, value));
            if (alreadyStopped is not null)
                return alreadyStopped;

            var waiter = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((tag, value, waiter));
            stopped = waiter.Task;
        }

        return await stopped.WaitAsync(StopTimeout, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _listener.Dispose();

    private bool IsTagged(Activity activity, string tag, string value) =>
        activity.TraceId == _traceId && activity.GetTagItem(tag) as string == value;

    private void OnStopped(Activity activity)
    {
        lock (_stopped)
        {
            _stopped.Add(activity);
            foreach (var waiter in _waiters.Where(waiter => IsTagged(activity, waiter.Tag, waiter.Value)))
                waiter.Stopped.TrySetResult(activity);
        }
    }
}
