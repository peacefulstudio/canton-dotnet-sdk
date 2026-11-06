// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientThrowingTryMembersTests
{
    private static readonly Party Alice = new("alice");
    private static readonly ContractId<DemoAsset> AssetId = new("00cid");

    private static CommandsSubmission Submission() => CommandsSubmission
        .Single(ExerciseCommand.For(AssetId, (ChoiceName)"Archive", DamlRecord.Create()))
        .WithActAs(Alice);

    private static ReassignmentSubmission Reassignment() =>
        ReassignmentSubmission.Of(new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"), Alice);

    private static FakeLedgerClient StagedClient(Exception failure, bool alsoFromTryMembers) => FakeLedgerClient.Create()
        .WithCreateResult(LedgerOutcomes.One(AssetId))
        .WithExerciseResult(LedgerOutcomes.One(DamlUnit.Instance))
        .WithSubmissionOutcome(LedgerOutcomes.One(
            LedgerResults.Transaction("update-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))))
        .WithTransactionTree(LedgerOutcomes.DamlError<TransactionTree>(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "INVALID_ARGUMENT", "no", new Dictionary<string, string>()))
        .WithReassignmentResult(LedgerOutcomes.DamlError<ContractStreamEvent<DemoAsset>>(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "INVALID_ARGUMENT", "no", new Dictionary<string, string>()))
        .WithThrowingCallFailure(failure, alsoFromTryMembers)
        .Build();

    private static readonly IReadOnlyDictionary<string, Func<FakeLedgerClient, Task>> TryMembers =
        new Dictionary<string, Func<FakeLedgerClient, Task>>
        {
            ["TryCreateAsync"] = client => client.TryCreateAsync(new DemoAsset(Alice, Alice, "GOLD", 1m), Alice),
            ["TryExerciseAsync"] = client => client.TryExerciseAsync<DamlUnit>(
                ExerciseCommand.For(AssetId, (ChoiceName)"Archive", DamlRecord.Create()), Alice),
            ["TrySubmitAndWaitForTransactionAsync"] = client => client.TrySubmitAndWaitForTransactionAsync(Submission()),
            ["TrySubmitAndWaitForTransactionAsyncWithSubmitter"] = client =>
                client.TrySubmitAndWaitForTransactionAsync(Submission(), Alice),
            ["TrySubmitAndWaitForTransactionTreeAsync"] = client =>
                client.TrySubmitAndWaitForTransactionTreeAsync(Submission(), Alice),
            ["TrySubmitAndWaitForReassignmentAsync"] = client =>
                client.TrySubmitAndWaitForReassignmentAsync<DemoAsset>(Reassignment()),
        };

    public static TheoryData<string> TryMemberNames() => [.. TryMembers.Keys];

    [Theory]
    [MemberData(nameof(TryMemberNames))]
    public async Task Try_member_raises_the_staged_exception_instance_when_opted_in(string member)
    {
        var failure = new OperationCanceledException("cancelled");
        var client = StagedClient(failure, alsoFromTryMembers: true);

        var raised = await Assert.ThrowsAsync<OperationCanceledException>(() => TryMembers[member](client));

        raised.Should().BeSameAs(failure);
    }

    [Theory]
    [MemberData(nameof(TryMemberNames))]
    public async Task Try_member_keeps_returning_its_staged_outcome_when_not_opted_in(string member)
    {
        var client = StagedClient(new OperationCanceledException("cancelled"), alsoFromTryMembers: false);

        var act = () => TryMembers[member](client);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Try_member_raises_a_LedgerOperationException_failure_when_opted_in()
    {
        var failure = new LedgerOperationException(
            "participant unreachable", new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        var client = StagedClient(failure, alsoFromTryMembers: true);

        var act = () => client.TryExerciseAsync<DamlUnit>(
            ExerciseCommand.For(AssetId, (ChoiceName)"Archive", DamlRecord.Create()), Alice);

        (await act.Should().ThrowAsync<LedgerOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task Try_member_records_its_submission_before_raising_the_staged_failure()
    {
        var client = StagedClient(new OperationCanceledException("cancelled"), alsoFromTryMembers: true);

        var act = () => client.TrySubmitAndWaitForTransactionAsync(Submission(), Alice, TimeSpan.FromSeconds(2));

        await act.Should().ThrowAsync<OperationCanceledException>();
        client.Submissions.Should().ContainSingle().Which
            .Should().BeOfType<RecordedSubmission.Commands>().Which.Timeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Try_member_still_throws_an_argument_exception_ahead_of_the_staged_failure()
    {
        var client = StagedClient(new OperationCanceledException("cancelled"), alsoFromTryMembers: true);

        var act = () => client.TrySubmitAndWaitForTransactionAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Throwing_member_raises_a_non_ledger_exception_staged_through_the_widened_overload()
    {
        var failure = new TimeoutException("no response");
        var client = FakeLedgerClient.Create().WithThrowingCallFailure(failure).Build();

        var act = () => client.GetLedgerApiVersionAsync();

        (await act.Should().ThrowAsync<TimeoutException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public void WithThrowingCallFailure_rejects_a_null_failure()
    {
        var act = () => FakeLedgerClient.Create().WithThrowingCallFailure(null!, alsoFromTryMembers: true);

        act.Should().Throw<ArgumentNullException>().WithParameterName("failure");
    }
}
