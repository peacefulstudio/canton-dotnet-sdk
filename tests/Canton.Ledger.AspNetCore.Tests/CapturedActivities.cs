// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

internal sealed class CapturedActivities : IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly List<Activity> _stopped = [];
    private readonly List<(string ErrorId, TaskCompletionSource<Activity> Stopped)> _waiters = [];
    private readonly ActivityListener _listener;

    internal CapturedActivities()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    internal async Task<Activity> RequestActivityFailingWithAsync(string errorId)
    {
        Task<Activity> stopped;
        lock (_stopped)
        {
            var alreadyStopped = _stopped.SingleOrDefault(activity => IsFailingWith(activity, errorId));
            if (alreadyStopped is not null)
                return alreadyStopped;

            var waiter = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((errorId, waiter));
            stopped = waiter.Task;
        }

        return await stopped.WaitAsync(StopTimeout, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _listener.Dispose();

    private static bool IsFailingWith(Activity activity, string errorId) =>
        activity.GetTagItem("daml.error_id") as string == errorId;

    private void OnStopped(Activity activity)
    {
        lock (_stopped)
        {
            _stopped.Add(activity);
            foreach (var waiter in _waiters.Where(waiter => IsFailingWith(activity, waiter.ErrorId)))
                waiter.Stopped.TrySetResult(activity);
        }
    }
}
