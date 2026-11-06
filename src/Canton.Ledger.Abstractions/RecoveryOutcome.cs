// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// What became of a command whose write failed with <c>CommitState.Unknown</c>, as resolved by
/// <see cref="RecoveryExtensions.RecoverAsync"/>. Discriminated union: callers <c>switch</c> on the
/// concrete subtype.
/// </summary>
public abstract record RecoveryOutcome
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected RecoveryOutcome() { }

    /// <summary>The participant accepted the command: it is on the ledger.</summary>
    /// <param name="UpdateId">The id of the update the accepted command produced.</param>
    /// <param name="Completion">The accepting completion.</param>
    public sealed record Committed(string UpdateId, Completion Completion) : RecoveryOutcome;

    /// <summary>
    /// The participant rejected the command and nothing accepted it afterwards: it is not on the
    /// ledger.
    /// </summary>
    /// <param name="Completion">The latest rejecting completion.</param>
    /// <param name="Status">The rejection verdict of <paramref name="Completion"/>.</param>
    public sealed record Rejected(Completion Completion, CompletionStatus Status) : RecoveryOutcome;

    /// <summary>
    /// No completion arrived before every awaited synchronizer's checkpoint passed the command's
    /// maximum record time, so the command never reached the ledger.
    /// </summary>
    public sealed record Lost : RecoveryOutcome;
}
