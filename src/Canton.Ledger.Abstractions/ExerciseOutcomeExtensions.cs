// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Contextual unwrap helpers for <see cref="ExerciseOutcome{T}"/>: turn a non-success
/// outcome into a <see cref="LedgerOperationException"/> whose message and
/// <see cref="LedgerOperationExceptionExtensions">Operation property</see> name the
/// operation that failed, read off <see cref="ExerciseOutcome{T}.Operation"/>. Complements the
/// upstream throwing wrappers in <c>Daml.Ledger.Abstractions.Extensions.ThrowingExercise</c>,
/// which carry no operation context.
/// </summary>
/// <remarks>
/// A generated <c>Try&lt;Choice&gt;Async</c> and <c>TryCreateAsync</c> stamp the operation on every
/// outcome they return, so a caller never restates it. An outcome with no
/// <see cref="ExerciseOutcome{T}.Operation"/>, such as one built by hand, throws an exception whose
/// message and <c>Operation</c> property name none; assign
/// <see cref="ExerciseOutcome{T}.Operation"/> with a <c>with</c> expression to name one.
/// </remarks>
public static class ExerciseOutcomeExtensions
{
    /// <summary>
    /// Returns the single success result, or throws a <see cref="LedgerOperationException"/>
    /// that names the outcome's <see cref="ExerciseOutcome{T}.Operation"/>, for example
    /// <c>ConnectionRequest.Accept</c>, in its message and exposes it via the
    /// <c>Operation</c> extension property. A <see cref="ExerciseOutcome{T}.DamlError"/>
    /// outcome surfaces its category, error id, and metadata on the exception; an
    /// <see cref="ExerciseOutcome{T}.InfraError"/> outcome surfaces its transport status
    /// code and source exception. A <see cref="ExerciseOutcome{T}.None"/> or
    /// <see cref="ExerciseOutcome{T}.Many"/> outcome throws with
    /// <see cref="LedgerOperationException.CommitState"/> of <see cref="CommitState.Committed"/>,
    /// because the transaction committed and only its result count was unexpected. A
    /// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/> outcome
    /// throws with <see cref="LedgerOperationException.CommitState"/> of
    /// <see cref="CommitState.Committed"/> and, when the response was decoded far enough to read
    /// one, the committed transaction's <see cref="LedgerOperationException.UpdateId"/>.
    /// </summary>
    /// <param name="outcome">The outcome to unwrap.</param>
    public static T OneOrThrow<T>(this ExerciseOutcome<T> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return OutcomeUnwrapPolicy.OneOfOutcome(outcome);
    }

    /// <summary>
    /// Awaits the outcome and unwraps it with
    /// <see cref="OneOrThrow{T}(ExerciseOutcome{T})"/>, so a <c>Try*</c> call can
    /// be unwrapped fluently:
    /// <c>await contractId.TryAcceptAsync(client, argument, submitter).OneOrThrowAsync()</c>.
    /// </summary>
    /// <param name="outcomeTask">The pending outcome to await and unwrap.</param>
    public static async Task<T> OneOrThrowAsync<T>(this Task<ExerciseOutcome<T>> outcomeTask)
    {
        ArgumentNullException.ThrowIfNull(outcomeTask);

        var outcome = await outcomeTask.ConfigureAwait(false);
        return outcome.OneOrThrow();
    }

    /// <summary>
    /// Throws a contextual <see cref="LedgerOperationException"/> for a
    /// <see cref="ExerciseOutcome{T}.DamlError"/> or
    /// <see cref="ExerciseOutcome{T}.InfraError"/> outcome and returns silently otherwise —
    /// for callers that discard the result and treat <c>One</c>, <c>None</c>, <c>Many</c>, and
    /// <c>CommittedUndecodable</c> alike as success: the command committed, and only the response
    /// this caller discards could not be decoded. The exception names the outcome's
    /// <see cref="ExerciseOutcome{T}.Operation"/>.
    /// </summary>
    /// <param name="outcome">The outcome to check.</param>
    public static void ThrowIfError<T>(this ExerciseOutcome<T> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        OutcomeUnwrapPolicy.DiscardOutcome(outcome);
    }
}
