// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// Submits a <see cref="CommandBatch"/> as one atomic transaction and reads each command's typed result
/// back from it.
/// </summary>
public static class CommandBatchExtensions
{
    /// <summary>
    /// Submits every command of <paramref name="batch"/>, in the order they were added, as one transaction,
    /// and returns each command's typed result in a <see cref="BatchResult"/>. The transaction commits all
    /// of them or none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every outcome carries the command id the submission was sent with, minted or supplied, in
    /// <see cref="ExerciseOutcome{T}.CommandId"/>. A rejected or failed submission, such as one whose second
    /// command found its contract already archived, carries the batch's <see cref="LedgerOperation.Batch"/> in
    /// <see cref="ExerciseOutcome{T}.Operation"/>, so <c>OneOrThrow()</c> names every command, e.g.
    /// <c>Connection.Disconnect ×2, InviteLink.Withdraw ×3</c>. A committed one does too.
    /// </para>
    /// <para>
    /// Each command's result is read from the transaction's root events, which are the commands' own nodes:
    /// a create from the first unread root create of its template, an exercise from the first unread root
    /// exercise of its choice on its contract. A create nested under an exercise in the batch is not read as
    /// a root one. When a command's root event is missing, or its result does not decode, the outcome is
    /// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/> carrying that command's
    /// <see cref="LedgerOperation.Command"/>: the transaction committed, so do not resubmit it.
    /// </para>
    /// </remarks>
    /// <param name="writer">The ledger writer.</param>
    /// <param name="batch">The commands to submit; at least one. Commands added after this call starts are
    /// not submitted.</param>
    /// <param name="submitter">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>
    /// <param name="workflowId">Optional workflow id; a <c>null</c> or blank value leaves <c>workflow_id</c> unset.</param>
    /// <param name="commandId">Optional command id for deduplication; a fresh id is minted only when omitted.</param>
    /// <param name="timeout">Optional per-attempt deadline; <c>null</c> falls back to the client-wide <c>Timeout</c> option.</param>
    /// <param name="configure">
    /// Optional hook that receives the submission built for this call and returns the one to submit, e.g.
    /// <c>s =&gt; s.WithDisclosedContracts(...)</c>. The commands belong to the batch: a hook that changes them,
    /// or returns <c>null</c>, makes the call throw <see cref="InvalidOperationException"/> before anything
    /// is submitted.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome of the submission, carrying the <see cref="BatchResult"/> on success.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="batch"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="batch"/> is empty, or <paramref name="commandId"/>
    /// is a default (uninitialized) value.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="configure"/> returned <c>null</c> or
    /// changed the submission's commands.</exception>
    public static Task<ExerciseOutcome<BatchResult>> TrySubmitBatchAsync(
        this ILedgerWriter writer,
        CommandBatch batch,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        Func<CommandsSubmission, CommandsSubmission>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(batch);
        WriterExtensionHelpers.ThrowIfDefault(commandId);

        var submitted = batch.Snapshot();
        if (submitted.Count == 0)
        {
            throw new ArgumentException("A batch needs at least one command to submit.", nameof(batch));
        }

        var commands = submitted.Select(entry => entry.Command).ToArray();
        var submission = CommandsSubmission.Multiple(commands)
            .WithCommandId(WriterExtensionHelpers.CommandIdOrMint(commandId))
            .WithOptionalWorkflowId(workflowId);

        return SubmitAsync(
            writer, batch, submitted, ApplyConfigure(submission, commands, configure), submitter, timeout, cancellationToken);
    }

    private static async Task<ExerciseOutcome<BatchResult>> SubmitAsync(
        ILedgerWriter writer,
        CommandBatch batch,
        IReadOnlyList<BatchEntry> submitted,
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var outcome = await writer
            .TrySubmitAndWaitForTransactionAsync(submission, submitter, timeout, cancellationToken)
            .ConfigureAwait(false);

        var projected = outcome
            .WithCommandId(submission.CommandId?.Value)
            .ProjectCommitted(transaction => batch.Project(submitted, transaction));
        return projected.Operation is null
            ? projected.WithOperation(CommandBatch.OperationOf(submitted))
            : projected;
    }

    private static CommandsSubmission ApplyConfigure(
        CommandsSubmission submission,
        IReadOnlyList<ICommand> commands,
        Func<CommandsSubmission, CommandsSubmission>? configure)
    {
        if (configure is null)
        {
            return submission;
        }

        var configured = configure(submission)
            ?? throw new InvalidOperationException(
                "The configure hook returned null; return the submission it was given, changed through its With... builders.");

        return configured.Commands.SequenceEqual(commands)
            ? configured
            : throw new InvalidOperationException(
                "The configure hook changed the submission's commands. The commands belong to the batch; adjust only the parts it does not expose.");
    }
}
