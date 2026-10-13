// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public class CreateByExerciseInterfaceViewTests
{
    private static readonly ExerciseCommand Choice = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo Alice = new Party("alice");

    private static CreatedContract Created(string contractId, params Identifier[] interfaceIds) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: SampleTemplate.TemplateId,
            Payload: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: [])
        {
            PackageName = "pkg-name",
            InterfaceIds = [.. interfaceIds],
        };

    private sealed class ConformingWriter : ILedgerWriter
    {
        public CommandsSubmission? LastSubmission { get; private set; }

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
            LastSubmission = submission;
            var result = new TransactionResult(
                "update-id",
                LedgerOffset.Begin,
                [Created("cid-created", SampleInterface.InterfaceId)],
                [],
                new CommandId("cmd-id")).WithRequestedInterfaces(submission.InterfaceViewRequests);
            return Task.FromResult<ExerciseOutcome<TransactionResult>>(new ExerciseOutcome<TransactionResult>.One(result));
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_for_an_interface_requests_its_view_and_returns_the_contract()
    {
        var writer = new ConformingWriter();

        var id = await writer.CreateOneByExerciseAsync<SampleInterface>(
            Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        id.Value.Should().Be("cid-created");
        writer.LastSubmission!.InterfaceViewRequests.Should().Equal(
            new DamlTypeDescriptor(new Identifier("iface-pkg", "Module", "ISample"), DamlTypeKind.Interface, "iface-name"));
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_for_an_interface_requests_its_view_and_returns_the_contracts()
    {
        var writer = new ConformingWriter();

        var ids = await writer.CreateManyByExerciseAsync<SampleInterface>(
            Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        ids.Select(id => id.Value).Should().Equal("cid-created");
        writer.LastSubmission!.InterfaceViewRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_for_an_interface_requests_its_view()
    {
        var writer = new ConformingWriter();

        var outcome = await writer.TryCreateOneByExerciseAsync<SampleInterface>(
            Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleInterface>>.One>()
            .Which.Result.Value.Should().Be("cid-created");
        writer.LastSubmission!.InterfaceViewRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_for_an_interface_requests_its_view()
    {
        var writer = new ConformingWriter();

        await writer.TryCreateManyByExerciseAsync<SampleInterface>(
            Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.InterfaceViewRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_for_a_template_requests_no_interface_view()
    {
        var writer = new ConformingWriter();

        await writer.TryCreateOneByExerciseAsync<SampleTemplate>(
            Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.InterfaceViewRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_for_an_interface_keeps_what_the_configure_hook_adds()
    {
        var writer = new ConformingWriter();

        await writer.CreateOneByExerciseAsync<SampleInterface>(
            Choice, Alice, configure: submission => submission.WithWorkflowId(new WorkflowId("wf-hook")),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.WorkflowId.Should().Be(new WorkflowId("wf-hook"));
        writer.LastSubmission.InterfaceViewRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_for_an_interface_reports_a_configure_hook_returning_null_as_misuse()
    {
        var writer = new ConformingWriter();

        var act = () => writer.CreateOneByExerciseAsync<SampleInterface>(
            Choice, Alice, configure: _ => null!, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The configure hook returned null*");
    }
}
