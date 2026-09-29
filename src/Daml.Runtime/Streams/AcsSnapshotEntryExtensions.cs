// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Streams;

/// <summary>
/// Projects an active-contract-set snapshot row into the shape the rest of the runtime
/// speaks. A snapshot row's create-arguments arrive already decoded into the template
/// type, so no decoding helper is needed here.
/// </summary>
public static class AcsSnapshotEntryExtensions
{
    /// <summary>
    /// Projects a snapshot row to the keyless <see cref="Contract{T}"/> shape — the same
    /// pairing <c>Contract&lt;T&gt;.FromCreatedEvent</c> produces on the write path. The row's
    /// wire-level key is not carried: a keyed template's snapshot goes through
    /// <see cref="ToContract{T, TKey}"/>, which decodes it.
    /// </summary>
    /// <remarks>
    /// The row's offset, synchronizer id and witness parties are discarded by design: a
    /// <see cref="Contract{T}"/> is a contract, not an observation of one, and the write path
    /// produces the same shape from a created event that carries none of them. Use
    /// <see cref="ToActiveContract{T}"/> to keep the offset and the synchronizer id.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="created"/> is <c>null</c>.</exception>
    public static Contract<T> ToContract<T>(this AcsSnapshotEntry<T>.Created created)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(created);
        return new Contract<T>(created.ContractId, created.Payload);
    }

    /// <summary>
    /// Projects a snapshot row to the keyless <see cref="Contract{T}"/> shape and keeps the
    /// provenance the row carried, pairing the two in an
    /// <see cref="ActiveContract{TContract}"/>. Use it wherever the offset and synchronizer id
    /// <see cref="ToContract{T}"/> discards are wanted.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="created"/> is <c>null</c>.</exception>
    public static ActiveContract<Contract<T>> ToActiveContract<T>(
        this AcsSnapshotEntry<T>.Created created)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(created);
        return new ActiveContract<Contract<T>>(
            created.ToContract(),
            created.Offset,
            created.SynchronizerId)
        {
            Disclosure = created.Disclosure,
        };
    }

    /// <summary>
    /// Projects a snapshot row to the keyed <see cref="Contract{T, TKey}"/> shape, decoding the
    /// row's wire-level key through the template's <see cref="IHasKey{TSelf, TKey}.Key"/>
    /// witness and carrying the ledger's hash of it.
    /// </summary>
    /// <remarks>
    /// As on <see cref="ToContract{T}"/>, the row's offset, synchronizer id and witness parties
    /// are discarded by design. Use <see cref="ToActiveContract{T, TKey}"/> to keep the offset
    /// and the synchronizer id.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="created"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The row carried no contract key, so the keyed
    /// shape cannot be populated.</exception>
    public static Contract<T, TKey> ToContract<T, TKey>(this AcsSnapshotEntry<T>.Created created)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        ArgumentNullException.ThrowIfNull(created);
        return new Contract<T, TKey>(
            created.ContractId,
            created.Payload,
            Contract<T, TKey>.DecodeKey(created.Key, created.ContractId.Value));
    }

    /// <summary>
    /// Projects a snapshot row to the keyed <see cref="Contract{T, TKey}"/> shape and keeps the
    /// provenance the row carried, pairing the two in an
    /// <see cref="ActiveContract{TContract}"/>. Use it wherever the offset and synchronizer id
    /// <see cref="ToContract{T, TKey}"/> discards are wanted.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="created"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The row carried no contract key, so the keyed
    /// shape cannot be populated.</exception>
    public static ActiveContract<Contract<T, TKey>> ToActiveContract<T, TKey>(
        this AcsSnapshotEntry<T>.Created created)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        ArgumentNullException.ThrowIfNull(created);
        return new ActiveContract<Contract<T, TKey>>(
            created.ToContract<T, TKey>(),
            created.Offset,
            created.SynchronizerId)
        {
            Disclosure = created.Disclosure,
        };
    }
}
