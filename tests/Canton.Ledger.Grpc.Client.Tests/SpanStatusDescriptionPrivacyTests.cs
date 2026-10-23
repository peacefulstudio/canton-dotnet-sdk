// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection("LedgerClient global ActivitySource")]
public sealed class SpanStatusDescriptionPrivacyTests : IDisposable
{
    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001" };
    private readonly GrpcChannel _channel;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public SpanStatusDescriptionPrivacyTests() => _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

    public void Dispose() => _channel.Dispose();

    private static string DetailNamingAParty() => $"party alice::1220{Guid.NewGuid():N} is unknown";

    private LedgerClient ClientRejectingEveryCallWith(RpcException rejection)
    {
        var invoker = new RejectingCallInvoker(rejection);
        return new LedgerClient(
            _options,
            _channel,
            new CommandService.CommandServiceClient(invoker),
            new UpdateService.UpdateServiceClient(invoker),
            new StateService.StateServiceClient(invoker),
            tokenProvider: _tokenProvider);
    }

    private static async Task<List<TItem>> CollectAsync<TItem>(IAsyncEnumerable<TItem> source)
    {
        var items = new List<TItem>();
        await foreach (var item in source)
        {
            items.Add(item);
        }
        return items;
    }

    [Fact]
    public async Task A_failed_unary_call_span_reports_only_the_status_code_by_default()
    {
        var detail = DetailNamingAParty();
        var client = ClientRejectingEveryCallWith(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<LedgerOperationException>();

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("Unavailable");
    }

    [Fact]
    public async Task A_failed_unary_call_span_carries_the_status_detail_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        var detail = DetailNamingAParty();
        var client = ClientRejectingEveryCallWith(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<LedgerOperationException>();

        capture.Activities.Should().ContainSingle().Which.StatusDescription.Should().Be(detail);
    }

    [Fact]
    public async Task A_retry_attempt_span_reports_only_the_status_code_by_default()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 1, Delay = TimeSpan.Zero };
        var detail = DetailNamingAParty();
        var client = ClientRejectingEveryCallWith(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<LedgerOperationException>();

        var retrySpan = capture.Activities.Should().ContainSingle(a => a.OperationName == LedgerCallInvoker.RetryAttemptActivityName).Subject;
        retrySpan.StatusDescription.Should().Be("Unavailable");
        retrySpan.GetTagItem(ActivityHelper.RpcGrpcStatusCode).Should().Be(14);
    }

    [Fact]
    public async Task A_retry_attempt_span_carries_the_status_detail_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 1, Delay = TimeSpan.Zero };
        var detail = DetailNamingAParty();
        var client = ClientRejectingEveryCallWith(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<LedgerOperationException>();

        capture.Activities.Should().ContainSingle(a => a.OperationName == LedgerCallInvoker.RetryAttemptActivityName)
            .Which.StatusDescription.Should().Be(detail);
    }

    [Fact]
    public async Task A_stream_fault_span_reports_only_the_status_code_by_default()
    {
        var detail = DetailNamingAParty();
        var client = ClientFaultingTheActiveContractsStreamWith(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var entries = await CollectAsync(client.SubscribeActiveAsync<FooBar>(
            ActAs, LedgerOffset.At(7L), cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().ContainSingle().Which.Should().BeOfType<AcsSnapshotEntry<FooBar>.StreamError>();
        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("Unavailable");
    }

    [Fact]
    public async Task A_stream_fault_span_carries_the_status_detail_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        var detail = DetailNamingAParty();
        var client = ClientFaultingTheActiveContractsStreamWith(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        await CollectAsync(client.SubscribeActiveAsync<FooBar>(
            ActAs, LedgerOffset.At(7L), cancellationToken: TestContext.Current.CancellationToken));

        capture.Activities.Should().ContainSingle().Which.StatusDescription.Should().Be(detail);
    }

    [Fact]
    public async Task A_rejected_exercise_span_reports_only_the_status_code_by_default()
    {
        var detail = DetailNamingAParty();
        var submissionClient = SubmissionClientRejectingWith(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        await submissionClient.TryExerciseAsync<object>(
            Exercise(), new RuntimeCommands.SubmitterInfo(new HashSet<Party> { ActAs }),
            cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("Unavailable");
    }

    [Fact]
    public async Task A_rejected_exercise_span_carries_the_status_detail_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        var detail = DetailNamingAParty();
        var submissionClient = SubmissionClientRejectingWith(new RpcException(new Status(StatusCode.Unavailable, detail)));
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        await submissionClient.TryExerciseAsync<object>(
            Exercise(), new RuntimeCommands.SubmitterInfo(new HashSet<Party> { ActAs }),
            cancellationToken: TestContext.Current.CancellationToken);

        capture.Activities.Should().ContainSingle().Which.StatusDescription.Should().Be(detail);
    }

    private LedgerClient ClientFaultingTheActiveContractsStreamWith(RpcException fault)
    {
        var callInvoker = Substitute.For<CallInvoker>();
        var stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        stateService
            .GetActiveContracts(
                Arg.Any<GetActiveContractsRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncServerStreamingCall<GetActiveContractsResponse>(
                new FakeStreamReader<GetActiveContractsResponse>([], fault),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
        return new LedgerClient(
            _options,
            _channel,
            new CommandService.CommandServiceClient(callInvoker),
            new UpdateService.UpdateServiceClient(_channel),
            stateService,
            tokenProvider: _tokenProvider);
    }

    private SubmissionClient SubmissionClientRejectingWith(RpcException rejection)
    {
        var invoker = new RejectingCallInvoker(rejection);
        return new SubmissionClient(
            new LedgerCallInvoker(_options, _tokenProvider),
            new CommandService.CommandServiceClient(invoker),
            new CommandSubmissionService.CommandSubmissionServiceClient(invoker),
            new GrpcCommandBuilder(_options),
            _options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LedgerClient>.Instance,
            (_, _, _, _) => throw new InvalidOperationException("point read not expected"));
    }

    private static RuntimeCommands.ExerciseCommand Exercise() =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<LedgerClientTests.TestTemplate>("00contract123"),
            new RuntimeCommands.ChoiceName("Archive"),
            DamlUnit.Instance);
}
