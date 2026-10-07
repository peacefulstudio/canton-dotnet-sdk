// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientFailureContractTests : IDisposable
{
    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _submissionService;

    public LedgerClientFailureContractTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _submissionService = Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        _submissionService,
        new CommandCompletionService.CommandCompletionServiceClient(_channel),
        new StaticTokenProvider("test-token"));

    [Fact]
    public async Task GetLedgerEndAsync_reports_an_unstructured_Unavailable_as_a_not_committed_failure()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "participant down"));
        StubGetLedgerEnd(Faulted<GetLedgerEndResponse>(unavailable));

        var act = () => CreateClient().GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Message.Should().Be("participant down");
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Category.Should().BeNull();
        thrown.ErrorId.Should().BeNull();
        thrown.Metadata.Should().BeNull();
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    [Fact]
    public async Task GetLedgerEndAsync_reports_a_structured_error_as_a_not_committed_failure_carrying_its_detail()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "PARTICIPANT_PRUNED_DATA_ACCESSED",
            "the data was pruned",
            "InvalidGivenCurrentSystemStateResourceMissing",
            StatusCode.NotFound);
        StubGetLedgerEnd(Faulted<GetLedgerEndResponse>(rejection));

        var act = () => CreateClient().GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Message.Should().Be("the data was pruned");
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.NotFound));
        thrown.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        thrown.ErrorId.Should().Be("PARTICIPANT_PRUNED_DATA_ACCESSED");
        thrown.Metadata.Should().Equal(new Dictionary<string, string>
        {
            ["category"] = "InvalidGivenCurrentSystemStateResourceMissing",
        });
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(rejection);
    }

    [Fact]
    public async Task GetLedgerEndAsync_reports_a_request_state_unknown_error_as_not_committed_because_a_read_commits_nothing()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "REQUEST_TIME_OUT",
            "timed out",
            "DeadlineExceededRequestStateUnknown",
            StatusCode.DeadlineExceeded);
        StubGetLedgerEnd(Faulted<GetLedgerEndResponse>(rejection));

        var act = () => CreateClient().GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_a_rejection_with_a_definite_category_as_not_committed()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "CONTRACT_NOT_FOUND",
            "unknown contract",
            "InvalidGivenCurrentSystemStateResourceMissing",
            StatusCode.NotFound);
        StubSubmitAndWait(Faulted<SubmitAndWaitResponse>(rejection));

        var act = () => CreateClient().SubmitAndWaitAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.NotFound));
        thrown.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        thrown.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(rejection);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_a_request_state_unknown_rejection_as_unknown()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "REQUEST_TIME_OUT",
            "timed out",
            "DeadlineExceededRequestStateUnknown",
            StatusCode.DeadlineExceeded);
        StubSubmitAndWait(Faulted<SubmitAndWaitResponse>(rejection));

        var act = () => CreateClient().SubmitAndWaitAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
        thrown.CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_an_unstructured_Unavailable_as_unknown()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "connection dropped"));
        StubSubmitAndWait(Faulted<SubmitAndWaitResponse>(unavailable));

        var act = () => CreateClient().SubmitAndWaitAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_a_deadline_overrun_as_a_DeadlineExceeded_status_with_an_unknown_commit_state()
    {
        var overrun = new RpcException(new Status(StatusCode.DeadlineExceeded, "deadline"));
        StubSubmitAndWait(Faulted<SubmitAndWaitResponse>(overrun));

        var act = () => CreateClient().SubmitAndWaitAsync(
            Submission(), timeout: TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded));
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.InnerException.Should().BeSameAs(overrun);
    }

    [Fact]
    public async Task SubmitAsync_reports_an_unstructured_Unavailable_as_unknown()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "connection dropped"));
        StubSubmit(Faulted<SubmitResponse>(unavailable));

        var act = () => CreateClient().SubmitAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    [Fact]
    public async Task SubmitAsync_reports_a_rejection_with_a_definite_category_as_not_committed()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "INVALID_ARGUMENT",
            "bad command",
            "InvalidIndependentOfSystemState",
            StatusCode.InvalidArgument);
        StubSubmit(Faulted<SubmitResponse>(rejection));

        var act = () => CreateClient().SubmitAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task GetLedgerEndAsync_retries_a_transient_failure_to_the_configured_attempts_and_translates_only_the_last()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        var finalFailure = new RpcException(new Status(StatusCode.Unavailable, "still down"));
        StubGetLedgerEnd(
            Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.Unavailable, "down 1"))),
            Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.Unavailable, "down 2"))),
            Faulted<GetLedgerEndResponse>(finalFailure));

        var act = () => CreateClient().GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Message.Should().Be("still down");
        thrown.InnerException.Should().BeSameAs(finalFailure);
        _ = _stateService.Received(3).GetLedgerEndAsync(
            Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLedgerEndAsync_keeps_a_caller_cancellation_as_an_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.Cancelled, "cancelled")));
            });

        var act = () => CreateClient().GetLedgerEndAsync(cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_raises_a_not_committed_failure_when_the_ledger_end_cannot_be_resolved()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "participant down"));
        StubGetLedgerEnd(Faulted<GetLedgerEndResponse>(unavailable));

        var act = async () =>
        {
            await foreach (var _ in CreateClient().SubscribeActiveAsync<FooBar>(
                ActAs, cancellationToken: TestContext.Current.CancellationToken)) { }
        };

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    [Fact]
    public async Task QueryActiveAsync_on_an_interface_raises_a_not_committed_failure_when_the_ledger_end_cannot_be_resolved()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "participant down"));
        StubGetLedgerEnd(Faulted<GetLedgerEndResponse>(unavailable));

        ICantonLedgerClient client = CreateClient();

        var act = () => client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    [Fact]
    public async Task SubscribeActiveAsync_on_an_interface_raises_a_not_committed_failure_when_the_ledger_end_cannot_be_resolved()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "participant down"));
        StubGetLedgerEnd(Faulted<GetLedgerEndResponse>(unavailable));

        ICantonLedgerClient client = CreateClient();

        var act = async () =>
        {
            await foreach (var _ in client.SubscribeActiveAsync(
                new ViewDescriptor<IViewedInterfaceMarker, ViewedInterfaceView>(), ActAs, cancellationToken: TestContext.Current.CancellationToken)) { }
        };

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(ActAs)
            .WithCommandId(new RuntimeCommands.CommandId("test-cmd"));

    private void StubGetLedgerEnd(params AsyncUnaryCall<GetLedgerEndResponse>[] calls) =>
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(calls[0], calls[1..]);

    private void StubSubmitAndWait(AsyncUnaryCall<SubmitAndWaitResponse> call) =>
        _commandService
            .SubmitAndWaitAsync(
                Arg.Any<SubmitAndWaitRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(call);

    private void StubSubmit(AsyncUnaryCall<SubmitResponse> call) =>
        _submissionService
            .SubmitAsync(
                Arg.Any<SubmitRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(call);

    private static AsyncUnaryCall<T> Faulted<T>(RpcException exception) =>
        new(
            Task.FromException<T>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });
}
