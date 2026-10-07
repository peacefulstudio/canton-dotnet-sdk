// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakePqsFilterParityTests : PqsFilterParityTests
{
    private static readonly PqsFilterSeed Seed = new(
        new Party("fake::pqs-filter-parity"),
        "fake-run",
        new ContractId<Marker>("00fake-marker-a"),
        new ContractId<Marker>("00fake-marker-b"),
        new ContractId<IHolding>("00fake-holding"),
        new ContractId<IHolding>("00fake-other-holding"));

    protected override Task<CapabilityLane<PqsFilterParityLane>> OpenAsync(CancellationToken cancellationToken)
    {
        var client = FakePqsClient.Create()
            .WithQueryResults([.. PqsFilterParitySeed.RichRecords(Seed).Select(Stage)])
            .WithQueryResults([.. PqsFilterParitySeed.OptionalCountsRows(Seed).Select(Stage)])
            .WithQueryResults([.. PqsFilterParitySeed.TypeCornersRows(Seed).Select(Stage)])
            .Build();
        return Task.FromResult(new CapabilityLane<PqsFilterParityLane>(
            new PqsFilterParityLane(client, Seed), () => ValueTask.CompletedTask));
    }

    private static int _nextDescendingIdSuffix = 99_999_999;

    private static Contract<T> Stage<T>(T data)
        where T : ITemplate =>
        new(new ContractId<T>($"00fake-{Interlocked.Decrement(ref _nextDescendingIdSuffix):D8}"), data);
}
