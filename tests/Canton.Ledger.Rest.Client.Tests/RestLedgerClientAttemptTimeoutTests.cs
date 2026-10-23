// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Resilience;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RestClientRegistration = Canton.Ledger.Rest.Client.ServiceCollectionExtensions;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientAttemptTimeoutTests
{
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(200);

    private sealed class HangingEndpoint : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("A hanging endpoint never answers.");
        }
    }

    private static ServiceProvider ClientOver(
        HangingEndpoint endpoint, int maxRetryAttempts, TimeSpan? clientTimeout = null)
    {
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options =>
        {
            options.HttpAddress = "http://localhost:7575";
            options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = maxRetryAttempts, Delay = TimeSpan.Zero };
            if (clientTimeout is { } timeout)
                options.Timeout = timeout;
        });
        services.AddHttpClient(RestClientRegistration.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => endpoint);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetLedgerEndAsync_grants_every_retry_attempt_its_own_timeout_against_a_hanging_endpoint()
    {
        var endpoint = new HangingEndpoint();
        using var provider = ClientOver(endpoint, maxRetryAttempts: 2);
        var client = provider.GetRequiredService<ICantonLedgerClient>();
        var clock = Stopwatch.StartNew();

        var act = () => client.GetLedgerEndAsync(AttemptTimeout, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        clock.Stop();
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        endpoint.Attempts.Should().Be(3);
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400));
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetLedgerEndAsync_without_a_call_timeout_grants_every_attempt_the_client_wide_Timeout()
    {
        var endpoint = new HangingEndpoint();
        using var provider = ClientOver(endpoint, maxRetryAttempts: 1, clientTimeout: AttemptTimeout);
        var client = provider.GetRequiredService<ICantonLedgerClient>();
        var clock = Stopwatch.StartNew();

        var act = () => client.GetLedgerEndAsync(timeout: null, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        clock.Stop();
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        endpoint.Attempts.Should().Be(2);
        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(200));
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetLedgerEndAsync_keeps_a_caller_cancellation_distinct_from_a_timeout_and_does_not_retry_it()
    {
        var endpoint = new HangingEndpoint();
        using var provider = ClientOver(endpoint, maxRetryAttempts: 2);
        var client = provider.GetRequiredService<ICantonLedgerClient>();
        using var caller = new CancellationTokenSource(AttemptTimeout);

        var act = () => client.GetLedgerEndAsync(TimeSpan.FromMinutes(5), caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        endpoint.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task GetLedgerEndAsync_retries_a_connect_that_exceeds_the_ConnectTimeout_of_a_host_supplied_SocketsHttpHandler()
    {
        var connects = 0;
        using var primary = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(100),
            ConnectCallback = async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref connects);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("A hanging connect never completes.");
            }
        };
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options =>
        {
            options.HttpAddress = "http://localhost:7575";
            options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        });
        services.AddHttpClient(RestClientRegistration.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => primary);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ICantonLedgerClient>();

        var act = () => client.GetLedgerEndAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        Volatile.Read(ref connects).Should().Be(3);
    }
}
