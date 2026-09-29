// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Testing.Localnet.Tests;

public class LedgerUserRightsGateTests
{
    private const string UserId = "c87743ab-80e0-4b83-935a-4c0582226691";
    private const string OtherUserId = "5b0e6c1f-2d7a-4e39-8c61-0f3a9b7d2e44";

    private static readonly TimeSpan PatientWaitLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ImpatientWaitLimit = TimeSpan.FromMilliseconds(50);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HoldStreamAsync_admits_streams_from_many_flows_side_by_side()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);

        using var first = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());
        using var second = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());

        gate.StateOf(UserId).OpenStreams.Should().Be(2);
    }

    [Fact]
    public async Task BeginRightsChangeAsync_waits_until_the_last_open_stream_on_the_user_closes()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        var first = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());
        var second = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());

        var change = gate.BeginRightsChangeAsync(UserId, Ct);
        gate.StateOf(UserId).Should().Be(new RightsGateState(2, false, 1, 0));

        first.Dispose();
        gate.StateOf(UserId).Should().Be(new RightsGateState(1, false, 1, 0));

        second.Dispose();
        gate.StateOf(UserId).Should().Be(new RightsGateState(0, true, 0, 0));
        (await change.AsTask().WaitAsync(PatientWaitLimit, Ct)).Dispose();
        gate.StateOf(UserId).Should().Be(new RightsGateState(0, false, 0, 0));
    }

    [Fact]
    public async Task BeginRightsChangeAsync_does_not_wait_for_a_stream_on_another_user()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var otherUsersStream = await InAnotherFlow(() => gate.HoldStreamAsync(OtherUserId, Ct).AsTask());

        var change = gate.BeginRightsChangeAsync(UserId, Ct);

        change.IsCompletedSuccessfully.Should().BeTrue();
        (await change).Dispose();
    }

    [Fact]
    public async Task HoldStreamAsync_queues_a_new_stream_behind_a_waiting_rights_change()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        var openBeforeTheChange = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());
        var change = gate.BeginRightsChangeAsync(UserId, Ct);

        var openedAfterTheChange = InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());
        await UntilAsync(() => gate.StateOf(UserId).WaitingStreams == 1);

        openBeforeTheChange.Dispose();
        gate.StateOf(UserId).Should().Be(new RightsGateState(0, true, 0, 1));

        (await change.AsTask().WaitAsync(PatientWaitLimit, Ct)).Dispose();
        gate.StateOf(UserId).Should().Be(new RightsGateState(1, false, 0, 0));
        (await openedAfterTheChange.WaitAsync(PatientWaitLimit, Ct)).Dispose();
    }

    [Fact]
    public async Task HoldStreamAsync_admits_a_second_stream_of_a_flow_that_already_holds_one_past_a_waiting_change()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var outer = await gate.HoldStreamAsync(UserId, Ct);
        var change = InAnotherFlow(() => gate.BeginRightsChangeAsync(UserId, Ct).AsTask());
        await UntilAsync(() => gate.StateOf(UserId).WaitingChanges == 1);

        var nested = gate.HoldStreamAsync(UserId, Ct);

        nested.IsCompletedSuccessfully.Should().BeTrue(
            "the waiting change can only be admitted once the outer stream closes, so queueing the nested one "
            + "behind it would deadlock the flow");
        (await nested).Dispose();
        outer.Dispose();
        (await change.WaitAsync(PatientWaitLimit, Ct)).Dispose();
    }

    [Fact]
    public async Task BeginRightsChangeAsync_throws_when_the_calling_flow_holds_a_stream_on_the_same_user()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var hold = await gate.HoldStreamAsync(UserId, Ct);

        var act = () => gate.BeginRightsChangeAsync(UserId, Ct);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(UserId).And.Contain("STALE_STREAM_AUTHORIZATION");
        gate.StateOf(UserId).Should().Be(new RightsGateState(1, false, 0, 0));
    }

    [Fact]
    public async Task BeginRightsChangeAsync_throws_when_a_flow_the_stream_holder_started_asks_for_it()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var hold = await gate.HoldStreamAsync(UserId, Ct);

        var act = () => Task.Run(() => gate.BeginRightsChangeAsync(UserId, Ct).AsTask());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task BeginRightsChangeAsync_waits_rather_than_throws_for_a_flow_unrelated_to_the_holder()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        var hold = await gate.HoldStreamAsync(UserId, Ct);

        var change = InAnotherFlow(() => gate.BeginRightsChangeAsync(UserId, Ct).AsTask());
        await UntilAsync(() => gate.StateOf(UserId).WaitingChanges == 1);

        hold.Dispose();
        (await change.WaitAsync(PatientWaitLimit, Ct)).Dispose();
    }

    [Fact]
    public async Task BeginRightsChangeAsync_admits_a_flow_whose_stream_has_closed()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        var hold = await gate.HoldStreamAsync(UserId, Ct);
        hold.Dispose();

        var change = gate.BeginRightsChangeAsync(UserId, Ct);

        change.IsCompletedSuccessfully.Should().BeTrue();
        (await change).Dispose();
    }

    [Fact]
    public async Task BeginRightsChangeAsync_admits_a_flow_that_holds_a_stream_only_on_another_user()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var otherUsersStream = await gate.HoldStreamAsync(OtherUserId, Ct);

        var change = gate.BeginRightsChangeAsync(UserId, Ct);

        change.IsCompletedSuccessfully.Should().BeTrue();
        (await change).Dispose();
    }

    [Fact]
    public async Task BeginRightsChangeAsync_cancelled_while_waiting_throws_and_admits_the_streams_queued_behind_it()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var abandon = new CancellationTokenSource();
        var openBeforeTheChange = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());
        var change = gate.BeginRightsChangeAsync(UserId, abandon.Token).AsTask();
        var queuedBehindTheChange = InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());
        await UntilAsync(() => gate.StateOf(UserId).WaitingStreams == 1);

        await abandon.CancelAsync();

        await change.Invoking(task => task.WaitAsync(PatientWaitLimit, Ct)).Should().ThrowAsync<OperationCanceledException>();
        (await queuedBehindTheChange.WaitAsync(PatientWaitLimit, Ct)).Dispose();
        openBeforeTheChange.Dispose();
        gate.StateOf(UserId).Should().Be(new RightsGateState(0, false, 0, 0));
    }

    [Fact]
    public async Task HoldStreamAsync_cancelled_while_waiting_throws_and_leaves_the_change_it_queued_behind_running()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var abandon = new CancellationTokenSource();
        var change = gate.BeginRightsChangeAsync(UserId, Ct);
        var stream = InAnotherFlow(() => gate.HoldStreamAsync(UserId, abandon.Token).AsTask());
        await UntilAsync(() => gate.StateOf(UserId).WaitingStreams == 1);

        await abandon.CancelAsync();

        await stream.Invoking(task => task.WaitAsync(PatientWaitLimit, Ct)).Should().ThrowAsync<OperationCanceledException>();
        gate.StateOf(UserId).Should().Be(new RightsGateState(0, true, 0, 0));
        (await change).Dispose();
    }

    [Fact]
    public async Task HoldStreamAsync_throws_without_queueing_when_the_token_is_already_cancelled()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => gate.HoldStreamAsync(UserId, cancelled.Token);

        act.Should().Throw<OperationCanceledException>();
        gate.StateOf(UserId).Should().Be(new RightsGateState(0, false, 0, 0));
    }

    [Fact]
    public async Task BeginRightsChangeAsync_throws_a_TimeoutException_naming_the_user_when_a_stream_outlives_the_wait_limit()
    {
        var gate = new LedgerUserRightsGate(ImpatientWaitLimit);
        using var neverCloses = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());

        var act = () => gate.BeginRightsChangeAsync(UserId, Ct).AsTask();

        (await act.Should().ThrowAsync<TimeoutException>()).Which.Message
            .Should().Contain(UserId).And.Contain("1 open stream(s)");
        gate.StateOf(UserId).Should().Be(new RightsGateState(1, false, 0, 0));
    }

    [Fact]
    public async Task HoldStreamAsync_throws_a_TimeoutException_behind_a_rights_change_that_never_ends()
    {
        var gate = new LedgerUserRightsGate(ImpatientWaitLimit);
        var neverEnds = gate.BeginRightsChangeAsync(UserId, Ct);

        var act = () => InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());

        (await act.Should().ThrowAsync<TimeoutException>()).Which.Message
            .Should().Contain(UserId).And.Contain("a rights change running");
        gate.StateOf(UserId).Should().Be(new RightsGateState(0, true, 0, 0));
        (await neverEnds).Dispose();
    }

    [Fact]
    public async Task Dispose_called_twice_on_a_StreamHold_closes_the_stream_once()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        using var stillOpen = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());
        var hold = await InAnotherFlow(() => gate.HoldStreamAsync(UserId, Ct).AsTask());

        hold.Dispose();
        hold.Dispose();

        gate.StateOf(UserId).OpenStreams.Should().Be(1);
    }

    [Fact]
    public async Task LedgerUserRightsGate_never_admits_a_rights_change_beside_an_open_stream_under_contention()
    {
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        var participant = new ContendedParticipant();
        var random = new Random(20260928);
        var flows = Enumerable.Range(0, 32)
            .Select(flow => (Seed: random.Next(), Changes: flow % 4 == 0))
            .Select(flow => InAnotherFlow(() => RunFlowAsync(gate, participant, new Random(flow.Seed), flow.Changes)))
            .ToArray();

        await Task.WhenAll(flows).WaitAsync(TimeSpan.FromSeconds(60), Ct);

        participant.Violations.Should().BeEmpty();
        participant.StreamsServed.Should().BeGreaterThan(0);
        participant.ChangesServed.Should().BeGreaterThan(0);
        gate.StateOf(UserId).Should().Be(new RightsGateState(0, false, 0, 0));
    }

    private static async Task<int> RunFlowAsync(
        LedgerUserRightsGate gate, ContendedParticipant participant, Random random, bool changesRights)
    {
        for (var step = 0; step < 40; step++)
        {
            if (changesRights && random.Next(3) == 0)
            {
                using var change = await gate.BeginRightsChangeAsync(UserId, Ct);
                await participant.ChangeRightsAsync(random.Next(3));
                continue;
            }

            using var hold = await gate.HoldStreamAsync(UserId, Ct);
            await participant.StreamAsync(random.Next(4));
            if (random.Next(4) == 0)
            {
                using var nested = await gate.HoldStreamAsync(UserId, Ct);
                await participant.StreamAsync(random.Next(2));
            }
        }

        return 0;
    }

    private static Task<T> InAnotherFlow<T>(Func<Task<T>> work)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(work);
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed > PatientWaitLimit)
            {
                throw new TimeoutException($"the gate did not reach the expected state within {PatientWaitLimit}");
            }

            await Task.Delay(1, Ct);
        }
    }

    private sealed class ContendedParticipant
    {
        private readonly Lock _sync = new();
        private int _openStreams;
        private bool _changing;

        internal List<string> Violations { get; } = [];

        internal int StreamsServed { get; private set; }

        internal int ChangesServed { get; private set; }

        internal async Task StreamAsync(int yields)
        {
            lock (_sync)
            {
                if (_changing)
                {
                    Violations.Add("a stream opened while a rights change was running");
                }

                _openStreams++;
                StreamsServed++;
            }

            await YieldAsync(yields);

            lock (_sync)
            {
                _openStreams--;
            }
        }

        internal async Task ChangeRightsAsync(int yields)
        {
            lock (_sync)
            {
                if (_openStreams > 0)
                {
                    Violations.Add($"a rights change ran beside {_openStreams} open stream(s)");
                }

                if (_changing)
                {
                    Violations.Add("two rights changes ran at once");
                }

                _changing = true;
                ChangesServed++;
            }

            await YieldAsync(yields);

            lock (_sync)
            {
                _changing = false;
            }
        }

        private static async Task YieldAsync(int yields)
        {
            for (var i = 0; i < yields; i++)
            {
                await Task.Yield();
            }
        }
    }
}
