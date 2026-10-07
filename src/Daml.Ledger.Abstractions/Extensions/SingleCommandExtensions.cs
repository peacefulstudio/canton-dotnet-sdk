// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime.Commands;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// The single-command submission path shared by generated choice exercisers and by the
/// hand-written write-path extensions, so the submission a single command is wrapped in
/// is built in one place rather than at every call site.
/// </summary>
public static class SingleCommandExtensions
{
    /// <summary>
    /// Submits <paramref name="command"/> as a single-command transaction and returns the
    /// submission task, minting a command id when the caller supplies none so every
    /// submission carries one rather than leaving <c>command_id</c> unset, where
    /// deduplicability would depend on the transport.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown synchronously when <paramref name="writer"/> or <paramref name="command"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown synchronously when <paramref name="commandId"/> is a default (uninitialized) value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown synchronously when <paramref name="configure"/> returns <c>null</c> or a submission
    /// whose commands are not the single <paramref name="command"/>.
    /// </exception>
    /// <param name="writer">The ledger writer.</param>
    /// <param name="command">The single command to submit.</param>
    /// <param name="submitter">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>
    /// <param name="workflowId">
    /// Optional workflow id; passed through to the ledger when non-empty. A <c>null</c> or empty
    /// value leaves <c>workflow_id</c> unset, because it is a correlation key and an empty one
    /// correlates nothing.
    /// </param>
    /// <param name="commandId">
    /// Optional command id for deduplication; a fresh id is minted only when omitted. Pass the
    /// same id across a retry of a lost-but-accepted submission so the ledger deduplicates the
    /// resubmission instead of re-executing the command. A minted id is not returned on a failed
    /// submission, so only a caller-supplied id makes an application-level retry deduplicable.
    /// </param>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport; transports without a
    /// server-side deadline apply a client-side bound only. <c>null</c> applies no deadline.
    /// </param>
    /// <param name="configure">
    /// Optional hook that receives the submission built for this call, already carrying the
    /// command, <paramref name="commandId"/> and <paramref name="workflowId"/>, and returns the
    /// one to submit. Use it to add what this method does not expose, for example
    /// <c>s =&gt; s.WithDisclosedContracts(holding.Disclosure!)</c>, or
    /// <c>WithDeduplicationPeriod</c>, <c>WithSynchronizerId</c> and <c>WithMinLedgerTime</c>.
    /// The submission's single command is owned by this method: a hook that returns a
    /// submission with any other command list, or <c>null</c>, makes the call throw
    /// <see cref="InvalidOperationException"/> before anything is submitted. The submitter's
    /// act-as and read-as parties replace any set on the returned submission when the writer
    /// dispatches it. <c>null</c> submits the submission unchanged.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome of the submission, carrying the resulting transaction on success.</returns>
    public static Task<ExerciseOutcome<TransactionResult>> TrySubmitSingleAsync(
        this ILedgerWriter writer,
        ICommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        Func<CommandsSubmission, CommandsSubmission>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(command);
        WriterExtensionHelpers.ThrowIfDefault(commandId);

        var submission = CommandsSubmission.Single(command)
            .WithCommandId(commandId ?? new CommandId(Guid.NewGuid().ToString()))
            .WithOptionalWorkflowId(workflowId);

        return writer.TrySubmitAndWaitForTransactionAsync(
            ApplyConfigure(submission, command, configure), submitter, timeout, cancellationToken);
    }

    /// <summary>
    /// Creates a <typeparamref name="TTemplate"/> contract from <paramref name="payload"/>, letting
    /// <paramref name="configure"/> adjust the submission first. Without a hook this is
    /// <see cref="ILedgerWriter.TryCreateAsync{TTemplate}(TTemplate, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
    /// unchanged. With one, the create command is submitted through
    /// <see cref="TrySubmitSingleAsync(ILedgerWriter, ICommand, SubmitterInfo, string?, CommandId?, TimeSpan?, Func{CommandsSubmission, CommandsSubmission}?, CancellationToken)"/>
    /// and the created contracts are projected onto <see cref="ContractId{T}"/> the way
    /// <see cref="CreateByExercise.TryCreateOneByExerciseAsync{TTemplate}(ILedgerWriter, ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, Func{CommandsSubmission, CommandsSubmission}?, CancellationToken)"/>
    /// projects them: one created contract yields <see cref="ExerciseOutcome{T}.One"/>, none yields
    /// <see cref="ExerciseOutcome{T}.None"/>, more than one yields <see cref="ExerciseOutcome{T}.Many"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown synchronously when <paramref name="writer"/> or <paramref name="payload"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown synchronously when <paramref name="commandId"/> is a default (uninitialized) value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown synchronously when <paramref name="configure"/> returns <c>null</c> or a submission
    /// whose commands are not the single create command.
    /// </exception>
    /// <typeparam name="TTemplate">The template type expected to be created.</typeparam>
    /// <param name="writer">The ledger writer.</param>
    /// <param name="payload">The template payload.</param>
    /// <param name="submitter">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>
    /// <param name="workflowId">
    /// Optional workflow id; <c>create-</c> followed by the lower-cased template type name when omitted
    /// and a hook is supplied, matching the workflow id the transports assign a create.
    /// </param>
    /// <param name="commandId">
    /// Optional command id for deduplication; a fresh id is minted only when omitted. Pass the same id
    /// across a retry of a lost-but-accepted submission so the ledger deduplicates the resubmission.
    /// </param>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport. <c>null</c> applies no deadline.
    /// </param>
    /// <param name="configure">
    /// Optional hook that receives the submission built for this call, already carrying the
    /// command, <paramref name="commandId"/> and <paramref name="workflowId"/>, and returns the
    /// one to submit. Use it to add what this method does not expose, for example
    /// <c>s =&gt; s.WithDisclosedContracts(holding.Disclosure!)</c>, or
    /// <c>WithDeduplicationPeriod</c>, <c>WithSynchronizerId</c> and <c>WithMinLedgerTime</c>.
    /// The submission's single command is owned by this method: a hook that returns a
    /// submission with any other command list, or <c>null</c>, makes the call throw
    /// <see cref="InvalidOperationException"/> before anything is submitted. The submitter's
    /// act-as and read-as parties replace any set on the returned submission when the writer
    /// dispatches it. <c>null</c> submits the submission unchanged.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome of the creation, carrying the created contract id on success.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        ILedgerWriter writer,
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        Func<CommandsSubmission, CommandsSubmission>? configure = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(payload);

        return configure is null
            ? writer.TryCreateAsync(payload, submitter, workflowId, commandId, timeout, cancellationToken)
            : TryCreateConfiguredAsync(writer, payload, submitter, workflowId, commandId, timeout, configure, cancellationToken);
    }

    private static async Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateConfiguredAsync<TTemplate>(
        ILedgerWriter writer,
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId,
        CommandId? commandId,
        TimeSpan? timeout,
        Func<CommandsSubmission, CommandsSubmission> configure,
        CancellationToken cancellationToken)
        where TTemplate : ITemplate
    {
        var outcome = await writer
            .TrySubmitSingleAsync(
                CreateCommand.For(payload),
                submitter,
                workflowId ?? $"create-{typeof(TTemplate).Name.ToLowerInvariant()}",
                commandId,
                timeout,
                configure,
                cancellationToken)
            .ConfigureAwait(false);

        return outcome.ProjectCommitted(CreateByExercise.ProjectSingleCreated<TTemplate>);
    }

    private static CommandsSubmission ApplyConfigure(
        CommandsSubmission submission,
        ICommand command,
        Func<CommandsSubmission, CommandsSubmission>? configure)
    {
        if (configure is null)
        {
            return submission;
        }

        var configured = configure(submission)
            ?? throw new InvalidOperationException(
                "The configure hook returned null; return the submission it was given, changed through its With... builders.");

        return configured.Commands.Count == 1 && configured.Commands[0].Equals(command)
            ? configured
            : throw new InvalidOperationException(
                "The configure hook changed the submission's command. The command belongs to the method that built the submission; adjust only the parts it does not expose.");
    }
}
