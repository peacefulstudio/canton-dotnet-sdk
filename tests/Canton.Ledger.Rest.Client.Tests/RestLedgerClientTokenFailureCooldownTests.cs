// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Commands;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientTokenFailureCooldownTests : IDisposable
{
    private const string IssuedToken = """{"access_token":"issued","expires_in":3600,"token_type":"Bearer"}""";

    private static readonly SubmitterInfo Submitter = new(new Party("party::alice"));

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly RecordingHttpHandler _identityProvider =
        new RecordingHttpHandler().WithResponse(HttpStatusCode.ServiceUnavailable, "{}");
    private readonly RecordingHttpHandler _participant =
        new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"offset":"42"}""");
    private readonly List<IDisposable> _disposables = [];
    private readonly RestLedgerClient _client;

    public RestLedgerClientTokenFailureCooldownTests()
    {
        var provider = new ClientCredentialsProvider(
            Options.Create(new ClientCredentialsOptions
            {
                ClientId = "client",
                ClientSecret = "secret",
                Domain = "https://auth.example.com"
            }),
            new SingleHandlerClientFactory(_identityProvider),
            _time);
        var options = new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            Retry = new RetryOptions { Enabled = true }
        };
        var pipeline = new RestRetryHandler(Options.Create(options))
        {
            InnerHandler = new BearerTokenHandler(provider) { InnerHandler = _participant }
        };
        var factory = new StubHttpClientFactory(pipeline);
        _disposables.AddRange([provider, factory]);
        _client = new RestLedgerClient(factory, Options.Create(options));
    }

    public void Dispose() => _disposables.ForEach(disposable => disposable.Dispose());

    [Fact]
    public async Task SubmitAndWaitAsync_inside_the_cooldown_window_is_reported_never_sent_like_the_failure_that_opened_it_without_calling_the_identity_provider()
    {
        await AssertNeverSent();
        _time.Advance(TimeSpan.FromMilliseconds(499));

        await AssertNeverSent();

        _identityProvider.Requests.Should().HaveCount(1, "Retry does not retry a token failure and the cooldown window spares the identity provider");
        _participant.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLedgerEndAsync_succeeds_on_the_first_call_after_the_cooldown_window_once_the_identity_provider_recovered()
    {
        await AssertNeverSent();
        _identityProvider.WithResponse(HttpStatusCode.OK, IssuedToken);
        _time.Advance(TimeSpan.FromMilliseconds(500));

        var offset = await _client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        offset.Value.Should().Be(42);
        _participant.LastRequest!.Headers.Authorization!.Parameter.Should().Be("issued");
    }

    private async Task AssertNeverSent()
    {
        var act = () => _client.SubmitAndWaitAsync(
            EntryPointInvocations.Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<TokenAcquisitionException>()
            .Which.InnerException.Should().BeOfType<HttpRequestException>()
            .Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private sealed class SingleHandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
