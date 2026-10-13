// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientCommandIdOnFailureTests : IDisposable
{
    private static readonly Party Alice = new("alice::ns1");

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _submissionService;
    private readonly StateService.StateServiceClient _stateService;

    public LedgerClientCommandIdOnFailureTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _submissionService = Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient(ITokenProvider? tokenProvider = null) => new(
        _options,
        _channel,
        _commandService,
        Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(Substitute.For<CallInvoker>()),
        _stateService,
        _submissionService,
        new CommandCompletionService.CommandCompletionServiceClient(_channel),
        tokenProvider ?? new StaticTokenProvider("test-token"));

    [Fact]
    public async Task SubmitAndWaitAsync_reports_the_caller_supplied_command_id_when_the_participant_rejects_it()
    {
        StubSubmitAndWaitRejection();

        var act = () => CreateClient().SubmitAndWaitAsync(Create("cmd-1"), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId
            .Should().Be(new RuntimeCommands.CommandId("cmd-1"));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_the_command_id_it_generated_when_the_participant_rejects_it()
    {
        string? sentCommandId = null;
        StubSubmitAndWaitRejection(request => sentCommandId = request.Commands.CommandId);

        var act = () => CreateClient().SubmitAndWaitAsync(Create(commandId: null), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        sentCommandId.Should().NotBeNullOrEmpty();
        thrown.CommandId.Should().Be(new RuntimeCommands.CommandId(sentCommandId!));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_the_command_id_when_the_token_cannot_be_acquired()
    {
        var tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new FormatException("bad token"));

        var act = () => CreateClient(tokenProvider).SubmitAndWaitAsync(Create("cmd-2"), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId
            .Should().Be(new RuntimeCommands.CommandId("cmd-2"));
    }

    [Fact]
    public async Task OneOrThrowAsync_after_TrySubmitSingleAsync_reports_the_command_id_the_helper_minted()
    {
        string? sentCommandId = null;
        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Do<SubmitAndWaitForTransactionRequest>(request => sentCommandId = request.Commands.CommandId),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(Faulted<SubmitAndWaitForTransactionResponse>(Rejection()));
        var command = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, []));

        var act = () => CreateClient()
            .TrySubmitSingleAsync(command, Alice, cancellationToken: TestContext.Current.CancellationToken)
            .OneOrThrowAsync("Create");

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        sentCommandId.Should().NotBeNullOrEmpty();
        thrown.CommandId.Should().Be(new RuntimeCommands.CommandId(sentCommandId!));
    }

    [Fact]
    public async Task SubmitAsync_reports_the_caller_supplied_command_id_when_the_participant_rejects_it()
    {
        _submissionService
            .SubmitAsync(Arg.Any<SubmitRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<SubmitResponse>(Rejection()));

        var act = () => CreateClient().SubmitAsync(Create("cmd-3"), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId
            .Should().Be(new RuntimeCommands.CommandId("cmd-3"));
    }

    [Fact]
    public async Task GetLedgerEndAsync_reports_no_command_id_when_the_participant_rejects_the_read()
    {
        _stateService
            .GetLedgerEndAsync(Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<GetLedgerEndResponse>(Rejection()));

        var act = () => CreateClient().GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().BeNull();
    }

    private void StubSubmitAndWaitRejection(Action<SubmitAndWaitRequest>? capture = null) =>
        _commandService
            .SubmitAndWaitAsync(
                Arg.Do<SubmitAndWaitRequest>(request => capture?.Invoke(request)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(Faulted<SubmitAndWaitResponse>(Rejection()));

    private static RpcException Rejection() =>
        LedgerClientTestFixtures.MakeDamlRpcException(
            "CONTRACT_NOT_FOUND",
            "Contract could not be found",
            "InvalidGivenCurrentSystemStateResourceMissing",
            StatusCode.NotFound);

    private static RuntimeCommands.CommandsSubmission Create(string? commandId)
    {
        var submission = RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(Alice);
        return commandId is null ? submission : submission.WithCommandId(new RuntimeCommands.CommandId(commandId));
    }

    private static AsyncUnaryCall<T> Faulted<T>(RpcException exception) =>
        new(
            Task.FromException<T>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });
}
