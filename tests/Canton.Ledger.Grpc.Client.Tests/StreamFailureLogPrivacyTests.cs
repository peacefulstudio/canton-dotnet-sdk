// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using Status = Grpc.Core.Status;
using IFoo = Canton.Ledger.Grpc.Client.Tests.LedgerClientSubscribeTests.IFoo;
using FooView = Canton.Ledger.Grpc.Client.Tests.LedgerClientSubscribeTests.FooView;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class StreamFailureLogPrivacyTests : IDisposable
{
    private static readonly RuntimeCommands.SubmitterInfo ActAs = new(new HashSet<Party> { new("party::alice") });

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly CapturingLoggerFactory _logs = new();
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly CommandCompletionService.CommandCompletionServiceClient _completionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");
    private readonly string _detail = $"party alice::1220{Guid.NewGuid():N} is unknown";

    public StreamFailureLogPrivacyTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _completionService = Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(callInvoker);
        var fault = new RpcException(new Status(StatusCode.Unavailable, _detail));
        _updateService
            .GetUpdates(Arg.Any<GetUpdatesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => FaultingCall<GetUpdatesResponse>(fault));
        _stateService
            .GetActiveContracts(Arg.Any<GetActiveContractsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => FaultingCall<GetActiveContractsResponse>(fault));
        _completionService
            .CompletionStream(Arg.Any<CompletionStreamRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => FaultingCall<CompletionStreamResponse>(fault));
    }

    public void Dispose()
    {
        _channel.Dispose();
        _logs.Dispose();
    }

    private LedgerClient Client() => new(
        _options,
        _channel,
        new CommandService.CommandServiceClient(_channel),
        _updateService,
        _stateService,
        new CommandSubmissionService.CommandSubmissionServiceClient(_channel),
        _completionService,
        _tokenProvider,
        versionService: null,
        interactiveSubmissionService: null,
        logger: _logs.CreateLogger<LedgerClient>());

    private static AsyncServerStreamingCall<T> FaultingCall<T>(RpcException fault) =>
        new(
            new FakeStreamReader<T>([], fault),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static async Task<List<TItem>> CollectAsync<TItem>(IAsyncEnumerable<TItem> source)
    {
        var items = new List<TItem>();
        await foreach (var item in source)
        {
            items.Add(item);
        }
        return items;
    }

    private string TheWarning() =>
        _logs.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning).Subject.Message;

    private static readonly ViewDescriptor<IFoo, FooView> FooViewDescriptor = new();

    [Fact]
    public async Task CompletionStreamAsync_logs_only_the_status_code_by_default()
    {
        var events = await CollectAsync(Client().CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().Be("Completion stream failed: Grpc { StatusCode = Unavailable } Unavailable");
        events.Should().ContainSingle().Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Which.Message.Should().Be(_detail);
    }

    [Fact]
    public async Task CompletionStreamAsync_logs_the_status_detail_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CollectAsync(Client().CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().Be($"Completion stream failed: Grpc {{ StatusCode = Unavailable }} {_detail}");
    }

    [Fact]
    public async Task SubscribeAsync_logs_only_the_status_code_by_default()
    {
        var events = await CollectAsync(Client().SubscribeAsync<FooBar>(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().Be("Subscribe stream failed for FooBar: Grpc { StatusCode = Unavailable } Unavailable");
        events.Should().ContainSingle().Which.Should().BeOfType<ContractStreamEvent<FooBar>.StreamError>()
            .Which.Message.Should().Be(_detail);
    }

    [Fact]
    public async Task SubscribeActiveAsync_logs_only_the_status_code_by_default()
    {
        await CollectAsync(Client().SubscribeActiveAsync<FooBar>(
            ActAs, LedgerOffset.At(7L), cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().Be("Subscribe stream failed for FooBar: Grpc { StatusCode = Unavailable } Unavailable");
    }

    [Fact]
    public async Task SubscribeActiveAsync_logs_the_status_detail_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CollectAsync(Client().SubscribeActiveAsync<FooBar>(
            ActAs, LedgerOffset.At(7L), cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().Be($"Subscribe stream failed for FooBar: Grpc {{ StatusCode = Unavailable }} {_detail}");
    }

    [Fact]
    public async Task The_interface_SubscribeAsync_logs_only_the_status_code_by_default()
    {
        await CollectAsync(Client().SubscribeAsync(FooViewDescriptor, ActAs, cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().Be("Subscribe stream failed for IFoo: Grpc { StatusCode = Unavailable } Unavailable");
    }

    [Fact]
    public async Task The_interface_SubscribeActiveAsync_logs_only_the_status_code_by_default()
    {
        await CollectAsync(Client().SubscribeActiveAsync(FooViewDescriptor, ActAs, LedgerOffset.At(7L), cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().Be("Subscribe stream failed for IFoo: Grpc { StatusCode = Unavailable } Unavailable");
    }
}
