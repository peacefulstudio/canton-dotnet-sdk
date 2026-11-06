// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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

public class FakeLedgerClientRecordedTimeoutTests
{
    private static readonly Party Alice = new("alice");
    private static readonly ContractId<DemoAsset> AssetId = new("00cid");
    private static readonly TimeSpan Passed = TimeSpan.FromSeconds(7);

    private static CommandsSubmission Submission() => CommandsSubmission
        .Single(ExerciseCommand.For(AssetId, (ChoiceName)"Archive", DamlRecord.Create()))
        .WithActAs(Alice);

    private static ReassignmentSubmission Reassignment() =>
        ReassignmentSubmission.Of(new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt"), Alice);

    private static SignedSubmission Signed() => new(
        new PreparedSubmission(new byte[] { 1, 2 }, new byte[] { 3, 4 }, HashingSchemeVersion.V2, null, null),
        [new PartySignatures(Alice, [new LedgerSignature(SignatureFormat.Der, new byte[] { 9 }, "fp", SigningAlgorithm.EcDsaSha256)])],
        "sub-1");

    private static FakeLedgerClient StagedClient() => FakeLedgerClient.Create()
        .WithCreateResult(LedgerOutcomes.One(AssetId))
        .WithExerciseResult(LedgerOutcomes.One(DamlUnit.Instance))
        .WithSubmitAndWaitResult(LedgerResults.SubmitAndWait(new CommandId("cmd-1"), "update-1", LedgerOffset.At(1)))
        .WithSubmissionOutcome(LedgerOutcomes.One(
            LedgerResults.Transaction("update-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))))
        .WithTransactionTree(LedgerOutcomes.DamlError<TransactionTree>(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "INVALID_ARGUMENT", "no", new Dictionary<string, string>()))
        .WithReassignmentResult(LedgerOutcomes.DamlError<ContractStreamEvent<DemoAsset>>(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "INVALID_ARGUMENT", "no", new Dictionary<string, string>()))
        .WithExecutedSubmission(new ExecutedSubmission("update-1", LedgerOffset.At(1)))
        .WithExecutedTransaction(LedgerResults.Transaction("update-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1")))
        .Build();

    private static readonly IReadOnlyDictionary<string, Func<FakeLedgerClient, TimeSpan?, Task>> TimedWrites =
        new Dictionary<string, Func<FakeLedgerClient, TimeSpan?, Task>>
        {
            ["TryCreateAsync"] = (client, timeout) => client.TryCreateAsync(
                new DemoAsset(Alice, Alice, "GOLD", 1m), Alice, timeout: timeout),
            ["TryExerciseAsync"] = (client, timeout) => client.TryExerciseAsync<DamlUnit>(
                ExerciseCommand.For(AssetId, (ChoiceName)"Archive", DamlRecord.Create()), Alice, timeout: timeout),
            ["SubmitAndWaitAsync"] = (client, timeout) => client.SubmitAndWaitAsync(Submission(), timeout),
            ["SubmitAndWaitAsyncWithSubmitter"] = (client, timeout) => client.SubmitAndWaitAsync(Submission(), Alice, timeout),
            ["SubmitAsync"] = (client, timeout) => client.SubmitAsync(Submission(), timeout),
            ["TrySubmitAndWaitForTransactionAsync"] = (client, timeout) =>
                client.TrySubmitAndWaitForTransactionAsync(Submission(), timeout),
            ["TrySubmitAndWaitForTransactionAsyncWithSubmitter"] = (client, timeout) =>
                client.TrySubmitAndWaitForTransactionAsync(Submission(), Alice, timeout),
            ["TrySubmitAndWaitForTransactionTreeAsync"] = (client, timeout) =>
                client.TrySubmitAndWaitForTransactionTreeAsync(Submission(), Alice, timeout),
            ["SubmitReassignmentAsync"] = (client, timeout) => client.SubmitReassignmentAsync(Reassignment(), timeout),
            ["TrySubmitAndWaitForReassignmentAsync"] = (client, timeout) =>
                client.TrySubmitAndWaitForReassignmentAsync<DemoAsset>(Reassignment(), timeout),
            ["ExecuteSubmissionAsync"] = (client, timeout) => client.ExecuteSubmissionAsync(Signed(), timeout),
            ["ExecuteSubmissionAndWaitAsync"] = (client, timeout) => client.ExecuteSubmissionAndWaitAsync(Signed(), timeout),
            ["ExecuteSubmissionAndWaitForTransactionAsync"] = (client, timeout) =>
                client.ExecuteSubmissionAndWaitForTransactionAsync(Signed(), Alice, timeout),
        };

    public static TheoryData<string> TimedWriteNames() => [.. TimedWrites.Keys];

    private static TimeSpan? TimeoutOf(RecordedSubmission recorded) => recorded switch
    {
        RecordedSubmission.Commands commands => commands.Timeout,
        RecordedSubmission.Reassignment reassignment => reassignment.Timeout,
        RecordedSubmission.Execution execution => execution.Timeout,
        _ => throw new InvalidOperationException("unexpected recorded submission"),
    };

    [Theory]
    [MemberData(nameof(TimedWriteNames))]
    public async Task Write_records_the_timeout_the_caller_passed(string member)
    {
        var client = StagedClient();

        await TimedWrites[member](client, Passed);

        TimeoutOf(client.Submissions.Should().ContainSingle().Which).Should().Be(TimeSpan.FromSeconds(7));
    }

    [Theory]
    [MemberData(nameof(TimedWriteNames))]
    public async Task Write_records_a_null_timeout_when_the_caller_passed_none(string member)
    {
        var client = StagedClient();

        await TimedWrites[member](client, null);

        TimeoutOf(client.Submissions.Should().ContainSingle().Which).Should().BeNull();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_records_the_submission_and_the_timeout()
    {
        var client = FakeLedgerClient.Create().WithTrafficCostEstimate(null).Build();
        var submission = Submission().WithCommandId(new CommandId("cmd-est"));

        await client.EstimateTrafficCostAsync(submission, TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        var recorded = client.TrafficCostEstimates.Should().ContainSingle().Which;
        recorded.Submission.Should().BeSameAs(submission);
        recorded.Timeout.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_records_a_null_timeout_when_the_caller_passed_none()
    {
        var client = FakeLedgerClient.Create().WithTrafficCostEstimate(null).Build();

        await client.EstimateTrafficCostAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        client.TrafficCostEstimates.Should().ContainSingle().Which.Timeout.Should().BeNull();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_records_each_call_in_call_order()
    {
        var client = FakeLedgerClient.Create().WithTrafficCostEstimate(null).Build();

        await client.EstimateTrafficCostAsync(Submission().WithCommandId(new CommandId("first")), cancellationToken: TestContext.Current.CancellationToken);
        await client.EstimateTrafficCostAsync(Submission().WithCommandId(new CommandId("second")), cancellationToken: TestContext.Current.CancellationToken);

        client.TrafficCostEstimates.Select(recorded => recorded.Submission.CommandId!).Should().Equal(new CommandId("first"), new CommandId("second"));
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_records_the_submission_even_when_no_estimate_is_staged()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.EstimateTrafficCostAsync(Submission());

        await act.Should().ThrowAsync<NotSupportedException>();
        client.TrafficCostEstimates.Should().ContainSingle();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_does_not_record_a_null_submission()
    {
        var client = FakeLedgerClient.Create().WithTrafficCostEstimate(null).Build();

        var act = () => client.EstimateTrafficCostAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
        client.TrafficCostEstimates.Should().BeEmpty();
    }

    [Fact]
    public void TrafficCostEstimates_is_empty_before_any_estimate()
    {
        FakeLedgerClient.Create().Build().TrafficCostEstimates.Should().BeEmpty();
    }
}
