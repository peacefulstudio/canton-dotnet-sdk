// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions;

internal static class SnapshotDrain
{
    public static async Task<ActiveSnapshot<TRow>> DrainAsync<TEntry, TRow>(
        Func<CancellationToken, IAsyncEnumerable<TEntry>> openStream,
        ISnapshotArmReader<TEntry, TRow> reader,
        SnapshotSubject subject,
        TimeSpan? timeout,
        UnreadableRows unreadableRows,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var rows = new List<TRow>();
        var unreadable = new List<UnreadableRow>();
        SnapshotArm<TRow>? previousArm = null;
        using var deadline = timeout is { } limit
            ? new CancellationTokenSource(limit, timeProvider)
            : new CancellationTokenSource();
        using var interruption = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await foreach (var entry in openStream(interruption.Token)
                .WithCancellation(interruption.Token).ConfigureAwait(false))
            {
                var arm = reader.Read(entry);
                var inFlightReassignmentOfPreviousRow = IsUnassignedRowOfPreviousRow(previousArm, arm);
                previousArm = inFlightReassignmentOfPreviousRow ? null : arm;
                switch (arm)
                {
                    case SnapshotArm<TRow>.Row row:
                        rows.Add(row.Value);
                        break;
                    case SnapshotArm<TRow>.Checkpoint:
                        return new ActiveSnapshot<TRow>(rows, unreadable);
                    case SnapshotArm<TRow>.Fault fault:
                        ThrowIfInterrupted();
                        throw LedgerOperationException.FromStreamFault(
                            $"The active-contract-set snapshot for {subject.Name} faulted after {rows.Count} "
                            + $"{subject.RowNoun}(s): {fault.Message}. Use {subject.ValueShapedAlternative} for "
                            + "value-shaped fault handling.",
                            fault.Status,
                            fault.Category,
                            fault.SourceException,
                            fault.ErrorId);
                    case SnapshotArm<TRow>.Unclassified when inFlightReassignmentOfPreviousRow:
                        break;
                    case SnapshotArm<TRow>.Unclassified unclassified when unreadableRows == UnreadableRows.Return:
                        unreadable.Add(new UnreadableRow(unclassified.Kind, unclassified.RawKind, unclassified.Offset));
                        break;
                    case SnapshotArm<TRow>.Unclassified unclassified:
                        throw new LedgerOperationException(
                            $"The active-contract-set snapshot for {subject.Name} carried an unclassified row "
                            + $"({DescribeKind(unclassified)}) at {DescribeOffset(unclassified.Offset)}, so the "
                            + $"returned {subject.RowNoun}s would be incomplete. Use {subject.ValueShapedAlternative} "
                            + "to handle it as a value.");
                    case SnapshotArm<TRow>.Unrecognised unrecognised:
                        throw new LedgerOperationException(
                            $"Unexpected snapshot entry {unrecognised.EntryTypeName} for {subject.Name}.");
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw DeadlineExceeded();
        }

        ThrowIfInterrupted();
        throw new LedgerOperationException(
            $"The active-contract-set snapshot for {subject.Name} ended after {rows.Count} {subject.RowNoun}(s) "
            + $"without its terminal checkpoint, so the returned {subject.RowNoun}s would be incomplete.");

        void ThrowIfInterrupted()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
            {
                throw DeadlineExceeded();
            }
        }

        LedgerOperationException DeadlineExceeded() =>
            LedgerOperationException.FromFailedCall(
                $"The active-contract-set snapshot for {subject.Name} did not reach its terminal checkpoint "
                + $"within {timeout} after {rows.Count} {subject.RowNoun}(s).",
                new TransportStatus.NoResponse(),
                DamlErrorCategory.DeadlineExceededRequestStateUnknown,
                errorId: null,
                metadata: null,
                innerException: null,
                CommitState.NotCommitted);
    }

    private static bool IsUnassignedRowOfPreviousRow<TRow>(SnapshotArm<TRow>? previous, SnapshotArm<TRow> arm) =>
        previous is SnapshotArm<TRow>.Row
        && arm is SnapshotArm<TRow>.Unclassified { Kind: UnclassifiedKind.UnassignedEvent };

    private static string DescribeKind<TRow>(SnapshotArm<TRow>.Unclassified unclassified) =>
        unclassified.RawKind is { Length: > 0 } rawKind
            ? $"{unclassified.Kind}: '{rawKind}'"
            : unclassified.Kind.ToString();

    private static string DescribeOffset(LedgerOffset? offset) =>
        offset is { } present ? $"offset {present.Value}" : "an unreported offset";
}
