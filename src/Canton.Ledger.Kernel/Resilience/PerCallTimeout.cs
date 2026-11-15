// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;

namespace Canton.Ledger.Kernel.Resilience;

/// <summary>
/// The one rule both transports apply to a call's timeout before it becomes a deadline.
/// <see cref="Timeout.InfiniteTimeSpan"/> applies no deadline, zero or any other negative value is
/// a caller error thrown at the call by <see cref="ThrowIfInvalid"/>, and a deadline longer than a
/// timer can wait is shortened to that wait.
/// </summary>
internal static class PerCallTimeout
{
    private static readonly TimeSpan LongestTimerWait = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// Rejects a per-call <paramref name="timeout"/> that is zero or negative and is not
    /// <see cref="Timeout.InfiniteTimeSpan"/>. Public members that take a timeout call it before
    /// anything else, so the caller error is thrown at the call and no task exists.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is zero or negative and is not <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    internal static void ThrowIfInvalid(TimeSpan? timeout) => PerCallTimeoutRule.ThrowIfInvalid(timeout);

    /// <summary>
    /// Resolves the deadline window for one attempt: <paramref name="timeout"/> when the call names
    /// one, otherwise <paramref name="clientTimeout"/>. Returns <see langword="null"/> when the call
    /// carries no deadline.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is zero or negative and is not <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    internal static TimeSpan? Resolve(TimeSpan? timeout, TimeSpan? clientTimeout)
    {
        ThrowIfInvalid(timeout);

        return (timeout ?? clientTimeout) switch
        {
            null => null,
            var window when window == Timeout.InfiniteTimeSpan => null,
            var window when window > LongestTimerWait => LongestTimerWait,
            var window => window,
        };
    }
}
