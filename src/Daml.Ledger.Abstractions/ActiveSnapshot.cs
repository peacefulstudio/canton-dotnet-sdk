// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// The rows of a drained active-contract-set snapshot, in the order the participant delivered
/// them, and the rows it delivered that could not be read.
/// </summary>
/// <remarks>
/// <see cref="Unreadable"/> is empty unless the snapshot was drained with
/// <see cref="UnreadableRows.Return"/>, so a caller that keeps the default policy can treat this
/// as the plain list it enumerates.
/// </remarks>
/// <typeparam name="TRow">The element type of the readable rows.</typeparam>
public sealed class ActiveSnapshot<TRow> : IReadOnlyList<TRow>
{
    private readonly IReadOnlyList<TRow> _rows;

    /// <summary>Creates a snapshot from its readable and unreadable rows.</summary>
    /// <param name="rows">The readable rows.</param>
    /// <param name="unreadable">The unreadable rows; <c>null</c> means none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is <c>null</c>.</exception>
    public ActiveSnapshot(IReadOnlyList<TRow> rows, IReadOnlyList<UnreadableRow>? unreadable = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;
        Unreadable = unreadable ?? [];
    }

    /// <summary>The rows the snapshot delivered that could not be read, in delivery order.</summary>
    public IReadOnlyList<UnreadableRow> Unreadable { get; }

    /// <inheritdoc/>
    public int Count => _rows.Count;

    /// <inheritdoc/>
    public TRow this[int index] => _rows[index];

    /// <inheritdoc/>
    public IEnumerator<TRow> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal ActiveSnapshot<TMapped> Select<TMapped>(Func<TRow, TMapped> map) =>
        new(_rows.Select(map).ToList(), Unreadable);
}
