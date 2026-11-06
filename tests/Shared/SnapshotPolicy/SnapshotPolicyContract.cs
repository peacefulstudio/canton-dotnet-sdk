// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Testing.SnapshotPolicy;

internal abstract record SnapshotStep
{
    private SnapshotStep() { }

    public sealed record Row(string ContractId, long Offset) : SnapshotStep;

    public sealed record Checkpoint(long Offset) : SnapshotStep;

    public sealed record Fault(
        TransportStatus Status,
        string Message,
        DamlErrorCategory? Category = null,
        string? ErrorId = null,
        Exception? SourceException = null) : SnapshotStep;

    public sealed record Unclassified(
        long? Offset,
        UnclassifiedKind Kind,
        string? RawKind = null) : SnapshotStep;

    public sealed record Stall(StallEnding Ending = StallEnding.ThrowsCancellation) : SnapshotStep;
}

internal enum StallEnding
{
    ThrowsCancellation,
    ReportsCancelledFaultInBand,
    EndsWithoutCheckpoint,
}

internal sealed record SnapshotPolicySubject(string Name, string RowNoun, string Alternative);

internal sealed record SnapshotRead(IReadOnlyList<string> Ids, IReadOnlyList<UnreadableRow> Unreadable);

internal interface ISnapshotPolicyDriver
{
    UnreadableRows Policy { get; }

    SnapshotPolicySubject Subject { get; }

    Task WhenStalled { get; }

    Task<SnapshotRead> DrainAsync(
        IReadOnlyList<SnapshotStep> script,
        bool streamHonoursCancellation,
        TimeSpan? timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken);
}
