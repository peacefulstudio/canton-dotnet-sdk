// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Localnet;

/// <summary>
/// Keeps a change to a Ledger user's rights from landing while a stream authorized as that user is
/// open in this process. A participant ends every open stream of a user whose rights change with
/// <c>STALE_STREAM_AUTHORIZATION</c>, and xUnit runs test classes in parallel, so without the gate
/// one class's grant or revoke ends another class's stream.
/// </summary>
/// <remarks>
/// <para>
/// A reader-writer gate keyed by user id. A stream takes the shared side through
/// <see cref="HoldStreamAsync"/> for as long as it is open; <see cref="ActAsRightsLease"/> takes the
/// exclusive side around each grant and each revoke. A rights change waits until every stream open
/// on its user has closed. Waiters are admitted in arrival order, so a stream that asks to open
/// while a rights change is waiting queues behind it, and a steady run of overlapping streams cannot
/// starve the change.
/// </para>
/// <para>
/// A flow is the async flow that called <see cref="HoldStreamAsync"/> together with every flow it
/// starts afterwards. A flow that already holds a stream on a user opens another one on that user
/// without queueing, since a waiting change could never be admitted before the first stream closes.
/// A rights change asked for by a flow that holds a stream on the same user throws rather than wait
/// on itself. Call <see cref="HoldStreamAsync"/> from the method that reads the stream, not through
/// an <c>async</c> wrapper: the wrapper's flow ends when it returns, and a change its caller then
/// asks for waits on the caller's own stream until the wait limit ends it with a
/// <see cref="TimeoutException"/>.
/// </para>
/// </remarks>
public sealed class LedgerUserRightsGate
{
    private static readonly TimeSpan SharedWaitLimit = TimeSpan.FromMinutes(5);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, UserAdmissions> _users = new(StringComparer.Ordinal);
    private readonly AsyncLocal<FlowStreams?> _flowStreams = new();
    private readonly TimeSpan _waitLimit;

    internal LedgerUserRightsGate(TimeSpan waitLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(waitLimit, TimeSpan.Zero);
        _waitLimit = waitLimit;
    }

    /// <summary>The gate every <see cref="ActAsRightsLease"/> and stream in this process shares.</summary>
    public static LedgerUserRightsGate Shared { get; } = new(SharedWaitLimit);

    /// <summary>
    /// Waits until no rights change on <paramref name="userId"/> is running or waiting ahead of this
    /// call, then holds the shared side until the returned hold is disposed. Dispose it once the
    /// stream has closed.
    /// </summary>
    /// <exception cref="TimeoutException">The gate did not admit the stream within its wait limit.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled first.</exception>
    public ValueTask<StreamHold> HoldStreamAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        cancellationToken.ThrowIfCancellationRequested();
        var flow = _flowStreams.Value ??= new FlowStreams();

        Waiter waiter;
        lock (_sync)
        {
            var user = AdmissionsFor(userId);
            if (user.AdmitsStreamNow(flow.HoldsOn(userId)))
            {
                return ValueTask.FromResult(user.OpenStream(this, flow));
            }

            waiter = user.Enqueue(Waiter.ForStream(flow));
        }

        return AwaitStreamAsync(userId, waiter, cancellationToken);
    }

    internal ValueTask<RightsChange> BeginRightsChangeAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        cancellationToken.ThrowIfCancellationRequested();
        var flow = _flowStreams.Value;

        Waiter waiter;
        lock (_sync)
        {
            if (flow?.HoldsOn(userId) == true)
            {
                throw new InvalidOperationException(
                    $"A rights change on Ledger user '{userId}' was asked for by a flow that holds a stream on "
                    + "that user. The change would wait for that stream to close, and the participant would end "
                    + "the stream with STALE_STREAM_AUTHORIZATION if it did not; close the stream first.");
            }

            var user = AdmissionsFor(userId);
            if (user.AdmitsChangeNow())
            {
                return ValueTask.FromResult(user.BeginChange(this));
            }

            waiter = user.Enqueue(Waiter.ForChange());
        }

        return AwaitChangeAsync(userId, waiter, cancellationToken);
    }

    internal RightsGateState StateOf(string userId)
    {
        lock (_sync)
        {
            return _users.TryGetValue(userId, out var user)
                ? new RightsGateState(user.OpenStreams, user.Changing, user.WaitingChanges, user.WaitingStreams)
                : new RightsGateState(0, false, 0, 0);
        }
    }

    private async ValueTask<StreamHold> AwaitStreamAsync(string userId, Waiter waiter, CancellationToken cancellationToken)
    {
        await AwaitAdmissionAsync(userId, waiter, "a stream", cancellationToken).ConfigureAwait(false);
        return new StreamHold(this, userId, waiter.Flow!);
    }

    private async ValueTask<RightsChange> AwaitChangeAsync(string userId, Waiter waiter, CancellationToken cancellationToken)
    {
        await AwaitAdmissionAsync(userId, waiter, "a rights change", cancellationToken).ConfigureAwait(false);
        return new RightsChange(this, userId);
    }

    private async Task AwaitAdmissionAsync(
        string userId, Waiter waiter, string admitting, CancellationToken cancellationToken)
    {
        using var waitLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitLimit.CancelAfter(_waitLimit);
        try
        {
            await waiter.Admitted.WaitAsync(waitLimit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Abandon(userId, waiter);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"The rights gate did not admit {admitting} on Ledger user '{userId}' within {_waitLimit}; "
                + $"when it gave up the user had {DescribeState(userId)}.");
        }
    }

    private void Abandon(string userId, Waiter waiter)
    {
        lock (_sync)
        {
            var user = _users[userId];
            if (waiter.Admitted.IsCompleted)
            {
                user.Release(waiter);
            }
            else
            {
                user.Withdraw(waiter);
            }

            user.AdmitWaiters();
        }
    }

    private string DescribeState(string userId)
    {
        var state = StateOf(userId);
        return $"{state.OpenStreams} open stream(s), {(state.Changing ? "a rights change running" : "no rights change running")}, "
            + $"{state.WaitingChanges} rights change(s) and {state.WaitingStreams} stream(s) waiting";
    }

    internal void CloseStream(string userId, FlowStreams flow)
    {
        lock (_sync)
        {
            var user = _users[userId];
            user.CloseStream(flow);
            user.AdmitWaiters();
        }
    }

    private void EndChange(string userId)
    {
        lock (_sync)
        {
            var user = _users[userId];
            user.EndChange();
            user.AdmitWaiters();
        }
    }

    private UserAdmissions AdmissionsFor(string userId)
    {
        if (!_users.TryGetValue(userId, out var user))
        {
            user = new UserAdmissions(userId);
            _users.Add(userId, user);
        }

        return user;
    }

    internal sealed class RightsChange : IDisposable
    {
        private readonly LedgerUserRightsGate _gate;
        private readonly string _userId;
        private int _ended;

        internal RightsChange(LedgerUserRightsGate gate, string userId)
        {
            _gate = gate;
            _userId = userId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                _gate.EndChange(_userId);
            }
        }
    }

    internal sealed class FlowStreams
    {
        private readonly Dictionary<string, int> _openByUser = new(StringComparer.Ordinal);

        internal bool HoldsOn(string userId) => _openByUser.ContainsKey(userId);

        internal void Opened(string userId) =>
            _openByUser[userId] = _openByUser.GetValueOrDefault(userId) + 1;

        internal void Closed(string userId)
        {
            var remaining = _openByUser[userId] - 1;
            if (remaining == 0)
            {
                _openByUser.Remove(userId);
            }
            else
            {
                _openByUser[userId] = remaining;
            }
        }
    }

    private sealed class Waiter
    {
        private Waiter(FlowStreams? flow) => Flow = flow;

        internal FlowStreams? Flow { get; }

        internal bool IsChange => Flow is null;

        internal Task Admitted => Admission.Task;

        internal TaskCompletionSource Admission { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static Waiter ForStream(FlowStreams flow) => new(flow);

        internal static Waiter ForChange() => new(null);
    }

    private sealed class UserAdmissions(string userId)
    {
        private readonly LinkedList<Waiter> _queue = new();

        internal int OpenStreams { get; private set; }

        internal bool Changing { get; private set; }

        internal int WaitingChanges => _queue.Count(waiter => waiter.IsChange);

        internal int WaitingStreams => _queue.Count(waiter => !waiter.IsChange);

        internal bool AdmitsStreamNow(bool flowHoldsAStream) =>
            !Changing && (flowHoldsAStream || WaitingChanges == 0);

        internal bool AdmitsChangeNow() => !Changing && OpenStreams == 0 && _queue.Count == 0;

        internal Waiter Enqueue(Waiter waiter)
        {
            _queue.AddLast(waiter);
            return waiter;
        }

        internal StreamHold OpenStream(LedgerUserRightsGate gate, FlowStreams flow)
        {
            OpenStreams++;
            flow.Opened(userId);
            return new StreamHold(gate, userId, flow);
        }

        internal RightsChange BeginChange(LedgerUserRightsGate gate)
        {
            Changing = true;
            return new RightsChange(gate, userId);
        }

        internal void CloseStream(FlowStreams flow)
        {
            OpenStreams--;
            flow.Closed(userId);
        }

        internal void EndChange() => Changing = false;

        internal void Withdraw(Waiter waiter) => _queue.Remove(waiter);

        internal void Release(Waiter waiter)
        {
            if (waiter.IsChange)
            {
                EndChange();
            }
            else
            {
                CloseStream(waiter.Flow!);
            }
        }

        internal void AdmitWaiters()
        {
            while (_queue.First is { } next && !Changing)
            {
                var waiter = next.Value;
                if (waiter.IsChange)
                {
                    if (OpenStreams > 0)
                    {
                        return;
                    }

                    Changing = true;
                }
                else
                {
                    OpenStreams++;
                    waiter.Flow!.Opened(userId);
                }

                _queue.RemoveFirst();
                waiter.Admission.SetResult();
            }
        }
    }
}
