// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions;

internal sealed class TemplateSnapshotArmReader<T>
    : ISnapshotArmReader<AcsSnapshotEntry<T>, AcsSnapshotEntry<T>.Created>
    where T : ITemplate, IDamlRecord<T>
{
    private static readonly TemplateSnapshotArmReader<T> Reader = new();

    private static readonly SnapshotSubject Subject =
        new(typeof(T).Name, "contract", "SubscribeActiveAsync");

    public static Task<List<AcsSnapshotEntry<T>.Created>> DrainAsync(
        ILedgerStreamer streamer,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset,
        bool includeDisclosure,
        CancellationToken cancellationToken) =>
        SnapshotDrain.DrainAsync(
            streamer.SubscribeActiveAsync<T>(submitter, activeAtOffset, includeDisclosure, cancellationToken),
            Reader,
            Subject,
            cancellationToken);

    public SnapshotArm<AcsSnapshotEntry<T>.Created> Read(AcsSnapshotEntry<T> entry) =>
        entry switch
        {
            AcsSnapshotEntry<T>.Created created =>
                new SnapshotArm<AcsSnapshotEntry<T>.Created>.Row(created),
            AcsSnapshotEntry<T>.Checkpoint =>
                new SnapshotArm<AcsSnapshotEntry<T>.Created>.Checkpoint(),
            AcsSnapshotEntry<T>.StreamError error =>
                new SnapshotArm<AcsSnapshotEntry<T>.Created>.Fault(
                    error.Status, error.Message, error.Category, error.ErrorId, error.SourceException),
            AcsSnapshotEntry<T>.Unclassified unclassified =>
                new SnapshotArm<AcsSnapshotEntry<T>.Created>.Unclassified(
                    unclassified.Kind, unclassified.RawKind, unclassified.Offset),
            _ => new SnapshotArm<AcsSnapshotEntry<T>.Created>.Unrecognised(entry.GetType().Name),
        };
}
