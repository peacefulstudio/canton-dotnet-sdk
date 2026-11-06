// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public class CommandBatchTests
{
    private const string RichTypesPackageName = "richtypes-h07285b495f9e";

    private static readonly Party Alice = new("alice");
    private static readonly ContractId<Marker> MintingMarker = new("00marker-mint");
    private static readonly ContractId<Marker> ArchivedMarker = new("00marker-archive");
    private static readonly ContractId<IHolding> DescribedHolding = new("00holding");

    private sealed class ScriptedWriter(ExerciseOutcome<TransactionResult> outcome) : ILedgerWriter
    {
        public CommandsSubmission? Submitted { get; private set; }

        public SubmitterInfo? Submitter { get; private set; }

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
            CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Submitted = submission;
            Submitter = submitter;
            return Task.FromResult(outcome);
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            throw new NotSupportedException();
    }

    private sealed record MixedBatch(
        CommandBatch Batch,
        BatchHandle<ContractId<Asset>> FirstAsset,
        BatchHandle<ContractId<Asset>> Minted,
        BatchHandle<ContractId<Asset>> SecondAsset,
        BatchHandle<DamlUnit> Archived,
        BatchHandle<string> Described);

    private static MixedBatch BuildMixedBatch()
    {
        var batch = new CommandBatch();
        var firstAsset = batch.AddCreate(new Asset(Alice, 10m));
        var minted = batch.Add(Marker.ChoiceMint, MintingMarker, new Marker.Mint(5m));
        var secondAsset = batch.AddCreate(new Asset(Alice, 20m));
        var archived = batch.Add(Marker.ChoiceArchive, ArchivedMarker, DamlUnit.Instance);
        var described = batch.Add(IHolding.ChoiceDescribe, DescribedHolding, new Describe("holding of"));
        return new MixedBatch(batch, firstAsset, minted, secondAsset, archived, described);
    }

    private static CreatedContract CreatedAsset(string contractId, int? nodeId) =>
        new(
            EventId: $"event-{contractId}",
            ContractId: contractId,
            TemplateId: Asset.TemplateId,
            Payload: DamlRecord.Create(),
            WitnessParties: [Alice],
            Signatories: [Alice],
            Observers: [])
        {
            PackageName = RichTypesPackageName,
            NodeId = nodeId,
        };

    private static ExercisedEvent Exercised(
        string contractId,
        Identifier templateId,
        string choice,
        DamlValue result,
        int? nodeId,
        int? lastDescendantNodeId,
        Identifier? interfaceId = null) =>
        new(contractId, templateId, interfaceId, new ChoiceName(choice), DamlRecord.Create(), result, false, [Alice], [Alice])
        {
            PackageName = RichTypesPackageName,
            NodeId = nodeId,
            LastDescendantNodeId = lastDescendantNodeId,
        };

    private static TransactionResult MixedTransaction(DamlValue mintResult) =>
        new(
            "update-1",
            LedgerOffset.At(7),
            [CreatedAsset("00asset-first", 0), CreatedAsset("00asset-minted", 2), CreatedAsset("00asset-second", 3)],
            ["00marker-archive"],
            new CommandId("cmd-1"))
        {
            ExercisedEvents =
            [
                Exercised("00marker-mint", Marker.TemplateId, "Mint", mintResult, 1, 2),
                Exercised("00marker-archive", Marker.TemplateId, "Archive", DamlUnit.Instance, 4, 4),
                Exercised("00holding", Asset.TemplateId, "Describe", new DamlText("holding of 10"), 5, 5, IHolding.InterfaceId),
            ],
        };

    private static LedgerOperation.Batch MixedOperation => new(
    [
        new(new Identifier("3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "RichTypes", "Asset"), "Create"),
        new(new Identifier("3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "RichTypes", "Marker"), "Mint"),
        new(new Identifier("3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "RichTypes", "Asset"), "Create"),
        new(new Identifier("3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "RichTypes", "Marker"), "Archive"),
        new(new Identifier("3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "RichTypes", "Holding"), "Describe"),
    ]);

    [Fact]
    public void Operation_names_every_command_in_the_order_it_was_added()
    {
        var mixed = BuildMixedBatch();

        mixed.Batch.Operation.Should().Be(MixedOperation);
        mixed.Batch.Operation.Label.Should().Be("Asset.Create ×2, Marker.Mint, Marker.Archive, Holding.Describe");
        mixed.Batch.Count.Should().Be(5);
    }

    [Fact]
    public void Operation_refuses_an_empty_batch()
    {
        var act = () => new CommandBatch().Operation;

        act.Should().Throw<InvalidOperationException>().WithMessage("An empty batch has no operation*");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_submits_every_command_in_the_order_it_was_added_as_one_submission()
    {
        var mixed = BuildMixedBatch();
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(MixedTransaction(new DamlContractId("00asset-minted"))));

        await writer.TrySubmitBatchAsync(
            mixed.Batch, Alice, workflowId: "disconnect", commandId: new CommandId("cmd-1"),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.Submitted!.Commands.Should().Equal(
            CreateCommand.For(new Asset(Alice, 10m)),
            MintingMarker.MintCommand(new Marker.Mint(5m)),
            CreateCommand.For(new Asset(Alice, 20m)),
            new ExerciseCommand(Marker.TemplateId, ArchivedMarker, new ChoiceName("Archive"), DamlRecord.Create()) { PackageName = RichTypesPackageName },
            new ExerciseCommand(IHolding.InterfaceId, DescribedHolding, new ChoiceName("Describe"), new Describe("holding of").ToRecord()));
        writer.Submitted.WorkflowId.Should().Be(new WorkflowId("disconnect"));
        writer.Submitted.CommandId.Should().Be(new CommandId("cmd-1"));
        writer.Submitter.Should().Be((SubmitterInfo)Alice);
    }

    [Fact]
    public async Task TrySubmitBatchAsync_reads_back_each_command_result_through_its_handle()
    {
        var mixed = BuildMixedBatch();
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(MixedTransaction(new DamlContractId("00asset-minted"))));

        var outcome = await writer.TrySubmitBatchAsync(mixed.Batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var results = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.One>().Subject.Result;
        results.Get(mixed.FirstAsset).Should().Be(new ContractId<Asset>("00asset-first"));
        results.Get(mixed.Minted).Should().Be(new ContractId<Asset>("00asset-minted"));
        results.Get(mixed.SecondAsset).Should().Be(new ContractId<Asset>("00asset-second"));
        results.Get(mixed.Archived).Should().Be(DamlUnit.Instance);
        results.Get(mixed.Described).Should().Be("holding of 10");
        results.Transaction.UpdateId.Should().Be("update-1");
        outcome.Operation.Should().Be(MixedOperation);
    }

    [Fact]
    public async Task TrySubmitBatchAsync_matches_creates_in_order_when_the_writer_reports_no_node_ids()
    {
        var batch = new CommandBatch();
        var first = batch.AddCreate(new Asset(Alice, 10m));
        var second = batch.AddCreate(new Asset(Alice, 20m));
        var transaction = new TransactionResult(
            "update-2",
            LedgerOffset.At(8),
            [CreatedAsset("00asset-a", nodeId: null), CreatedAsset("00asset-b", nodeId: null)],
            [],
            new CommandId("cmd-2"));
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(transaction));

        var outcome = await writer.TrySubmitBatchAsync(batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var results = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.One>().Subject.Result;
        results.Get(first).Should().Be(new ContractId<Asset>("00asset-a"));
        results.Get(second).Should().Be(new ContractId<Asset>("00asset-b"));
    }

    [Fact]
    public async Task TrySubmitBatchAsync_stamps_the_batch_operation_and_command_id_on_a_rejected_submission()
    {
        var mixed = BuildMixedBatch();
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract could not be found with id 00marker-archive",
            new Dictionary<string, string>()));

        var outcome = await writer.TrySubmitBatchAsync(
            mixed.Batch, Alice, commandId: new CommandId("cmd-3"), cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.DamlError>().Subject;
        error.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        error.Operation.Should().Be(MixedOperation);
        error.CommandId.Should().Be("cmd-3");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_failure_names_every_command_in_the_batch_when_unwrapped()
    {
        var mixed = BuildMixedBatch();
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract could not be found with id 00marker-archive",
            new Dictionary<string, string>()));

        var outcome = await writer.TrySubmitBatchAsync(mixed.Batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var act = () => OutcomeUnwrapPolicy.OneOfOutcome(outcome);
        act.Should().Throw<LedgerOperationException>().Which.Message.Should().Be(
            "Asset.Create ×2, Marker.Mint, Marker.Archive, Holding.Describe: Daml error "
            + "[InvalidGivenCurrentSystemStateResourceMissing/CONTRACT_NOT_FOUND]: Contract could not be found with id 00marker-archive");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_reports_a_result_that_fails_to_decode_as_committed_undecodable_naming_its_command()
    {
        var mixed = BuildMixedBatch();
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(MixedTransaction(new DamlText("not a contract id"))));

        var outcome = await writer.TrySubmitBatchAsync(
            mixed.Batch, Alice, commandId: new CommandId("cmd-4"), cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("update-1");
        undecodable.Operation.Should().Be(new LedgerOperation.Command(
            new Identifier("3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "RichTypes", "Marker"), "Mint"));
        undecodable.CommandId.Should().Be("cmd-4");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_reports_a_command_with_no_root_event_as_committed_undecodable_naming_it()
    {
        var batch = new CommandBatch();
        batch.AddCreate(new Asset(Alice, 10m));
        batch.Add(Marker.ChoiceArchive, ArchivedMarker, DamlUnit.Instance);
        var transaction = new TransactionResult(
            "update-5", LedgerOffset.At(9), [CreatedAsset("00asset-a", 0)], [], new CommandId("cmd-5"));
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(transaction));

        var outcome = await writer.TrySubmitBatchAsync(batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("update-5");
        undecodable.Message.Should().Be(
            "Transaction update-5 committed, but carries no root 'Archive' exercise on contract '00marker-archive' for command 2 of the batch.");
        undecodable.Operation.Should().Be(new LedgerOperation.Command(
            new Identifier("3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "RichTypes", "Marker"), "Archive"));
    }

    [Fact]
    public async Task TrySubmitBatchAsync_refuses_to_guess_between_creates_when_no_node_ids_separate_nested_ones()
    {
        var batch = new CommandBatch();
        batch.AddCreate(new Asset(Alice, 10m));
        batch.Add(Marker.ChoiceMint, MintingMarker, new Marker.Mint(5m));
        batch.AddCreate(new Asset(Alice, 20m));
        var transaction = new TransactionResult(
            "update-7",
            LedgerOffset.At(11),
            [CreatedAsset("00asset-first", nodeId: null), CreatedAsset("00asset-minted", nodeId: null), CreatedAsset("00asset-second", nodeId: null)],
            [],
            new CommandId("cmd-7"))
        {
            ExercisedEvents = [Exercised("00marker-mint", Marker.TemplateId, "Mint", new DamlContractId("00asset-minted"), null, null)],
        };
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(transaction));

        var outcome = await writer.TrySubmitBatchAsync(batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.CommittedUndecodable>()
            .Which.Message.Should().StartWith("Cannot tell which 'Asset' create is command 1 of the batch");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_does_not_read_a_create_nested_under_an_exercise_as_a_root_create()
    {
        var batch = new CommandBatch();
        batch.Add(Marker.ChoiceMint, MintingMarker, new Marker.Mint(5m));
        batch.AddCreate(new Asset(Alice, 20m));
        var transaction = new TransactionResult(
            "update-6", LedgerOffset.At(10), [CreatedAsset("00asset-minted", 1)], [], new CommandId("cmd-6"))
        {
            ExercisedEvents = [Exercised("00marker-mint", Marker.TemplateId, "Mint", new DamlContractId("00asset-minted"), 0, 1)],
        };
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(transaction));

        var outcome = await writer.TrySubmitBatchAsync(batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.CommittedUndecodable>()
            .Which.Message.Should().Be(
                "Transaction update-6 committed, but carries no root create of 'Asset' for command 2 of the batch.");
    }

    [Fact]
    public async Task BatchResult_Get_refuses_a_handle_from_another_batch()
    {
        var mixed = BuildMixedBatch();
        var foreign = new CommandBatch().AddCreate(new Asset(Alice, 10m));
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(MixedTransaction(new DamlContractId("00asset-minted"))));
        var outcome = await writer.TrySubmitBatchAsync(mixed.Batch, Alice, cancellationToken: TestContext.Current.CancellationToken);
        var results = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.One>().Subject.Result;

        var act = () => results.Get(foreign);

        act.Should().Throw<ArgumentException>().WithParameterName("handle").WithMessage("The handle belongs to another batch.*");
    }

    [Fact]
    public async Task BatchResult_Get_refuses_a_handle_added_after_the_batch_was_submitted()
    {
        var mixed = BuildMixedBatch();
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(MixedTransaction(new DamlContractId("00asset-minted"))));
        var outcome = await writer.TrySubmitBatchAsync(mixed.Batch, Alice, cancellationToken: TestContext.Current.CancellationToken);
        var results = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.One>().Subject.Result;
        var late = mixed.Batch.AddCreate(new Asset(Alice, 30m));

        var act = () => results.Get(late);

        act.Should().Throw<ArgumentException>().WithParameterName("handle")
            .WithMessage("The handle was added after the batch was submitted.*");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_refuses_an_empty_batch()
    {
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.None());

        var act = () => writer.TrySubmitBatchAsync(new CommandBatch(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("batch");
        writer.Submitted.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitBatchAsync_applies_the_configure_hook_to_the_submission()
    {
        var mixed = BuildMixedBatch();
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.One(MixedTransaction(new DamlContractId("00asset-minted"))));

        await writer.TrySubmitBatchAsync(
            mixed.Batch, Alice, configure: s => s.WithSynchronizerId(new SynchronizerId("sync-1")),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.Submitted!.SynchronizerId.Should().Be(new SynchronizerId("sync-1"));
    }

    [Fact]
    public async Task TrySubmitBatchAsync_refuses_a_configure_hook_that_changes_the_commands()
    {
        var mixed = BuildMixedBatch();
        var writer = new ScriptedWriter(new ExerciseOutcome<TransactionResult>.None());

        var act = () => writer.TrySubmitBatchAsync(
            mixed.Batch, Alice, configure: s => s with { Commands = [.. s.Commands.Take(2)] },
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The configure hook changed the submission's commands.*");
        writer.Submitted.Should().BeNull();
    }
}
