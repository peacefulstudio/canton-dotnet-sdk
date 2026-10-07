// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

/// <summary>The write capability: submit commands, create contracts, exercise choices.</summary>
public interface ILedgerWriter
{
    /// <summary>
    /// Exercises a choice using a <see cref="SubmitterInfo"/> and returns a structured
    /// outcome distinguishing success, Daml errors, and infrastructure errors. The
    /// caller switches on the result instead of catching exceptions. This is the
    /// primary authorization-carrying overload: the <paramref name="submitter"/>
    /// carries the act-as parties and any optional read-as parties through to the
    /// implementation.
    /// Use <see cref="Extensions.ThrowingExercise.ExerciseAsync{TResult}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,CommandId?,TimeSpan?,CancellationToken)"/>
    /// for the throwing convenience overload.
    /// </summary>
    /// <typeparam name="TResult">The result type of the choice.</typeparam>
    /// <param name="command">The exercise command.</param>
    /// <param name="submitter">
    /// The submitter authorization (act-as parties and optional read-as parties). A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party submission
    /// passes the party itself here.
    /// </param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="commandId">
    /// Optional command id for deduplication; a fresh id is minted only when omitted. Pass the
    /// same id across a retry of a lost-but-accepted submission so the ledger deduplicates the
    /// resubmission instead of re-executing the command.
    /// </param>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport. gRPC transports
    /// map it to <c>CallOptions.Deadline</c>, which bounds server-side call duration —
    /// still not a hard guarantee that participant-side work stops the instant the
    /// deadline elapses. Transports without a server-side deadline, such as JSON/REST,
    /// apply a client-side bound only and cannot stop the participant working on the
    /// call. An overrun is a transport failure and surfaces as an
    /// <see cref="ExerciseOutcome{TResult}.InfraError"/> outcome. <c>null</c>
    /// applies no deadline; <paramref name="cancellationToken"/> then remains the only bound.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token. When this token is cancelled, implementations must throw
    /// <see cref="OperationCanceledException"/> (or a subtype, e.g. <see cref="TaskCanceledException"/>)
    /// rather than mapping the cancellation to an <see cref="ExerciseOutcome{TResult}.InfraError"/>
    /// outcome — <c>InfraError</c> is reserved for genuine transport/infrastructure failures
    /// unrelated to the caller's own cancellation, matching the contract the streaming
    /// <see cref="ILedgerStreamer.SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/> methods already document.
    /// </param>
    /// <returns>A structured outcome; callers switch on the concrete subtype.</returns>
    Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits one or more commands as a single atomic transaction and waits for
    /// completion.
    /// </summary>
    /// <param name="submission">The commands submission.</param>
    /// <param name="submitter">
    /// The submitter authorization (act-as parties and optional read-as parties). Applied via
    /// <see cref="CommandsSubmission.WithSubmitter(SubmitterInfo)"/> before dispatch, overwriting any
    /// act-as/read-as parties already set on <paramref name="submission"/>.
    /// </param>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport — see
    /// <see cref="TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
    /// for the deadline contract. An overrun is a failure with no answer and raises a
    /// <see cref="LedgerOperationException"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token. When this token is cancelled, implementations must throw
    /// <see cref="OperationCanceledException"/> (or a subtype, e.g. <see cref="TaskCanceledException"/>)
    /// rather than wrapping the cancellation in a <see cref="LedgerOperationException"/> — this
    /// method signals failure by throwing, and <c>LedgerOperationException</c> is reserved for
    /// failures that come from the participant or the wire, never the caller's own cancellation,
    /// matching the contract the streaming
    /// <see cref="ILedgerStreamer.SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/> methods already document.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is <see langword="null"/>, thrown synchronously at the call.</exception>
    /// <exception cref="LedgerOperationException">
    /// The call failed: the participant could not be reached, did not answer within the deadline, rejected the
    /// submission, or committed it and answered with a body the client could not decode. The exception carries the
    /// transport-native <see cref="LedgerOperationException.Status"/> and, when the participant sent them, the
    /// <see cref="LedgerOperationException.Category"/>, <see cref="LedgerOperationException.ErrorId"/> and
    /// <see cref="LedgerOperationException.Metadata"/>. Read <see cref="LedgerOperationException.CommitState"/> before
    /// retrying: <see cref="CommitState.Unknown"/> when no answer arrived or the participant could not tell (resubmit
    /// with the same command id), <see cref="CommitState.NotCommitted"/> for a structured rejection, except that
    /// the error ids take precedence over the category: <c>DUPLICATE_COMMAND</c> is
    /// <see cref="CommitState.Committed"/> because the ledger already accepted a command with that command id
    /// (<see cref="CommitState.Unknown"/> when its <c>accepted</c> metadata is <c>"false"</c>), and
    /// <c>SUBMISSION_ALREADY_IN_FLIGHT</c> is <see cref="CommitState.Unknown"/>, and
    /// <see cref="CommitState.Committed"/> for an undecodable answer (do not resubmit).
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <returns>
    /// A <see cref="SubmitAndWaitResult"/> carrying the effective
    /// <see cref="SubmitAndWaitResult.CommandId"/> the participant recorded for the
    /// submission (used for deduplication — surfaced so callers can correlate the
    /// completion even when the id was assigned by the client), together with the
    /// resulting transaction's <see cref="SubmitAndWaitResult.UpdateId"/> and
    /// <see cref="SubmitAndWaitResult.CompletionOffset"/>.
    /// </returns>
    Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits commands and waits for the transaction result. Returns an
    /// <see cref="ExerciseOutcome{TransactionResult}"/> distinguishing success, structured
    /// Daml errors (with category, ID, metadata), and infrastructure errors —
    /// callers <c>switch</c> on the outcome instead of catching exceptions. Use the
    /// <see cref="TransactionResultExtensions"/> helpers (<c>Single&lt;T&gt;</c>,
    /// <c>TrySingle&lt;T&gt;</c>, <c>All&lt;T&gt;</c>) on the success payload to project
    /// created contracts to typed <see cref="ContractId{T}"/> values. A successful result
    /// must also include <see cref="TransactionResult.ExercisedEvents"/> with the exercise
    /// argument and result values, including nonconsuming exercises. Generated interface-choice
    /// wrappers use those events to decode the choice's declared result; implementations must
    /// request the transaction details needed to preserve them.
    /// </summary>
    /// <param name="submission">The commands submission.</param>
    /// <param name="submitter">
    /// The submitter authorization (act-as parties and optional read-as parties). Applied via
    /// <see cref="CommandsSubmission.WithSubmitter(SubmitterInfo)"/> before dispatch, overwriting any
    /// act-as/read-as parties already set on <paramref name="submission"/>.
    /// </param>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport — see
    /// <see cref="TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
    /// for the deadline contract. An overrun surfaces as an
    /// <see cref="ExerciseOutcome{TransactionResult}.InfraError"/> outcome.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token. Cancelling it must surface as
    /// <see cref="OperationCanceledException"/>, not as an
    /// <see cref="ExerciseOutcome{TransactionResult}.InfraError"/> outcome — see the
    /// contract documented on
    /// <see cref="TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>.
    /// </param>
    /// <returns>The outcome of the submission.</returns>
    Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new contract using a <see cref="SubmitterInfo"/> and projects the
    /// result to <see cref="ExerciseOutcome{T}"/> over <see cref="ContractId{T}"/>.
    /// This is the primary authorization-carrying overload: the
    /// <paramref name="submitter"/> carries the act-as parties and any optional
    /// read-as parties through to the implementation.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created.</typeparam>
    /// <param name="payload">The template payload.</param>
    /// <param name="submitter">
    /// The submitter authorization (act-as parties and optional read-as parties). A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party submission
    /// passes the party itself here.
    /// </param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="commandId">
    /// Optional command id for deduplication; a fresh id is minted only when omitted. Pass the
    /// same id across a retry of a lost-but-accepted submission so the ledger deduplicates the
    /// resubmission instead of re-executing the command.
    /// </param>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport — see
    /// <see cref="TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
    /// for the deadline contract. An overrun surfaces as an
    /// <see cref="ExerciseOutcome{TResult}.InfraError"/> outcome.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token. Cancelling it must surface as
    /// <see cref="OperationCanceledException"/>, not as an
    /// <see cref="ExerciseOutcome{TResult}.InfraError"/> outcome — see the contract
    /// documented on <see cref="TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>.
    /// </param>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate;
}
