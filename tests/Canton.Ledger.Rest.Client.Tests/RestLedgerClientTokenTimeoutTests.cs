// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientTokenTimeoutTests : IDisposable
{
    private static readonly SubmitterInfo Submitter = new(new Party("party::alice"));
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(200);

    private readonly RecordingHttpHandler _participant = new();
    private readonly StubHttpClientFactory _factory;
    private readonly RestLedgerClient _client;

    public RestLedgerClientTokenTimeoutTests()
    {
        _factory = new StubHttpClientFactory(
            new BearerTokenHandler(new HangingTokenProvider()) { InnerHandler = _participant });
        _client = new RestLedgerClient(
            _factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task SubmitAndWaitAsync_reports_a_token_fetch_outlasting_the_call_timeout_as_a_never_sent_NoResponse()
    {
        var act = () => _client.SubmitAndWaitAsync(
            EntryPointInvocations.Submission(), Submitter, CallTimeout, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<TokenAcquisitionException>();
        _participant.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_reports_a_token_fetch_outlasting_the_call_timeout_as_an_InfraError_carrying_it()
    {
        var outcome = await _client.TrySubmitAndWaitForTransactionAsync(
            EntryPointInvocations.Submission(), Submitter, CallTimeout, TestContext.Current.CancellationToken);

        var failure = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>().Which;
        failure.Status.Should().Be(new TransportStatus.NoResponse());
        failure.SourceException.Should().BeOfType<TokenAcquisitionException>();
        _participant.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAndWaitAsync_keeps_a_caller_cancellation_during_the_token_fetch_as_an_OperationCanceledException()
    {
        using var callerCancellation = new CancellationTokenSource(CallTimeout);

        var act = () => _client.SubmitAndWaitAsync(
            EntryPointInvocations.Submission(), Submitter, TimeSpan.FromMinutes(5), callerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _participant.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CompletionStreamAsync_reports_a_token_fetch_outlasting_the_HttpClient_timeout_as_a_StreamError_carrying_it()
    {
        var participant = new RecordingHttpHandler();
        using var factory = new StubHttpClientFactory(
            new BearerTokenHandler(new HangingTokenProvider()) { InnerHandler = participant }, clientTimeout: CallTimeout);
        var client = new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));

        var events = new List<CompletionStreamEvent>();
        await foreach (var completionEvent in client.CompletionStreamAsync(
            Submitter, LedgerOffset.At(5), TestContext.Current.CancellationToken))
        {
            events.Add(completionEvent);
        }

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.NoResponse());
        error.SourceException.Should().BeOfType<TokenAcquisitionException>();
        participant.Requests.Should().BeEmpty();
    }

    private sealed class HangingTokenProvider : ITokenProvider
    {
        public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "never-issued";
        }
    }
}
