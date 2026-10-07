// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for the multi-synchronizer failure path of
/// <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient.TrySubmitAndWaitForReassignmentAsync{T}"/>
/// over gRPC: a reassignment the participant rejects for a multi-synchronizer reason surfaces as an
/// <see cref="ExerciseOutcome{T}.DamlError"/> carrying the participant's error category, not as a thrown
/// exception. The REST twin is <c>RestReassignmentRejectionConformanceTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public class ReassignmentRejectionConformanceTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private const string SingleSyncMessage =
        "the participant reports fewer than two connected synchronizers, so this is the "
        + "single-synchronizer lane. Bring up a multi-synchronizer participant to run this "
        + "reassignment conformance test.";

    private const decimal AssetAmount = 100m;

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_surfaces_an_unassign_by_a_non_stakeholder_as_a_DamlError()
    {
        await using var fixture = OpenFixture();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, target) = await RequireTwoSynchronizersAsync(harness, cancellationToken);

        var issuer = await harness.HostPartyOnBothSynchronizersAsync("rejection-issuer", source, target, cancellationToken);
        var stranger = await harness.HostPartyOnBothSynchronizersAsync("rejection-stranger", source, target, cancellationToken);
        var contractId = await harness.CreateAssetAsync(issuer, source, AssetAmount, cancellationToken);

        var outcome = await harness.TryUnassignAsync(stranger, contractId, source, target, cancellationToken);

        var error = Assert.IsType<ExerciseOutcome<ContractStreamEvent<Asset>>.DamlError>(outcome);
        Assert.Equal(DamlErrorCategory.InvalidIndependentOfSystemState, error.Category);
        Assert.Equal("INVALID_ARGUMENT", error.ErrorId);
        Assert.Contains("is not a stakeholder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_surfaces_an_unassign_to_a_synchronizer_not_hosting_the_stakeholder_as_a_DamlError()
    {
        await using var fixture = OpenFixture();
        await using var harness = ReassignmentHarness.FromFixture(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, target) = await RequireTwoSynchronizersAsync(harness, cancellationToken);

        var issuer = await harness.HostPartyOnOneSynchronizerAsync("rejection-source-only", source, cancellationToken);
        var contractId = await harness.CreateAssetAsync(issuer, source, AssetAmount, cancellationToken);

        var outcome = await harness.TryUnassignAsync(issuer, contractId, source, target, cancellationToken);

        var error = Assert.IsType<ExerciseOutcome<ContractStreamEvent<Asset>>.DamlError>(outcome);
        Assert.Equal(DamlErrorCategory.InvalidIndependentOfSystemState, error.Category);
        Assert.Equal("INVALID_ARGUMENT", error.ErrorId);
        Assert.Contains("stakeholders are not active on the target synchronizer", error.Message, StringComparison.Ordinal);
    }

    private static LocalnetFixture OpenFixture()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        return LocalnetFixture.FromEnvironment();
    }

    private static async Task<(string Source, string Target)> RequireTwoSynchronizersAsync(
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

        return (synchronizers[0].SynchronizerId, synchronizers[1].SynchronizerId);
    }
}
