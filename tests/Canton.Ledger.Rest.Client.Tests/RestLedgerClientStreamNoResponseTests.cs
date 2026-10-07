// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientStreamNoResponseTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");
    private static readonly HttpRequestException ConnectionRefused = new("connection refused");

    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private sealed record TestTemplate : ITemplate, IDamlRecord<TestTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "NoResponseTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);

        public static TestTemplate FromRecord(DamlRecord record) => new();
    }

    private sealed class ScriptedTransport(params object[] script) : HttpMessageHandler
    {
        public sealed record Answer(HttpStatusCode StatusCode, string Body);

        public sealed record Hang;

        private readonly TaskCompletionSource _hanging = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestsSeen;

        public int RequestsSeen => _requestsSeen;

        public Task Hanging => _hanging.Task;

        public static Answer Ok(string body) => new(HttpStatusCode.OK, body);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = script[Math.Min(_requestsSeen, script.Length - 1)];
            _requestsSeen++;
            switch (step)
            {
                case Exception failure:
                    throw failure;
                case Hang:
                    _hanging.TrySetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    throw new InvalidOperationException("unreachable");
                case Answer answer:
                    return new HttpResponseMessage(answer.StatusCode)
                    {
                        Content = new StringContent(answer.Body, Encoding.UTF8, "application/json"),
                        RequestMessage = request,
                    };
                default:
                    throw new InvalidOperationException($"Unscripted step {step}");
            }
        }
    }

    private sealed class TimingOutHttpClientFactory(HttpMessageHandler handler, TimeSpan timeout) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:7575"), Timeout = timeout };
    }

    private RestLedgerClient ClientOver(ScriptedTransport transport, TimeSpan? httpClientTimeout = null)
    {
        _disposables.Add(transport);
        IHttpClientFactory factory;
        if (httpClientTimeout is { } timeout)
        {
            factory = new TimingOutHttpClientFactory(transport, timeout);
        }
        else
        {
            var stub = new StubHttpClientFactory(transport);
            _disposables.Add(stub);
            factory = stub;
        }

        return new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }

    private const string OffsetPlaceholder = "OFFSET";

    private const string TransactionEntry =
        """{"update": {"Transaction": {"value": {"offset": "OFFSET", "synchronizerId": "sync-1", "events": [{"CreatedEvent": {"offset": "OFFSET", "contractId": "00holding-OFFSET", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "NoResponseTemplate"}, "createArgument": {}, "witnessParties": ["party::alice"]}}]}}}}""";

    private const string CompletionCheckpointEntry =
        """{"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "OFFSET"}}}}""";

    private const string ActiveContractEntry =
        """{"contractEntry": {"JsActiveContract": {"createdEvent": {"offset": "3", "contractId": "OFFSET", "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "NoResponseTemplate"}, "createArgument": {}, "witnessParties": ["party::alice"]}, "synchronizerId": "sync-1"}}}""";

    private const string InterfaceActivePage =
        """
        {"activeContracts": [{
          "contractEntry": {
            "JsActiveContract": {
              "createdEvent": {
                "offset": "9",
                "contractId": "00impl",
                "templateId": {"packageId": "impl-pkg", "moduleName": "Token.Impl", "entityName": "Asset"},
                "createArgument": {"amount": "999"},
                "interfaceViews": [{
                  "interfaceId": {"packageId": "viewed-pkg", "moduleName": "Token.Api", "entityName": "IViewedHolding"},
                  "viewStatus": {"code": 0, "message": ""},
                  "viewValue": {"amount": "42.5"}
                }],
                "witnessParties": ["party::alice"]
              },
              "synchronizerId": "sync-1",
              "reassignmentCounter": "0"
            }
          }
        }], "nextPageToken": "page-2"}
        """;

    private static string Window(string entry, string offset) =>
        "[" + entry.Replace(OffsetPlaceholder, offset, StringComparison.Ordinal) + "]";

    private static string TransactionWindow(string offset) => Window(TransactionEntry, offset);

    private static string CompletionCheckpointWindow(string offset) => Window(CompletionCheckpointEntry, offset);

    private static string ActiveContractsPage(string? nextPageToken, string contractId)
    {
        var nextPageTokenField = nextPageToken is null ? "" : $", \"nextPageToken\": \"{nextPageToken}\"";
        return "{\"activeAtOffset\": 9, \"activeContracts\": " +
            Window(ActiveContractEntry, contractId) + nextPageTokenField + "}";
    }

    private static async Task<List<TEvent>> DrainAsync<TEvent>(IAsyncEnumerable<TEvent> stream)
    {
        var events = new List<TEvent>();
        await foreach (var streamEvent in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static readonly ViewDescriptor<IViewedInterfaceMarker, ViewedInterfaceView> ViewedInterface = new();

    private static readonly RuntimeCommands.SubmitterInfo AliceSubmitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    [Fact]
    public async Task SubscribeAsync_ends_with_StreamError_NoResponse_when_the_first_window_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ConnectionRefused);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<ContractStreamEvent<TestTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.Message.Should().Be("connection refused");
        error.SourceException.Should().BeSameAs(ConnectionRefused);
        transport.RequestsSeen.Should().Be(1);
    }

    [Fact]
    public async Task SubscribeAsync_delivers_the_first_window_then_ends_with_StreamError_NoResponse_when_the_next_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ScriptedTransport.Ok(TransactionWindow("11")), ConnectionRefused);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TestTemplate>.Created>()
            .Which.ContractId.Value.Should().Be("00holding-11");
        var error = events[1].Should().BeOfType<ContractStreamEvent<TestTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
        transport.RequestsSeen.Should().Be(2);
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_ends_with_StreamError_NoResponse_when_the_first_window_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ConnectionRefused);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.SubscribeLedgerEffectsAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<ContractStreamEvent<TestTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_delivers_the_first_window_then_ends_with_StreamError_NoResponse_when_the_next_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ScriptedTransport.Ok(TransactionWindow("11")), ConnectionRefused);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.SubscribeLedgerEffectsAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TestTemplate>.Created>();
        var error = events[1].Should().BeOfType<ContractStreamEvent<TestTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
    }

    [Fact]
    public async Task CompletionStreamAsync_delivers_the_first_window_then_ends_with_StreamError_NoResponse_when_the_next_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(
            ScriptedTransport.Ok(CompletionCheckpointWindow("11")), ConnectionRefused);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.CompletionStreamAsync(
            AliceSubmitter, LedgerOffset.At(5), TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<CompletionStreamEvent.Checkpoint>();
        var error = events[1].Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
        transport.RequestsSeen.Should().Be(2);
    }

    [Fact]
    public async Task GetCompletionsAsync_ends_with_StreamError_NoResponse_when_the_first_window_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ConnectionRefused);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.GetCompletionsAsync(
            [Alice], LedgerOffset.At(5), TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
    }

    [Fact]
    public async Task SubscribeActiveAsync_yields_the_first_page_then_StreamError_NoResponse_and_no_checkpoint_when_the_second_page_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(
            ScriptedTransport.Ok(ActiveContractsPage("page-2", "00a")), ConnectionRefused);
        var client = ClientOver(transport);

        var entries = await DrainAsync(client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(9), cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().HaveCount(2);
        entries[0].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.Created>()
            .Which.ContractId.Value.Should().Be("00a");
        var error = entries[1].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
        entries.OfType<AcsSnapshotEntry<TestTemplate>.Checkpoint>().Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeActiveAsync_ends_with_StreamError_NoResponse_and_no_checkpoint_when_the_first_page_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ConnectionRefused);
        var client = ClientOver(transport);

        var entries = await DrainAsync(client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(9), cancellationToken: TestContext.Current.CancellationToken));

        var error = entries.Should().ContainSingle()
            .Which.Should().BeOfType<AcsSnapshotEntry<TestTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
    }

    [Fact]
    public async Task Interface_SubscribeAsync_delivers_the_first_window_then_ends_with_StreamError_NoResponse_when_the_next_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ScriptedTransport.Ok(TransactionWindow("11")), ConnectionRefused);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.SubscribeAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ViewedInterface, Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        var error = events[^1].Should()
            .BeOfType<InterfaceStreamEvent<IViewedInterfaceMarker, ViewedInterfaceView>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
    }

    [Fact]
    public async Task Interface_SubscribeLedgerEffectsAsync_ends_with_StreamError_NoResponse_when_the_first_window_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ConnectionRefused);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.SubscribeLedgerEffectsAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ViewedInterface, Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle().Which.Should()
            .BeOfType<InterfaceStreamEvent<IViewedInterfaceMarker, ViewedInterfaceView>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
    }

    [Fact]
    public async Task Interface_SubscribeActiveAsync_yields_the_first_page_then_StreamError_NoResponse_and_no_checkpoint_when_the_second_page_never_reaches_the_participant()
    {
        var transport = new ScriptedTransport(ScriptedTransport.Ok(InterfaceActivePage), ConnectionRefused);
        var client = ClientOver(transport);

        var entries = await DrainAsync(client.SubscribeActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ViewedInterface,
            Alice,
            LedgerOffset.At(9),
            cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().HaveCount(2);
        entries[0].Should().BeOfType<InterfaceAcsSnapshotEntry<IViewedInterfaceMarker, ViewedInterfaceView>.Created>()
            .Which.ContractId.Value.Should().Be("00impl");
        var error = entries[1].Should()
            .BeOfType<InterfaceAcsSnapshotEntry<IViewedInterfaceMarker, ViewedInterfaceView>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(ConnectionRefused);
        entries.OfType<InterfaceAcsSnapshotEntry<IViewedInterfaceMarker, ViewedInterfaceView>.Checkpoint>()
            .Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeActiveAsync_without_an_active_at_offset_throws_NoResponse_as_NotCommitted_when_the_ledger_end_cannot_be_resolved()
    {
        var client = ClientOver(new ScriptedTransport(ConnectionRefused));

        var act = () => DrainAsync(client.SubscribeActiveAsync<TestTemplate>(
            Alice, cancellationToken: TestContext.Current.CancellationToken));

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.Which.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Which.InnerException.Should().BeSameAs(ConnectionRefused);
    }

    [Fact]
    public async Task Interface_SubscribeActiveAsync_without_an_active_at_offset_throws_NoResponse_as_NotCommitted_when_the_ledger_end_cannot_be_resolved()
    {
        var client = ClientOver(new ScriptedTransport(ConnectionRefused));

        var act = () => DrainAsync(client.SubscribeActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ViewedInterface, Alice, cancellationToken: TestContext.Current.CancellationToken));

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.Which.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Which.InnerException.Should().BeSameAs(ConnectionRefused);
    }

    [Fact]
    public async Task SubscribeAsync_ends_with_StreamError_NoResponse_when_the_HttpClient_times_out_on_the_first_window()
    {
        var transport = new ScriptedTransport(new ScriptedTransport.Hang());
        var client = ClientOver(transport, httpClientTimeout: TimeSpan.FromMilliseconds(100));

        var events = await DrainAsync(client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<ContractStreamEvent<TestTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.Message.Should().Be("The stream window request timed out before the participant answered.");
        error.SourceException.Should().BeOfType<TaskCanceledException>()
            .Which.InnerException.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task CompletionStreamAsync_delivers_the_first_window_then_ends_with_StreamError_NoResponse_when_the_HttpClient_times_out_on_the_next()
    {
        var transport = new ScriptedTransport(
            ScriptedTransport.Ok(CompletionCheckpointWindow("11")), new ScriptedTransport.Hang());
        var client = ClientOver(transport, httpClientTimeout: TimeSpan.FromMilliseconds(100));

        var events = await DrainAsync(client.CompletionStreamAsync(
            AliceSubmitter, LedgerOffset.At(5), TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        var error = events[1].Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_ends_with_StreamError_NoResponse_when_the_HttpClient_times_out_on_the_second_page()
    {
        var transport = new ScriptedTransport(
            ScriptedTransport.Ok(ActiveContractsPage("page-2", "00a")), new ScriptedTransport.Hang());
        var client = ClientOver(transport, httpClientTimeout: TimeSpan.FromMilliseconds(100));

        var entries = await DrainAsync(client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(9), cancellationToken: TestContext.Current.CancellationToken));

        entries.Should().HaveCount(2);
        entries[1].Should().BeOfType<AcsSnapshotEntry<TestTemplate>.StreamError>()
            .Which.Status.Should().Be(new TransportStatus.NoResponse());
    }

    [Fact]
    public async Task SubscribeAsync_ends_with_StreamError_NoResponse_when_a_handler_times_out_with_a_bare_TimeoutException()
    {
        var tokenTimedOut = new TimeoutException("Token acquisition timed out");
        var transport = new ScriptedTransport(tokenTimedOut);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<ContractStreamEvent<TestTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(tokenTimedOut);
    }

    [Fact]
    public async Task CompletionStreamAsync_delivers_the_first_window_then_ends_with_StreamError_NoResponse_when_a_handler_times_out_with_a_bare_TimeoutException()
    {
        var tokenTimedOut = new TimeoutException("Token acquisition timed out");
        var transport = new ScriptedTransport(ScriptedTransport.Ok(CompletionCheckpointWindow("11")), tokenTimedOut);
        var client = ClientOver(transport);

        var events = await DrainAsync(client.CompletionStreamAsync(
            AliceSubmitter, LedgerOffset.At(5), TestContext.Current.CancellationToken));

        events.Should().HaveCount(2);
        var error = events[1].Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeSameAs(tokenTimedOut);
    }

    [Fact]
    public async Task SubscribeAsync_throws_OperationCanceledException_when_the_caller_cancels_a_window_in_flight()
    {
        var transport = new ScriptedTransport(new ScriptedTransport.Hang());
        var client = ClientOver(transport);
        using var cancellation = new CancellationTokenSource();

        var drain = DrainAsync(client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, cancellation.Token));
        await transport.Hanging;
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => drain).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CompletionStreamAsync_throws_OperationCanceledException_when_the_caller_cancels_a_window_in_flight()
    {
        var transport = new ScriptedTransport(new ScriptedTransport.Hang());
        var client = ClientOver(transport);
        using var cancellation = new CancellationTokenSource();

        var drain = DrainAsync(client.CompletionStreamAsync(AliceSubmitter, LedgerOffset.At(5), cancellation.Token));
        await transport.Hanging;
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => drain).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_throws_OperationCanceledException_when_the_caller_cancels_a_page_in_flight()
    {
        var transport = new ScriptedTransport(new ScriptedTransport.Hang());
        var client = ClientOver(transport);
        using var cancellation = new CancellationTokenSource();

        var drain = DrainAsync(client.SubscribeActiveAsync<TestTemplate>(
            Alice, LedgerOffset.At(9), cancellationToken: cancellation.Token));
        await transport.Hanging;
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => drain).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SubscribeAsync_surfaces_an_exception_that_is_not_a_transport_failure_unchanged()
    {
        var transport = new ScriptedTransport(new InvalidOperationException("handler bug"));
        var client = ClientOver(transport);

        var drain = DrainAsync(client.SubscribeAsync<TestTemplate>(
            Alice, LedgerOffset.At(5), toOffset: null, TestContext.Current.CancellationToken));

        (await FluentActions.Awaiting(() => drain).Should().ThrowAsync<InvalidOperationException>()).WithMessage("handler bug");
    }
}
