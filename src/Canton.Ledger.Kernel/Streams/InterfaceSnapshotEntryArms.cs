// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Kernel.Streams;

internal static class InterfaceSnapshotEntryArms<TInterface, TView>
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord<TView>
{
    public static InterfaceAcsSnapshotEntry<TInterface, TView> From(
        InterfaceStreamEvent<TInterface, TView> entry,
        DisclosedContract? disclosure) => entry switch
    {
        InterfaceStreamEvent<TInterface, TView>.Created created =>
            new InterfaceAcsSnapshotEntry<TInterface, TView>.Created(
                created.ContractId,
                created.Payload,
                created.Key,
                created.Offset,
                created.SynchronizerId,
                created.WitnessParties)
            {
                Disclosure = disclosure,
            },
        InterfaceStreamEvent<TInterface, TView>.Unassigned unassigned =>
            new InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified(
                unassigned.Offset, UnclassifiedKind.UnassignedEvent),
        InterfaceStreamEvent<TInterface, TView>.Unclassified unclassified =>
            new InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified(
                unclassified.Offset, unclassified.Kind, unclassified.RawKind),
        _ => throw new InvalidOperationException(
            $"Active-contract snapshot produced an unexpected entry variant: {entry.GetType().Name}"),
    };
}
