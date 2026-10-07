// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Kernel.Streams;

internal sealed class InterfaceArms<TInterface, TView>
    : IStreamArms<InterfaceStreamEvent<TInterface, TView>, TInterface, TView>
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord<TView>
{
    public static InterfaceStreamEvent<TInterface, TView> Created(
        ContractId<TInterface> contractId,
        TView payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties) =>
        new InterfaceStreamEvent<TInterface, TView>.Created(
            contractId, payload, key, offset, synchronizerId, witnessParties);

    public static InterfaceStreamEvent<TInterface, TView> Archived(
        ContractId<TInterface> contractId,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties) =>
        new InterfaceStreamEvent<TInterface, TView>.Archived(contractId, offset, synchronizerId, witnessParties);

    public static InterfaceStreamEvent<TInterface, TView> Exercised(
        ContractId<TInterface> contractId,
        ChoiceName choiceName,
        DamlValue choiceArgument,
        DamlValue exerciseResult,
        bool consuming,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties) =>
        new InterfaceStreamEvent<TInterface, TView>.Exercised(
            contractId, choiceName, choiceArgument, exerciseResult, consuming, offset, synchronizerId, witnessParties);

    public static InterfaceStreamEvent<TInterface, TView> Assigned(
        ContractId<TInterface> contractId,
        TView payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties) =>
        new InterfaceStreamEvent<TInterface, TView>.Assigned(
            contractId, payload, key, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    public static InterfaceStreamEvent<TInterface, TView> Unassigned(
        ContractId<TInterface> contractId,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties) =>
        new InterfaceStreamEvent<TInterface, TView>.Unassigned(
            contractId, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    public static InterfaceStreamEvent<TInterface, TView> Unclassified(
        LedgerOffset? offset,
        UnclassifiedKind kind,
        string? rawKind) =>
        new InterfaceStreamEvent<TInterface, TView>.Unclassified(offset, kind, rawKind);

    public static InterfaceStreamEvent<TInterface, TView> Checkpoint(LedgerOffset offset) =>
        new InterfaceStreamEvent<TInterface, TView>.Checkpoint(offset);
}
