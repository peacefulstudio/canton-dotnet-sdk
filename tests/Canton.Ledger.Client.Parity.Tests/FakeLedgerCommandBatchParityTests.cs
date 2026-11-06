// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerCommandBatchParityTests : LedgerCommandBatchParityTests
{
    private const string MarkerId = "00fake-marker";

    protected override Task<CapabilityLane<(ILedgerWriter Writer, Party Owner)>> OpenWriterAsync(
        BatchScenario scenario,
        CancellationToken cancellationToken)
    {
        var owner = new Party("fake::batch-owner");
        var builder = FakeLedgerClient.Create()
            .WithCreateResult<Marker>(new ExerciseOutcome<ContractId<Marker>>.One(new ContractId<Marker>(MarkerId)));
        var client = (scenario switch
        {
            BatchScenario.Commits => builder.WithSubmissionOutcome(LedgerOutcomes.One(CommittedBatch(owner))),
            BatchScenario.RejectedPartway => builder
                .WithExerciseResult<DamlUnit>(new ExerciseOutcome<DamlUnit>.One(DamlUnit.Instance))
                .WithSubmissionOutcome(LedgerOutcomes.DamlError<TransactionResult>(
                    DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                    "CONTRACT_NOT_FOUND",
                    $"Contract could not be found with id {MarkerId}",
                    new Dictionary<string, string>())),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        }).Build();

        return Task.FromResult(new CapabilityLane<(ILedgerWriter, Party)>((client, owner), client.DisposeAsync));
    }

    private static TransactionResult CommittedBatch(Party owner) =>
        LedgerResults.Transaction(
            "fake-batch-update",
            LedgerOffset.At(2),
            [
                StagedAsset(owner, "00fake-asset-first", 10m, nodeId: 0),
                StagedAsset(owner, "00fake-asset-minted", 5m, nodeId: 2),
                StagedAsset(owner, "00fake-asset-second", 20m, nodeId: 3),
            ],
            [],
            new CommandId("fake-batch-command"))
        with
        {
            ExercisedEvents =
            [
                new ExercisedEvent(
                    MarkerId,
                    Marker.TemplateId,
                    null,
                    Marker.ChoiceMint.Name,
                    new Marker.Mint(5m).ToRecord(),
                    new DamlContractId("00fake-asset-minted"),
                    false,
                    [owner],
                    [owner])
                {
                    PackageName = Marker.PackageName,
                    NodeId = 1,
                    LastDescendantNodeId = 2,
                },
            ],
        };

    private static CreatedContract StagedAsset(Party owner, string contractId, decimal amount, int nodeId) =>
        new(
            EventId: $"fake-event-{nodeId}",
            ContractId: contractId,
            TemplateId: Asset.TemplateId,
            Payload: new Asset(owner, amount).ToRecord(),
            WitnessParties: [owner],
            Signatories: [owner],
            Observers: [])
        {
            PackageName = Asset.PackageName,
            NodeId = nodeId,
        };
}
