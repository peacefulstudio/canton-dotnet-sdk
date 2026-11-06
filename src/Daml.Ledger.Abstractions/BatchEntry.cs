// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

internal abstract class BatchEntry(ICommand command)
{
    internal ICommand Command { get; } = command;

    internal LedgerOperation.Command Operation { get; } = LedgerOperation.Of(command)!;

    internal abstract ExerciseOutcome<object?> Resolve(BatchRoots roots, TransactionResult transaction, int ordinal);

    private protected static ExerciseOutcome<object?> Ambiguous(TransactionResult transaction, string template, int ordinal)
    {
        var updateId = string.IsNullOrEmpty(transaction.UpdateId) ? null : transaction.UpdateId;
        return new ExerciseOutcome<object?>.CommittedUndecodable(
            updateId,
            $"Cannot tell which '{template}' create is command {ordinal} of the batch: the transaction has more of them than the batch submitted and no node ids to tell nested creates apart. Stage only the batch's own creates.");
    }

    private protected static ExerciseOutcome<object?> Missing(TransactionResult transaction, string what, int ordinal)
    {
        var updateId = string.IsNullOrEmpty(transaction.UpdateId) ? null : transaction.UpdateId;
        var committed = updateId is null ? "The transaction" : $"Transaction {updateId}";
        return new ExerciseOutcome<object?>.CommittedUndecodable(
            updateId, $"{committed} committed, but carries no root {what} for command {ordinal} of the batch.");
    }
}

internal sealed class ExerciseEntry<TOwner, TArg, TResult>(
    ExerciseCommand command,
    Choice<TOwner, TArg, TResult> choice,
    ContractId<TOwner> contractId) : BatchEntry(command)
    where TOwner : IDamlType
{
    internal override ExerciseOutcome<object?> Resolve(BatchRoots roots, TransactionResult transaction, int ordinal)
    {
        var exercised = roots.ClaimExercise(candidate =>
            string.Equals(candidate.ContractId, contractId.Value, StringComparison.Ordinal)
            && string.Equals(candidate.ChoiceName.Value, choice.Name.Value, StringComparison.Ordinal)
            && ExerciseResultProjector.IsOwnedBy(candidate, TOwner.DamlTypeId));
        if (exercised is null)
        {
            return Missing(transaction, $"'{choice.Name.Value}' exercise on contract '{contractId.Value}'", ordinal);
        }

        try
        {
            return new ExerciseOutcome<object?>.One(
                choice.ResultDecoder(ExerciseOutcomeProjection.ReadCarried(exercised.ExerciseResult, choice)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExerciseOutcome<object?>.CommittedUndecodable(
                string.IsNullOrEmpty(transaction.UpdateId) ? null : transaction.UpdateId, ex.Message, ex);
        }
    }
}

internal sealed class CreateEntry<TTemplate>(CreateCommand command) : BatchEntry(command)
    where TTemplate : ITemplate
{
    internal override ExerciseOutcome<object?> Resolve(BatchRoots roots, TransactionResult transaction, int ordinal)
    {
        bool IsThisTemplate(CreatedContract candidate) =>
            MarkerMatch.Template(TTemplate.DamlTypeId, candidate.PackageName, candidate.TemplateId);

        if (roots.CreatesAreAmbiguous<TTemplate>(IsThisTemplate))
        {
            return Ambiguous(transaction, TTemplate.TemplateId.EntityName, ordinal);
        }

        var created = roots.ClaimCreate(IsThisTemplate);
        return created is null
            ? Missing(transaction, $"create of '{TTemplate.TemplateId.EntityName}'", ordinal)
            : new ExerciseOutcome<object?>.One(new ContractId<TTemplate>(created.ContractId));
    }
}
