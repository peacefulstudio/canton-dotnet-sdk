// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.Threading;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

internal enum ValueIntent
{
    RequireOne,
    RequireOneForOperation,
}

internal enum DiscardIntent
{
    RejectNone,
    AcceptNone,
}

internal static class OutcomeUnwrapPolicy
{
    internal const string OperationDataKey = "Canton.Ledger.Operation";

    internal static T Value<T>(
        ExerciseOutcome<T> outcome,
        ValueIntent intent,
        string label,
        Func<string>? describeNone = null,
        Func<int, string>? describeMany = null,
        CancellationToken cancellationToken = default)
    {
        var rules = RulesFor(intent);
        if (rules.CallerDescribesCounts)
        {
            ArgumentNullException.ThrowIfNull(describeNone);
            ArgumentNullException.ThrowIfNull(describeMany);
        }

        if (outcome is ExerciseOutcome<T>.One one)
        {
            return one.Result;
        }

        throw FailureOf(outcome, rules, label, describeNone, describeMany, cancellationToken)!;
    }

    internal static void Discard<T>(
        ExerciseOutcome<T> outcome,
        DiscardIntent intent,
        string label,
        CancellationToken cancellationToken = default)
    {
        if (FailureOf(outcome, RulesFor(intent), label, describeNone: null, describeMany: null, cancellationToken) is { } failure)
        {
            throw failure;
        }
    }

    private static Rules RulesFor(ValueIntent intent) => intent switch
    {
        ValueIntent.RequireOne => RequireOneRules.Instance,
        ValueIntent.RequireOneForOperation => RequireOneForOperationRules.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, null),
    };

    private static Rules RulesFor(DiscardIntent intent) => intent switch
    {
        DiscardIntent.RejectNone => RejectNoneRules.Instance,
        DiscardIntent.AcceptNone => AcceptNoneRules.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, null),
    };

    private static Exception? FailureOf<T>(
        ExerciseOutcome<T> outcome,
        Rules rules,
        string label,
        Func<string>? describeNone,
        Func<int, string>? describeMany,
        CancellationToken cancellationToken) =>
        outcome switch
        {
            ExerciseOutcome<T>.One => null,
            ExerciseOutcome<T>.None =>
                rules.FailsOnNone ? Committed(rules.NoneMessage(label, describeNone), rules, label) : null,
            ExerciseOutcome<T>.Many many =>
                rules.FailsOnManyAndUndecodable ? Committed(rules.ManyMessage(label, many.ContractIds, describeMany), rules, label) : null,
            ExerciseOutcome<T>.DamlError e => rules.Tag(
                new LedgerOperationException(rules.DamlErrorMessage(label, e.Category, e.ErrorId, e.Message), e.Category, e.ErrorId, e.Metadata),
                label),
            ExerciseOutcome<T>.InfraError e => InfraFailure(e, rules, label, cancellationToken),
            ExerciseOutcome<T>.CommittedUndecodable e => rules.FailsOnManyAndUndecodable
                ? rules.Tag(new LedgerOperationException(rules.UndecodableMessage(label, e.UpdateId, e.Message), e.UpdateId, e.SourceException), label)
                : null,
            _ => rules.Unexpected(outcome.GetType().Name, label),
        };

    private static LedgerOperationException Committed(string message, Rules rules, string label) =>
        rules.Tag(LedgerOperationException.CommittedWithoutDetail(message), label);

    private static LedgerOperationException InfraFailure<T>(
        ExerciseOutcome<T>.InfraError error,
        Rules rules,
        string label,
        CancellationToken cancellationToken)
    {
        if (rules.CancellationWins)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return rules.Tag(
            new LedgerOperationException(rules.InfraErrorMessage(label, error.Status, error.Message), error.Status, error.Category, error.SourceException),
            label);
    }

    private abstract class Rules
    {
        internal virtual bool CallerDescribesCounts => false;

        internal abstract bool FailsOnNone { get; }

        internal abstract bool FailsOnManyAndUndecodable { get; }

        internal abstract bool CancellationWins { get; }

        internal virtual string NoneMessage(string label, Func<string>? describeNone) =>
            throw new UnreachableException();

        internal virtual string ManyMessage(string label, EquatableArray<string> contractIds, Func<int, string>? describeMany) =>
            throw new UnreachableException();

        internal abstract string DamlErrorMessage(string label, DamlErrorCategory category, string errorId, string message);

        internal abstract string InfraErrorMessage(string label, TransportStatus status, string message);

        internal abstract string UndecodableMessage(string label, string? updateId, string message);

        internal abstract Exception? Unexpected(string outcomeTypeName, string label);

        internal abstract LedgerOperationException Tag(LedgerOperationException exception, string label);
    }

    private abstract class UnlabelledMessages : Rules
    {
        internal sealed override bool CancellationWins => true;

        internal sealed override string DamlErrorMessage(string label, DamlErrorCategory category, string errorId, string message) =>
            $"Daml error [{category}/{errorId}]: {message}";

        internal sealed override string InfraErrorMessage(string label, TransportStatus status, string message) =>
            $"Infrastructure error [{status}]: {message}";

        internal sealed override string UndecodableMessage(string label, string? updateId, string message) =>
            $"Committed but undecodable [{updateId ?? "unknown"}]: {message}";

        internal sealed override LedgerOperationException Tag(LedgerOperationException exception, string label) => exception;
    }

    private abstract class OperationNamedMessages : Rules
    {
        internal sealed override bool CancellationWins => false;

        internal sealed override string DamlErrorMessage(string label, DamlErrorCategory category, string errorId, string message) =>
            $"{label}: Daml error [{category}/{errorId}]: {message}";

        internal sealed override string InfraErrorMessage(string label, TransportStatus status, string message) =>
            $"{label}: infrastructure error [{status}]: {message}";

        internal sealed override string UndecodableMessage(string label, string? updateId, string message) =>
            $"{label}: committed but undecodable: {message}";

        internal sealed override LedgerOperationException Tag(LedgerOperationException exception, string label)
        {
            exception.Data[OperationDataKey] = label;
            return exception;
        }
    }

    private sealed class RequireOneRules : UnlabelledMessages
    {
        internal static readonly RequireOneRules Instance = new();

        internal override bool CallerDescribesCounts => true;

        internal override bool FailsOnNone => true;

        internal override bool FailsOnManyAndUndecodable => true;

        internal override string NoneMessage(string label, Func<string>? describeNone) => describeNone!();

        internal override string ManyMessage(string label, EquatableArray<string> contractIds, Func<int, string>? describeMany) =>
            describeMany!(contractIds.Count);

        internal override Exception? Unexpected(string outcomeTypeName, string label) =>
            new UnreachableException($"Unexpected outcome {outcomeTypeName} from {label}.");
    }

    private sealed class RejectNoneRules : UnlabelledMessages
    {
        internal static readonly RejectNoneRules Instance = new();

        internal override bool FailsOnNone => true;

        internal override bool FailsOnManyAndUndecodable => false;

        internal override string NoneMessage(string label, Func<string>? describeNone) =>
            $"{label} returned no transaction (None); the command committed, but produced no transaction. Use {label} for the non-throwing path.";

        internal override Exception? Unexpected(string outcomeTypeName, string label) =>
            new LedgerOperationException($"Unexpected outcome {outcomeTypeName} from {label}.");
    }

    private sealed class RequireOneForOperationRules : OperationNamedMessages
    {
        internal static readonly RequireOneForOperationRules Instance = new();

        internal override bool FailsOnNone => true;

        internal override bool FailsOnManyAndUndecodable => true;

        internal override string NoneMessage(string label, Func<string>? describeNone) =>
            $"{label}: expected exactly one result but the operation produced none (None).";

        internal override string ManyMessage(string label, EquatableArray<string> contractIds, Func<int, string>? describeMany) =>
            $"{label}: expected exactly one result but the operation produced {contractIds.Count} " +
            $"(contract ids: {string.Join(", ", contractIds)}).";

        internal override Exception? Unexpected(string outcomeTypeName, string label) =>
            Tag(new LedgerOperationException($"{label}: unexpected outcome {outcomeTypeName}."), label);
    }

    private sealed class AcceptNoneRules : OperationNamedMessages
    {
        internal static readonly AcceptNoneRules Instance = new();

        internal override bool FailsOnNone => false;

        internal override bool FailsOnManyAndUndecodable => false;

        internal override Exception? Unexpected(string outcomeTypeName, string label) => null;
    }
}
