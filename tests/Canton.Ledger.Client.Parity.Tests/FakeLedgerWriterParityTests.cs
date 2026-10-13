// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Codegen.Testing.Conformance.RichTypes;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerWriterParityTests : LedgerWriterParityTests
{
    protected override Task<CapabilityLane<(ILedgerWriter Writer, ICantonLedgerClient Client, Party Owner)>> OpenWriterAsync(
        CancellationToken cancellationToken)
    {
        var owner = new Party("fake::marker-owner");
        var client = FakeLedgerClient.Create()
            .WithCreateResult<Marker>(new ExerciseOutcome<ContractId<Marker>>.One(new ContractId<Marker>("00fake-marker")))
            .WithExerciseResult<DamlUnit>(new ExerciseOutcome<DamlUnit>.One(DamlUnit.Instance))
            .WithSubmissionOutcome(LedgerOutcomes.One(LedgerResults.Transaction(
                "fake-update", LedgerOffset.At(1), [StagedAsset(owner)], [], new CommandId("fake-command"))))
            .Build();

        return Task.FromResult(
            new CapabilityLane<(ILedgerWriter, ICantonLedgerClient, Party)>((client, client, owner), client.DisposeAsync));
    }

    private static CreatedContract StagedAsset(Party owner) =>
        new(
            EventId: "fake-asset-event",
            ContractId: "00fake-asset",
            TemplateId: Asset.TemplateId,
            Payload: DamlRecord.Create(),
            WitnessParties: [owner],
            Signatories: [owner],
            Observers: [])
        {
            PackageName = Asset.PackageName,
            InterfaceIds = [IHolding.InterfaceId],
        };
}
