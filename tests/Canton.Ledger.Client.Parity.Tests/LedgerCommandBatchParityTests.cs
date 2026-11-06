// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="CommandBatchExtensions.TrySubmitBatchAsync"/>, run against the
/// in-memory Fake, REST and gRPC through one shared set of test bodies. REST and gRPC allocate a real party
/// and upload <c>richtypes.dar</c> against a live LocalNet; the Fake stages, for each
/// <see cref="BatchScenario"/>, the answers that ledger gives.
/// </summary>
public abstract class LedgerCommandBatchParityTests
{
    /// <summary>What the test body submits, so the Fake lane can stage the ledger's answers to it.</summary>
    protected enum BatchScenario
    {
        /// <summary>A Marker create, then a batch of two Asset creates around a Mint on that Marker.</summary>
        Commits,

        /// <summary>A Marker create and archive, then a batch whose second command archives that Marker again.</summary>
        RejectedPartway,
    }

    /// <summary>Opens a lane over this provider's <see cref="ILedgerWriter"/> for one test.</summary>
    protected abstract Task<CapabilityLane<(ILedgerWriter Writer, Party Owner)>> OpenWriterAsync(
        BatchScenario scenario,
        CancellationToken cancellationToken);

    [Fact]
    public async Task TrySubmitBatchAsync_commits_a_mixed_batch_and_reads_each_result_in_submission_order()
    {
        await using var lane = await OpenWriterAsync(BatchScenario.Commits, TestContext.Current.CancellationToken);
        var (writer, owner) = lane.Capability;
        var marker = await CreateMarkerAsync(writer, owner);
        var batch = new CommandBatch();
        var first = batch.AddCreate(new Asset(owner, 10m));
        var minted = batch.Add(Marker.ChoiceMint, marker, new Marker.Mint(5m));
        var second = batch.AddCreate(new Asset(owner, 20m));

        var outcome = await writer.TrySubmitBatchAsync(batch, owner, cancellationToken: TestContext.Current.CancellationToken);

        var results = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.One>().Subject.Result;
        AmountOf(results, results.Get(first)).Should().Be(10m);
        AmountOf(results, results.Get(minted)).Should().Be(5m);
        AmountOf(results, results.Get(second)).Should().Be(20m);
        outcome.Operation!.Label.Should().Be("Asset.Create ×2, Marker.Mint");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_rejects_the_whole_batch_when_a_command_partway_through_fails_and_names_every_command()
    {
        await using var lane = await OpenWriterAsync(BatchScenario.RejectedPartway, TestContext.Current.CancellationToken);
        var (writer, owner) = lane.Capability;
        var marker = await CreateMarkerAsync(writer, owner);
        var archive = await writer.TryExerciseAsync<DamlUnit>(
            ExerciseCommand.For(marker, Marker.ChoiceArchive.Name, DamlRecord.Create()),
            owner,
            cancellationToken: TestContext.Current.CancellationToken);
        archive.Should().BeOfType<ExerciseOutcome<DamlUnit>.One>();
        var batch = new CommandBatch();
        batch.AddCreate(new Asset(owner, 10m));
        batch.Add(Marker.ChoiceArchive, marker, DamlUnit.Instance);
        batch.AddCreate(new Asset(owner, 20m));

        var outcome = await writer.TrySubmitBatchAsync(batch, owner, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.DamlError>();
        var act = () => outcome.OneOrThrow();
        act.Should().Throw<LedgerOperationException>().Which.Operation.Should().Be("Asset.Create ×2, Marker.Archive");
    }

    private static async Task<ContractId<Marker>> CreateMarkerAsync(ILedgerWriter writer, Party owner)
    {
        var created = await writer.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);
        return created.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>().Subject.Result;
    }

    private static decimal AmountOf(BatchResult results, ContractId<Asset> asset) =>
        Asset.FromRecord(results.Transaction.CreatedContracts.Single(created => created.ContractId == asset.Value).Payload).Amount;
}
