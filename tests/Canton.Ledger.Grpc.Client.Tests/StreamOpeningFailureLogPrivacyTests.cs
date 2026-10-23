// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
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
using IFoo = Canton.Ledger.Grpc.Client.Tests.LedgerClientSubscribeTests.IFoo;
using FooView = Canton.Ledger.Grpc.Client.Tests.LedgerClientSubscribeTests.FooView;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class StreamOpeningFailureLogPrivacyTests : IDisposable
{
    private const string TokenFailureMessage = "token endpoint refused the client credentials";

    private static readonly RuntimeCommands.SubmitterInfo ActAs = new(new HashSet<Party> { new("party::alice") });
    private static readonly ViewDescriptor<IFoo, FooView> FooViewDescriptor = new();

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly CapturingLoggerFactory _logs = new();

    public StreamOpeningFailureLogPrivacyTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
    }

    public void Dispose()
    {
        _channel.Dispose();
        _logs.Dispose();
    }

    private LedgerClient Client()
    {
        var tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException(TokenFailureMessage)));
        return new LedgerClient(
            _options,
            _channel,
            new CommandService.CommandServiceClient(_channel),
            new UpdateService.UpdateServiceClient(_channel),
            new StateService.StateServiceClient(_channel),
            new CommandSubmissionService.CommandSubmissionServiceClient(_channel),
            new CommandCompletionService.CommandCompletionServiceClient(_channel),
            tokenProvider,
            versionService: null,
            interactiveSubmissionService: null,
            logger: _logs.CreateLogger<LedgerClient>());
    }

    private static async Task Drain<TItem>(IAsyncEnumerable<TItem> source)
    {
        await foreach (var _ in source)
        {
        }
    }

    private string TheWarning() =>
        _logs.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning).Subject.Message;

    [Fact]
    public async Task CompletionStreamAsync_logs_the_token_failure_when_the_stream_cannot_open()
    {
        await Drain(Client().CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().StartWith("Completion stream failed: NoResponse").And.Contain(TokenFailureMessage);
    }

    [Fact]
    public async Task GetCompletionsAsync_logs_the_token_failure_when_the_stream_cannot_open()
    {
        await Drain(Client().GetCompletionsAsync([new Party("party::alice")], cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().StartWith("Completion stream failed: NoResponse").And.Contain(TokenFailureMessage);
    }

    [Fact]
    public async Task SubscribeAsync_logs_the_token_failure_when_the_stream_cannot_open()
    {
        await Drain(Client().SubscribeAsync<FooBar>(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().StartWith("Subscribe stream failed for FooBar: NoResponse").And.Contain(TokenFailureMessage);
    }

    [Fact]
    public async Task SubscribeActiveAsync_logs_the_token_failure_when_the_snapshot_cannot_open()
    {
        await Drain(Client().SubscribeActiveAsync<FooBar>(
            ActAs, LedgerOffset.At(7L), cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().StartWith("Subscribe stream failed for FooBar: NoResponse").And.Contain(TokenFailureMessage);
    }

    [Fact]
    public async Task The_interface_SubscribeAsync_logs_the_token_failure_when_the_stream_cannot_open()
    {
        await Drain(Client().SubscribeAsync(FooViewDescriptor, ActAs, cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().StartWith("Subscribe stream failed for IFoo: NoResponse").And.Contain(TokenFailureMessage);
    }

    [Fact]
    public async Task The_interface_SubscribeActiveAsync_logs_the_token_failure_when_the_snapshot_cannot_open()
    {
        await Drain(Client().SubscribeActiveAsync(
            FooViewDescriptor, ActAs, LedgerOffset.At(7L), cancellationToken: TestContext.Current.CancellationToken));

        TheWarning().Should().StartWith("Subscribe stream failed for IFoo: NoResponse").And.Contain(TokenFailureMessage);
    }
}
