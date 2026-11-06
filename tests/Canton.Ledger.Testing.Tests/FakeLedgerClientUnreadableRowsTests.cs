// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientUnreadableRowsTests
{
    private static readonly Party Alice = new("alice");

    [Fact]
    public async Task SnapshotAsync_returning_unreadable_rows_lists_an_unclassified_row_of_the_staged_snapshot()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Unclassified<DemoAsset>(LedgerOffset.At(3), UnclassifiedKind.DecodeFailure),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(3)))
            .Build();

        var contracts = await client.SnapshotAsync<DemoAsset>(
            Alice, unreadableRows: UnreadableRows.Return, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().BeEmpty();
        contracts.Unreadable.Should().Equal(new UnreadableRow(UnclassifiedKind.DecodeFailure, null, LedgerOffset.At(3)));
    }

    [Fact]
    public async Task SnapshotAsync_keeping_the_default_policy_throws_on_the_same_unclassified_row()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Unclassified<DemoAsset>(LedgerOffset.At(3), UnclassifiedKind.DecodeFailure),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(3)))
            .Build();

        var snapshotting = async () => await client.SnapshotAsync<DemoAsset>(
            Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshotting.Should().ThrowAsync<LedgerOperationException>().WithMessage("*DecodeFailure*offset 3*");
    }
}
