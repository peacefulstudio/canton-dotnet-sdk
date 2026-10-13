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

public class ThrowingHelpersCommandIdTests
{
    private static readonly ExerciseCommand Choice = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo Alice = new Party("alice");

    private static readonly CommandId Explicit = new("cmd-explicit");

    private sealed class FailingWriter : ILedgerWriter
    {
        public CommandId? ReceivedCommandId { get; private set; }

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            ReceivedCommandId = commandId;
            return Task.FromResult<ExerciseOutcome<TResult>>(Failure<TResult>());
        }

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
            CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedCommandId = submission.CommandId;
            return Task.FromResult<ExerciseOutcome<TransactionResult>>(Failure<TransactionResult>());
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            throw new NotSupportedException();

        private static ExerciseOutcome<T> Failure<T>() => new ExerciseOutcome<T>.InfraError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "participant unreachable");
    }

    [Fact]
    public async Task ExerciseAsync_with_a_result_reports_the_caller_supplied_command_id_on_failure()
    {
        var act = () => new FailingWriter().ExerciseAsync<int>(Choice, Alice, commandId: Explicit);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().Be(new CommandId("cmd-explicit"));
    }

    [Fact]
    public async Task ExerciseAsync_with_a_result_reports_the_command_id_it_generated_on_failure()
    {
        var writer = new FailingWriter();

        var act = () => writer.ExerciseAsync<int>(Choice, Alice);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        thrown.CommandId.Should().NotBeNull().And.Be(writer.ReceivedCommandId);
        Guid.TryParse(thrown.CommandId!.Value.Value, out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExerciseAsync_without_a_result_reports_the_caller_supplied_command_id_on_failure()
    {
        var act = () => new FailingWriter().ExerciseAsync(Choice, Alice, commandId: Explicit);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().Be(new CommandId("cmd-explicit"));
    }

    [Fact]
    public async Task ExerciseAsync_without_a_result_reports_the_command_id_it_generated_on_failure()
    {
        var writer = new FailingWriter();

        var act = () => writer.ExerciseAsync(Choice, Alice);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().NotBeNull().And.Be(writer.ReceivedCommandId);
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_reports_the_command_id_it_generated_on_failure()
    {
        var writer = new FailingWriter();

        var act = () => writer.CreateOneByExerciseAsync<SampleTemplate>(Choice, Alice);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().NotBeNull().And.Be(writer.ReceivedCommandId);
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_reports_the_caller_supplied_command_id_on_failure()
    {
        var act = () => new FailingWriter().CreateManyByExerciseAsync<SampleTemplate>(Choice, Alice, commandId: Explicit);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().Be(new CommandId("cmd-explicit"));
    }

    [Fact]
    public async Task ExerciseAsync_without_a_result_reports_the_command_id_a_configure_hook_set_on_failure()
    {
        var writer = new FailingWriter();

        var act = () => writer.ExerciseAsync(
            Choice, Alice, commandId: Explicit, configure: submission => submission.WithCommandId(new CommandId("cmd-configured")));

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().Be(new CommandId("cmd-configured"));
        writer.ReceivedCommandId.Should().Be(new CommandId("cmd-configured"));
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_reports_the_command_id_a_configure_hook_set_on_failure()
    {
        var act = () => new FailingWriter().CreateOneByExerciseAsync<SampleTemplate>(
            Choice, Alice, configure: submission => submission.WithCommandId(new CommandId("cmd-configured")));

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().Be(new CommandId("cmd-configured"));
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_reports_the_command_id_a_configure_hook_set_on_failure()
    {
        var act = () => new FailingWriter().CreateManyByExerciseAsync<SampleTemplate>(
            Choice, Alice, commandId: Explicit, configure: submission => submission.WithCommandId(new CommandId("cmd-configured")));

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().Be(new CommandId("cmd-configured"));
    }

    [Fact]
    public void An_exception_raised_outside_a_command_carries_no_command_id()
    {
        new LedgerOperationException("no command").CommandId.Should().BeNull();
    }
}
