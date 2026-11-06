// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using Daml.Runtime.Contracts;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// The committed transaction of a <see cref="CommandBatch"/> and each of its commands' typed results,
/// read back through the <see cref="BatchHandle{TResult}"/> the command's <c>Add</c> returned.
/// </summary>
public sealed class BatchResult
{
    private readonly CommandBatch _batch;
    private readonly object?[] _results;

    internal BatchResult(CommandBatch batch, TransactionResult transaction, object?[] results)
    {
        _batch = batch;
        _results = results;
        Transaction = transaction;
    }

    /// <summary>The committed transaction the results were read from.</summary>
    public TransactionResult Transaction { get; }

    /// <summary>The number of commands the batch submitted.</summary>
    public int Count => _results.Length;

    /// <summary>Reads the typed result of the command <paramref name="handle"/> names.</summary>
    /// <typeparam name="TResult">The command's result type.</typeparam>
    /// <param name="handle">The handle the command's <c>Add</c> returned.</param>
    /// <returns>The command's result: the choice result for an exercise, the created contract's id for a create.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="handle"/> belongs to another batch, or was added
    /// after this batch was submitted.</exception>
    public TResult Get<TResult>(BatchHandle<TResult> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!ReferenceEquals(handle.Batch, _batch))
        {
            throw new ArgumentException("The handle belongs to another batch.", nameof(handle));
        }

        if (handle.Position >= _results.Length)
        {
            throw new ArgumentException("The handle was added after the batch was submitted.", nameof(handle));
        }

        return (TResult)_results[handle.Position]!;
    }
}
