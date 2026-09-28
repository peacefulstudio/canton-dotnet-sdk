// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Commands;

/// <summary>
/// A command deduplication period, in both directions of the Ledger API: the period a
/// submission asks the participant to deduplicate over
/// (<see cref="CommandsSubmission.DeduplicationPeriod"/>, projected onto the
/// <c>Commands.deduplication_period</c> oneof), and the period a participant reported for a
/// completed command. Discriminated union: callers <c>switch</c> on the concrete subtype, so a
/// period is either an <see cref="Offset"/> or a <see cref="Duration"/> and never both — the
/// protobuf <c>deduplication_period</c> oneof made unrepresentable to misread.
/// </summary>
public abstract record DeduplicationPeriod
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected DeduplicationPeriod() { }

    /// <summary>
    /// The period starts at a completion-stream offset.
    /// </summary>
    /// <param name="Start">The offset the period starts after (exclusive);
    /// <see cref="LedgerOffset.Begin"/> when it starts at participant begin.</param>
    public sealed record Offset(LedgerOffset Start) : DeduplicationPeriod;

    /// <summary>
    /// The period is a length of time, interpreted relative to the participant's clock at
    /// some point during the submission's processing.
    /// </summary>
    /// <param name="Length">The length of the period.</param>
    public sealed record Duration(TimeSpan Length) : DeduplicationPeriod;
}
