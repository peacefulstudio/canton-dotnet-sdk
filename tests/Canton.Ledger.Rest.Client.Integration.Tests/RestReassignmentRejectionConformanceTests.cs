// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client.Integration.Tests;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for the multi-synchronizer failure path of
/// <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient.TrySubmitAndWaitForReassignmentAsync{T}"/>
/// over the JSON Ledger API: a reassignment the participant rejects for a multi-synchronizer reason
/// surfaces as an <see cref="ExerciseOutcome{T}.DamlError"/> carrying the participant's error category,
/// not as a thrown exception. The gRPC twin is
/// <c>Canton.Ledger.Grpc.Client.Integration.Tests.ReassignmentRejectionConformanceTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public class RestReassignmentRejectionConformanceTests
{
    private const string PartyIdHint = "rest-rejection";
    private const decimal AssetAmount = 100m;

    private static string DarPath() => RichTypesDar.Path;

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_surfaces_an_unassign_by_a_non_stakeholder_as_a_DamlError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestConformanceLane.OpenAsync(cancellationToken);
        var harness = new RestReassignmentHarness(lane);
        var synchronizers = await harness.DiscoverSynchronizerPairAsync(cancellationToken);
        await harness.VetRichTypesDarOnBothAsync(DarPath(), synchronizers, cancellationToken);

        var issuer = await harness.HostPartyOnBothSynchronizersAsync($"{PartyIdHint}-issuer", synchronizers, cancellationToken);
        var stranger = await harness.HostPartyOnBothSynchronizersAsync($"{PartyIdHint}-stranger", synchronizers, cancellationToken);
        var contractId = await harness.CreateAssetAsync(issuer, synchronizers.Source, AssetAmount, cancellationToken);

        var outcome = await harness.TryUnassignAsync(stranger, contractId, synchronizers, cancellationToken);

        var error = Assert.IsType<ExerciseOutcome<ContractStreamEvent<Asset>>.DamlError>(outcome);
        Assert.Equal(DamlErrorCategory.InvalidIndependentOfSystemState, error.Category);
        Assert.Equal("INVALID_ARGUMENT", error.ErrorId);
        Assert.Contains("is not a stakeholder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_surfaces_an_unassign_to_a_synchronizer_not_hosting_the_stakeholder_as_a_DamlError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestConformanceLane.OpenAsync(cancellationToken);
        var harness = new RestReassignmentHarness(lane);
        var synchronizers = await harness.DiscoverSynchronizerPairAsync(cancellationToken);
        await harness.VetRichTypesDarOnBothAsync(DarPath(), synchronizers, cancellationToken);

        var issuer = await harness.HostPartyOnOneSynchronizerAsync($"{PartyIdHint}-source-only", synchronizers.Source, cancellationToken);
        var contractId = await harness.CreateAssetAsync(issuer, synchronizers.Source, AssetAmount, cancellationToken);

        var outcome = await harness.TryUnassignAsync(issuer, contractId, synchronizers, cancellationToken);

        var error = Assert.IsType<ExerciseOutcome<ContractStreamEvent<Asset>>.DamlError>(outcome);
        Assert.Equal(DamlErrorCategory.InvalidIndependentOfSystemState, error.Category);
        Assert.Equal("INVALID_ARGUMENT", error.ErrorId);
        Assert.Contains("stakeholders are not active on the target synchronizer", error.Message, StringComparison.Ordinal);
    }
}
