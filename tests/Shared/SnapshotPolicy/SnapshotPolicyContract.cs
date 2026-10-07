// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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
}

internal sealed record SnapshotPolicySubject(string Name, string RowNoun, string Alternative);

internal interface ISnapshotPolicyDriver
{
    SnapshotPolicySubject Subject { get; }

    Task<IReadOnlyList<string>> DrainAsync(
        IReadOnlyList<SnapshotStep> script,
        bool streamHonoursCancellation,
        CancellationToken cancellationToken);
}
