// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Data;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using SignatureFormat = Canton.Ledger.Abstractions.SignatureFormat;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientPreSendFailureTests : IDisposable
{
    private static readonly Party Alice = new("alice::ns1");
    private static readonly RuntimeCommands.SubmitterInfo Submitter = new(Alice);

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient _interactiveService;
    private readonly FormatException _tokenFailure = new("the token is not valid base64");

    public LedgerClientPreSendFailureTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _interactiveService =
            Substitute.ForPartsOf<Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient()
    {
        var tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw _tokenFailure);
        return new LedgerClient(
            _options,
            _channel,
            _commandService,
            Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(Substitute.For<CallInvoker>()),
            _stateService,
            Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(Substitute.For<CallInvoker>()),
            Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(Substitute.For<CallInvoker>()),
            tokenProvider,
            interactiveSubmissionService: _interactiveService);
    }

    [Fact]
    public async Task GetLedgerEndAsync_lets_a_failure_before_the_request_is_sent_escape_unchanged()
    {
        var act = () => CreateClient().GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        await AssertEscapesUnchangedWithoutSending(act);
    }

    [Fact]
    public async Task GetUpdateByIdAsync_lets_a_failure_before_the_request_is_sent_escape_unchanged()
    {
        var act = () => CreateClient().GetUpdateByIdAsync("u-1", Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertEscapesUnchangedWithoutSending(act);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_lets_a_failure_before_the_request_is_sent_escape_unchanged()
    {
        var act = () => CreateClient().SubmitAndWaitAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        await AssertEscapesUnchangedWithoutSending(act);
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_lets_a_failure_before_the_request_is_sent_escape_unchanged()
    {
        var act = () => CreateClient().ExecuteSubmissionAndWaitAsync(Signed(), cancellationToken: TestContext.Current.CancellationToken);

        await AssertEscapesUnchangedWithoutSending(act);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_lets_a_failure_before_the_request_is_sent_escape_unchanged()
    {
        var act = () => CreateClient().TrySubmitAndWaitForTransactionAsync(
            Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertEscapesUnchangedWithoutSending(act);
    }

    private async Task AssertEscapesUnchangedWithoutSending<T>(Func<Task<T>> act)
    {
        (await act.Should().ThrowExactlyAsync<FormatException>()).Which.Should().BeSameAs(_tokenFailure);
        _ = _commandService.DidNotReceiveWithAnyArgs()
            .SubmitAndWaitAsync(default!, default(Metadata), default(DateTime?), default);
        _ = _stateService.DidNotReceiveWithAnyArgs()
            .GetLedgerEndAsync(default!, default(Metadata), default(DateTime?), default);
        _ = _interactiveService.DidNotReceiveWithAnyArgs()
            .ExecuteSubmissionAndWaitAsync(default!, default(Metadata), default(DateTime?), default);
    }

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("test-cmd"));

    private static SignedSubmission Signed() =>
        new(
            new PreparedSubmission(
                new Interactive.PreparedTransaction { Metadata = new Interactive.Metadata { TransactionUuid = "uuid-1" } }
                    .ToByteArray(),
                new byte[] { 9, 9 },
                HashingSchemeVersion.V3,
                null,
                null),
            [
                new PartySignatures(
                    Alice,
                    [new LedgerSignature(SignatureFormat.Der, new byte[] { 1, 2, 3 }, "fp-1", SigningAlgorithm.EcDsaSha256)]),
            ],
            "sub-1",
            null,
            null);
}
