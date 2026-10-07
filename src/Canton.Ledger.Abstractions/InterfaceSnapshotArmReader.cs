// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Abstractions;

internal sealed class InterfaceSnapshotArmReader<TInterface, TView>
    : ISnapshotArmReader<
        InterfaceAcsSnapshotEntry<TInterface, TView>,
        InterfaceAcsSnapshotEntry<TInterface, TView>.Created>
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord<TView>
{
    private static readonly InterfaceSnapshotArmReader<TInterface, TView> Reader = new();

    private static readonly SnapshotSubject Subject = new(
        typeof(TInterface).Name,
        "interface view",
        $"SubscribeActiveAsync({typeof(TInterface).Name}.View, ...)");

    public static Task<List<InterfaceAcsSnapshotEntry<TInterface, TView>.Created>> DrainAsync(
        IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> entries,
        CancellationToken cancellationToken) =>
        SnapshotDrain.DrainAsync(entries, Reader, Subject, cancellationToken);

    public SnapshotArm<InterfaceAcsSnapshotEntry<TInterface, TView>.Created> Read(
        InterfaceAcsSnapshotEntry<TInterface, TView> entry) =>
        entry switch
        {
            InterfaceAcsSnapshotEntry<TInterface, TView>.Created created =>
                new SnapshotArm<InterfaceAcsSnapshotEntry<TInterface, TView>.Created>.Row(created),
            InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint =>
                new SnapshotArm<InterfaceAcsSnapshotEntry<TInterface, TView>.Created>.Checkpoint(),
            InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError error =>
                new SnapshotArm<InterfaceAcsSnapshotEntry<TInterface, TView>.Created>.Fault(
                    error.Status, error.Message, error.Category, error.ErrorId, error.SourceException),
            InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified unclassified =>
                new SnapshotArm<InterfaceAcsSnapshotEntry<TInterface, TView>.Created>.Unclassified(
                    unclassified.Kind, unclassified.RawKind, unclassified.Offset),
            _ => new SnapshotArm<InterfaceAcsSnapshotEntry<TInterface, TView>.Created>.Unrecognised(
                entry.GetType().Name),
        };
}
