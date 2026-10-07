// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Kernel.Streams;

internal interface IStreamArms<TEvent, TMarker, TPayload>
    where TMarker : IDamlType
    where TPayload : IDamlRecord<TPayload>
{
    static abstract TEvent Created(
        ContractId<TMarker> contractId,
        TPayload payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties);

    static abstract TEvent Archived(
        ContractId<TMarker> contractId,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties);

    static abstract TEvent Exercised(
        ContractId<TMarker> contractId,
        ChoiceName choiceName,
        DamlValue choiceArgument,
        DamlValue exerciseResult,
        bool consuming,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties);

    static abstract TEvent Assigned(
        ContractId<TMarker> contractId,
        TPayload payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties);

    static abstract TEvent Unassigned(
        ContractId<TMarker> contractId,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties);

    static abstract TEvent Unclassified(
        LedgerOffset? offset,
        UnclassifiedKind kind,
        string? rawKind);

    static abstract TEvent Checkpoint(LedgerOffset offset);
}
