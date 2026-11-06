// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Resolves a write that failed with <c>CommitState.Unknown</c> from the completion stream.
/// </summary>
public static class RecoveryExtensions
{
    private static readonly TimeSpan ReopenDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Reads the completion stream for <paramref name="commandId"/> until the stream shows the
    /// command committed, rejected or lost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A completion matches when its command id equals <paramref name="commandId"/> and its
    /// <c>act_as</c> parties equal <paramref name="submitter"/>'s <c>ActAs</c> set; the submission
    /// id is not compared. The first matching acceptance returns
    /// <see cref="RecoveryOutcome.Committed"/> at once. A matching rejection is remembered, the
    /// latest one winning, because the same command id can be resubmitted and accepted later.
    /// </para>
    /// <para>
    /// The command is lost once a checkpoint carries a record time strictly greater than
    /// <paramref name="maxRecordTime"/> for every awaited synchronizer with no completion in between: the
    /// result is then <see cref="RecoveryOutcome.Rejected"/> when a rejection was seen, otherwise
    /// <see cref="RecoveryOutcome.Lost"/>. A checkpoint exactly at <paramref name="maxRecordTime"/> does
    /// not count. The awaited synchronizers are the ones the participant is connected to for any
    /// <c>ActAs</c> party, looked up once when the call starts, or only <paramref name="synchronizerId"/>
    /// when given.
    /// </para>
    /// <para>
    /// Compute <paramref name="maxRecordTime"/> as the time you submitted the command, plus the
    /// synchronizer's <c>ledgerTimeRecordTimeTolerance</c> (60 seconds by default; the Ledger API does not
    /// expose it), plus a margin for clock skew between your clock and the ledger's. An underestimate is
    /// recoverable: resubmit the command with the same command id inside the deduplication window and the
    /// participant answers <c>DUPLICATE_COMMAND</c>, which surfaces as <c>CommitState.Committed</c>.
    /// </para>
    /// <para>
    /// Known gaps. Once the deduplication window (<c>max-deduplication-duration</c>, 7 days by default)
    /// has expired, matching on the change id is no longer safe. Recovering as a different user than the
    /// one that submitted is not covered. A <paramref name="beginExclusive"/> older than the
    /// participant's pruning offset fails with <c>PARTICIPANT_PRUNED_DATA_ACCESSED</c> rather than
    /// returning <see cref="RecoveryOutcome.Lost"/>; that failure is the symptom of a start offset taken
    /// too long ago.
    /// </para>
    /// <para>
    /// A stream that ends without a verdict is reopened after the last observed offset. A stream fault is
    /// not retried.
    /// </para>
    /// </remarks>
    /// <param name="client">The ledger client to read the completion stream from.</param>
    /// <param name="commandId">The command id of the write that failed with an unknown outcome, from
    /// <c>LedgerOperationException.CommandId</c>.</param>
    /// <param name="submitter">The submitter the command was submitted as.</param>
    /// <param name="beginExclusive">The ledger end captured before submitting; the stream starts strictly
    /// after it.</param>
    /// <param name="maxRecordTime">The latest record time at which the command can still complete.</param>
    /// <param name="synchronizerId">When given, only this synchronizer's checkpoints count towards
    /// <see cref="RecoveryOutcome.Lost"/>; when <c>null</c>, every connected synchronizer must pass.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verdict the completion stream gave.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
    /// <exception cref="LedgerOperationException">The completion stream faulted, the connected synchronizers
    /// could not be read, or the participant reported none for the submitter. Reading changes nothing, so
    /// <see cref="LedgerOperationException.CommitState"/> is always <see cref="CommitState.NotCommitted"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<RecoveryOutcome> RecoverAsync(
        this ICantonLedgerClient client,
        CommandId commandId,
        SubmitterInfo submitter,
        LedgerOffset beginExclusive,
        DateTimeOffset maxRecordTime,
        string? synchronizerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        cancellationToken.ThrowIfCancellationRequested();

        var awaitedSynchronizers = synchronizerId is null
            ? await ConnectedSynchronizerIdsAsync(client, submitter, cancellationToken).ConfigureAwait(false)
            : new HashSet<string> { synchronizerId };
        var synchronizersPastDeadline = new HashSet<string>();
        CompletionStreamEvent.CommandRejected? latestRejection = null;
        var resumeOffset = beginExclusive;

        while (true)
        {
            await foreach (var streamEvent in client
                .CompletionStreamAsync(submitter, resumeOffset, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                switch (streamEvent)
                {
                    case CompletionStreamEvent.CommandAccepted accepted:
                        if (Matches(accepted.Completion, commandId, submitter))
                        {
                            return new RecoveryOutcome.Committed(accepted.UpdateId, accepted.Completion);
                        }

                        resumeOffset = accepted.Completion.Offset;
                        break;

                    case CompletionStreamEvent.CommandRejected rejected:
                        if (Matches(rejected.Completion, commandId, submitter))
                        {
                            latestRejection = rejected;
                        }

                        resumeOffset = rejected.Completion.Offset;
                        break;

                    case CompletionStreamEvent.Checkpoint checkpoint:
                        resumeOffset = checkpoint.Offset;
                        foreach (var synchronizerTime in checkpoint.SynchronizerTimes)
                        {
                            if (awaitedSynchronizers.Contains(synchronizerTime.SynchronizerId)
                                && synchronizerTime.RecordTime > maxRecordTime)
                            {
                                synchronizersPastDeadline.Add(synchronizerTime.SynchronizerId);
                            }
                        }

                        if (synchronizersPastDeadline.Count == awaitedSynchronizers.Count)
                        {
                            return latestRejection is null
                                ? new RecoveryOutcome.Lost()
                                : new RecoveryOutcome.Rejected(latestRejection.Completion, latestRejection.Status);
                        }

                        break;

                    case CompletionStreamEvent.StreamError error:
                        throw new LedgerOperationException(
                            error.Message,
                            error.Status,
                            CommitState.NotCommitted,
                            error.Category,
                            error.ErrorId,
                            metadata: null,
                            error.SourceException);
                }
            }

            await Task.Delay(ReopenDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool Matches(Completion completion, CommandId commandId, SubmitterInfo submitter) =>
        completion.CommandId == commandId && submitter.ActAs.SetEquals(completion.ActAs);

    private static async Task<HashSet<string>> ConnectedSynchronizerIdsAsync(
        ICantonLedgerClient client, SubmitterInfo submitter, CancellationToken cancellationToken)
    {
        var synchronizerIds = new HashSet<string>();
        foreach (var party in submitter.ActAs)
        {
            var connected = await client.GetConnectedSynchronizersAsync(
                party, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var synchronizer in connected)
            {
                synchronizerIds.Add(synchronizer.SynchronizerId);
            }
        }

        if (synchronizerIds.Count == 0)
        {
            throw new LedgerOperationException(
                "The participant reports no connected synchronizer for the submitter, so a lost command cannot be told from a pending one.");
        }

        return synchronizerIds;
    }
}
