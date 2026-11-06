// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public sealed class RecoveryExtensionsTests
{
    private static readonly Party Alice = new("alice::1220");
    private static readonly Party Bob = new("bob::1220");
    private static readonly CommandId CommandOfInterest = new("cmd-1");
    private static readonly DateTimeOffset MaxRecordTime = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeDeadline = MaxRecordTime.AddSeconds(-1);
    private static readonly DateTimeOffset AfterDeadline = MaxRecordTime.AddSeconds(1);
    private static readonly TimeSpan StallProbe = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ReopenBound = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Completion CompletionOf(
        CommandId commandId, long offset, params Party[] actAs) =>
        new(
            commandId,
            LedgerOffset.At(offset),
            EquatableArray.Create(actAs),
            new SynchronizerTime("sync-a::1220", BeforeDeadline),
            SubmissionId: null,
            UserId: null,
            DeduplicationPeriod: null,
            PaidTrafficCost: 0,
            TraceContext: null);

    private static CompletionStreamEvent Accepted(long offset, string updateId, params Party[] actAs) =>
        new CompletionStreamEvent.CommandAccepted(CompletionOf(CommandOfInterest, offset, actAs), updateId);

    private static CompletionStreamEvent Rejected(long offset, int code, string message, params Party[] actAs) =>
        new CompletionStreamEvent.CommandRejected(
            CompletionOf(CommandOfInterest, offset, actAs),
            new CompletionStatus(code, message, ErrorId: null, new Dictionary<string, string>()));

    private static CompletionStreamEvent CheckpointAt(long offset, params (string SynchronizerId, DateTimeOffset RecordTime)[] times) =>
        new CompletionStreamEvent.Checkpoint(
            LedgerOffset.At(offset),
            [.. times.Select(time => new SynchronizerTime(time.SynchronizerId, time.RecordTime))]);

    private static ConnectedSynchronizer Connected(string synchronizerId) =>
        new(synchronizerId + "-alias", synchronizerId, SynchronizerPermissionLevel.Submission);

    private static async IAsyncEnumerable<CompletionStreamEvent> Streaming(
        IReadOnlyList<CompletionStreamEvent> events,
        bool thenStall,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var streamEvent in events)
        {
            yield return streamEvent;
        }

        if (thenStall)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private static ICantonLedgerClient ClientStreaming(
        IReadOnlyList<CompletionStreamEvent> events,
        IReadOnlyList<ConnectedSynchronizer>? connected = null,
        bool thenStall = true)
    {
        var client = Substitute.For<ICantonLedgerClient>();
        client.CompletionStreamAsync(Arg.Any<SubmitterInfo>(), Arg.Any<LedgerOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call => Streaming(events, thenStall, call.Arg<CancellationToken>()));
        client.GetConnectedSynchronizersAsync(
                Arg.Any<Party?>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connected ?? [Connected("sync-a::1220")]));
        return client;
    }

    private static Task<RecoveryOutcome> RecoverAsync(
        ICantonLedgerClient client,
        SubmitterInfo? submitter = null,
        string? synchronizerId = null,
        CancellationToken cancellationToken = default) =>
        client.RecoverAsync(
            CommandOfInterest,
            submitter ?? Alice,
            LedgerOffset.At(5),
            MaxRecordTime,
            synchronizerId,
            cancellationToken);

    private static CancellationToken StallProbeToken() => new CancellationTokenSource(StallProbe).Token;

    private static CancellationToken ReopenBoundToken() => new CancellationTokenSource(ReopenBound).Token;

    [Fact]
    public async Task RecoverAsync_returns_Committed_with_the_update_id_of_a_matching_acceptance()
    {
        var client = ClientStreaming([Accepted(10, "update-9", Alice)]);

        var outcome = await RecoverAsync(client, cancellationToken: Ct);

        var committed = outcome.Should().BeOfType<RecoveryOutcome.Committed>().Subject;
        committed.UpdateId.Should().Be("update-9");
        committed.Completion.Offset.Should().Be(LedgerOffset.At(10));
    }

    [Fact]
    public async Task RecoverAsync_streams_from_the_begin_exclusive_offset_as_the_submitter()
    {
        var client = ClientStreaming([Accepted(10, "update-9", Alice)]);

        await RecoverAsync(client, cancellationToken: Ct);

        client.Received(1).CompletionStreamAsync(Alice, LedgerOffset.At(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverAsync_ignores_a_completion_of_another_command_id()
    {
        var other = new CompletionStreamEvent.CommandAccepted(CompletionOf(new CommandId("cmd-2"), 10, Alice), "update-other");
        var client = ClientStreaming([other, CheckpointAt(11, ("sync-a::1220", AfterDeadline))]);

        var outcome = await RecoverAsync(client, cancellationToken: Ct);

        outcome.Should().BeOfType<RecoveryOutcome.Lost>();
    }

    [Fact]
    public async Task RecoverAsync_ignores_the_same_command_id_acted_as_by_a_different_party()
    {
        var client = ClientStreaming([Accepted(10, "update-bob", Bob), CheckpointAt(11, ("sync-a::1220", AfterDeadline))]);

        var outcome = await RecoverAsync(client, cancellationToken: Ct);

        outcome.Should().BeOfType<RecoveryOutcome.Lost>();
    }

    [Fact]
    public async Task RecoverAsync_ignores_the_same_command_id_acted_as_by_an_overlapping_but_unequal_party_set()
    {
        var client = ClientStreaming(
            [Accepted(10, "update-both", Alice, Bob), CheckpointAt(11, ("sync-a::1220", AfterDeadline))]);

        var outcome = await RecoverAsync(client, cancellationToken: Ct);

        outcome.Should().BeOfType<RecoveryOutcome.Lost>();
    }

    [Fact]
    public async Task RecoverAsync_matches_a_multi_party_completion_when_the_act_as_sets_are_equal()
    {
        var client = ClientStreaming([Accepted(10, "update-both", Bob, Alice)]);

        var outcome = await RecoverAsync(client, new SubmitterInfo(new HashSet<Party> { Alice, Bob }), cancellationToken: Ct);

        outcome.Should().BeOfType<RecoveryOutcome.Committed>().Which.UpdateId.Should().Be("update-both");
    }

    [Fact]
    public async Task RecoverAsync_returns_Committed_for_an_acceptance_that_follows_a_rejection_of_the_same_change_id()
    {
        var client = ClientStreaming([Rejected(10, 14, "unavailable", Alice), Accepted(12, "update-12", Alice)]);

        var outcome = await RecoverAsync(client, cancellationToken: Ct);

        outcome.Should().BeOfType<RecoveryOutcome.Committed>().Which.UpdateId.Should().Be("update-12");
    }

    [Fact]
    public async Task RecoverAsync_returns_Rejected_with_the_latest_status_once_the_deadline_checkpoint_arrives()
    {
        var client = ClientStreaming(
        [
            Rejected(10, 14, "first", Alice),
            Rejected(12, 9, "second", Alice),
            CheckpointAt(13, ("sync-a::1220", AfterDeadline)),
        ]);

        var outcome = await RecoverAsync(client, cancellationToken: Ct);

        var rejected = outcome.Should().BeOfType<RecoveryOutcome.Rejected>().Subject;
        rejected.Status.Code.Should().Be(9);
        rejected.Status.Message.Should().Be("second");
        rejected.Completion.Offset.Should().Be(LedgerOffset.At(12));
    }

    [Fact]
    public async Task RecoverAsync_keeps_waiting_after_a_rejection_until_the_deadline_checkpoint()
    {
        var client = ClientStreaming([Rejected(10, 14, "unavailable", Alice)]);

        var act = () => RecoverAsync(client, cancellationToken: StallProbeToken());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecoverAsync_returns_Lost_when_a_checkpoint_passes_the_max_record_time_without_a_completion()
    {
        var client = ClientStreaming([CheckpointAt(11, ("sync-a::1220", AfterDeadline))]);

        var outcome = await RecoverAsync(client, cancellationToken: Ct);

        outcome.Should().BeOfType<RecoveryOutcome.Lost>();
    }

    [Fact]
    public async Task RecoverAsync_keeps_waiting_while_a_checkpoint_is_before_the_max_record_time()
    {
        var client = ClientStreaming([CheckpointAt(11, ("sync-a::1220", BeforeDeadline))]);

        var act = () => RecoverAsync(client, cancellationToken: StallProbeToken());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecoverAsync_keeps_waiting_while_a_checkpoint_is_exactly_at_the_max_record_time()
    {
        var client = ClientStreaming([CheckpointAt(11, ("sync-a::1220", MaxRecordTime))]);

        var act = () => RecoverAsync(client, cancellationToken: StallProbeToken());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecoverAsync_keeps_waiting_while_a_checkpoint_carries_no_synchronizer_time()
    {
        var client = ClientStreaming([CheckpointAt(11)]);

        var act = () => RecoverAsync(client, cancellationToken: StallProbeToken());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecoverAsync_keeps_waiting_while_only_one_of_two_connected_synchronizers_is_past_the_max_record_time()
    {
        var client = ClientStreaming(
            [CheckpointAt(11, ("sync-a::1220", AfterDeadline), ("sync-b::1220", BeforeDeadline))],
            [Connected("sync-a::1220"), Connected("sync-b::1220")]);

        var act = () => RecoverAsync(client, cancellationToken: StallProbeToken());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecoverAsync_returns_Lost_once_every_connected_synchronizer_has_passed_the_max_record_time_in_some_checkpoint()
    {
        var client = ClientStreaming(
            [
                CheckpointAt(11, ("sync-a::1220", AfterDeadline), ("sync-b::1220", BeforeDeadline)),
                CheckpointAt(12, ("sync-b::1220", AfterDeadline)),
            ],
            [Connected("sync-a::1220"), Connected("sync-b::1220")]);

        var outcome = await RecoverAsync(client, cancellationToken: Ct);

        outcome.Should().BeOfType<RecoveryOutcome.Lost>();
    }

    [Fact]
    public async Task RecoverAsync_counts_only_the_given_synchronizer_and_does_not_look_up_the_connected_ones()
    {
        var client = ClientStreaming(
            [CheckpointAt(11, ("sync-b::1220", AfterDeadline), ("sync-a::1220", BeforeDeadline))],
            [Connected("sync-a::1220"), Connected("sync-b::1220")]);

        var outcome = await RecoverAsync(client, synchronizerId: "sync-b::1220", cancellationToken: Ct);

        outcome.Should().BeOfType<RecoveryOutcome.Lost>();
        await client.DidNotReceiveWithAnyArgs().GetConnectedSynchronizersAsync(cancellationToken: Ct);
    }

    [Fact]
    public async Task RecoverAsync_keeps_waiting_when_the_given_synchronizer_has_not_passed_the_max_record_time()
    {
        var client = ClientStreaming(
            [CheckpointAt(11, ("sync-b::1220", AfterDeadline), ("sync-a::1220", BeforeDeadline))]);

        var act = () => RecoverAsync(client, synchronizerId: "sync-a::1220", cancellationToken: StallProbeToken());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecoverAsync_looks_up_the_connected_synchronizers_once_for_each_act_as_party()
    {
        var client = ClientStreaming([CheckpointAt(11, ("sync-a::1220", AfterDeadline))]);

        await RecoverAsync(client, new SubmitterInfo(new HashSet<Party> { Alice, Bob }), cancellationToken: Ct);

        await client.Received(1).GetConnectedSynchronizersAsync(
            Alice, Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetConnectedSynchronizersAsync(
            Bob, Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverAsync_throws_when_the_participant_reports_no_connected_synchronizer_for_the_submitter()
    {
        var client = ClientStreaming([CheckpointAt(11, ("sync-a::1220", AfterDeadline))], connected: []);

        var act = () => RecoverAsync(client, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Message.Should().Contain("no connected synchronizer");
    }

    [Fact]
    public async Task RecoverAsync_throws_a_LedgerOperationException_carrying_the_error_id_of_a_stream_error()
    {
        var client = ClientStreaming(
        [
            new CompletionStreamEvent.StreamError(
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
                "stream broke",
                DamlErrorCategory.TransientServerFailure,
                "STALE_STREAM_AUTHORIZATION"),
        ]);

        var act = () => RecoverAsync(client, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.ErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
        thrown.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.Message.Should().Be("stream broke");
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task RecoverAsync_throws_for_a_pruned_start_offset_instead_of_reporting_Lost()
    {
        var client = ClientStreaming(
        [
            new CompletionStreamEvent.StreamError(
                new TransportStatus.Grpc(GrpcStatusCode.FailedPrecondition),
                "offset 5 is pruned",
                DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
                "PARTICIPANT_PRUNED_DATA_ACCESSED"),
        ]);

        var act = () => RecoverAsync(client, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.ErrorId.Should().Be("PARTICIPANT_PRUNED_DATA_ACCESSED");
    }

    [Fact]
    public async Task RecoverAsync_throws_a_stream_error_even_after_a_remembered_rejection()
    {
        var client = ClientStreaming(
        [
            Rejected(10, 14, "unavailable", Alice),
            new CompletionStreamEvent.StreamError(new TransportStatus.NoResponse(), "connection reset"),
        ]);

        var act = () => RecoverAsync(client, cancellationToken: Ct);

        await act.Should().ThrowAsync<LedgerOperationException>();
    }

    [Fact]
    public async Task RecoverAsync_reopens_the_stream_after_the_last_observed_offset_when_it_ends_without_a_verdict()
    {
        var client = Substitute.For<ICantonLedgerClient>();
        client.GetConnectedSynchronizersAsync(
                Arg.Any<Party?>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConnectedSynchronizer>>([Connected("sync-a::1220")]));
        client.CompletionStreamAsync(Alice, LedgerOffset.At(5), Arg.Any<CancellationToken>())
            .Returns(call => Streaming([CheckpointAt(8, ("sync-a::1220", BeforeDeadline))], thenStall: false, call.Arg<CancellationToken>()));
        client.CompletionStreamAsync(Alice, LedgerOffset.At(8), Arg.Any<CancellationToken>())
            .Returns(call => Streaming([Accepted(9, "update-9", Alice)], thenStall: false, call.Arg<CancellationToken>()));

        var outcome = await RecoverAsync(client, cancellationToken: ReopenBoundToken());

        outcome.Should().BeOfType<RecoveryOutcome.Committed>().Which.UpdateId.Should().Be("update-9");
    }

    [Fact]
    public async Task RecoverAsync_keeps_a_rejection_remembered_before_the_stream_ended_across_the_reopen()
    {
        var client = Substitute.For<ICantonLedgerClient>();
        client.GetConnectedSynchronizersAsync(
                Arg.Any<Party?>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConnectedSynchronizer>>([Connected("sync-a::1220")]));
        client.CompletionStreamAsync(Alice, LedgerOffset.At(5), Arg.Any<CancellationToken>())
            .Returns(call => Streaming([Rejected(7, 9, "rejected-before-reopen", Alice)], thenStall: false, call.Arg<CancellationToken>()));
        client.CompletionStreamAsync(Alice, LedgerOffset.At(7), Arg.Any<CancellationToken>())
            .Returns(call => Streaming([CheckpointAt(8, ("sync-a::1220", AfterDeadline))], thenStall: false, call.Arg<CancellationToken>()));

        var outcome = await RecoverAsync(client, cancellationToken: ReopenBoundToken());

        outcome.Should().BeOfType<RecoveryOutcome.Rejected>().Which.Status.Message.Should().Be("rejected-before-reopen");
    }

    [Fact]
    public async Task RecoverAsync_keeps_the_synchronizers_already_past_the_max_record_time_across_the_reopen()
    {
        var client = Substitute.For<ICantonLedgerClient>();
        client.GetConnectedSynchronizersAsync(
                Arg.Any<Party?>(), Arg.Any<string?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConnectedSynchronizer>>([Connected("sync-a::1220"), Connected("sync-b::1220")]));
        client.CompletionStreamAsync(Alice, LedgerOffset.At(5), Arg.Any<CancellationToken>())
            .Returns(call => Streaming([CheckpointAt(8, ("sync-a::1220", AfterDeadline))], thenStall: false, call.Arg<CancellationToken>()));
        client.CompletionStreamAsync(Alice, LedgerOffset.At(8), Arg.Any<CancellationToken>())
            .Returns(call => Streaming([CheckpointAt(9, ("sync-b::1220", AfterDeadline))], thenStall: false, call.Arg<CancellationToken>()));

        var outcome = await RecoverAsync(client, cancellationToken: ReopenBoundToken());

        outcome.Should().BeOfType<RecoveryOutcome.Lost>();
    }

    [Fact]
    public async Task RecoverAsync_throws_OperationCanceledException_when_the_token_is_already_cancelled()
    {
        var client = ClientStreaming([Accepted(10, "update-9", Alice)]);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => RecoverAsync(client, cancellationToken: cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecoverAsync_throws_ArgumentNullException_for_a_null_client()
    {
        var act = () => RecoveryExtensions.RecoverAsync(
            null!, CommandOfInterest, Alice, LedgerOffset.At(5), MaxRecordTime, cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
