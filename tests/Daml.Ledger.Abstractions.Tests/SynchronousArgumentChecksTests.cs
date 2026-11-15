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

/// <summary>
/// The async ledger helpers promise that an invalid argument throws at the call, before the
/// returned task is awaited. Every test invokes the helper without awaiting it, so a helper
/// that reports the fault through a faulted task fails here where a <c>ThrowAsync</c> assertion
/// would pass either way.
/// </summary>
public class SynchronousArgumentChecksTests
{
    private static readonly ExerciseCommand Choice = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo Alice = new Party("alice");

    private static readonly ILedgerWriter UnreachableWriter = new UnreachableLedgerWriter();

    private static readonly ILedgerWriter NullWriter = null!;

    [Fact]
    public void ExerciseAsync_with_a_result_throws_synchronously_for_a_null_writer()
    {
        Action act = () => _ = NullWriter.ExerciseAsync<int>(Choice, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void ExerciseAsync_with_a_result_throws_synchronously_for_a_null_command()
    {
        Action act = () => _ = UnreachableWriter.ExerciseAsync<int>(null!, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("command");
    }

    [Fact]
    public void ExerciseAsync_with_a_result_throws_synchronously_for_a_default_command_id()
    {
        Action act = () => _ = UnreachableWriter.ExerciseAsync<int>(Choice, Alice, commandId: default(CommandId));

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("commandId");
    }

    [Fact]
    public void ExerciseAsync_without_a_result_throws_synchronously_for_a_null_writer()
    {
        Action act = () => _ = NullWriter.ExerciseAsync(Choice, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void ExerciseAsync_without_a_result_throws_synchronously_for_a_default_command_id()
    {
        Action act = () => _ = UnreachableWriter.ExerciseAsync(Choice, Alice, commandId: default(CommandId));

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("commandId");
    }

    [Fact]
    public void TryCreateOneByExerciseAsync_throws_synchronously_for_a_null_writer()
    {
        Action act = () => _ = NullWriter.TryCreateOneByExerciseAsync<SampleTemplate>(Choice, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void TryCreateOneByExerciseAsync_throws_synchronously_for_a_default_command_id()
    {
        Action act = () => _ = UnreachableWriter.TryCreateOneByExerciseAsync<SampleTemplate>(
            Choice, Alice, commandId: default(CommandId));

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("commandId");
    }

    [Fact]
    public void TryCreateManyByExerciseAsync_throws_synchronously_for_a_null_writer()
    {
        Action act = () => _ = NullWriter.TryCreateManyByExerciseAsync<SampleTemplate>(Choice, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void TryCreateManyByExerciseAsync_throws_synchronously_for_a_default_command_id()
    {
        Action act = () => _ = UnreachableWriter.TryCreateManyByExerciseAsync<SampleTemplate>(
            Choice, Alice, commandId: default(CommandId));

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("commandId");
    }

    [Fact]
    public void CreateOneByExerciseAsync_throws_synchronously_for_a_null_writer()
    {
        Action act = () => _ = NullWriter.CreateOneByExerciseAsync<SampleTemplate>(Choice, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void CreateOneByExerciseAsync_throws_synchronously_for_a_default_command_id()
    {
        Action act = () => _ = UnreachableWriter.CreateOneByExerciseAsync<SampleTemplate>(
            Choice, Alice, commandId: default(CommandId));

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("commandId");
    }

    [Fact]
    public void CreateManyByExerciseAsync_throws_synchronously_for_a_null_writer()
    {
        Action act = () => _ = NullWriter.CreateManyByExerciseAsync<SampleTemplate>(Choice, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void CreateManyByExerciseAsync_throws_synchronously_for_a_default_command_id()
    {
        Action act = () => _ = UnreachableWriter.CreateManyByExerciseAsync<SampleTemplate>(
            Choice, Alice, commandId: default(CommandId));

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("commandId");
    }

    [Fact]
    public void TryCreateAsync_throws_synchronously_for_a_null_writer()
    {
        Action act = () => _ = SingleCommandExtensions.TryCreateAsync(NullWriter, new SampleTemplate(), Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void TryCreateAsync_without_a_configure_hook_throws_synchronously_for_a_default_command_id()
    {
        Action act = () => _ = SingleCommandExtensions.TryCreateAsync(
            UnreachableWriter, new SampleTemplate(), Alice, commandId: default(CommandId));

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("commandId");
    }

    [Fact]
    public void TryCreateAsync_with_a_configure_hook_throws_synchronously_for_a_default_command_id()
    {
        Action act = () => _ = SingleCommandExtensions.TryCreateAsync(
            UnreachableWriter, new SampleTemplate(), Alice, commandId: default(CommandId), configure: submission => submission);

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("commandId");
    }

    private sealed class UnreachableLedgerWriter : ILedgerWriter
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
            throw new NotSupportedException();

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            throw new NotSupportedException();
    }
}
