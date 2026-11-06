// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// The typed commands of one atomic submission, in the order they are added. Each <c>Add</c> returns a
/// <see cref="BatchHandle{TResult}"/> that reads that command's typed result back from the
/// <see cref="BatchResult"/> that
/// <see cref="Extensions.CommandBatchExtensions.TrySubmitBatchAsync(ILedgerWriter, CommandBatch, SubmitterInfo, string?, CommandId?, TimeSpan?, Func{CommandsSubmission, CommandsSubmission}?, CancellationToken)"/>
/// returns, so the commands take the generated choice descriptors and payloads instead of hand-built
/// <see cref="ExerciseCommand"/>s.
/// </summary>
/// <example>
/// <code>
/// var batch = new CommandBatch();
/// var ended = batch.Add(Connection.ChoiceDisconnect, connectionId, new Connection.Disconnect(actor));
/// var link = batch.AddCreate(new InviteLink(...));
/// var results = (await client.TrySubmitBatchAsync(batch, actor)).OneOrThrow();
/// ContractId&lt;InviteLink&gt; created = results.Get(link);
/// </code>
/// </example>
public sealed class CommandBatch
{
    private readonly List<BatchEntry> _entries = [];

    /// <summary>The number of commands added so far.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Every command added so far, in order, as the <see cref="LedgerOperation.Batch"/> a submission of
    /// this batch is stamped with, e.g. to name a span before submitting it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No command has been added.</exception>
    public LedgerOperation.Batch Operation =>
        _entries.Count == 0
            ? throw new InvalidOperationException("An empty batch has no operation; add a command first.")
            : OperationOf(_entries);

    /// <summary>
    /// Adds an exercise of <paramref name="choice"/> on <paramref name="contractId"/>, built the way
    /// <see cref="ExerciseCommand.For{TOwner}(ContractId{TOwner}, ChoiceName, Daml.Runtime.Data.DamlValue)"/>
    /// builds it. <typeparamref name="TOwner"/> may be a template or an interface.
    /// </summary>
    /// <typeparam name="TOwner">The template or interface that declares the choice.</typeparam>
    /// <typeparam name="TArg">The choice argument type.</typeparam>
    /// <typeparam name="TResult">The choice result type.</typeparam>
    /// <param name="choice">The generated choice descriptor, e.g. <c>Connection.ChoiceDisconnect</c>.</param>
    /// <param name="contractId">The contract to exercise the choice on.</param>
    /// <param name="argument">The choice argument.</param>
    /// <returns>The handle that reads the choice's decoded result back from the <see cref="BatchResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="choice"/> or <paramref name="contractId"/> is null.</exception>
    public BatchHandle<TResult> Add<TOwner, TArg, TResult>(
        Choice<TOwner, TArg, TResult> choice,
        ContractId<TOwner> contractId,
        TArg argument)
        where TOwner : IDamlType
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(contractId);

        var command = ExerciseCommand.For(contractId, choice.Name, choice.ArgumentEncoder(argument));
        return Append<TResult>(new ExerciseEntry<TOwner, TArg, TResult>(command, choice, contractId));
    }

    /// <summary>
    /// Adds a create of <paramref name="payload"/>, built the way <see cref="CreateCommand.For{T}(T)"/> builds it.
    /// </summary>
    /// <typeparam name="TTemplate">The template to create.</typeparam>
    /// <param name="payload">The template payload.</param>
    /// <returns>The handle that reads the created contract's id back from the <see cref="BatchResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
    public BatchHandle<ContractId<TTemplate>> AddCreate<TTemplate>(TTemplate payload)
        where TTemplate : ITemplate
    {
        ArgumentNullException.ThrowIfNull(payload);

        return Append<ContractId<TTemplate>>(new CreateEntry<TTemplate>(CreateCommand.For(payload)));
    }

    internal IReadOnlyList<BatchEntry> Snapshot() => [.. _entries];

    internal static LedgerOperation.Batch OperationOf(IReadOnlyList<BatchEntry> entries) =>
        new([.. entries.Select(entry => entry.Operation)]);

    internal ExerciseOutcome<BatchResult> Project(IReadOnlyList<BatchEntry> submitted, TransactionResult transaction)
    {
        var roots = new BatchRoots(transaction, submitted);
        var results = new object?[submitted.Count];
        for (var position = 0; position < submitted.Count; position++)
        {
            var entry = submitted[position];
            var resolved = entry.Resolve(roots, transaction, position + 1);
            if (resolved is not ExerciseOutcome<object?>.One one)
            {
                return Undecodable(resolved, entry);
            }

            results[position] = one.Result;
        }

        return new ExerciseOutcome<BatchResult>.One(new BatchResult(this, transaction, results));
    }

    private static ExerciseOutcome<BatchResult> Undecodable(ExerciseOutcome<object?> resolved, BatchEntry entry) =>
        resolved is ExerciseOutcome<object?>.CommittedUndecodable undecodable
            ? new ExerciseOutcome<BatchResult>.CommittedUndecodable(undecodable.UpdateId, undecodable.Message, undecodable.SourceException)
            {
                Operation = entry.Operation,
            }
            : throw new InvalidOperationException($"A batch entry resolved to {resolved.GetType().Name}.");

    private BatchHandle<TResult> Append<TResult>(BatchEntry entry)
    {
        _entries.Add(entry);
        return new BatchHandle<TResult>(this, _entries.Count - 1);
    }
}
