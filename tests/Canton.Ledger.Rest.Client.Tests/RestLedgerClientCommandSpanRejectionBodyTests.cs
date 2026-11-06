// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientCommandSpanRejectionBodyTests : IDisposable
{
    private const string ContractNotFoundBody =
        """{"code":"CONTRACT_NOT_FOUND","cause":"Contract could not be found","context":{"contract_id":"00feed"},"errorCategory":11,"grpcCodeValue":5}""";

    private static readonly Party Alice = new("party::alice");

    private readonly RestLedgerClientOptions _options = new() { HttpAddress = "http://localhost:7575" };
    private readonly List<StubHttpClientFactory> _factories = [];
    private readonly List<ScriptedRejectionServer> _servers = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }

        foreach (var server in _servers)
        {
            server.Dispose();
        }
    }

    private RestLedgerClient ClientOver(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        _factories.Add(factory);
        return new RestLedgerClient(factory, Options.Create(_options));
    }

    private RestLedgerClient ClientOver(Func<Stream, CancellationToken, Task> script)
    {
        var server = new ScriptedRejectionServer(script);
        _servers.Add(server);
        return ClientOver(server.CreateHandler());
    }

    private static RuntimeCommands.CommandsSubmission ArchiveSubmission() =>
        new RuntimeCommands.CommandsSubmission(
            [
                new RuntimeCommands.ExerciseCommand(
                    new RuntimeIdentifier("pkg", "Module", "Template"),
                    new ContractId<OwnedTemplate>("00abc"),
                    new RuntimeCommands.ChoiceName("Archive"),
                    DamlUnit.Instance),
            ])
            .WithActAs(Alice);

    private static ActivityCapture? CaptureWhen(bool listenerOn) =>
        listenerOn ? ActivityCapture.Of(LedgerActivitySourceNames.RestLedgerClient) : null;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAndWaitAsync_surfaces_a_chunked_rejection_over_the_cap_the_same_with_and_without_a_span_listener(
        bool listenerOn)
    {
        _options.MaxResponseBodyBytes = 10;
        using var capture = CaptureWhen(listenerOn);
        var client = ClientOver(ScriptedRejectionServer.ChunkedBody(
            HttpStatusCode.NotFound, ContractNotFoundBody[..60], ContractNotFoundBody[60..]));

        var act = () => client.SubmitAndWaitAsync(ArchiveSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.NotFound));
        thrown.ErrorId.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAndWaitAsync_surfaces_a_chunked_rejection_within_the_cap_the_same_with_and_without_a_span_listener(
        bool listenerOn)
    {
        using var capture = CaptureWhen(listenerOn);
        var client = ClientOver(ScriptedRejectionServer.ChunkedBody(
            HttpStatusCode.NotFound, ContractNotFoundBody[..60], ContractNotFoundBody[60..]));

        var act = () => client.SubmitAndWaitAsync(ArchiveSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>()).Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAndWaitAsync_surfaces_a_rejection_body_cut_off_mid_stream_the_same_with_and_without_a_span_listener(
        bool listenerOn)
    {
        using var capture = CaptureWhen(listenerOn);
        var client = ClientOver(ScriptedRejectionServer.CutOffBody(
            HttpStatusCode.NotFound, ContractNotFoundBody[..20], ContractNotFoundBody.Length));

        var act = () => client.SubmitAndWaitAsync(ArchiveSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.InnerException.Should().BeAssignableTo<HttpRequestException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAndWaitAsync_stays_cancellable_while_the_rejection_body_stalls_with_and_without_a_span_listener(
        bool listenerOn)
    {
        using var capture = CaptureWhen(listenerOn);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var partialBodySent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = ClientOver(ScriptedRejectionServer.CutOffBody(
            HttpStatusCode.NotFound,
            ContractNotFoundBody[..20],
            ContractNotFoundBody.Length,
            async token =>
            {
                partialBodySent.SetResult();
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }));

        var act = () => client.SubmitAndWaitAsync(ArchiveSubmission(), cancellationToken: caller.Token);
        var call = act();
        await partialBodySent.Task.WaitAsync(TestContext.Current.CancellationToken);
        await caller.CancelAsync();

        await call.Invoking(pending => pending).Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAndWaitAsync_surfaces_the_rejection_when_the_response_cap_exceeds_the_buffering_limit(
        bool listenerOn)
    {
        _options.MaxResponseBodyBytes = 3_000_000_000;
        using var capture = CaptureWhen(listenerOn);
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.NotFound, ContractNotFoundBody);
        var client = ClientOver(new RestActivityHandler { InnerHandler = transport });

        var act = () => client.SubmitAndWaitAsync(ArchiveSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>()).Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        if (capture is not null)
        {
            capture.Activities.Should().ContainSingle().Subject
                .GetTagItem("daml.error_id").Should().Be("CONTRACT_NOT_FOUND");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAndWaitAsync_reads_a_rejection_body_under_its_own_window_past_the_attempt_deadline_with_and_without_a_span_listener(
        bool listenerOn)
    {
        using var capture = CaptureWhen(listenerOn);
        var client = ClientOver(ScriptedRejectionServer.StagedBody(
            HttpStatusCode.NotFound,
            ContractNotFoundBody.Length,
            (TimeSpan.FromMilliseconds(1500), ContractNotFoundBody[..20]),
            (TimeSpan.Zero, ContractNotFoundBody[20..])));

        var act = () => client.SubmitAndWaitAsync(
            ArchiveSubmission(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.NotFound));
        if (capture is not null)
        {
            capture.Activities.Should().ContainSingle().Subject
                .GetTagItem("daml.error_id").Should().Be("CONTRACT_NOT_FOUND");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAndWaitAsync_reports_no_answer_when_a_rejection_body_outlasts_its_own_window_with_and_without_a_span_listener(
        bool listenerOn)
    {
        using var capture = CaptureWhen(listenerOn);
        var client = ClientOver(ScriptedRejectionServer.StagedBody(
            HttpStatusCode.NotFound,
            ContractNotFoundBody.Length,
            (TimeSpan.FromMilliseconds(1500), ContractNotFoundBody[..20]),
            (TimeSpan.FromSeconds(30), ContractNotFoundBody[20..])));

        var act = () => client.SubmitAndWaitAsync(
            ArchiveSubmission(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.ErrorId.Should().BeNull();
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.Message.Should().Contain("deadline while reading the response body");
    }
}
