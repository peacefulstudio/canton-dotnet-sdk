// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Outcomes;

internal static class ExerciseResultProjector
{
    internal static ExerciseOutcome<TResult> Project<TResult>(
        TransactionResult transaction,
        DamlTypeDescriptor owner,
        ChoiceName choiceName,
        Func<DamlValue, TResult> resultDecoder,
        string contractId) =>
        Project(
            transaction,
            exercised => IsOwnedBy(exercised, owner),
            choiceName,
            exercised => resultDecoder(exercised.ExerciseResult),
            contractId);

    internal static ExerciseOutcome<TResult> ProjectCommandedExercise<TResult>(
        TransactionResult transaction,
        Identifier commandTarget,
        ChoiceName choiceName,
        Func<ExercisedEvent, TResult> exercisedDecoder,
        string contractId) =>
        Project(transaction, exercised => IsCommandedOn(exercised, commandTarget), choiceName, exercisedDecoder, contractId);

    private static ExerciseOutcome<TResult> Project<TResult>(
        TransactionResult transaction,
        Func<ExercisedEvent, bool> isOwnedByCaller,
        ChoiceName choiceName,
        Func<ExercisedEvent, TResult> exercisedDecoder,
        string contractId)
    {
        var updateId = string.IsNullOrEmpty(transaction.UpdateId) ? null : transaction.UpdateId;
        var exercised = Locate(transaction, isOwnedByCaller, choiceName, contractId)
            ?? throw new InvalidOperationException(NoExercisedEventMessage(updateId, choiceName, contractId));

        try
        {
            return new ExerciseOutcome<TResult>.One(exercisedDecoder(exercised));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExerciseOutcome<TResult>.CommittedUndecodable(updateId, ex.Message, ex);
        }
    }

    private static ExercisedEvent? Locate(
        TransactionResult transaction,
        Func<ExercisedEvent, bool> isOwnedByCaller,
        ChoiceName choiceName,
        string contractId)
    {
        foreach (var exercised in transaction.ExercisedEvents)
        {
            if (string.Equals(exercised.ContractId, contractId, StringComparison.Ordinal)
                && isOwnedByCaller(exercised)
                && string.Equals(exercised.ChoiceName.Value, choiceName.Value, StringComparison.Ordinal))
            {
                return exercised;
            }
        }

        return null;
    }

    private static bool IsOwnedBy(ExercisedEvent exercised, DamlTypeDescriptor owner) =>
        Names(owner.Kind == DamlTypeKind.Interface ? exercised.InterfaceId : exercised.TemplateId, owner.Identifier);

    private static bool IsCommandedOn(ExercisedEvent exercised, Identifier commandTarget) =>
        Names(exercised.TemplateId, commandTarget) || Names(exercised.InterfaceId, commandTarget);

    private static bool Names(Identifier? exercisedOn, Identifier owner) =>
        exercisedOn is { } identifier
            && string.Equals(identifier.ModuleName, owner.ModuleName, StringComparison.Ordinal)
            && string.Equals(identifier.EntityName, owner.EntityName, StringComparison.Ordinal);

    private static string NoExercisedEventMessage(string? updateId, ChoiceName choiceName, string contractId) =>
        $"Submission succeeded but no '{choiceName.Value}' exercise on contract '{contractId}' was recorded on {DescribeTransaction(updateId)}. " +
        "The transaction returned for this submission carries no exercised event for it. " +
        "Either a custom ILedgerWriter did not project the transaction's exercised events into TransactionResult.ExercisedEvents, " +
        "or the transaction was requested in a shape without exercised events (ACS_DELTA); " +
        "request the LEDGER_EFFECTS shape with verbose events.";

    private static string DescribeTransaction(string? updateId) =>
        updateId is null ? "a transaction that declares no update id" : $"transaction {updateId}";
}
