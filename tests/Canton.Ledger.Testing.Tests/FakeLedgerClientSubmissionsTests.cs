// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientSubmissionsTests
{
    private static readonly Party Alice = new("alice");
    private static readonly Party Bob = new("bob");
    private static readonly Party Carol = new("carol");
    private static readonly ContractId<DemoAsset> AssetId = new("00cid");

    private static SubmitterInfo AliceReadingAsBobAndCarol { get; } =
        new(Alice, new HashSet<Party> { Bob, Carol }.ToFrozenSet());

    private static ExerciseCommand Transfer() =>
        new(DemoAsset.TemplateId, AssetId, new ChoiceName("Transfer"), DamlRecord.Create());

    private static CommandsSubmission SingleCommandSubmission(FakeLedgerClient client) =>
        client.Submissions.Should().ContainSingle().Which
            .Should().BeOfType<RecordedSubmission.Commands>().Which.Submission;

    private static ReassignmentSubmission Reassignment() =>
        ReassignmentSubmission.Of(new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"), Alice);

    [Fact]
    public void Submissions_is_empty_before_any_write()
    {
        var client = FakeLedgerClient.Create().Build();

        client.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateAsync_records_the_create_command_the_submitter_and_the_workflow_id()
    {
        var payload = new DemoAsset(Alice, Alice, "GOLD", 1m);
        var client = FakeLedgerClient.Create()
            .WithCreateResult(LedgerOutcomes.One(AssetId))
            .Build();

        await client.TryCreateAsync(
            payload,
            AliceReadingAsBobAndCarol,
            workflowId: "wf-create",
            cancellationToken: TestContext.Current.CancellationToken);

        var recorded = SingleCommandSubmission(client);
        recorded.Commands.Should().Equal(CreateCommand.For(payload));
        recorded.ActAs.Should().Equal(Alice);
        recorded.ReadAs.Should().BeEquivalentTo([Bob, Carol]);
        recorded.WorkflowId.Should().Be(new WorkflowId("wf-create"));
    }

    [Fact]
    public async Task TryCreateAsync_records_the_default_workflow_id_a_real_client_would_send()
    {
        var client = FakeLedgerClient.Create().WithCreateResult(LedgerOutcomes.One(AssetId)).Build();

        await client.TryCreateAsync(
            new DemoAsset(Alice, Alice, "GOLD", 1m), Alice, cancellationToken: TestContext.Current.CancellationToken);

        SingleCommandSubmission(client).WorkflowId.Should().Be(new WorkflowId("create-demoasset"));
    }

    [Fact]
    public async Task TryExerciseAsync_records_the_exercise_command_the_submitter_and_the_command_id()
    {
        var client = FakeLedgerClient.Create()
            .WithExerciseResult(LedgerOutcomes.One(DamlUnit.Instance))
            .Build();

        await client.TryExerciseAsync<DamlUnit>(
            Transfer(),
            Alice,
            commandId: new CommandId("cmd-7"),
            cancellationToken: TestContext.Current.CancellationToken);

        var recorded = SingleCommandSubmission(client);
        recorded.Commands.Should().Equal(Transfer());
        recorded.ActAs.Should().Equal(Alice);
        recorded.ReadAs.Should().BeNull();
        recorded.WorkflowId.Should().Be(new WorkflowId("exercise-transfer"));
        recorded.CommandId.Should().Be(new CommandId("cmd-7"));
    }

    [Fact]
    public async Task Submissions_keeps_writes_of_every_kind_in_the_order_they_were_made()
    {
        var client = FakeLedgerClient.Create()
            .WithCreateResult(LedgerOutcomes.One(AssetId))
            .WithExerciseResult(LedgerOutcomes.One(DamlUnit.Instance))
            .Build();

        await client.TryCreateAsync(new DemoAsset(Alice, Alice, "GOLD", 1m), Alice, cancellationToken: TestContext.Current.CancellationToken);
        await client.SubmitReassignmentAsync(Reassignment(), cancellationToken: TestContext.Current.CancellationToken);
        await client.TryExerciseAsync<DamlUnit>(Transfer(), Bob, cancellationToken: TestContext.Current.CancellationToken);

        client.Submissions.Select(recorded => recorded.GetType()).Should().Equal(
            typeof(RecordedSubmission.Commands), typeof(RecordedSubmission.Reassignment), typeof(RecordedSubmission.Commands));
        client.Submissions.OfType<RecordedSubmission.Commands>().Select(recorded => recorded.Submission.Commands[0].CommandType)
            .Should().Equal("Create", "Exercise");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_records_the_submission_with_the_submitter_applied()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(
                LedgerResults.Transaction("update-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))))
            .Build();
        var preSetActAs = CommandsSubmission.Single(Transfer()).WithActAs(Carol);

        await client.TrySubmitAndWaitForTransactionAsync(
            preSetActAs, AliceReadingAsBobAndCarol, cancellationToken: TestContext.Current.CancellationToken);

        var recorded = SingleCommandSubmission(client);
        recorded.ActAs.Should().Equal(Alice);
        recorded.ReadAs.Should().BeEquivalentTo([Bob, Carol]);
        recorded.Commands.Should().Equal(Transfer());
    }

    [Fact]
    public async Task SubmitAsync_and_TrySubmitAndWaitForTransactionTreeAsync_record_their_submission()
    {
        var client = FakeLedgerClient.Create()
            .WithTransactionTree(LedgerOutcomes.None<TransactionTree>())
            .Build();
        var submission = CommandsSubmission.Single(Transfer()).WithActAs(Alice);

        await client.SubmitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);
        await client.TrySubmitAndWaitForTransactionTreeAsync(submission, Bob, cancellationToken: TestContext.Current.CancellationToken);

        var recorded = client.Submissions.OfType<RecordedSubmission.Commands>().Select(entry => entry.Submission).ToArray();
        recorded.Select(entry => entry.Commands[0]).Should().Equal(Transfer(), Transfer());
        recorded.Select(entry => entry.ActAs![0]).Should().Equal(Alice, Bob);
    }

    [Fact]
    public async Task SubmitReassignmentAsync_records_the_reassignment_submission()
    {
        var client = FakeLedgerClient.Create().Build();

        await client.SubmitReassignmentAsync(
            Reassignment().WithCommandId(new CommandId("cmd-r")), cancellationToken: TestContext.Current.CancellationToken);

        var recorded = client.Submissions.Should().ContainSingle().Which
            .Should().BeOfType<RecordedSubmission.Reassignment>().Which.Submission;
        recorded.Command.Should().Be(new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"));
        recorded.Submitter.Should().Be(Alice);
        recorded.CommandId.Should().Be(new CommandId("cmd-r"));
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_records_the_reassignment_submission()
    {
        var client = FakeLedgerClient.Create()
            .WithReassignmentResult(LedgerOutcomes.None<ContractStreamEvent<DemoAsset>>())
            .Build();

        await client.TrySubmitAndWaitForReassignmentAsync<DemoAsset>(
            Reassignment(), cancellationToken: TestContext.Current.CancellationToken);

        client.Submissions.Should().ContainSingle().Which
            .Should().BeOfType<RecordedSubmission.Reassignment>().Which.Submission.Submitter.Should().Be(Alice);
    }

    private static SignedSubmission Signed(string submissionId) => new(
        new PreparedSubmission(new byte[] { 1, 2 }, new byte[] { 3, 4 }, HashingSchemeVersion.V2, null, null),
        [new PartySignatures(Alice, [new LedgerSignature(SignatureFormat.Der, new byte[] { 9 }, "fp", SigningAlgorithm.EcDsaSha256)])],
        submissionId);

    [Fact]
    public async Task The_three_interactive_executes_record_the_signed_submission_they_were_handed()
    {
        var client = FakeLedgerClient.Create()
            .WithExecutedSubmission(new ExecutedSubmission("update-1", LedgerOffset.At(7)))
            .WithExecutedTransaction(LedgerResults.Transaction("update-2", LedgerOffset.At(8), [], [], new CommandId("cmd-2")))
            .Build();

        await client.ExecuteSubmissionAsync(Signed("sub-1"), cancellationToken: TestContext.Current.CancellationToken);
        await client.ExecuteSubmissionAndWaitAsync(Signed("sub-2"), cancellationToken: TestContext.Current.CancellationToken);
        await client.ExecuteSubmissionAndWaitForTransactionAsync(Signed("sub-3"), Alice, cancellationToken: TestContext.Current.CancellationToken);

        client.Submissions.Should().AllBeOfType<RecordedSubmission.Execution>();
        client.Submissions.Cast<RecordedSubmission.Execution>().Select(recorded => recorded.Submission.SubmissionId)
            .Should().Equal("sub-1", "sub-2", "sub-3");
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_does_not_record_a_submission_it_rejects()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.ExecuteSubmissionAsync(Signed(" "));

        await act.Should().ThrowAsync<ArgumentException>();
        client.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_is_not_a_write_and_is_not_recorded()
    {
        var client = FakeLedgerClient.Create().WithTrafficCostEstimate(null).Build();

        await client.EstimateTrafficCostAsync(
            CommandsSubmission.Single(Transfer()).WithActAs(Alice), cancellationToken: TestContext.Current.CancellationToken);

        client.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAndWaitAsync_returns_the_staged_result_and_records_the_submission()
    {
        var staged = LedgerResults.SubmitAndWait(new CommandId("cmd-9"), "update-9", LedgerOffset.At(9));
        var client = FakeLedgerClient.Create().WithSubmitAndWaitResult(staged).Build();

        var result = await client.SubmitAndWaitAsync(
            CommandsSubmission.Single(Transfer()).WithCommandId(new CommandId("cmd-9")),
            Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(staged);
        var recorded = SingleCommandSubmission(client);
        recorded.ActAs.Should().Equal(Alice);
        recorded.CommandId.Should().Be(new CommandId("cmd-9"));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_the_command_id_it_minted_for_a_submission_without_one()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmitAndWaitResult(LedgerResults.SubmitAndWait(new CommandId("staged"), "update-9", LedgerOffset.At(9)))
            .Build();

        var result = await client.SubmitAndWaitAsync(
            CommandsSubmission.Single(Transfer()).WithActAs(Alice),
            cancellationToken: TestContext.Current.CancellationToken);

        result.CommandId.Should().NotBe(new CommandId("staged"));
        SingleCommandSubmission(client).CommandId.Should().Be(result.CommandId);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_advances_the_ledger_end_by_one_offset_when_it_succeeds()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(10))
            .WithSubmitAndWaitResult(LedgerResults.SubmitAndWait(new CommandId("cmd-1"), "update-1", LedgerOffset.At(11)))
            .Build();

        await client.SubmitAndWaitAsync(
            CommandsSubmission.Single(Transfer()).WithActAs(Alice), cancellationToken: TestContext.Current.CancellationToken);

        (await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Should().Be(LedgerOffset.At(11));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_without_a_staged_result_names_the_builder_call()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.SubmitAndWaitAsync(CommandsSubmission.Single(Transfer()).WithActAs(Alice));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithSubmitAndWaitResult");
    }
}
