// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientCommandBatchTests
{
    private static readonly Party Alice = new("alice");

    private static CreatedContract StagedAsset(string contractId) =>
        new(
            EventId: $"event-{contractId}",
            ContractId: contractId,
            TemplateId: Asset.TemplateId,
            Payload: DamlRecord.Create(),
            WitnessParties: [Alice],
            Signatories: [Alice],
            Observers: [])
        {
            PackageName = Asset.PackageName,
        };

    private static ExercisedEvent StagedMint(string markerId, string mintedId) =>
        new(markerId, Marker.TemplateId, null, new ChoiceName("Mint"), DamlRecord.Create(), new DamlContractId(mintedId), false, [Alice], [Alice])
        {
            PackageName = Marker.PackageName,
        };

    [Fact]
    public async Task TrySubmitBatchAsync_records_one_submission_of_every_command_and_reads_each_result_from_the_staged_transaction()
    {
        var staged = LedgerResults.Transaction(
            "fake-update", LedgerOffset.At(4), [StagedAsset("00asset-first"), StagedAsset("00asset-second")], [], new CommandId("fake-cmd"))
            with
            {
                ExercisedEvents = [StagedMint("00marker", "00asset-minted")],
            };
        var client = FakeLedgerClient.Create().WithSubmissionOutcome(LedgerOutcomes.One(staged)).Build();
        var batch = new CommandBatch();
        var firstAsset = batch.AddCreate(new Asset(Alice, 10m));
        var minted = batch.Add(Marker.ChoiceMint, new ContractId<Marker>("00marker"), new Marker.Mint(5m));
        var secondAsset = batch.AddCreate(new Asset(Alice, 20m));

        var outcome = await client.TrySubmitBatchAsync(
            batch, Alice, commandId: new CommandId("cmd-batch"), cancellationToken: TestContext.Current.CancellationToken);

        var recorded = client.Submissions.Should().ContainSingle().Which
            .Should().BeOfType<RecordedSubmission.Commands>().Subject.Submission;
        recorded.Commands.Select(command => command.CommandType).Should().Equal("Create", "Exercise", "Create");
        recorded.CommandId.Should().Be(new CommandId("cmd-batch"));
        var results = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.One>().Subject.Result;
        results.Get(firstAsset).Should().Be(new ContractId<Asset>("00asset-first"));
        results.Get(minted).Should().Be(new ContractId<Asset>("00asset-minted"));
        results.Get(secondAsset).Should().Be(new ContractId<Asset>("00asset-second"));
        outcome.CommandId.Should().Be("cmd-batch");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_on_a_staged_rejection_names_every_command_when_unwrapped()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.DamlError<TransactionResult>(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND",
                "Contract could not be found with id 00marker-old",
                new Dictionary<string, string>()))
            .Build();
        var batch = new CommandBatch();
        batch.AddCreate(new Asset(Alice, 10m));
        batch.Add(Marker.ChoiceArchive, new ContractId<Marker>("00marker-old"), DamlUnit.Instance);

        var outcome = await client.TrySubmitBatchAsync(batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var act = () => outcome.OneOrThrow();
        act.Should().Throw<LedgerOperationException>()
            .Which.Message.Should().Be(
                "Asset.Create, Marker.Archive: Daml error [InvalidGivenCurrentSystemStateResourceMissing/CONTRACT_NOT_FOUND]: "
                + "Contract could not be found with id 00marker-old");
    }
}
