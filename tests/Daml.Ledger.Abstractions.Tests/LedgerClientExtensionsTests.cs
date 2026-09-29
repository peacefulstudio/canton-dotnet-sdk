// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Verifies <see cref="ThrowingExercise"/>: the throwing convenience wrappers around
/// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>.
/// </summary>
public class LedgerClientExtensionsTests
{
    private static readonly ExerciseCommand SampleCommand = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly TransactionResult SampleTransaction = new(
        "update-id", LedgerOffset.Begin, [], [], new CommandId("cmd-id"));

    private static TransactionResult TransactionCreating(params string[] contractIds) =>
        new("update-id", LedgerOffset.Begin,
            [.. contractIds.Select(CreatedOf)],
            [],
            new CommandId("cmd-id"));

    private static CreatedContract CreatedOf(string contractId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: SampleTemplate.TemplateId,
            Payload: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: []);

    [Fact]
    public async Task ExerciseAsync_returns_value_when_TryExerciseAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.One(42));

        var result = await client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExerciseAsync_throws_InvalidOperationException_when_TryExerciseAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExerciseAsync_throws_InvalidOperationException_when_TryExerciseAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    [Fact]
    public async Task ExerciseAsync_throws_OperationCanceledException_not_LedgerOperationException_when_TryExerciseAsync_returns_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "Cancelled"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_OperationCanceledException_not_LedgerOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "Cancelled"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExerciseAsync_throws_InvalidOperationException_when_TryExerciseAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.None());

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*None*");
    }

    [Fact]
    public async Task ExerciseAsync_throws_InvalidOperationException_when_TryExerciseAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.Many(["cid-1", "cid-2", "cid-3"]));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Many*3*");
    }

    [Fact]
    public async Task ExerciseAsync_forwards_the_timeout_to_TryExerciseAsync()
    {
        var stub = new StubLedgerClient(new ExerciseOutcome<int>.One(42));

        await stub.ExerciseAsync<int>(
            SampleCommand, new Party("alice"), timeout: TimeSpan.FromSeconds(7),
            cancellationToken: TestContext.Current.CancellationToken);

        stub.LastExerciseTimeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task ExerciseAsync_omitted_timeout_reaches_TryExerciseAsync_as_null()
    {
        var stub = new StubLedgerClient(new ExerciseOutcome<int>.One(42));

        await stub.ExerciseAsync<int>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        stub.LastExerciseTimeout.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_void_forwards_the_timeout_to_TrySubmitAndWaitForTransactionAsync()
    {
        var stub = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.One(SampleTransaction));

        await stub.ExerciseAsync(
            SampleCommand, new Party("alice"), timeout: TimeSpan.FromSeconds(7),
            cancellationToken: TestContext.Current.CancellationToken);

        stub.LastTransactionTimeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task ExerciseAsync_void_omitted_timeout_reaches_TrySubmitAndWaitForTransactionAsync_as_null()
    {
        var stub = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.One(SampleTransaction));

        await stub.ExerciseAsync(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        stub.LastTransactionTimeout.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_void_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.One(SampleTransaction));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_LedgerOperationException_when_TrySubmitSingleAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*None*");
    }

    [Fact]
    public async Task ExerciseAsync_void_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.Many(["cid-1", "cid-2"]));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_CommittedUndecodable()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.CommittedUndecodable(
            "u1", "could not decode result", new InvalidOperationException("decode failed")));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_InvalidOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_InvalidOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_returns_value_when_TryExerciseAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.One(99));
        var submitter = new SubmitterInfo(new Party("alice"));

        var result = await client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(99);
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_OperationCanceledException_not_LedgerOperationException_when_TryExerciseAsync_returns_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "Cancelled"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.None());
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*None*");
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.Many(["cid-1", "cid-2", "cid-3"]));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Many*3*");
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.One(SampleTransaction));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_LedgerOperationException_when_TrySubmitSingleAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*None*");
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.Many(["cid-1", "cid-2"]));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_InvalidOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_InvalidOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_OperationCanceledException_not_LedgerOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "Cancelled"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_carrying_the_DamlError_outcome()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string> { ["cid"] = "00abc" });
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        exception.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        exception.Metadata.Should().ContainKey("cid").WhoseValue.Should().Be("00abc");
        exception.Status.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_carrying_the_InfraError_outcome()
    {
        var sourceException = new InvalidOperationException("transport failed");
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset", SourceException: sourceException));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        exception.InnerException.Should().BeSameAs(sourceException);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_carrying_the_InfraError_category_and_status_code_together()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(
            new TransportStatus.Http(HttpStatusCode.BadRequest), "Bad Request", DamlErrorCategory.InvalidIndependentOfSystemState));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        exception.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.BadRequest));
        exception.ErrorId.Should().BeNull(
            "a classified transport failure carries no recoverable Canton error id, and both facts have to " +
            "survive on the one exception rather than the caller having to pick which to discard");
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_null_InnerException_when_InfraError_has_no_SourceException()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_carrying_the_CommittedUndecodable_outcome()
    {
        var sourceException = new InvalidOperationException("decode failed");
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.CommittedUndecodable(
            "u1", "could not decode result", sourceException));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.InnerException.Should().BeSameAs(sourceException);
        exception.UpdateId.Should().Be("u1");
        exception.Message.Should().Contain("u1").And.Contain("could not decode result");
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_a_null_update_id_when_the_decode_failure_precedes_it()
    {
        var sourceException = new InvalidOperationException("decode failed");
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.CommittedUndecodable(
            null, "could not decode result", sourceException));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.UpdateId.Should().BeNull();
        exception.Message.Should().Contain("unknown").And.Contain("could not decode result");
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_without_error_detail_for_None_and_Many()
    {
        ILedgerClient noneClient = new StubLedgerClient(new ExerciseOutcome<int>.None());
        ILedgerClient manyClient = new StubLedgerClient(new ExerciseOutcome<int>.Many(["cid-1", "cid-2"]));

        Func<Task> noneAct = () => noneClient.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);
        Func<Task> manyAct = () => manyClient.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var noneException = (await noneAct.Should().ThrowAsync<LedgerOperationException>()).Which;
        var manyException = (await manyAct.Should().ThrowAsync<LedgerOperationException>()).Which;
        noneException.Category.Should().BeNull();
        noneException.Status.Should().BeNull();
        manyException.Category.Should().BeNull();
        manyException.Status.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_CommitState_Committed_for_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.None());

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.CommitState.Should().Be(
            CommitState.Committed,
            "None means the command committed but produced no transaction; a caller reading "
            + "CommitState as NotCommitted here would risk resubmitting already-accepted work");
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_CommitState_Committed_for_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.Many(["cid-1", "cid-2"]));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.CommitState.Should().Be(
            CommitState.Committed,
            "Many means the command committed but produced more than one transaction; a caller "
            + "reading CommitState as NotCommitted here would risk resubmitting already-accepted work");
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_CommitState_NotCommitted_for_DamlError()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_CommitState_Unknown_for_DamlError_DeadlineExceededRequestStateUnknown()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.DeadlineExceededRequestStateUnknown,
            "REQUEST_TIME_OUT",
            "Deadline exceeded",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.CommitState.Should().Be(
            CommitState.Unknown,
            "the ledger itself reported that the outcome of the request is unknown, so a caller "
            + "must retry only with the same command id");
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_CommitState_Unknown_for_DamlError_UnknownCategory()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.Unknown,
            "SOME_ERROR_ID",
            "Unclassified error",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.CommitState.Should().Be(
            CommitState.Unknown,
            "the transport could not classify the category, so it might have been "
            + "DeadlineExceededRequestStateUnknown, and a caller must retry only with the same command id");
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_CommitState_Unknown_for_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.CommitState.Should().Be(
            CommitState.Unknown,
            "the transport failure happened after the command was sent, so the ledger may already "
            + "have committed it");
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_CommitState_Committed_for_CommittedUndecodable()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.CommittedUndecodable(
            "u1", "could not decode result", new InvalidOperationException("decode failed")));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.CommitState.Should().Be(CommitState.Committed);
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_LedgerOperationException_with_CommitState_Committed_for_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.CommitState.Should().Be(
            CommitState.Committed,
            "the void ExerciseAsync path constructs its own LedgerOperationException for None, "
            + "separately from the typed ResultOrThrow path");
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_LedgerOperationException_carrying_the_DamlError_outcome()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "LOCAL_VERDICT_LOCKED_CONTRACTS",
            "Contract is locked",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        exception.ErrorId.Should().Be("LOCAL_VERDICT_LOCKED_CONTRACTS");
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_LedgerOperationException_carrying_the_InfraError_outcome()
    {
        var sourceException = new TimeoutException("deadline transport failure");
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded), "Deadline exceeded", SourceException: sourceException));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded));
        exception.InnerException.Should().BeSameAs(sourceException);
        exception.CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_returns_One_when_transaction_created_exactly_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-created")));

        var outcome = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.One>()
            .Which.Result.Value.Should().Be("cid-created");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_returns_None_when_transaction_created_no_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating()));

        var outcome = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.None>();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_returns_Many_when_transaction_created_more_than_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1", "cid-2")));

        var outcome = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var many = outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.Many>().Which;
        many.Count.Should().Be(2);
        many.ContractIds.Should().Equal("cid-1", "cid-2");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_propagates_writer_level_Many_without_collapsing_to_None()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.Many(["cid-1", "cid-2", "cid-3"]));

        var outcome = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var many = outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.Many>().Which;
        many.Count.Should().Be(3);
        many.ContractIds.Should().Equal("cid-1", "cid-2", "cid-3");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_propagates_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string> { ["cid"] = "00abc" });
        ILedgerClient client = new StubLedgerClient(outcome);

        var result = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var remapped = result.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.DamlError>().Subject;
        remapped.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        remapped.Metadata.Should().Equal(outcome.Metadata);
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_propagates_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset"));

        var result = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.InfraError>()
            .Which.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_carries_the_InfraError_category_through_the_remap()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(
            new TransportStatus.Http(HttpStatusCode.BadRequest), "Bad Request", DamlErrorCategory.InvalidIndependentOfSystemState));

        var result = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.InfraError>()
            .Which.Category.Should().Be(
                DamlErrorCategory.InvalidIndependentOfSystemState,
                "remapping the outcome onto the created contract id without forwarding the category silently " +
                "discards a classification the transport determined without a structured Canton error to carry it");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_propagates_CommittedUndecodable()
    {
        var sourceException = new InvalidOperationException("decode failed");
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.CommittedUndecodable(
            "u1", "could not decode result", sourceException));

        var result = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var remapped = result.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.CommittedUndecodable>().Subject;
        remapped.UpdateId.Should().Be("u1");
        remapped.Message.Should().Be("could not decode result");
        remapped.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_returns_One_with_empty_list_when_transaction_created_no_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating()));

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<EquatableArray<ContractId<SampleTemplate>>>.One>()
            .Which.Result.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_returns_One_with_single_item_when_transaction_created_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1")));

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<EquatableArray<ContractId<SampleTemplate>>>.One>()
            .Which.Result.Select(id => id.Value).Should().Equal("cid-1");
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_returns_One_with_all_items_when_transaction_created_many_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1", "cid-2", "cid-3")));

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<EquatableArray<ContractId<SampleTemplate>>>.One>()
            .Which.Result.Select(id => id.Value).Should().Equal("cid-1", "cid-2", "cid-3");
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_returns_a_One_comparing_equal_to_the_same_ids_in_order()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1", "cid-2")));

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().Be(
            new ExerciseOutcome<EquatableArray<ContractId<SampleTemplate>>>.One([new("cid-1"), new("cid-2")]),
            "the One arm carries the value-comparing array All<T>() projected, so two outcomes over the "
            + "same created contracts are equal; boxing that array back into an IReadOnlyList changes the "
            + "outcome's closed type and moves == on the payload off the surface the projection handed over");
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_propagates_writer_level_Many_without_collapsing_to_One()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.Many(["cid-1", "cid-2", "cid-3"]));

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var many = outcome.Should().BeOfType<ExerciseOutcome<EquatableArray<ContractId<SampleTemplate>>>.Many>().Which;
        many.Count.Should().Be(3);
        many.ContractIds.Should().Equal("cid-1", "cid-2", "cid-3");
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_propagates_writer_level_None_without_collapsing_to_an_empty_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<EquatableArray<ContractId<SampleTemplate>>>.None>(
            "a writer that produced no transaction is not a choice that created no contracts, and the "
            + "default EquatableArray is a valid empty array, so a collapse to One would read as success");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_returns_id_when_transaction_created_exactly_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1")));

        var id = await client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        id.Value.Should().Be("cid-1");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_throws_LedgerOperationException_when_transaction_created_no_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating()));

        Func<Task> act = () => client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*expected exactly one*");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_throws_LedgerOperationException_mentioning_CreateManyByExerciseAsync_when_transaction_created_many_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1", "cid-2")));

        Func<Task> act = () => client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*CreateManyByExerciseAsync*");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_throws_LedgerOperationException_on_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_throws_OperationCanceledException_when_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "Cancelled"));

        Func<Task> act = () => client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_returns_single_item_list_when_transaction_created_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1")));

        var ids = await client.CreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        ids.Select(id => id.Value).Should().Equal("cid-1");
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_returns_empty_list_when_transaction_created_no_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating()));

        var ids = await client.CreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_returns_an_EquatableArray_comparing_equal_to_the_same_ids_in_order()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1", "cid-2")));

        EquatableArray<ContractId<SampleTemplate>> ids = await client.CreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        EquatableArray<ContractId<SampleTemplate>> sameIds = [new("cid-1"), new("cid-2")];
        (ids == sameIds).Should().BeTrue(
            "the create-by-exercise twin hands back the value-comparing array All<T>() projected, "
            + "so == on it compares content rather than the identity of two separately built sequences");
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_throws_LedgerOperationException_on_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());

        Func<Task> act = () => client.CreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>(
            "the default EquatableArray is a valid empty array, so returning it for a writer that "
            + "produced no transaction would report 'the choice created nothing' rather than failing")
            .WithMessage("*yielded no result (None)*");
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_throws_LedgerOperationException_on_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.CreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task DisposeAsync_default_bridge_delegates_to_Dispose()
    {
        var client = new StubLedgerClient(new ExerciseOutcome<int>.None());

        await ((IAsyncDisposable)(ILedgerClient)client).DisposeAsync();

        client.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Read_paths_accept_an_interface_marker_through_its_view_witness()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());

        var exercised = await client.TryCreateOneByExerciseAsync<SampleInterface>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);
        var subscription = client.SubscribeAsync(
            SampleInterface.View, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);
        var events = new List<InterfaceStreamEvent<SampleInterface, SampleView>>();
        await foreach (var evt in subscription)
        {
            events.Add(evt);
        }

        exercised.Should().BeOfType<ExerciseOutcome<ContractId<SampleInterface>>.None>();
        events.Should().BeEmpty();
    }

    /// <summary>
    /// Minimal <see cref="ILedgerClient"/> stub that returns a pre-configured
    /// <see cref="ExerciseOutcome{T}"/> from both the <c>TryExerciseAsync&lt;TResult&gt;</c>
    /// and <c>TrySubmitAndWaitForTransactionAsync</c> primitives. The outcome is stored
    /// as <c>object</c> and cast on retrieval so a single non-generic stub can satisfy
    /// the generic contract for any <c>TResult</c>. The throwing extension wrappers and
    /// the <c>Party</c>-<c>actAs</c> overloads both route here. Each primitive records the
    /// <c>timeout</c> it was handed, so a wrapper that accepts one and drops it is visible.
    /// </summary>
    private sealed class StubLedgerClient : NotSupportedLedgerClient
    {
        private readonly object _outcome;

        public StubLedgerClient(object outcome) => _outcome = outcome;

        public TimeSpan? LastExerciseTimeout { get; private set; }

        public TimeSpan? LastTransactionTimeout { get; private set; }

        public override Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            LastExerciseTimeout = timeout;
            return Task.FromResult((ExerciseOutcome<TResult>)_outcome);
        }

        public override Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission,
            SubmitterInfo submitter,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            LastTransactionTimeout = timeout;
            return Task.FromResult((ExerciseOutcome<TransactionResult>)_outcome);
        }

        public override Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(new ExerciseOutcome<ContractId<TTemplate>>.None());

        public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            => EmptyAsync<ContractStreamEvent<T>>(cancellationToken);

        public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            => EmptyAsync<ContractStreamEvent<T>>(cancellationToken);

        public override IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            bool includeDisclosure = false,
            CancellationToken cancellationToken = default)
            => EmptyAsync<AcsSnapshotEntry<T>>(cancellationToken);

        public override Task<LedgerOffset> GetLedgerEndAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LedgerOffset.Begin);

        public bool Disposed { get; private set; }

        public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            => EmptyAsync<InterfaceStreamEvent<TInterface, TView>>(cancellationToken);

        public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            => EmptyAsync<InterfaceStreamEvent<TInterface, TView>>(cancellationToken);

        public override IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            bool includeDisclosure = false,
            CancellationToken cancellationToken = default)
            => EmptyAsync<InterfaceAcsSnapshotEntry<TInterface, TView>>(cancellationToken);

        public override void Dispose()
        {
            base.Dispose();
            Disposed = true;
        }

        private static async IAsyncEnumerable<TItem> EmptyAsync<TItem>(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }
}

internal sealed record SampleTemplate : ITemplate
{
    public static Identifier TemplateId => new("pkg", "Module", "Template");
    public static string PackageId => "pkg";
    public static string PackageName => "pkg-name";
    public static Version PackageVersion => new(1, 0, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create();
    public static SampleTemplate FromRecord(DamlRecord record) => new();
}

internal sealed record SampleInterface : IDamlInterface, IHasView<SampleView>
{
    public static Identifier InterfaceId => new("iface-pkg", "Module", "ISample");
    public static string PackageId => "iface-pkg";
    public static string PackageName => "iface-name";
    public static Version PackageVersion => new(1, 0, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
    public static ViewDescriptor<SampleInterface, SampleView> View { get; } = new();

    public DamlRecord ToRecord() => DamlRecord.Create();
}

internal sealed record SampleView : IDamlRecord<SampleView>
{
    public DamlRecord ToRecord() => DamlRecord.Create();
    public static SampleView FromRecord(DamlRecord record) => new();
    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        throw new NotSupportedException();
}
