// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

public abstract class LedgerRetriedWriteCommitStateParityTests
{
    protected abstract Task<CapabilityLane<RetriedWriteLane>> OpenRetryingAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_resolves_a_create_whose_first_response_was_lost_to_its_committed_transaction()
    {
        var outcome = await LoseFirstResponseAsync(
            static lane => lane.TryOperation,
            static (lane, submission, ct) => lane.Client.TrySubmitAndWaitForTransactionAsync(submission, lane.Owner, cancellationToken: ct),
            consuming: false);

        var created = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result;
        created.UpdateId.Should().NotBeEmpty();
        created.CreatedContracts.Should().ContainSingle();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_reports_a_consuming_exercise_whose_first_response_was_lost_as_an_infrastructure_error()
    {
        var outcome = await LoseFirstResponseAsync(
            static lane => lane.TryOperation,
            static (lane, submission, ct) => lane.Client.TrySubmitAndWaitForTransactionAsync(submission, lane.Owner, cancellationToken: ct),
            consuming: true);

        var infraError = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>().Subject;
        infraError.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        infraError.Message.Should().Contain("Contract could not be found");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_a_create_whose_first_response_was_lost_as_committed_by_its_duplicate_command()
    {
        var thrown = await LoseFirstResponseAndCaptureFailureAsync(
            static lane => lane.ThrowingOperation,
            static (lane, submission, ct) => lane.Client.SubmitAndWaitAsync(submission, lane.Owner, cancellationToken: ct),
            consuming: false);

        thrown.ErrorId.Should().Be("DUPLICATE_COMMAND");
        thrown.CommitState.Should().Be(CommitState.Committed);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_a_consuming_exercise_whose_first_response_was_lost_as_unknown()
    {
        var thrown = await LoseFirstResponseAndCaptureFailureAsync(
            static lane => lane.ThrowingOperation,
            static (lane, submission, ct) => lane.Client.SubmitAndWaitAsync(submission, lane.Owner, cancellationToken: ct),
            consuming: true);

        thrown.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        thrown.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        thrown.CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public async Task SubmitAsync_never_reports_a_create_whose_first_response_was_lost_as_not_committed()
    {
        var thrown = await LoseFirstResponseAndCaptureFailureAsync(
            static lane => lane.FireAndForgetOperation,
            static (lane, submission, ct) => lane.Client.SubmitAsync(submission, cancellationToken: ct),
            consuming: false);

        thrown.CommitState.Should().BeOneOf(CommitState.Unknown, CommitState.Committed);
    }

    [Fact]
    public async Task SubmitAsync_never_reports_a_consuming_exercise_whose_first_response_was_lost_as_not_committed()
    {
        var thrown = await LoseFirstResponseAndCaptureFailureAsync(
            static lane => lane.FireAndForgetOperation,
            static (lane, submission, ct) => lane.Client.SubmitAsync(submission, cancellationToken: ct),
            consuming: true);

        thrown.CommitState.Should().BeOneOf(CommitState.Unknown, CommitState.Committed);
    }

    private async Task<LedgerOperationException> LoseFirstResponseAndCaptureFailureAsync<T>(
        Func<RetriedWriteLane, string> operation,
        Func<RetriedWriteLane, CommandsSubmission, CancellationToken, Task<T>> submit,
        bool consuming)
    {
        try
        {
            var returned = await LoseFirstResponseAsync(operation, submit, consuming);
            throw new InvalidOperationException($"Expected the retried write to fail, but it returned {returned}.");
        }
        catch (LedgerOperationException failure)
        {
            return failure;
        }
    }

    private async Task<T> LoseFirstResponseAsync<T>(
        Func<RetriedWriteLane, string> operation,
        Func<RetriedWriteLane, CommandsSubmission, CancellationToken, Task<T>> submit,
        bool consuming)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var capability = await OpenRetryingAsync(cancellationToken);
        var lane = capability.Capability;
        var command = consuming ? await ArchiveACreatedMarkerAsync(lane, cancellationToken) : CreateCommand.For(new Marker(lane.Owner));
        var submission = CommandsSubmission
            .Single(command)
            .WithActAs(lane.Owner)
            .WithCommandId(new CommandId(Guid.NewGuid().ToString()));

        lane.Tap.Clear();
        lane.Tap.LoseNextResponseTo(operation(lane));
        return await submit(lane, submission, cancellationToken);
    }

    private static async Task<ICommand> ArchiveACreatedMarkerAsync(RetriedWriteLane lane, CancellationToken cancellationToken)
    {
        var created = await lane.Client.TryCreateAsync(new Marker(lane.Owner), lane.Owner, cancellationToken: cancellationToken);
        var markerCid = ((ExerciseOutcome<ContractId<Marker>>.One)created).Result;
        return ExerciseCommand.For(markerCid, Marker.ChoiceArchive.Name, DamlRecord.Create());
    }
}

public sealed record RetriedWriteLane(
    ICantonLedgerClient Client,
    Party Owner,
    LostResponseTap Tap,
    string TryOperation,
    string ThrowingOperation,
    string FireAndForgetOperation);
