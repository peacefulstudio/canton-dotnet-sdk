// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// A contract paired with the provenance the participant reported alongside it in an
/// active-contract-set snapshot: the synchronizer it sits on, and the offset of the update
/// that last created or assigned it.
/// </summary>
/// <remarks>
/// <para>
/// Composition, not a slot on the contract shapes. <see cref="Contract{T}"/> and
/// <see cref="Contract{T, TKey}"/> stay payload-and-identity values whose equality is contract
/// equality; two observations of the same contract from different snapshots are the same
/// contract and two different <see cref="ActiveContract{TContract}"/> values.
/// </para>
/// <para>
/// <typeparamref name="TContract"/> is deliberately unconstrained, so any projection of a
/// snapshot row wraps here — a keyless <see cref="Contract{T}"/>, a keyed
/// <see cref="Contract{T, TKey}"/>, or a consumer's own interface-view pairing built from an
/// <c>InterfaceAcsSnapshotEntry&lt;TInterface, TView&gt;.Created</c> row.
/// </para>
/// </remarks>
/// <typeparam name="TContract">The wrapped contract shape.</typeparam>
/// <param name="Contract">The contract, in the shape the row was projected into.</param>
/// <param name="LastUpdateOffset">The offset of the update that last created or assigned the
/// contract. It orders and it identifies — active contracts can be sequenced by it and one can
/// be quoted by it — but it does <b>not</b> resume: it may point at an already-pruned update,
/// so handing it to a subscription is the mistake this name exists to prevent. A consumer
/// persisting resume state takes it from the snapshot's terminal checkpoint, never from a
/// per-contract offset.</param>
/// <param name="SynchronizerId">The synchronizer the contract is active on.</param>
public sealed record ActiveContract<TContract>(
    TContract Contract,
    LedgerOffset LastUpdateOffset,
    SynchronizerId SynchronizerId)
{
    /// <summary>
    /// The contract, ready to attach to another party's submission as an explicit
    /// disclosure. <c>null</c> unless the snapshot was opened with
    /// <c>includeDisclosure: true</c> and the participant returned a blob.
    /// </summary>
    public DisclosedContract? Disclosure { get; init; }
}
