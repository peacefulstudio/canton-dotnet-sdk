// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class TryRelabelAsyncTests
{
    private static readonly ContractId<RichRecord> Target = new("rich-cid");
    private static readonly RichRecord.Relabel Argument = new("renamed");

    private static CreatedContract CreatedOf(string contractId, Identifier templateId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: templateId,
            Payload: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: []);

    private static TransactionResult TransactionCreating(string newRichRecordId) =>
        new(
            UpdateId: "upd-1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [CreatedOf(newRichRecordId, RichRecord.TemplateId)],
            ArchivedContractIds: [],
            CommandId: default);

    [Fact]
    public async Task TryRelabelAsync_projects_the_created_rich_record_on_success()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.One(TransactionCreating("new-rich-cid")));

        var outcome = await Target.TryRelabelAsync(client, Argument, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.One>();
        ((ExerciseOutcome<RelabelResult>.One)outcome).Result.RichRecord.Value.Should().Be("new-rich-cid");
    }

    [Fact]
    public async Task TryRelabelAsync_builds_an_exercise_command_for_the_relabel_choice_on_the_target()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.One(TransactionCreating("new-rich-cid")));

        await Target.TryRelabelAsync(client, Argument, new Party("alice"), workflowId: "wf-7",
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastSubmission.Should().NotBeNull();
        client.LastSubmission!.WorkflowId.Should().Be(new WorkflowId("wf-7"));
        client.LastSubmission.CommandId.Should().NotBeNull(
            "a generated exerciser must route through the shared submission helper, which assigns a command id");
        var command = client.LastSubmission.Commands.Should().ContainSingle().Which
            .Should().BeOfType<ExerciseCommand>().Subject;
        command.ContractId.Value.Should().Be("rich-cid");
        command.Choice.Should().Be(new ChoiceName("Relabel"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task TryRelabelAsync_omits_workflow_id_when_blank(string? workflowId)
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.One(TransactionCreating("new-rich-cid")));

        await Target.TryRelabelAsync(client, Argument, new Party("alice"), workflowId: workflowId,
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastSubmission!.WorkflowId.Should().BeNull();
    }

    [Fact]
    public async Task TryRelabelAsync_forwards_a_non_blank_workflow_id_verbatim()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.One(TransactionCreating("new-rich-cid")));

        await Target.TryRelabelAsync(client, Argument, new Party("alice"), workflowId: " padded ",
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastSubmission!.WorkflowId.Should().NotBeNull();
        client.LastSubmission!.WorkflowId!.Value.Value.Should().Be(" padded ");
    }

    [Fact]
    public async Task TryRelabelAsync_maps_a_daml_error_outcome_through_to_the_typed_result()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND",
                "gone",
                new Dictionary<string, string>()));

        var outcome = await Target.TryRelabelAsync(client, Argument, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.DamlError>().Subject;
        error.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        error.Message.Should().Be("gone");
    }

    [Fact]
    public async Task TryRelabelAsync_maps_an_infra_error_outcome_through_to_the_typed_result()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.InfraError(
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable"));

        var outcome = await Target.TryRelabelAsync(client, Argument, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.InfraError>().Subject;
        error.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        error.Message.Should().Be("unavailable");
    }

    [Fact]
    public async Task TryRelabelAsync_throws_on_null_contract_id()
    {
        using var client = new FakeLedgerClient();

        var act = async () => await ((ContractId<RichRecord>)null!).TryRelabelAsync(client, Argument, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TryRelabelAsync_throws_on_null_argument()
    {
        using var client = new FakeLedgerClient();

        var act = async () => await Target.TryRelabelAsync(client, null!, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
