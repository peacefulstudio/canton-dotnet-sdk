// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
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
/// Behavioural parity for every shape of per-call timeout against a participant that never answers.
/// An infinite timeout, or one longer than a timer can wait, applies no practical deadline, so only
/// the caller's cancellation ends the call. Zero and negative timeouts are caller errors, thrown at
/// the call before a task exists. A normal timeout ends the call as a transport failure.
/// </summary>
public sealed class LedgerDegenerateTimeoutParityTests
{
    private const string Grpc = "gRPC";
    private const string Rest = "REST";
    private static readonly TimeSpan CallerCap = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CallerBackstopBehindDeadline = TimeSpan.FromSeconds(30);
    private static readonly Party Owner = new("party::alice");

    public enum Expected
    {
        EndedByCallerCancellation,
        RejectedAsCallerError,
        EndedByDeadline,
    }

    public static TheoryData<string, TimeSpan, Expected> Shapes()
    {
        var rows = new TheoryData<string, TimeSpan, Expected>();
        foreach (var transport in new[] { Grpc, Rest })
        {
            rows.Add(transport, TimeSpan.FromMilliseconds(-1), Expected.EndedByCallerCancellation);
            rows.Add(transport, TimeSpan.Zero, Expected.RejectedAsCallerError);
            rows.Add(transport, TimeSpan.FromMilliseconds(-5), Expected.RejectedAsCallerError);
            rows.Add(transport, TimeSpan.FromDays(60), Expected.EndedByCallerCancellation);
            rows.Add(transport, TimeSpan.MaxValue, Expected.EndedByCallerCancellation);
            rows.Add(transport, TimeSpan.FromMilliseconds(200), Expected.EndedByDeadline);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task A_read_applies_one_rule_to_every_timeout_shape_on_every_transport(
        string transport, TimeSpan timeout, Expected expected)
    {
        using var provider = ProviderFor(transport);
        var client = provider.GetRequiredService<ICantonLedgerClient>();
        using var caller = CallerCancellation(expected);

        var act = () => client.GetLedgerEndAsync(timeout, caller.Token);

        switch (expected)
        {
            case Expected.EndedByCallerCancellation:
                await act.Should().ThrowAsync<OperationCanceledException>();
                break;
            case Expected.RejectedAsCallerError:
                var callWithoutTask = () => { _ = act(); };
                callWithoutTask.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("timeout");
                break;
            case Expected.EndedByDeadline:
                (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.Status.Should().Be(DeadlineStatus(transport));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expected), expected, "Unknown expectation.");
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task A_write_applies_one_rule_to_every_timeout_shape_on_every_transport(
        string transport, TimeSpan timeout, Expected expected)
    {
        using var provider = ProviderFor(transport);
        var client = provider.GetRequiredService<ICantonLedgerClient>();
        using var caller = CallerCancellation(expected);
        var submission = CommandsSubmission
            .Single(CreateCommand.For(new Marker(Owner)))
            .WithActAs(Owner)
            .WithCommandId(new CommandId("degenerate-timeout-parity"));

        var act = () => client.TrySubmitAndWaitForTransactionAsync(submission, Owner, timeout, caller.Token);

        switch (expected)
        {
            case Expected.EndedByCallerCancellation:
                await act.Should().ThrowAsync<OperationCanceledException>();
                break;
            case Expected.RejectedAsCallerError:
                var callWithoutTask = () => { _ = act(); };
                callWithoutTask.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("timeout");
                break;
            case Expected.EndedByDeadline:
                (await act()).Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>()
                    .Which.Status.Should().Be(DeadlineStatus(transport));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expected), expected, "Unknown expectation.");
        }
    }

    private static CancellationTokenSource CallerCancellation(Expected expected)
    {
        var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        caller.CancelAfter(expected == Expected.EndedByDeadline ? CallerBackstopBehindDeadline : CallerCap);
        return caller;
    }

    private static TransportStatus DeadlineStatus(string transport) => transport switch
    {
        Grpc => new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded),
        Rest => new TransportStatus.NoResponse(),
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
    };

    private static ServiceProvider ProviderFor(string transport)
    {
        var services = new ServiceCollection();
        switch (transport)
        {
            case Grpc:
                services.AddLedgerClient(options =>
                {
                    options.GrpcAddress = "http://localhost:1";
                    options.ConfigureChannel = channel => channel.HttpHandler = new HangingEndpoint();
                });
                break;
            case Rest:
                services.AddRestLedgerClient(options => options.HttpAddress = "http://localhost:1");
                services.AddHttpClient(RestClientRegistration.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new HangingEndpoint());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport.");
        }

        return services.BuildServiceProvider();
    }

    private sealed class HangingEndpoint : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("A hanging endpoint never answers.");
        }
    }
}
