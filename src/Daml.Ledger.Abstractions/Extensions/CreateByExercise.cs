// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// Create-by-exercise convenience: exercise a choice and lift the contracts it created
/// into typed <see cref="ContractId{TTemplate}"/> values. The single-result
/// (<c>*One*</c>) and multi-result (<c>*Many*</c>) shapes are split so callers name up
/// front how many contracts of the target template they expect, and no outcome is ever
/// silently collapsed into a success shape.
/// </summary>
public static class CreateByExercise
{
    /// <summary>
    /// Exercises <paramref name="choice"/> and lifts the single created
    /// <typeparamref name="TTemplate"/> into an <see cref="ExerciseOutcome{T}"/> over its
    /// <see cref="ContractId{T}"/>. Exactly one created <typeparamref name="TTemplate"/>
    /// yields <see cref="ExerciseOutcome{T}.One"/>; none yields
    /// <see cref="ExerciseOutcome{T}.None"/>; more than one yields
    /// <see cref="ExerciseOutcome{T}.Many"/> carrying every created contract id.
    /// A writer-level <see cref="ExerciseOutcome{T}.Many"/> (multiple root transactions)
    /// is propagated faithfully rather than discarded. Use
    /// <see cref="TryCreateManyByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,CommandId?,TimeSpan?,CancellationToken)"/>
    /// when the choice is expected to create any number of <typeparamref name="TTemplate"/> contracts.
    /// </summary>
    public static async Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateOneByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        ArgumentNullException.ThrowIfNull(writer);

        var outcome = await writer
            .TrySubmitSingleAsync(choice, submitter, workflowId, commandId, timeout, cancellationToken)
            .ConfigureAwait(false);

        return RemapExerciseOutcome(outcome, ProjectSingleCreated<TTemplate>);
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> and lifts every created
    /// <typeparamref name="TTemplate"/> into an <see cref="ExerciseOutcome{T}"/> over the
    /// <see cref="EquatableArray{T}">EquatableArray&lt;ContractId&lt;TTemplate&gt;&gt;</see>
    /// that <see cref="TransactionResultExtensions.All{T}"/> projects, so two outcomes over the
    /// same created contracts compare equal.
    /// A conforming writer yields only <see cref="ExerciseOutcome{T}.One"/> (whose array holds
    /// all created <typeparamref name="TTemplate"/> contracts, in transaction order — it may be
    /// empty when the choice created none), <see cref="ExerciseOutcome{T}.DamlError"/>, or
    /// <see cref="ExerciseOutcome{T}.InfraError"/>. A writer-level
    /// <see cref="ExerciseOutcome{T}.None"/> or <see cref="ExerciseOutcome{T}.Many"/> is
    /// only reachable from a non-conforming writer and is propagated faithfully rather
    /// than collapsed.
    /// </summary>
    public static async Task<ExerciseOutcome<EquatableArray<ContractId<TTemplate>>>> TryCreateManyByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        ArgumentNullException.ThrowIfNull(writer);

        var outcome = await writer
            .TrySubmitSingleAsync(choice, submitter, workflowId, commandId, timeout, cancellationToken)
            .ConfigureAwait(false);

        return RemapExerciseOutcome(
            outcome,
            result => new ExerciseOutcome<EquatableArray<ContractId<TTemplate>>>.One(result.All<TTemplate>()));
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> and returns the single created
    /// <typeparamref name="TTemplate"/> contract id, throwing on any other outcome.
    /// Throws <see cref="LedgerOperationException"/> when the choice created no
    /// <typeparamref name="TTemplate"/> (expected exactly one), when it created more than
    /// one (use
    /// <see cref="CreateManyByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,CommandId?,TimeSpan?,CancellationToken)"/>
    /// instead), or on a Daml or infrastructure error. For structured handling, use
    /// <see cref="TryCreateOneByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,CommandId?,TimeSpan?,CancellationToken)"/>.
    /// </summary>
    public static async Task<ContractId<TTemplate>> CreateOneByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        var outcome = await writer
            .TryCreateOneByExerciseAsync<TTemplate>(choice, submitter, workflowId, commandId, timeout, cancellationToken)
            .ConfigureAwait(false);

        return outcome.ResultOrThrow(
            "TryCreateOneByExerciseAsync",
            static () => $"Exercising the choice created no {typeof(TTemplate).Name}; expected exactly one.",
            static count => $"Exercising the choice created {count} {typeof(TTemplate).Name} contracts; expected exactly one. Use CreateManyByExerciseAsync to collect them.",
            cancellationToken);
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> and returns every created
    /// <typeparamref name="TTemplate"/> contract id in transaction order, throwing on
    /// error. Carried as the
    /// <see cref="EquatableArray{T}">EquatableArray&lt;ContractId&lt;TTemplate&gt;&gt;</see>
    /// <see cref="TransactionResultExtensions.All{T}"/> projects, so two results over the same
    /// created contracts compare equal; it may be empty when the choice created no
    /// <typeparamref name="TTemplate"/>. Throws
    /// <see cref="LedgerOperationException"/> on a Daml or infrastructure error, or when a
    /// non-conforming writer yields a <c>None</c> / <c>Many</c> outcome. For structured
    /// handling, use
    /// <see cref="TryCreateManyByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,CommandId?,TimeSpan?,CancellationToken)"/>.
    /// </summary>
    public static async Task<EquatableArray<ContractId<TTemplate>>> CreateManyByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        var outcome = await writer
            .TryCreateManyByExerciseAsync<TTemplate>(choice, submitter, workflowId, commandId, timeout, cancellationToken)
            .ConfigureAwait(false);

        return outcome.ResultOrThrow(
            "TryCreateManyByExerciseAsync",
            static () => $"Exercising the choice yielded no result (None) for {typeof(TTemplate).Name}; a conforming writer returns One carrying every created contract.",
            static count => $"Exercising the choice yielded Many ({count}) for {typeof(TTemplate).Name}; a conforming writer returns One carrying every created contract.",
            cancellationToken);
    }

    private static ExerciseOutcome<TCreated> RemapExerciseOutcome<TCreated>(
        ExerciseOutcome<TransactionResult> outcome,
        Func<TransactionResult, ExerciseOutcome<TCreated>> projectCreated)
    {
        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One one => projectCreated(one.Result),
            ExerciseOutcome<TransactionResult>.None => new ExerciseOutcome<TCreated>.None(),
            ExerciseOutcome<TransactionResult>.Many many => new ExerciseOutcome<TCreated>.Many(many.ContractIds),
            ExerciseOutcome<TransactionResult>.DamlError e => new ExerciseOutcome<TCreated>.DamlError(e.Category, e.ErrorId, e.Message, e.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError e => new ExerciseOutcome<TCreated>.InfraError(e.Status, e.Message, e.Category, e.SourceException),
            ExerciseOutcome<TransactionResult>.CommittedUndecodable e => new ExerciseOutcome<TCreated>.CommittedUndecodable(e.UpdateId, e.Message, e.SourceException),
            _ => throw new UnreachableException($"Unexpected outcome {outcome.GetType().Name} from TrySubmitSingleAsync."),
        };
    }

    private static ExerciseOutcome<ContractId<TTemplate>> ProjectSingleCreated<TTemplate>(TransactionResult result)
        where TTemplate : IDamlType
    {
        var created = result.All<TTemplate>();
        return created.Count switch
        {
            1 => new ExerciseOutcome<ContractId<TTemplate>>.One(created[0]),
            0 => new ExerciseOutcome<ContractId<TTemplate>>.None(),
            _ => new ExerciseOutcome<ContractId<TTemplate>>.Many([.. created.Select(id => id.Value)]),
        };
    }
}
