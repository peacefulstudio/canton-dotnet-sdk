// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="CommandsSubmission.WithDeduplicationPeriod"/>, run live
/// against the gRPC and JSON transports through one shared set of bodies: the period a caller sets
/// reaches the participant, which deduplicates a resubmission inside it, accepts a period longer
/// than its configured maximum, refuses a period whose start would fall before the earliest ledger
/// time, and reports a period back on the resulting completion.
/// </summary>
/// <remarks>
/// <para>
/// Canton's configured <c>ledger-api.max-deduplication-duration</c> (30 seconds on LocalNet) is only
/// the period it applies when the submission sets none; it is not an upper bound on a submitted
/// duration. Canton rejects a duration as <c>INVALID_DEDUPLICATION_PERIOD</c> only when the
/// period's start underflows its earliest timestamp, 0001-01-01T00:00:00Z, or precedes the last
/// pruning.
/// </para>
/// <para>
/// There is no Fake lane: the in-memory client does not model ledger-side deduplication, and the
/// wire mapping both builders share is pinned without a participant by
/// <see cref="LedgerDeduplicationWireParityTests"/>. The completion is asserted to carry a period
/// rather than the exact one submitted, because a participant may widen the period it applied.
/// </para>
/// </remarks>
public abstract class LedgerDeduplicationParityTests
{
    private const string StaleStreamAuthorization = "STALE_STREAM_AUTHORIZATION";
    private const int CompletionDrainAttempts = 4;

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleAuthorizationBackoff = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Opens a lane over this provider's <see cref="ICantonLedgerClient"/>, with a freshly allocated
    /// party the client may act as.
    /// </summary>
    protected abstract Task<CapabilityLane<(ICantonLedgerClient Client, Party Owner)>> OpenDeduplicationAsync(
        CancellationToken cancellationToken);

    private static CommandsSubmission MarkerSubmission(Party owner, DeduplicationPeriod period) =>
        CommandsSubmission
            .Single(CreateCommand.For(new Marker(owner)))
            .WithActAs(owner)
            .WithCommandId(new CommandId(Guid.NewGuid().ToString()))
            .WithDeduplicationPeriod(period);

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_rejects_a_resubmission_inside_the_deduplication_period_as_a_duplicate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenDeduplicationAsync(cancellationToken);
        var (client, owner) = lane.Capability;
        var submission = MarkerSubmission(owner, new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5)));

        var first = await client.TrySubmitAndWaitForTransactionAsync(
            submission, owner, cancellationToken: cancellationToken);
        var second = await client.TrySubmitAndWaitForTransactionAsync(
            submission, owner, cancellationToken: cancellationToken);

        first.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>();
        var duplicate = second.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>().Subject;
        duplicate.ErrorId.Should().Be("DUPLICATE_COMMAND");
        duplicate.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_accepts_a_period_beyond_the_participant_maximum()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenDeduplicationAsync(cancellationToken);
        var (client, owner) = lane.Capability;
        var submission = MarkerSubmission(owner, new DeduplicationPeriod.Duration(TimeSpan.FromDays(3650)));

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, owner, cancellationToken: cancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_surfaces_a_period_starting_before_the_earliest_ledger_time_as_invalid()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenDeduplicationAsync(cancellationToken);
        var (client, owner) = lane.Capability;
        var periodReachingBeforeYearOne = TimeSpan.FromDays(1_000_000);
        var submission = MarkerSubmission(owner, new DeduplicationPeriod.Duration(periodReachingBeforeYearOne));

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, owner, cancellationToken: cancellationToken);

        var invalid = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>().Subject;
        invalid.ErrorId.Should().Be("INVALID_DEDUPLICATION_PERIOD");
        invalid.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
    }

    [Fact]
    public async Task CompletionStreamAsync_reports_a_deduplication_period_for_a_submission_that_set_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenDeduplicationAsync(cancellationToken);
        var (client, owner) = lane.Capability;
        var submission = MarkerSubmission(owner, new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5)));
        var beforeSubmission = await client.GetLedgerEndAsync(cancellationToken: cancellationToken);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, owner, cancellationToken: cancellationToken);
        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>();

        var accepted = await AcceptedCompletionAsync(
            client, owner, beforeSubmission, submission.CommandId!.Value, cancellationToken);

        accepted.Should().NotBeNull("the accepted command's completion has to reach the stream");
        accepted!.Completion.DeduplicationPeriod.Should().NotBeNull(
            "the participant reports the period it deduplicated the submission over");
    }

    private static async Task<CompletionStreamEvent.CommandAccepted?> AcceptedCompletionAsync(
        ICantonLedgerClient client,
        Party submitter,
        LedgerOffset beginExclusive,
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= CompletionDrainAttempts; attempt++)
        {
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(DrainTimeout);
            var endedOnStaleAuthorization = false;

            try
            {
                await foreach (var streamEvent in client.CompletionStreamAsync(submitter, beginExclusive, window.Token))
                {
                    switch (streamEvent)
                    {
                        case CompletionStreamEvent.CommandAccepted accepted
                            when accepted.Completion.CommandId == commandId:
                            return accepted;
                        case CompletionStreamEvent.StreamError { ErrorId: StaleStreamAuthorization }:
                            endedOnStaleAuthorization = true;
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
                when (window.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (!endedOnStaleAuthorization)
            {
                return null;
            }

            await Task.Delay(StaleAuthorizationBackoff, cancellationToken);
        }

        return null;
    }
}
