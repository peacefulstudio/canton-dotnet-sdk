// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public class TryHelpersOperationTests
{
    private static readonly Identifier Template = new("pkg", "Module", "Template");

    private static readonly ExerciseCommand Choice = new(
        Template,
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo Alice = new Party("alice");

    private sealed class FailingWriter : ILedgerWriter
    {
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.InfraError(
                    new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "participant unreachable"));

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.InfraError(
                    new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "participant unreachable"));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_stamps_the_template_and_choice_of_an_exercise_on_a_failed_outcome()
    {
        var outcome = await new FailingWriter().TrySubmitSingleAsync(Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>();
        outcome.Operation.Should().Be(new LedgerOperation.Command(new Identifier("pkg", "Module", "Template"), "DoIt"));
        outcome.Operation!.Label.Should().Be("Template.DoIt");
    }

    [Fact]
    public async Task TrySubmitSingleAsync_stamps_the_template_and_Create_for_a_create_command()
    {
        var outcome = await new FailingWriter().TrySubmitSingleAsync(
            CreateCommand.For(new SampleTemplate()), Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Operation.Should().Be(new LedgerOperation.Command(new Identifier("pkg", "Module", "Template"), "Create"));
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_stamps_the_template_and_choice_of_the_exercise()
    {
        var outcome = await new FailingWriter().TryCreateOneByExerciseAsync<SampleTemplate>(
            Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Operation.Should().Be(new LedgerOperation.Command(new Identifier("pkg", "Module", "Template"), "DoIt"));
    }

    [Fact]
    public async Task TryCreateAsync_with_a_configure_hook_stamps_the_template_and_Create()
    {
        var outcome = await SingleCommandExtensions.TryCreateAsync(
            new FailingWriter(), new SampleTemplate(), Alice, configure: submission => submission,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Operation.Should().Be(new LedgerOperation.Command(new Identifier("pkg", "Module", "Template"), "Create"));
    }

    [Fact]
    public async Task TryCreateAsync_without_a_configure_hook_stamps_the_template_and_Create()
    {
        var outcome = await SingleCommandExtensions.TryCreateAsync(
            new FailingWriter(), new SampleTemplate(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Operation.Should().Be(new LedgerOperation.Command(new Identifier("pkg", "Module", "Template"), "Create"));
    }
}
