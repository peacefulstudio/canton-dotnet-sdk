// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions;

internal abstract record SnapshotArm<TRow>
{
    private SnapshotArm() { }

    internal sealed record Row(TRow Value) : SnapshotArm<TRow>;

    internal sealed record Checkpoint : SnapshotArm<TRow>;

    internal sealed record Fault(
        TransportStatus Status,
        string Message,
        DamlErrorCategory? Category,
        string? ErrorId,
        Exception? SourceException) : SnapshotArm<TRow>;

    internal sealed record Unclassified(
        UnclassifiedKind Kind,
        string? RawKind,
        LedgerOffset? Offset) : SnapshotArm<TRow>;

    internal sealed record Unrecognised(string EntryTypeName) : SnapshotArm<TRow>;
}

internal sealed record SnapshotSubject(string Name, string RowNoun, string ValueShapedAlternative);

internal interface ISnapshotArmReader<in TEntry, TRow>
{
    SnapshotArm<TRow> Read(TEntry entry);
}
