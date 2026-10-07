// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Kernel.Streams;

internal sealed class ContractArms<T> : IStreamArms<ContractStreamEvent<T>, T, T>
    where T : ITemplate, IDamlRecord<T>
{
    public static ContractStreamEvent<T> Created(
        ContractId<T> contractId,
        T payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties) =>
        new ContractStreamEvent<T>.Created(contractId, payload, key, offset, synchronizerId, witnessParties);

    public static ContractStreamEvent<T> Archived(
        ContractId<T> contractId,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties) =>
        new ContractStreamEvent<T>.Archived(contractId, offset, synchronizerId, witnessParties);

    public static ContractStreamEvent<T> Exercised(
        ContractId<T> contractId,
        ChoiceName choiceName,
        DamlValue choiceArgument,
        DamlValue exerciseResult,
        bool consuming,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties) =>
        new ContractStreamEvent<T>.Exercised(
            contractId, choiceName, choiceArgument, exerciseResult, consuming, offset, synchronizerId, witnessParties);

    public static ContractStreamEvent<T> Assigned(
        ContractId<T> contractId,
        T payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties) =>
        new ContractStreamEvent<T>.Assigned(
            contractId, payload, key, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    public static ContractStreamEvent<T> Unassigned(
        ContractId<T> contractId,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties) =>
        new ContractStreamEvent<T>.Unassigned(
            contractId, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    public static ContractStreamEvent<T> Unclassified(
        LedgerOffset? offset,
        UnclassifiedKind kind,
        string? rawKind) =>
        new ContractStreamEvent<T>.Unclassified(offset, kind, rawKind);

    public static ContractStreamEvent<T> Checkpoint(LedgerOffset offset) =>
        new ContractStreamEvent<T>.Checkpoint(offset);
}
