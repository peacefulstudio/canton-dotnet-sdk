// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Xunit;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerCallInvokerTokenFailureCooldownTests : IDisposable
{
    private const string IssuedToken = """{"access_token":"issued","expires_in":3600,"token_type":"Bearer"}""";

    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly ScriptedIdentityProvider _identityProvider = new();
    private readonly ClientCredentialsProvider _tokenProvider;
    private readonly LedgerCallInvoker _invoker;
    private int _sentCalls;

    public LedgerCallInvokerTokenFailureCooldownTests()
    {
        _tokenProvider = new ClientCredentialsProvider(
            Options.Create(new ClientCredentialsOptions
            {
                ClientId = "client",
                ClientSecret = "secret",
                Domain = "https://auth.example.com"
            }),
            new SingleHandlerClientFactory(_identityProvider),
            _time);
        _invoker = new LedgerCallInvoker(
            new LedgerClientOptions
            {
                GrpcAddress = "https://participant.example:6001",
                Retry = new RetryOptions { Enabled = true }
            },
            _tokenProvider);
    }

    public void Dispose() => _tokenProvider.Dispose();

    [Fact]
    public async Task InvokeAsync_inside_the_cooldown_window_fails_never_sent_like_the_failure_that_opened_it_without_calling_the_identity_provider()
    {
        await AssertNeverSentTokenFailure();
        _time.Advance(TimeSpan.FromMilliseconds(499));

        await AssertNeverSentTokenFailure();

        _identityProvider.Requests.Should().Be(1, "Retry does not retry a token failure and the cooldown window spares the identity provider");
        _sentCalls.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_succeeds_on_the_first_call_after_the_cooldown_window_once_the_identity_provider_recovered()
    {
        await AssertNeverSentTokenFailure();
        _identityProvider.Recover();
        _time.Advance(TimeSpan.FromMilliseconds(500));

        var response = await Invoke();

        response.Offset.Should().Be(7L);
        _sentCalls.Should().Be(1);
    }

    private async Task AssertNeverSentTokenFailure()
    {
        var act = () => Invoke().AsTask();

        var failure = (await act.Should().ThrowExactlyAsync<TokenAcquisitionException>()).Which;
        failure.InnerException.Should().BeOfType<HttpRequestException>()
            .Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private ValueTask<GetLedgerEndResponse> Invoke() =>
        _invoker.InvokeAsync(
            (_, _, _) =>
            {
                _sentCalls++;
                return new AsyncUnaryCall<GetLedgerEndResponse>(
                    Task.FromResult(new GetLedgerEndResponse { Offset = 7L }),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            },
            TestContext.Current.CancellationToken);

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class ScriptedIdentityProvider : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.ServiceUnavailable;
        private string _body = "{}";
        private int _requests;

        public int Requests => _requests;

        public void Recover()
        {
            _status = HttpStatusCode.OK;
            _body = IssuedToken;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SingleHandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
