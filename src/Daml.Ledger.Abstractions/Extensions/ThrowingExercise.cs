// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// Throwing convenience overloads over <see cref="ILedgerWriter"/>.
/// These wrap the structured <c>Try*</c> primitives and throw on error outcomes.
/// </summary>
public static class ThrowingExercise
{
    /// <summary>
    /// Exercises a choice using a multi-party <see cref="SubmitterInfo"/> and returns
    /// the typed result. Throws <see cref="LedgerOperationException"/> on error.
    /// For structured error handling, use
    /// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand,SubmitterInfo,string?,CommandId?,TimeSpan?,CancellationToken)"/> instead.
    /// </summary>
    public static async Task<TResult> ExerciseAsync<TResult>(
        this ILedgerWriter writer,
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriterExtensionHelpers.ThrowIfDefault(commandId);
        var outcome = await writer.TryExerciseAsync<TResult>(command, submitter, workflowId, commandId, timeout, cancellationToken).ConfigureAwait(false);
        return outcome.GetResultOrThrow(cancellationToken);
    }

    /// <summary>
    /// Exercises a void choice with an explicit submitter, throwing on failure.
    /// Submits the exercise as a single-command transaction and discards the result.
    /// A <c>None</c> outcome throws <see cref="LedgerOperationException"/>: a method whose
    /// contract is to throw on failure cannot return normally when the writer reports that
    /// the submission produced no transaction.
    /// A <c>Many</c> outcome succeeds: the transaction committed, so reporting it as a
    /// submission failure would risk a resubmission of already-accepted work. A
    /// <c>CommittedUndecodable</c> outcome succeeds for the same reason — the transaction
    /// committed, only its response could not be decoded, which this void caller discards
    /// regardless.
    /// Structured Daml errors and infrastructure errors also throw
    /// <see cref="LedgerOperationException"/>.
    /// For structured error handling, use
    /// <see cref="SingleCommandExtensions.TrySubmitSingleAsync(ILedgerWriter,ICommand,SubmitterInfo,string?,CommandId?,TimeSpan?,Func{CommandsSubmission,CommandsSubmission}?,CancellationToken)"/> instead.
    /// </summary>
    public static async Task ExerciseAsync(
        this ILedgerWriter writer,
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        Func<CommandsSubmission, CommandsSubmission>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var outcome = await writer
            .TrySubmitSingleAsync(command, submitter, workflowId, commandId, timeout, configure, cancellationToken)
            .ConfigureAwait(false);
        outcome.ThrowIfError(cancellationToken);
    }

    private static T GetResultOrThrow<T>(this ExerciseOutcome<T> outcome, CancellationToken cancellationToken) =>
        outcome.ResultOrThrow(
            "TryExerciseAsync",
            static () => "TryExerciseAsync returned no result (None); expected exactly one. Use TryExerciseAsync for structured handling.",
            static count => $"TryExerciseAsync returned Many ({count} results); use TryCreateManyByExerciseAsync to collect created contract ids, or inspect the TransactionResult directly.",
            cancellationToken);

    private static void ThrowIfError<T>(this ExerciseOutcome<T> outcome, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case ExerciseOutcome<T>.One:
            case ExerciseOutcome<T>.Many:
            case ExerciseOutcome<T>.CommittedUndecodable:
                break;
            case ExerciseOutcome<T>.None:
                throw LedgerOperationException.CommittedWithoutDetail(
                    "TrySubmitSingleAsync returned no transaction (None); the command committed, but produced no transaction. Use TrySubmitSingleAsync for the non-throwing path.");
            case ExerciseOutcome<T>.DamlError e:
                throw e.ToException();
            case ExerciseOutcome<T>.InfraError e:
                e.ThrowAsCancellationOrException(cancellationToken);
                break;
            default:
                throw new LedgerOperationException(
                    $"Unexpected outcome {outcome.GetType().Name} from TrySubmitSingleAsync.");
        }
    }
}
