// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class StreamDecodeFailureLogPrivacyTests : IDisposable
{
    private const string ContractId = "00poison-that-names-a-party";

    private static readonly RuntimeCommands.SubmitterInfo ActAs = new(new HashSet<Party> { new("party::alice") });

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly CapturingLoggerFactory _logs = new();
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly CommandCompletionService.CommandCompletionServiceClient _completionService;

    public StreamDecodeFailureLogPrivacyTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _completionService = Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(callInvoker);
        _updateService
            .GetUpdates(Arg.Any<GetUpdatesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => StreamOf(new GetUpdatesResponse { Transaction = TransactionWithCreatedEventWithoutArguments() }));
        _completionService
            .CompletionStream(Arg.Any<CompletionStreamRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => StreamOf(new CompletionStreamResponse { Completion = new Com.Daml.Ledger.Api.V2.Completion { Offset = 7L, UpdateId = "u1" } }));
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
        Substitute.ForPartsOf<StateService.StateServiceClient>(Substitute.For<CallInvoker>()),
        new CommandSubmissionService.CommandSubmissionServiceClient(_channel),
        _completionService,
        new StaticTokenProvider("test-token"),
        versionService: null,
        interactiveSubmissionService: null,
        logger: _logs.CreateLogger<LedgerClient>());

    private static Transaction TransactionWithCreatedEventWithoutArguments()
    {
        var transaction = new Transaction { UpdateId = "u-poison", Offset = 5L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event
        {
            Created = new CreatedEvent
            {
                ContractId = ContractId,
                PackageName = "test-package",
                TemplateId = new Com.Daml.Ledger.Api.V2.Identifier { PackageId = "test-pkg", ModuleName = "Sample.Foo", EntityName = "FooBar" },
                Offset = 5L,
            },
        });
        return transaction;
    }

    private static AsyncServerStreamingCall<T> StreamOf<T>(T item) =>
        new(
            new FakeStreamReader<T>([item]),
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

    private Exception? LoggedExceptionOfWarningStarting(string messageStart) =>
        _logs.Records
            .Where(r => r.Level == LogLevel.Warning && r.Message.StartsWith(messageStart, StringComparison.Ordinal))
            .Select(r => r.Exception)
            .Single();

    [Fact]
    public async Task A_template_stream_decode_failure_logs_the_exception_type_without_its_contract_id_by_default()
    {
        await CollectAsync(Client().SubscribeAsync<FooBar>(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        LoggedExceptionOfWarningStarting("Could not decode event at offset 5 on the FooBar stream")!
            .Message.Should().Be("Canton.Ledger.Abstractions.MalformedResponseException");
    }

    [Fact]
    public async Task A_template_stream_decode_failure_logs_the_original_exception_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CollectAsync(Client().SubscribeAsync<FooBar>(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        LoggedExceptionOfWarningStarting("Could not decode event at offset 5 on the FooBar stream")
            .Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Contain($"CreatedEvent for contract '{ContractId}' has no create_arguments");
    }

    [Fact]
    public async Task A_completion_decode_failure_logs_the_exception_type_without_its_message_by_default()
    {
        await CollectAsync(Client().CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        LoggedExceptionOfWarningStarting("Completion stream could not decode the completion at offset 7")!
            .Message.Should().Be("Canton.Ledger.Abstractions.MalformedResponseException");
    }

    [Fact]
    public async Task A_completion_decode_failure_logs_the_original_exception_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CollectAsync(Client().CompletionStreamAsync(ActAs, cancellationToken: TestContext.Current.CancellationToken));

        LoggedExceptionOfWarningStarting("Completion stream could not decode the completion at offset 7")
            .Should().BeOfType<MalformedResponseException>();
    }
}
