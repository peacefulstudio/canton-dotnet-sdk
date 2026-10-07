// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Kernel.Streams;

internal static class ContractSnapshotEntryArms<T>
    where T : ITemplate, IDamlRecord<T>
{
    public static AcsSnapshotEntry<T> From(ContractStreamEvent<T> entry, DisclosedContract? disclosure) => entry switch
    {
        ContractStreamEvent<T>.Created created => new AcsSnapshotEntry<T>.Created(
            created.ContractId, created.Payload, created.Key, created.Offset, created.SynchronizerId, created.WitnessParties)
        {
            Disclosure = disclosure,
        },
        ContractStreamEvent<T>.Unassigned unassigned => new AcsSnapshotEntry<T>.Unclassified(
            unassigned.Offset, UnclassifiedKind.UnassignedEvent),
        ContractStreamEvent<T>.Unclassified unclassified => new AcsSnapshotEntry<T>.Unclassified(
            unclassified.Offset, unclassified.Kind, unclassified.RawKind),
        _ => throw new InvalidOperationException(
            $"Active-contract snapshot produced an unexpected entry variant: {entry.GetType().Name}"),
    };
}
