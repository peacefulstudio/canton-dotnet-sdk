// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client.Integration.Tests;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Category", "MultiSync")]
public class IncompleteReassignmentSnapshotLiveTests
{
    private const string PartyIdHint = "rest-incomplete-snapshot-issuer";
    private const decimal AssetAmount = 100m;

    private static string DarPath() => RichTypesDar.Path;

    [Fact]
    public async Task SubscribeActiveAsync_snapshot_includes_a_contract_that_is_unassigned_and_not_yet_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestLiveLane.OpenAsync(cancellationToken);
        var harness = new RestReassignmentHarness(lane);
        var inFlight = await UnassignWithoutAssigningAsync(harness, cancellationToken);

        var snapshot = await harness.SubscribeActiveSnapshotAsync(inFlight.Issuer, cancellationToken);

        AssertContainsTheIncompleteReassignment(snapshot, inFlight);
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_snapshot_includes_a_contract_that_is_unassigned_and_not_yet_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestLiveLane.OpenAsync(cancellationToken);
        var harness = new RestReassignmentHarness(lane);
        var inFlight = await UnassignWithoutAssigningAsync(harness, cancellationToken);

        var snapshot = await harness.ActiveContractsPageSnapshotAsync(inFlight.Issuer, cancellationToken);

        AssertContainsTheIncompleteReassignment(snapshot, inFlight);
    }

    [Fact]
    public async Task SnapshotActiveAsync_returns_a_contract_that_is_unassigned_and_not_yet_assigned_on_its_source_synchronizer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestLiveLane.OpenAsync(cancellationToken);
        var harness = new RestReassignmentHarness(lane);
        var inFlight = await UnassignWithoutAssigningAsync(harness, cancellationToken);

        var snapshot = await harness.SnapshotActiveAssetsAsync(inFlight.Issuer, cancellationToken);

        var returned = Assert.Single(snapshot, active => active.Contract.Id.Value == inFlight.ContractId);
        Assert.Equal(inFlight.SourceSynchronizerId, returned.SynchronizerId.Value);
        Assert.Empty(snapshot.Unreadable);
    }

    [Fact]
    public async Task QueryActiveAsync_returns_a_holding_that_is_unassigned_and_not_yet_assigned_on_its_source_synchronizer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestLiveLane.OpenAsync(cancellationToken);
        var harness = new RestReassignmentHarness(lane);
        var inFlight = await UnassignWithoutAssigningAsync(harness, cancellationToken);

        var holdings = await harness.QueryActiveHoldingsAsync(inFlight.Issuer, cancellationToken);

        var returned = Assert.Single(holdings, active => active.Contract.Id.Value == inFlight.ContractId);
        Assert.Equal(inFlight.SourceSynchronizerId, returned.SynchronizerId.Value);
        Assert.Equal(AssetAmount, returned.Contract.View.Amount);
        Assert.Empty(holdings.Unreadable);
    }

    private static async Task<InFlightReassignment> UnassignWithoutAssigningAsync(
        RestReassignmentHarness harness, CancellationToken cancellationToken)
    {
        var synchronizers = await harness.DiscoverSynchronizerPairAsync(cancellationToken);
        await harness.VetRichTypesDarOnBothAsync(DarPath(), synchronizers, cancellationToken);

        var issuer = await harness.HostPartyOnBothSynchronizersAsync(PartyIdHint, synchronizers, cancellationToken);
        var contractId = await harness.CreateAssetAsync(
            issuer, synchronizers.Source, AssetAmount, cancellationToken);

        var unassigned = await harness.SubmitAndWaitUnassignAsync(
            issuer, contractId, synchronizers, cancellationToken);
        Assert.Equal(contractId, unassigned.ContractId.Value);

        return new InFlightReassignment(issuer, contractId, synchronizers.Source.Value);
    }

    private static void AssertContainsTheIncompleteReassignment(
        IReadOnlyList<AcsSnapshotEntry<Asset>> snapshot, InFlightReassignment inFlight)
    {
        var created = Assert.Single(
            snapshot.OfType<AcsSnapshotEntry<Asset>.Created>(),
            entry => entry.ContractId.Value == inFlight.ContractId);
        Assert.Equal(inFlight.SourceSynchronizerId, created.SynchronizerId.Value);
        Assert.Contains(
            snapshot,
            entry => entry is AcsSnapshotEntry<Asset>.Unclassified { Kind: UnclassifiedKind.UnassignedEvent });
    }

    private sealed record InFlightReassignment(Party Issuer, string ContractId, string SourceSynchronizerId);
}
