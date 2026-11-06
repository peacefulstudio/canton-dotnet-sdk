// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientSubmissionOutcomeSequenceTests
{
    private static readonly Party Alice = new("alice");

    private static readonly ExerciseOutcome<TransactionResult> Stale = LedgerOutcomes.DamlError<TransactionResult>(
        DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, "CONTRACT_NOT_FOUND", "stale", new Dictionary<string, string>());

    private static readonly ExerciseOutcome<TransactionResult> Accepted = LedgerOutcomes.One(
        LedgerResults.Transaction("update-2", LedgerOffset.At(2), [], [], new CommandId("ignored")));

    private static readonly ExerciseOutcome<TransactionResult> Followed = LedgerOutcomes.One(
        LedgerResults.Transaction("update-3", LedgerOffset.At(3), [], [], new CommandId("ignored")));

    private static CommandsSubmission Submission() => CommandsSubmission
        .Single(ExerciseCommand.For(new ContractId<DemoAsset>("00cid"), (ChoiceName)"Archive", DamlRecord.Create()))
        .WithActAs(Alice);

    private static async Task<string?> UpdateIdOfNextSubmission(FakeLedgerClient client)
    {
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission(), cancellationToken: TestContext.Current.CancellationToken);
        return outcome is ExerciseOutcome<TransactionResult>.One one ? one.Result.UpdateId : null;
    }

    [Fact]
    public async Task WithSubmissionOutcome_given_a_sequence_answers_each_submission_with_the_next_outcome()
    {
        var client = FakeLedgerClient.Create().WithSubmissionOutcome(Stale, Accepted, Followed).Build();

        var first = await client.TrySubmitAndWaitForTransactionAsync(
            Submission(), cancellationToken: TestContext.Current.CancellationToken);
        var second = await UpdateIdOfNextSubmission(client);
        var third = await UpdateIdOfNextSubmission(client);

        first.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>();
        second.Should().Be("update-2");
        third.Should().Be("update-3");
    }

    [Fact]
    public async Task WithSubmissionOutcome_given_a_sequence_repeats_the_last_outcome_once_exhausted()
    {
        var client = FakeLedgerClient.Create().WithSubmissionOutcome(Stale, Accepted).Build();

        await client.TrySubmitAndWaitForTransactionAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        (await UpdateIdOfNextSubmission(client)).Should().Be("update-2");
        (await UpdateIdOfNextSubmission(client)).Should().Be("update-2");
        (await UpdateIdOfNextSubmission(client)).Should().Be("update-2");
    }

    [Fact]
    public async Task WithSubmissionOutcome_given_one_outcome_still_answers_every_submission_with_it()
    {
        var client = FakeLedgerClient.Create().WithSubmissionOutcome(Accepted).Build();

        (await UpdateIdOfNextSubmission(client)).Should().Be("update-2");
        (await UpdateIdOfNextSubmission(client)).Should().Be("update-2");
    }

    [Fact]
    public async Task WithSubmissionOutcome_sequence_advances_across_both_TrySubmitAndWaitForTransactionAsync_overloads()
    {
        var client = FakeLedgerClient.Create().WithSubmissionOutcome(Stale, Accepted).Build();

        var first = await client.TrySubmitAndWaitForTransactionAsync(
            Submission(), Alice, cancellationToken: TestContext.Current.CancellationToken);
        var second = await UpdateIdOfNextSubmission(client);

        first.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>();
        second.Should().Be("update-2");
    }

    [Fact]
    public async Task WithSubmissionOutcome_sequence_stamps_each_outcome_with_the_command_id_it_was_sent_under()
    {
        var client = FakeLedgerClient.Create().WithSubmissionOutcome(Stale, Accepted).Build();

        var first = await client.TrySubmitAndWaitForTransactionAsync(
            Submission().WithCommandId(new CommandId("cmd-a")), cancellationToken: TestContext.Current.CancellationToken);
        var second = await client.TrySubmitAndWaitForTransactionAsync(
            Submission().WithCommandId(new CommandId("cmd-b")), cancellationToken: TestContext.Current.CancellationToken);

        first.CommandId.Should().Be("cmd-a");
        second.CommandId.Should().Be("cmd-b");
    }

    [Fact]
    public async Task WithSubmissionOutcome_sequence_advances_the_ledger_end_only_for_committed_outcomes()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(10))
            .WithSubmissionOutcome(Stale, Accepted)
            .Build();

        await client.TrySubmitAndWaitForTransactionAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);
        var afterRejection = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        await client.TrySubmitAndWaitForTransactionAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);
        var afterAcceptance = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        afterRejection.Should().Be(LedgerOffset.At(10));
        afterAcceptance.Should().Be(LedgerOffset.At(11));
    }

    [Fact]
    public async Task Built_client_does_not_share_its_sequence_position_with_another_client_from_the_same_builder()
    {
        var builder = FakeLedgerClient.Create().WithSubmissionOutcome(Stale, Accepted);
        var first = builder.Build();
        var second = builder.Build();

        await first.TrySubmitAndWaitForTransactionAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        (await second.TrySubmitAndWaitForTransactionAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken))
            .Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>();
    }

    [Fact]
    public void WithSubmissionOutcome_rejects_an_empty_sequence()
    {
        var act = () => FakeLedgerClient.Create().WithSubmissionOutcome();

        act.Should().Throw<ArgumentException>().WithParameterName("outcomes");
    }

    [Fact]
    public void WithSubmissionOutcome_rejects_a_null_sequence()
    {
        var act = () => FakeLedgerClient.Create().WithSubmissionOutcome(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("outcomes");
    }

    [Fact]
    public void WithSubmissionOutcome_rejects_a_null_outcome_in_the_sequence()
    {
        var act = () => FakeLedgerClient.Create().WithSubmissionOutcome(Stale, null!);

        act.Should().Throw<ArgumentException>().WithParameterName("outcomes");
    }

    [Fact]
    public async Task WithSubmissionOutcome_called_again_replaces_the_staged_sequence()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(Stale, Stale)
            .WithSubmissionOutcome(Accepted)
            .Build();

        (await UpdateIdOfNextSubmission(client)).Should().Be("update-2");
    }
}
