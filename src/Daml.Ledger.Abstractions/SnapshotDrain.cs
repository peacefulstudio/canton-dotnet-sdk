// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions;

internal static class SnapshotDrain
{
    public static async Task<List<TRow>> DrainAsync<TEntry, TRow>(
        IAsyncEnumerable<TEntry> entries,
        ISnapshotArmReader<TEntry, TRow> reader,
        SnapshotSubject subject,
        CancellationToken cancellationToken)
    {
        var rows = new List<TRow>();

        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (reader.Read(entry))
            {
                case SnapshotArm<TRow>.Row row:
                    rows.Add(row.Value);
                    break;
                case SnapshotArm<TRow>.Checkpoint:
                    return rows;
                case SnapshotArm<TRow>.Fault fault:
                    cancellationToken.ThrowIfCancellationRequested();
                    throw LedgerOperationException.FromStreamFault(
                        $"The active-contract-set snapshot for {subject.Name} faulted after {rows.Count} "
                        + $"{subject.RowNoun}(s): {fault.Message}. Use {subject.ValueShapedAlternative} for "
                        + "value-shaped fault handling.",
                        fault.Status,
                        fault.Category,
                        fault.SourceException,
                        fault.ErrorId);
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

        cancellationToken.ThrowIfCancellationRequested();
        throw new LedgerOperationException(
            $"The active-contract-set snapshot for {subject.Name} ended after {rows.Count} {subject.RowNoun}(s) "
            + $"without its terminal checkpoint, so the returned {subject.RowNoun}s would be incomplete.");
    }

    private static string DescribeKind<TRow>(SnapshotArm<TRow>.Unclassified unclassified) =>
        unclassified.RawKind is { Length: > 0 } rawKind
            ? $"{unclassified.Kind}: '{rawKind}'"
            : unclassified.Kind.ToString();

    private static string DescribeOffset(LedgerOffset? offset) =>
        offset is { } present ? $"offset {present.Value}" : "an unreported offset";
}
