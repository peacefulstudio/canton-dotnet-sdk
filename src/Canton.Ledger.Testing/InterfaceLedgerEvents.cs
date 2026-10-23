// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

/// <summary>
/// Static factories for the <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}"/> variants an
/// interface-view active-contract-set snapshot stream yields. Each thinly wraps the corresponding
/// public record constructor so a test can stage snapshot entries without hand-writing the
/// constructor calls. Pair these with
/// <see cref="FakeLedgerClientBuilder.WithActiveInterfaceContracts{TInterface, TView}"/>.
/// </summary>
public static class InterfaceLedgerEvents
{
    /// <summary>Builds an <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Created"/> snapshot entry.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the contract is observed through.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <returns>The created snapshot entry.</returns>
    public static InterfaceAcsSnapshotEntry<TInterface, TView> Created<TInterface, TView>(
        ContractId<TInterface> contractId,
        TView payload,
        ContractKey? key,
        LedgerOffset offset,
        SynchronizerId synchronizerId,
        EquatableArray<Party> witnessParties)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceAcsSnapshotEntry<TInterface, TView>.Created(contractId, payload, key, offset, synchronizerId, witnessParties);

    /// <summary>Builds the terminal <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Checkpoint"/> snapshot entry.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the snapshot is for.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <returns>The checkpoint snapshot entry.</returns>
    public static InterfaceAcsSnapshotEntry<TInterface, TView> Checkpoint<TInterface, TView>(LedgerOffset offset)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint(new StakeholderResume(offset));

    /// <summary>Builds an in-band <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.StreamError"/> snapshot entry.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the snapshot is for.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <param name="status">What the transport reported for the fault.</param>
    /// <param name="message">Status detail from the participant or transport.</param>
    /// <param name="category">Classification of the transport failure, or <c>null</c> to stage a
    /// fault the transport could not classify.</param>
    /// <param name="errorId">The participant's own Canton error code, or <c>null</c> to stage a
    /// fault that carried no structured error to read one from.</param>
    /// <param name="sourceException">Transport exception that ended the snapshot, or <c>null</c> to
    /// stage a fault carried in-band rather than thrown.</param>
    /// <returns>The stream-error snapshot entry.</returns>
    public static InterfaceAcsSnapshotEntry<TInterface, TView> StreamError<TInterface, TView>(
        TransportStatus status,
        string message,
        DamlErrorCategory? category = null,
        string? errorId = null,
        Exception? sourceException = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError(status, message, category, errorId, sourceException);

    /// <summary>Builds an <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Unclassified"/> snapshot entry.</summary>
    /// <typeparam name="TInterface">The Daml interface marker the snapshot is for.</typeparam>
    /// <typeparam name="TView">The interface's view record.</typeparam>
    /// <param name="offset">The ledger offset of the unrecognised row, or <c>null</c> when it could
    /// not be placed on the ledger.</param>
    /// <param name="kind">Why the row could not be classified.</param>
    /// <param name="rawKind">The transport's raw descriptor; required exactly when <paramref name="kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/>.</param>
    /// <returns>The unclassified snapshot entry.</returns>
    public static InterfaceAcsSnapshotEntry<TInterface, TView> Unclassified<TInterface, TView>(
        LedgerOffset? offset,
        UnclassifiedKind kind,
        string? rawKind = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        new InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified(offset, kind, rawKind);
}
