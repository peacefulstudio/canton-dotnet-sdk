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

public class TryHelpersCommandIdTests
{
    private static readonly ExerciseCommand Choice = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo Alice = new Party("alice");

    private sealed class UnreportingWriter : ILedgerWriter
    {
        public CommandId? ReceivedCommandId { get; private set; }

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
            ReceivedCommandId = submission.CommandId;
            return Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.InfraError(
                    new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "participant unreachable"));
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_reports_the_command_id_it_minted_on_the_outcome()
    {
        var writer = new UnreportingWriter();

        var outcome = await writer.TrySubmitSingleAsync(Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.CommandId.Should().NotBeNullOrEmpty().And.Be(writer.ReceivedCommandId!.Value.Value);
        Guid.TryParse(outcome.CommandId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_reports_the_caller_supplied_command_id_on_the_outcome()
    {
        var outcome = await new UnreportingWriter().TrySubmitSingleAsync(
            Choice, Alice, commandId: new CommandId("cmd-explicit"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.CommandId.Should().Be("cmd-explicit");
    }

    [Fact]
    public async Task TrySubmitSingleAsync_reports_the_command_id_a_configure_hook_set_on_the_outcome()
    {
        var writer = new UnreportingWriter();

        var outcome = await writer.TrySubmitSingleAsync(
            Choice, Alice, configure: submission => submission.WithCommandId(new CommandId("cmd-configured")),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.CommandId.Should().Be("cmd-configured");
        writer.ReceivedCommandId.Should().Be(new CommandId("cmd-configured"));
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_reports_the_command_id_it_minted_on_the_outcome()
    {
        var writer = new UnreportingWriter();

        var outcome = await writer.TryCreateOneByExerciseAsync<SampleTemplate>(
            Choice, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.CommandId.Should().NotBeNullOrEmpty().And.Be(writer.ReceivedCommandId!.Value.Value);
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_reports_the_caller_supplied_command_id_on_the_outcome()
    {
        var outcome = await new UnreportingWriter()
            .TryCreateManyByExerciseAsync<SampleTemplate>(
                Choice, Alice, commandId: new CommandId("cmd-explicit"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.CommandId.Should().Be("cmd-explicit");
    }

    [Fact]
    public async Task TryCreateAsync_with_a_configure_hook_reports_the_command_id_it_minted_on_the_outcome()
    {
        var writer = new UnreportingWriter();

        var outcome = await SingleCommandExtensions.TryCreateAsync(
            writer, new SampleTemplate(), Alice, configure: submission => submission,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.CommandId.Should().NotBeNullOrEmpty().And.Be(writer.ReceivedCommandId!.Value.Value);
    }
}
