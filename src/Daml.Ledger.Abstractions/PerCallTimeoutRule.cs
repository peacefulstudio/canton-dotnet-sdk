// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// The one rule every public member that takes a per-call <c>timeout</c> applies to a degenerate
/// value, shared by the writer helpers here, both transports' clients and the test fakes so a
/// caller sees the same error wherever the timeout is read.
/// </summary>
internal static class PerCallTimeoutRule
{
    /// <summary>
    /// Rejects a per-call <paramref name="timeout"/> that is zero or negative and is not
    /// <see cref="Timeout.InfiniteTimeSpan"/>. A public member calls it before anything else, so the
    /// caller error is thrown at the call and no task exists.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is zero or negative and is not <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    internal static void ThrowIfInvalid(TimeSpan? timeout)
    {
        if (timeout is { } requested && requested != Timeout.InfiniteTimeSpan && requested <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                requested,
                "A per-call timeout must be positive, or Timeout.InfiniteTimeSpan for no deadline.");
        }
    }
}
