// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing.Localnet;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

public class CapabilityLaneTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GateStallLongerThanTheBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DeadlineForTheBudgetToFire = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HoldStreamWithBudgetAsync_does_not_charge_time_queued_behind_a_rights_change_to_the_budget()
    {
        var userId = Guid.NewGuid().ToString();
        var lane = LaneFor(userId);
        var change = await LedgerUserRightsGate.Shared.BeginRightsChangeAsync(userId, Ct);

        var admission = lane.HoldStreamWithBudgetAsync(Budget, Ct);
        await Task.Delay(GateStallLongerThanTheBudget, Ct);
        change.Dispose();
        using var hold = await admission;

        hold.Token.IsCancellationRequested.Should().BeFalse(
            "a stream that queued at the gate for longer than the budget has only just been admitted");
    }

    [Fact]
    public async Task HoldStreamWithBudgetAsync_fires_the_budget_after_admission()
    {
        var lane = LaneFor(Guid.NewGuid().ToString());

        using var hold = await lane.HoldStreamWithBudgetAsync(Budget, Ct);
        var wait = () => Task.Delay(DeadlineForTheBudgetToFire, hold.Token);

        await wait.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HoldStreamWithBudgetAsync_fires_the_budget_for_a_lane_with_no_gated_user()
    {
        var lane = new CapabilityLane<object>(new object(), () => ValueTask.CompletedTask);

        using var hold = await lane.HoldStreamWithBudgetAsync(Budget, Ct);
        var wait = () => Task.Delay(DeadlineForTheBudgetToFire, hold.Token);

        await wait.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HoldStreamWithBudgetAsync_holds_the_stream_until_disposed()
    {
        var userId = Guid.NewGuid().ToString();
        var lane = LaneFor(userId);

        var hold = await lane.HoldStreamWithBudgetAsync(Budget, Ct);
        LedgerUserRightsGate.Shared.StateOf(userId).OpenStreams.Should().Be(1);

        hold.Dispose();
        LedgerUserRightsGate.Shared.StateOf(userId).OpenStreams.Should().Be(0);
    }

    [Fact]
    public async Task HoldStreamWithBudgetAsync_cancels_with_the_token_of_the_caller()
    {
        var lane = LaneFor(Guid.NewGuid().ToString());
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        using var hold = await lane.HoldStreamWithBudgetAsync(DeadlineForTheBudgetToFire, caller.Token);
        await caller.CancelAsync();

        hold.Token.IsCancellationRequested.Should().BeTrue();
    }

    private static CapabilityLane<object> LaneFor(string userId) =>
        new(new object(), () => ValueTask.CompletedTask, rightsGatedUserId: userId);
}
