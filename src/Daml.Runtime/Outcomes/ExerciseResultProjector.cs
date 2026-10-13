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
        string? commandTargetPackageName,
        ChoiceName choiceName,
        Func<ExercisedEvent, TResult> exercisedDecoder,
        string contractId) =>
        Project(
            transaction,
            exercised => IsCommandedOn(exercised, commandTarget, commandTargetPackageName),
            choiceName,
            exercisedDecoder,
            contractId);

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
        owner.Kind == DamlTypeKind.Interface
            ? exercised.InterfaceId is { } interfaceId && MarkerMatch.Interface(owner, interfaceId)
            : MarkerMatch.Template(owner, exercised.PackageName, exercised.TemplateId);

    private static bool IsCommandedOn(ExercisedEvent exercised, Identifier commandTarget, string? commandTargetPackageName)
    {
        var target = new DamlTypeDescriptor(commandTarget, DamlTypeKind.Template, commandTargetPackageName ?? string.Empty);
        var templateMatches = PackageNames.IsPlaceholder(commandTargetPackageName)
            ? exercised.TemplateId.PackageId == commandTarget.PackageId && SameModuleAndEntity(exercised.TemplateId, commandTarget)
            : MarkerMatch.Template(target, exercised.PackageName, exercised.TemplateId);
        return templateMatches
            || (exercised.InterfaceId is { } interfaceId && MarkerMatch.Interface(target, interfaceId));
    }

    private static bool SameModuleAndEntity(Identifier left, Identifier right) =>
        string.Equals(left.ModuleName, right.ModuleName, StringComparison.Ordinal)
        && string.Equals(left.EntityName, right.EntityName, StringComparison.Ordinal);

    private static string NoExercisedEventMessage(string? updateId, ChoiceName choiceName, string contractId) =>
        $"Submission succeeded but no '{choiceName.Value}' exercise on contract '{contractId}' was recorded on {DescribeTransaction(updateId)}. " +
        "The transaction returned for this submission carries no exercised event for it. " +
        "Either a custom ILedgerWriter did not project the transaction's exercised events into TransactionResult.ExercisedEvents, " +
        "or the transaction was requested in a shape without exercised events (ACS_DELTA); " +
        "request the LEDGER_EFFECTS shape with verbose events.";

    private static string DescribeTransaction(string? updateId) =>
        updateId is null ? "a transaction that declares no update id" : $"transaction {updateId}";
}
