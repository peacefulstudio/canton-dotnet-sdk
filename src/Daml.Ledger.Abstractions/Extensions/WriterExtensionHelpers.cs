// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Daml.Runtime.Commands;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

internal static class WriterExtensionHelpers
{
    internal static void ThrowIfDefault(CommandId? commandId)
    {
        if (commandId == default(CommandId))
        {
            throw new ArgumentException(
                "A default (uninitialized) CommandId cannot be submitted. Construct one from a non-empty string, or omit the argument to have a fresh id minted.",
                nameof(commandId));
        }
    }

    internal static T ResultOrThrow<T>(
        this ExerciseOutcome<T> outcome,
        string operation,
        Func<string> describeNone,
        Func<int, string> describeMany,
        CancellationToken cancellationToken) =>
        outcome switch
        {
            ExerciseOutcome<T>.One one => one.Result,
            ExerciseOutcome<T>.None => throw LedgerOperationException.CommittedWithoutDetail(describeNone()),
            ExerciseOutcome<T>.Many many => throw LedgerOperationException.CommittedWithoutDetail(describeMany(many.Count)),
            ExerciseOutcome<T>.DamlError e => throw e.ToException(),
            ExerciseOutcome<T>.InfraError e => e.ThrowAsCancellationOrException(cancellationToken),
            ExerciseOutcome<T>.CommittedUndecodable e => throw e.ToException(),
            _ => throw new UnreachableException($"Unexpected outcome {outcome.GetType().Name} from {operation}."),
        };

    internal static LedgerOperationException ToException<T>(this ExerciseOutcome<T>.DamlError error) =>
        new($"Daml error [{error.Category}/{error.ErrorId}]: {error.Message}",
            error.Category, error.ErrorId, error.Metadata);

    internal static LedgerOperationException ToException<T>(this ExerciseOutcome<T>.InfraError error) =>
        new($"Infrastructure error [{error.Status}]: {error.Message}",
            error.Status, error.Category, error.SourceException);

    internal static LedgerOperationException ToException<T>(this ExerciseOutcome<T>.CommittedUndecodable error) =>
        new($"Committed but undecodable [{error.UpdateId ?? "unknown"}]: {error.Message}", error.UpdateId, error.SourceException);

    [DoesNotReturn]
    internal static T ThrowAsCancellationOrException<T>(this ExerciseOutcome<T>.InfraError error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw error.ToException();
    }
}
