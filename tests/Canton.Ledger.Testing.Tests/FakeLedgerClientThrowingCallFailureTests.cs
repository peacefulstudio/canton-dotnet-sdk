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
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientThrowingCallFailureTests
{
    private static readonly Party Alice = new("alice");
    private static readonly SynchronizerId Source = (SynchronizerId)"src";
    private static readonly SynchronizerId Target = (SynchronizerId)"tgt";
    private static readonly SubmitterInfo Submitter = (SubmitterInfo)Alice;
    private static readonly ContractId<DemoAsset> AssetId = new("00cid");

    private static readonly LedgerOperationException StagedFailure = new(
        "participant unreachable",
        new TransportStatus.Grpc(GrpcStatusCode.Unavailable));

    private static CommandsSubmission Submission() => CommandsSubmission
        .Single(CreateCommand.For(new DemoAsset(Alice, Alice, "GOLD", 1m)))
        .WithActAs(Alice);

    private static ReassignmentSubmission Reassignment() =>
        ReassignmentSubmission.Of(new UnassignCommand("00cid", Source, Target), Alice);

    private static SignedSubmission Signed() => new(
        new PreparedSubmission(new byte[] { 1, 2 }, new byte[] { 3, 4 }, HashingSchemeVersion.V2, null, null),
        [new PartySignatures(Alice, [new LedgerSignature(SignatureFormat.Der, new byte[] { 9 }, "fp", SigningAlgorithm.EcDsaSha256)])],
        "sub-1");

    private static readonly IReadOnlyDictionary<string, Func<FakeLedgerClient, Task>> ThrowingMembers =
        new Dictionary<string, Func<FakeLedgerClient, Task>>
        {
            ["GetLedgerEndAsync"] = client => client.GetLedgerEndAsync(),
            ["GetConnectedSynchronizersAsync"] = client => client.GetConnectedSynchronizersAsync(),
            ["GetLedgerApiVersionAsync"] = client => client.GetLedgerApiVersionAsync(),
            ["SubmitAndWaitAsync"] = client => client.SubmitAndWaitAsync(Submission()),
            ["SubmitAndWaitAsyncWithSubmitter"] = client => client.SubmitAndWaitAsync(Submission(), Submitter),
            ["SubmitAsync"] = client => client.SubmitAsync(Submission()),
            ["SubmitReassignmentAsync"] = client => client.SubmitReassignmentAsync(Reassignment()),
            ["GetUpdateByOffsetAsync"] = client => client.GetUpdateByOffsetAsync(LedgerOffset.At(1), Submitter),
            ["GetUpdateByIdAsync"] = client => client.GetUpdateByIdAsync("update-1", Submitter),
            ["GetUpdateTreeByOffsetAsync"] = client => client.GetUpdateTreeByOffsetAsync(LedgerOffset.At(1), Submitter),
            ["EstimateTrafficCostAsync"] = client => client.EstimateTrafficCostAsync(Submission()),
            ["GetContractAsync"] = client => client.GetContractAsync(AssetId, Submitter),
            ["GetEventsByContractIdAsync"] = client => client.GetEventsByContractIdAsync(AssetId, Submitter),
            ["GetDisclosureAsync"] = client => client.GetDisclosureAsync(AssetId, Submitter),
            ["GetActiveContractsPageAsync"] = client => client.GetActiveContractsPageAsync<DemoAsset>(Submitter),
            ["GetLatestPrunedOffsetsAsync"] = client => client.GetLatestPrunedOffsetsAsync(),
            ["GetUpdatesPageAsync"] = client => client.GetUpdatesPageAsync(Submitter),
            ["PrepareSubmissionAsync"] = client => client.PrepareSubmissionAsync(Submission()),
            ["ExecuteSubmissionAsync"] = client => client.ExecuteSubmissionAsync(Signed()),
            ["ExecuteSubmissionAndWaitAsync"] = client => client.ExecuteSubmissionAndWaitAsync(Signed()),
            ["ExecuteSubmissionAndWaitForTransactionAsync"] = client =>
                client.ExecuteSubmissionAndWaitForTransactionAsync(Signed(), Submitter),
            ["GetPreferredPackagesAsync"] = client =>
                client.GetPreferredPackagesAsync([new PackageVettingRequirement([Alice], "pkg-name")]),
            ["GetPreferredPackageVersionAsync"] = client => client.GetPreferredPackageVersionAsync([Alice], "pkg-name"),
        };

    private static readonly IReadOnlyDictionary<string, Func<FakeLedgerClient, object>> GuardedMembers =
        new Dictionary<string, Func<FakeLedgerClient, object>>
        {
            ["SubmitAndWaitAsync"] = client => client.SubmitAndWaitAsync(null!),
            ["SubmitAsync"] = client => client.SubmitAsync(null!),
            ["SubmitReassignmentAsync"] = client => client.SubmitReassignmentAsync(null!),
            ["GetUpdateByIdAsync"] = client => client.GetUpdateByIdAsync(null!, Submitter),
            ["EstimateTrafficCostAsync"] = client => client.EstimateTrafficCostAsync(null!),
            ["PrepareSubmissionAsync"] = client => client.PrepareSubmissionAsync(null!),
            ["ExecuteSubmissionAsync"] = client => client.ExecuteSubmissionAsync(null!),
            ["GetPreferredPackagesAsync"] = client => client.GetPreferredPackagesAsync(null!),
        };

    public static TheoryData<string> ThrowingMemberNames() => [.. ThrowingMembers.Keys];

    public static TheoryData<string> GuardedMemberNames() => [.. GuardedMembers.Keys];

    private static FakeLedgerClient ClientFailingWith(LedgerOperationException failure) =>
        FakeLedgerClient.Create().WithThrowingCallFailure(failure).Build();

    [Theory]
    [MemberData(nameof(ThrowingMemberNames))]
    public async Task Throwing_member_raises_the_staged_exception_instance_from_its_task(string member)
    {
        var client = ClientFailingWith(StagedFailure);

        var task = ThrowingMembers[member](client);

        var raised = await Assert.ThrowsAsync<LedgerOperationException>(() => task);
        raised.Should().BeSameAs(StagedFailure);
    }

    [Fact]
    public async Task Throwing_member_with_its_answer_staged_still_raises_the_staged_failure()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(5))
            .WithThrowingCallFailure(StagedFailure)
            .Build();

        var act = () => client.GetLedgerEndAsync();

        (await act.Should().ThrowAsync<LedgerOperationException>()).Which.Should().BeSameAs(StagedFailure);
    }

    [Fact]
    public async Task Staged_failure_reaches_the_caller_with_status_category_error_id_and_commit_state_intact()
    {
        var client = ClientFailingWith(StagedFailure);

        var act = () => client.SubmitAsync(Submission());

        var raised = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        raised.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        raised.Category.Should().BeNull();
        raised.ErrorId.Should().BeNull();
        raised.CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public async Task Throwing_member_raises_a_staged_status_and_commit_state_failure_with_every_field_intact()
    {
        var metadata = new Dictionary<string, string> { ["retryIn"] = "5s" };
        var cause = new TimeoutException("no response");
        var staged = new LedgerOperationException(
            "read got no answer",
            new TransportStatus.NoResponse(),
            CommitState.NotCommitted,
            DamlErrorCategory.TransientServerFailure,
            "SERVICE_NOT_RUNNING",
            metadata,
            cause);
        var client = ClientFailingWith(staged);

        var act = () => client.GetLedgerEndAsync();

        var raised = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        raised.Should().BeSameAs(staged);
        raised.Status.Should().Be(new TransportStatus.NoResponse());
        raised.CommitState.Should().Be(CommitState.NotCommitted);
        raised.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        raised.ErrorId.Should().Be("SERVICE_NOT_RUNNING");
        raised.Metadata.Should().BeSameAs(metadata);
        raised.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public async Task TryExerciseAsync_ignores_the_staged_failure_and_returns_its_staged_outcome()
    {
        var staged = new ExerciseOutcome<DamlUnit>.One(DamlUnit.Instance);
        var client = FakeLedgerClient.Create()
            .WithExerciseResult<DamlUnit>(staged)
            .WithThrowingCallFailure(StagedFailure)
            .Build();
        var command = ExerciseCommand.For(AssetId, (ChoiceName)"Archive", DamlRecord.Create());

        var outcome = await client.TryExerciseAsync<DamlUnit>(command, Submitter, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeSameAs(staged);
    }

    [Fact]
    public async Task CompletionStreamAsync_ignores_the_staged_failure_and_yields_its_staged_events()
    {
        var checkpoint = new CompletionStreamEvent.Checkpoint(LedgerOffset.At(3));
        var client = FakeLedgerClient.Create()
            .WithCompletionEvents(checkpoint)
            .WithThrowingCallFailure(StagedFailure)
            .Build();

        var events = new List<CompletionStreamEvent>();
        await foreach (var streamEvent in client.CompletionStreamAsync(Submitter, cancellationToken: TestContext.Current.CancellationToken).WithCancellation(TestContext.Current.CancellationToken))
        {
            events.Add(streamEvent);
        }

        events.Should().ContainSingle().Which.Should().BeSameAs(checkpoint);
    }

    [Fact]
    public async Task Unstaged_member_without_a_staged_failure_keeps_its_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetLedgerApiVersionAsync();

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("WithLedgerApiVersion");
    }

    [Theory]
    [MemberData(nameof(GuardedMemberNames))]
    public void Null_argument_guard_fires_synchronously_ahead_of_the_staged_failure(string member)
    {
        var client = ClientFailingWith(StagedFailure);

        var act = () => GuardedMembers[member](client);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithThrowingCallFailure_rejects_a_null_exception()
    {
        var builder = FakeLedgerClient.Create();

        var act = () => builder.WithThrowingCallFailure(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("failure");
    }
}
