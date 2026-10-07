// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

internal static class ParticipantRejectionCommitState
{
    private const string DuplicateCommandErrorId = "DUPLICATE_COMMAND";
    private const string SubmissionAlreadyInFlightErrorId = "SUBMISSION_ALREADY_IN_FLIGHT";
    private const string AcceptedMetadataKey = "accepted";

    internal static CommitState Of(
        DamlErrorCategory category,
        string? errorId,
        IReadOnlyDictionary<string, string>? metadata) => errorId switch
    {
        DuplicateCommandErrorId => OfDuplicateCommand(metadata),
        SubmissionAlreadyInFlightErrorId => CommitState.Unknown,
        _ => OfCategory(category),
    };

    private static CommitState OfDuplicateCommand(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is not null
        && metadata.TryGetValue(AcceptedMetadataKey, out var accepted)
        && string.Equals(accepted, "false", StringComparison.OrdinalIgnoreCase)
            ? CommitState.Unknown
            : CommitState.Committed;

    private static CommitState OfCategory(DamlErrorCategory category) =>
        category is DamlErrorCategory.DeadlineExceededRequestStateUnknown or DamlErrorCategory.Unknown
            ? CommitState.Unknown
            : CommitState.NotCommitted;
}
