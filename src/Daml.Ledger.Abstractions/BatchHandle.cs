// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Ledger.Abstractions;

/// <summary>
/// Names one command of a <see cref="CommandBatch"/>, returned by the <c>Add</c> that added it, so its
/// typed result reads back through <see cref="BatchResult.Get{TResult}(BatchHandle{TResult})"/>.
/// </summary>
/// <typeparam name="TResult">The command's result type: the choice result for an exercise, the
/// created contract's id for a create.</typeparam>
public sealed class BatchHandle<TResult>
{
    internal BatchHandle(CommandBatch batch, int position)
    {
        Batch = batch;
        Position = position;
    }

    internal CommandBatch Batch { get; }

    internal int Position { get; }
}
