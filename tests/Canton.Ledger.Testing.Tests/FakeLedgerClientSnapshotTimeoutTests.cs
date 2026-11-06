// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientSnapshotTimeoutTests
{
    private static readonly Party Alice = new("alice");

    [Fact]
    public void TimeProvider_defaults_to_the_system_clock()
    {
        var client = FakeLedgerClient.Create().Build();

        client.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void TimeProvider_is_the_clock_the_builder_staged()
    {
        var clock = new FakeTimeProvider();

        var client = FakeLedgerClient.Create().WithTimeProvider(clock).Build();

        client.TimeProvider.Should().BeSameAs(clock);
    }

    [Fact]
    public async Task QueryActiveAsync_with_a_zero_timeout_reports_the_deadline_failure()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithTimeProvider(new FakeTimeProvider())
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(
                HoldingCheckpoint())
            .Build();

        var querying = async () => await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Alice, timeout: TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await querying.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task QueryActiveAsync_with_a_timeout_that_has_not_expired_returns_the_snapshot()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithTimeProvider(new FakeTimeProvider())
            .WithActiveInterfaceContracts<IDemoHoldingView, DemoHoldingView>(
                HoldingCheckpoint())
            .Build();

        var holdings = await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Alice, timeout: TimeSpan.FromSeconds(30), cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().BeEmpty();
    }

    [Fact]
    public async Task SnapshotAsync_with_a_zero_timeout_reports_the_deadline_failure()
    {
        var client = FakeLedgerClient.Create()
            .WithTimeProvider(new FakeTimeProvider())
            .WithActiveContracts(LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(1)))
            .Build();

        var snapshotting = async () => await client.SnapshotAsync<DemoAsset>(
            Alice, timeout: TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await snapshotting.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
    }

    [Fact]
    public async Task SnapshotActiveAsync_with_a_timeout_that_has_not_expired_returns_the_snapshot()
    {
        var client = FakeLedgerClient.Create()
            .WithTimeProvider(new FakeTimeProvider())
            .WithActiveContracts(LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(1)))
            .Build();

        var contracts = await client.SnapshotActiveAsync<DemoAsset>(
            Alice, timeout: TimeSpan.FromSeconds(30), cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().BeEmpty();
    }

    private static InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView> HoldingCheckpoint() =>
        new InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Checkpoint(
            new StakeholderResume(LedgerOffset.At(1)));
}
