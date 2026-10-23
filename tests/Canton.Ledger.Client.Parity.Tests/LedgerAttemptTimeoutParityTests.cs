// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Testing.Helpers;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RestClientRegistration = Canton.Ledger.Rest.Client.ServiceCollectionExtensions;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioural parity for a participant that accepts a request and never answers. With retry
/// enabled, a per-call timeout must bound every attempt on every transport: the client makes one
/// attempt per retry, the call ends only after every attempt has timed out, and the failure is a
/// timeout, never a caller cancellation. Only the transport status differs — gRPC reports its own
/// <c>DeadlineExceeded</c> code, REST reports that no answer arrived.
/// </summary>
/// <remarks>
/// These rows need no participant, so they run in the unit lane. A REST endpoint sees exactly one
/// request per attempt. A gRPC endpoint sees at most one: Grpc.Net.Client 2.84.0
/// (<c>GrpcCall.RunCall</c>) fails an attempt whose deadline has already lapsed before it reaches
/// the HTTP handler, which a loaded machine produces on the first call in a process, so the gRPC
/// rows count attempts from the client's own retry spans and bound the endpoint's count to 1-3.
/// </remarks>
public sealed class LedgerAttemptTimeoutParityTests
{
    private const string Grpc = "gRPC";
    private const string Rest = "REST";
    private const int MaxRetryAttempts = 2;
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Party Owner = new("party::alice");

    public static TheoryData<string> Transports() => new() { Grpc, Rest };

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task A_read_that_the_participant_never_answers_is_attempted_once_per_retry_and_times_out_on_every_transport(
        string transport)
    {
        var endpoint = new HangingEndpoint();
        using var provider = ProviderFor(transport, endpoint);
        var client = provider.GetRequiredService<ICantonLedgerClient>();
        using var capture = ActivityCapture.Of(RetrySourceFor(transport));
        var clock = Stopwatch.StartNew();

        var act = () => client.GetLedgerEndAsync(AttemptTimeout, TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        clock.Stop();
        failure.Status.Should().Be(ExpectedStatus(transport));
        failure.CommitState.Should().Be(CommitState.NotCommitted);
        AssertEveryAttemptWasMade(transport, capture, endpoint);
        AssertEveryAttemptWasGrantedItsOwnTimeout(clock.Elapsed);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task A_write_that_the_participant_never_answers_is_attempted_once_per_retry_and_reported_as_an_infrastructure_error_on_every_transport(
        string transport)
    {
        var endpoint = new HangingEndpoint();
        using var provider = ProviderFor(transport, endpoint);
        var client = provider.GetRequiredService<ICantonLedgerClient>();
        var submission = CommandsSubmission
            .Single(CreateCommand.For(new Marker(Owner)))
            .WithActAs(Owner)
            .WithCommandId(new CommandId("attempt-timeout-parity"));
        using var capture = ActivityCapture.Of(RetrySourceFor(transport));
        var clock = Stopwatch.StartNew();

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, Owner, AttemptTimeout, TestContext.Current.CancellationToken);

        clock.Stop();
        var failure = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>().Subject;
        failure.Status.Should().Be(ExpectedStatus(transport));
        AssertEveryAttemptWasMade(transport, capture, endpoint);
        AssertEveryAttemptWasGrantedItsOwnTimeout(clock.Elapsed);
    }

    [Fact]
    public void A_call_without_a_timeout_falls_back_to_thirty_seconds_on_every_transport()
    {
        new LedgerClientOptions { GrpcAddress = "http://localhost:1" }.Timeout
            .Should().Be(TimeSpan.FromSeconds(30));
        new RestLedgerClientOptions { HttpAddress = "http://localhost:1" }.Timeout
            .Should().Be(TimeSpan.FromSeconds(30));
    }

    private static string RetrySourceFor(string transport) => transport switch
    {
        Grpc => LedgerActivitySourceNames.GrpcLedgerClient,
        Rest => LedgerActivitySourceNames.RestLedgerClient,
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
    };

    private static void AssertEveryAttemptWasMade(string transport, ActivityCapture capture, HangingEndpoint endpoint)
    {
        capture.Activities
            .Count(activity => activity.OperationName.EndsWith(".RetryAttempt", StringComparison.Ordinal))
            .Should().Be(2);
        switch (transport)
        {
            case Grpc:
                endpoint.Attempts.Should().BeInRange(1, 3);
                break;
            case Rest:
                endpoint.Attempts.Should().Be(3);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport.");
        }
    }

    private static void AssertEveryAttemptWasGrantedItsOwnTimeout(TimeSpan elapsed)
    {
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400));
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    private static TransportStatus ExpectedStatus(string transport) => transport switch
    {
        Grpc => new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded),
        Rest => new TransportStatus.NoResponse(),
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
    };

    private static ServiceProvider ProviderFor(string transport, HangingEndpoint endpoint)
    {
        var retry = new RetryOptions { Enabled = true, MaxRetryAttempts = MaxRetryAttempts, Delay = TimeSpan.Zero };
        var services = new ServiceCollection();
        switch (transport)
        {
            case Grpc:
                services.AddLedgerClient(options =>
                {
                    options.GrpcAddress = "http://localhost:1";
                    options.Retry = retry;
                    options.ConfigureChannel = channel => channel.HttpHandler = endpoint;
                });
                break;
            case Rest:
                services.AddRestLedgerClient(options =>
                {
                    options.HttpAddress = "http://localhost:1";
                    options.Retry = retry;
                });
                services.AddHttpClient(RestClientRegistration.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => endpoint);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport.");
        }

        return services.BuildServiceProvider();
    }

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
}
