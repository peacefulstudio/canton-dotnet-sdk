// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Category", "MultiSync")]
public class IncompleteReassignmentSnapshotLiveTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private const string SingleSyncMessage =
        "the participant reports fewer than two connected synchronizers, so this is the "
        + "single-synchronizer lane. Bring up a multi-synchronizer participant to run this "
        + "reassignment live test.";

    private const string PartyIdHint = "incomplete-snapshot-issuer";
    private const decimal AssetAmount = 100m;

    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task SubscribeActiveAsync_snapshot_includes_a_contract_that_is_unassigned_and_not_yet_assigned()
    {
        await using var fixture = OpenFixtureOrSkip();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;
        var inFlight = await UnassignWithoutAssigningAsync(harness, cancellationToken);

        var snapshot = await harness.SubscribeActiveSnapshotAsync(inFlight.Issuer, cancellationToken);

        AssertContainsTheIncompleteReassignment(snapshot, inFlight);
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_snapshot_includes_a_contract_that_is_unassigned_and_not_yet_assigned()
    {
        await using var fixture = OpenFixtureOrSkip();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;
        var inFlight = await UnassignWithoutAssigningAsync(harness, cancellationToken);

        var snapshot = await harness.ActiveContractsPageSnapshotAsync(inFlight.Issuer, cancellationToken);

        AssertContainsTheIncompleteReassignment(snapshot, inFlight);
    }

    [Fact]
    public async Task SnapshotActiveAsync_returns_a_contract_that_is_unassigned_and_not_yet_assigned_on_its_source_synchronizer()
    {
        await using var fixture = OpenFixtureOrSkip();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;
        var inFlight = await UnassignWithoutAssigningAsync(harness, cancellationToken);

        var snapshot = await harness.SnapshotActiveAssetsAsync(inFlight.Issuer, cancellationToken);

        var returned = Assert.Single(snapshot, active => active.Contract.Id.Value == inFlight.ContractId);
        Assert.Equal(inFlight.SourceSynchronizerId, returned.SynchronizerId.Value);
        Assert.Empty(snapshot.Unreadable);
    }

    [Fact]
    public async Task QueryActiveAsync_returns_a_holding_that_is_unassigned_and_not_yet_assigned_on_its_source_synchronizer()
    {
        await using var fixture = OpenFixtureOrSkip();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;
        var inFlight = await UnassignWithoutAssigningAsync(harness, cancellationToken);

        var holdings = await harness.QueryActiveHoldingsAsync(inFlight.Issuer, cancellationToken);

        var returned = Assert.Single(holdings, active => active.Contract.Id.Value == inFlight.ContractId);
        Assert.Equal(inFlight.SourceSynchronizerId, returned.SynchronizerId.Value);
        Assert.Equal(AssetAmount, returned.Contract.View.Amount);
        Assert.Empty(holdings.Unreadable);
    }

    private static LocalnetFixture OpenFixtureOrSkip()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        return LocalnetFixture.FromEnvironment();
    }

    private static async Task<InFlightReassignment> UnassignWithoutAssigningAsync(
        ReassignmentHarness harness, CancellationToken cancellationToken)
    {
        await harness.UploadRichTypesDarAsync(cancellationToken);

        var synchronizers = await harness.ParticipantSynchronizersAsync(cancellationToken);
        if (synchronizers.Count < 2)
        {
            if (MultiSyncReassignmentGate.Required)
            {
                Assert.Fail($"Failing (multi-sync required): {SingleSyncMessage}");
            }

            Assert.Skip($"Skipping: {SingleSyncMessage}");
        }

        var sourceSynchronizerId = synchronizers[0].SynchronizerId;
        var targetSynchronizerId = synchronizers[1].SynchronizerId;

        var issuer = await harness.HostPartyOnBothSynchronizersAsync(
            PartyIdHint, sourceSynchronizerId, targetSynchronizerId, cancellationToken);
        var contractId = await harness.CreateAssetAsync(
            issuer, sourceSynchronizerId, AssetAmount, cancellationToken);

        var beginExclusiveOffset = await harness.LedgerEndAsync(cancellationToken);
        await harness.UnassignAsync(
            issuer, contractId, sourceSynchronizerId, targetSynchronizerId, cancellationToken);
        var unassigned = await harness.ObserveUnassignedAsync(
            ReassignmentEventFormats.Wildcard(issuer),
            beginExclusiveOffset,
            contractId,
            ObservationTimeout,
            cancellationToken);
        Assert.NotNull(unassigned);

        return new InFlightReassignment(issuer, contractId, sourceSynchronizerId);
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
