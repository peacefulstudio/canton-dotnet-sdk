// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using Daml.Runtime.Contracts;

namespace Daml.Runtime.Outcomes;

/// <summary>
/// Projects a transaction-level <see cref="ExerciseOutcome{T}"/> (as returned by
/// <c>ILedgerWriter.TrySubmitAndWaitForTransactionAsync</c>) onto a typed result outcome,
/// running a caller-supplied projector over the committed transaction while propagating every
/// non-committed outcome faithfully. Centralises the outcome-mapping switch that codegen-emitted
/// <c>Try&lt;Choice&gt;Async</c> exercisers would otherwise each inline, so the exhaustive handling of
/// every <see cref="ExerciseOutcome{T}"/> variant lives — and is unit-tested — in one place.
/// </summary>
public static class ExerciseOutcomeProjection
{
    /// <summary>
    /// Maps an <see cref="ExerciseOutcome{T}"/> over <see cref="TransactionResult"/> onto an
    /// <see cref="ExerciseOutcome{T}"/> over <typeparamref name="TProjected"/>:
    /// <list type="bullet">
    ///   <item><see cref="ExerciseOutcome{T}.One"/> — the transaction committed;
    ///   <paramref name="projectCommitted"/> is invoked on it and its result returned unchanged (so
    ///   the projector may itself yield <c>One</c>, <c>None</c>, or <c>Many</c>).</item>
    ///   <item><see cref="ExerciseOutcome{T}.None"/> / <see cref="ExerciseOutcome{T}.Many"/> — re-wrapped
    ///   over <typeparamref name="TProjected"/>, <c>Many</c> carrying the same <c>ContractIds</c>.
    ///   These are only reachable from a non-conforming writer — a conforming
    ///   <c>TrySubmitAndWaitForTransactionAsync</c> yields <c>One</c>, <c>DamlError</c>, or
    ///   <c>InfraError</c> — and are propagated faithfully rather than collapsed into a success shape or a
    ///   thrown exception, so a committed transaction is never misread as a submission failure and blindly
    ///   resubmitted.</item>
    ///   <item><see cref="ExerciseOutcome{T}.DamlError"/> / <see cref="ExerciseOutcome{T}.InfraError"/> —
    ///   re-wrapped over <typeparamref name="TProjected"/> with every field preserved.</item>
    /// </list>
    /// </summary>
    /// <typeparam name="TProjected">The projected success payload type.</typeparam>
    /// <param name="outcome">The transaction-level outcome to project.</param>
    /// <param name="projectCommitted">Projects the committed <see cref="TransactionResult"/> onto a typed
    /// result outcome. Invoked only for the <see cref="ExerciseOutcome{T}.One"/> case.</param>
    /// <returns>The projected outcome over <typeparamref name="TProjected"/>.</returns>
    public static ExerciseOutcome<TProjected> ProjectCommitted<TProjected>(
        this ExerciseOutcome<TransactionResult> outcome,
        Func<TransactionResult, ExerciseOutcome<TProjected>> projectCommitted)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(projectCommitted);

        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One one => projectCommitted(one.Result),
            ExerciseOutcome<TransactionResult>.None => new ExerciseOutcome<TProjected>.None(),
            ExerciseOutcome<TransactionResult>.Many many => new ExerciseOutcome<TProjected>.Many(many.ContractIds),
            ExerciseOutcome<TransactionResult>.DamlError e => new ExerciseOutcome<TProjected>.DamlError(e.Category, e.ErrorId, e.Message, e.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError e => new ExerciseOutcome<TProjected>.InfraError(e.Status, e.Message, e.Category, e.SourceException),
            ExerciseOutcome<TransactionResult>.CommittedUndecodable e => new ExerciseOutcome<TProjected>.CommittedUndecodable(e.UpdateId, e.Message, e.SourceException),
            _ => throw new UnreachableException($"Unexpected outcome {outcome.GetType().Name} from TrySubmitAndWaitForTransactionAsync."),
        };
    }
}
