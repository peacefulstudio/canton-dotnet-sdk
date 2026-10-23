// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

/// <summary>
/// Static factories for the <see cref="InterfaceStreamEvent{TInterface, TView}"/> variants an
/// interface-view stream (ACS-delta or ledger-effects shape) yields. Each thinly wraps the
/// corresponding public record constructor. Pair these with
/// <see cref="FakeLedgerClientBuilder.WithInterfaceEvents{TInterface, TView}"/> or
/// <see cref="FakeLedgerClientBuilder.WithInterfaceLedgerEffects{TInterface, TView}"/>.
/// </summary>
public static class InterfaceEvents
{
    /// <summary>Builds an <see cref="InterfaceStreamEvent{TInterface, TView}.Created"/> event.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the contract is observed through.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <returns>The created event.</returns>
    public static InterfaceStreamEvent<TInterface, TView> Created<TInterface, TView>(
        ContractId<TInterface> contractId,
        TView payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceStreamEvent<TInterface, TView>.Created(contractId, payload, key, offset, synchronizerId, witnessParties);

    /// <summary>Builds an <see cref="InterfaceStreamEvent{TInterface, TView}.Archived"/> event (ACS-delta shape).</summary>
    /// <typeparam name="TInterface">The Daml interface marker the contract is observed through.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <returns>The archived event.</returns>
    public static InterfaceStreamEvent<TInterface, TView> Archived<TInterface, TView>(
        ContractId<TInterface> contractId,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceStreamEvent<TInterface, TView>.Archived(contractId, offset, synchronizerId, witnessParties);

    /// <summary>Builds an <see cref="InterfaceStreamEvent{TInterface, TView}.Exercised"/> event (ledger-effects shape).</summary>
    /// <typeparam name="TInterface">The Daml interface marker the contract is observed through.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <returns>The exercised event.</returns>
    public static InterfaceStreamEvent<TInterface, TView> Exercised<TInterface, TView>(
        ContractId<TInterface> contractId,
        ChoiceName choiceName,
        DamlValue choiceArgument,
        DamlValue exerciseResult,
        bool consuming,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceStreamEvent<TInterface, TView>.Exercised(
            contractId, choiceName, choiceArgument, exerciseResult, consuming, offset, synchronizerId, witnessParties);

    /// <summary>Builds an <see cref="InterfaceStreamEvent{TInterface, TView}.Assigned"/> reassignment event.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the contract is observed through.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <returns>The assigned event.</returns>
    public static InterfaceStreamEvent<TInterface, TView> Assigned<TInterface, TView>(
        ContractId<TInterface> contractId,
        TView payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceStreamEvent<TInterface, TView>.Assigned(
            contractId, payload, key, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    /// <summary>Builds an <see cref="InterfaceStreamEvent{TInterface, TView}.Unassigned"/> reassignment event.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the contract is observed through.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <returns>The unassigned event.</returns>
    public static InterfaceStreamEvent<TInterface, TView> Unassigned<TInterface, TView>(
        ContractId<TInterface> contractId,
        LedgerOffset offset,
        SynchronizerId source,
        SynchronizerId target,
        string reassignmentId,
        long reassignmentCounter,
        EquatableArray<Party> witnessParties)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceStreamEvent<TInterface, TView>.Unassigned(
            contractId, offset, source, target, reassignmentId, reassignmentCounter, witnessParties);

    /// <summary>Builds an <see cref="InterfaceStreamEvent{TInterface, TView}.Checkpoint"/> event.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the stream is for.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <returns>The checkpoint event.</returns>
    public static InterfaceStreamEvent<TInterface, TView> Checkpoint<TInterface, TView>(LedgerOffset offset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceStreamEvent<TInterface, TView>.Checkpoint(offset);

    /// <summary>Builds an in-band <see cref="InterfaceStreamEvent{TInterface, TView}.StreamError"/> event.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the stream is for.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <param name="status">What the transport reported for the fault.</param>
    /// <param name="message">Status detail from the participant or transport.</param>
    /// <param name="category">Classification of the transport failure, or <c>null</c> to stage a
    /// fault the transport could not classify.</param>
    /// <param name="errorId">The participant's own Canton error code, or <c>null</c> to stage a
    /// fault that carried no structured error to read one from.</param>
    /// <param name="sourceException">Transport exception that ended the stream, or <c>null</c> to
    /// stage a fault carried in-band rather than thrown.</param>
    /// <returns>The stream-error event.</returns>
    public static InterfaceStreamEvent<TInterface, TView> StreamError<TInterface, TView>(
        TransportStatus status,
        string message,
        DamlErrorCategory? category = null,
        string? errorId = null,
        Exception? sourceException = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceStreamEvent<TInterface, TView>.StreamError(status, message, category, errorId, sourceException);

    /// <summary>Builds an <see cref="InterfaceStreamEvent{TInterface, TView}.Unclassified"/> event.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the stream is for.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <param name="offset">The ledger offset of the unrecognised event, or <c>null</c> when it could
    /// not be placed on the ledger.</param>
    /// <param name="kind">Why the event could not be mapped to a typed variant.</param>
    /// <param name="rawKind">The transport's raw descriptor; required exactly when <paramref name="kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/>.</param>
    /// <returns>The unclassified event.</returns>
    public static InterfaceStreamEvent<TInterface, TView> Unclassified<TInterface, TView>(
        LedgerOffset? offset,
        UnclassifiedKind kind,
        string? rawKind = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceStreamEvent<TInterface, TView>.Unclassified(offset, kind, rawKind);
}
