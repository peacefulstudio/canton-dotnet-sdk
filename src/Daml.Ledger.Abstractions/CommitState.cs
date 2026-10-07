// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// Whether a failed <see cref="LedgerOperationException"/> operation's command committed to
/// the ledger. See <see cref="LedgerOperationException.CommitState"/> for the mapping from
/// each kind of call and each <see cref="ExerciseOutcome{T}"/> case.
/// </summary>
public enum CommitState
{
    /// <summary>
    /// The command did not commit. Retrying is safe.
    /// </summary>
    NotCommitted,

    /// <summary>
    /// The command committed to the ledger. Do not resubmit it.
    /// </summary>
    Committed,

    /// <summary>
    /// Whether the command committed could not be determined — a transport failure occurred
    /// after the command was sent (a timeout or a dropped connection),
    /// or the ledger reported <see cref="DamlErrorCategory.DeadlineExceededRequestStateUnknown"/>.
    /// Retry only with the same command id: command-id deduplication resolves the duplicate if
    /// the original request did commit.
    /// </summary>
    Unknown,
}
