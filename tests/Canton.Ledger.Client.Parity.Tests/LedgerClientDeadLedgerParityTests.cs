// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioural parity for a ledger that never answers. A read against an address nothing listens
/// on must raise the same <see cref="LedgerOperationException"/> on every transport: the error
/// category and the <see cref="CommitState"/> agree, and only the transport status differs — gRPC
/// reports its own <c>Unavailable</c> code, REST reports that no answer arrived.
/// </summary>
/// <remarks>
/// These rows need no participant, so they run in the unit lane.
/// </remarks>
public sealed class LedgerClientDeadLedgerParityTests
{
    private const string Grpc = "gRPC";
    private const string Rest = "REST";
    private const string DeadAddress = "http://127.0.0.1:1";

    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(new HashSet<Party> { new("party::alice") }, new HashSet<Party>());

    public static TheoryData<string, string> DeadLedgerReads()
    {
        var data = new TheoryData<string, string>();
        foreach (var read in new[]
                 {
                     nameof(ICantonLedgerClient.GetLedgerEndAsync),
                     nameof(ICantonLedgerClient.GetLedgerApiVersionAsync),
                     nameof(ICantonLedgerClient.GetConnectedSynchronizersAsync),
                     nameof(ICantonLedgerClient.GetUpdateByIdAsync),
                 })
        {
            data.Add(Grpc, read);
            data.Add(Rest, read);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DeadLedgerReads))]
    public async Task A_read_against_a_dead_ledger_is_a_NotCommitted_LedgerOperationException_on_every_transport(
        string transport,
        string read)
    {
        using var scope = ClientScope.For(transport);

        var act = () => Invoke(scope.Client, read);

        var failure = (await act.Should().ThrowAsync<LedgerOperationException>(
                "{0} {1} must surface the neutral failure type, not a raw transport exception", transport, read))
            .Which;
        failure.CommitState.Should().Be(CommitState.NotCommitted);
        failure.Status.Should().Be(ExpectedStatus(transport));
    }

    private static TransportStatus ExpectedStatus(string transport) => transport switch
    {
        Grpc => new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
        Rest => new TransportStatus.NoResponse(),
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
    };

    private static Task Invoke(ICantonLedgerClient client, string read) => read switch
    {
        nameof(ICantonLedgerClient.GetLedgerEndAsync) => client.GetLedgerEndAsync(),
        nameof(ICantonLedgerClient.GetLedgerApiVersionAsync) => client.GetLedgerApiVersionAsync(),
        nameof(ICantonLedgerClient.GetConnectedSynchronizersAsync) => client.GetConnectedSynchronizersAsync(),
        nameof(ICantonLedgerClient.GetUpdateByIdAsync) => client.GetUpdateByIdAsync("update-1", Submitter),
        _ => throw new ArgumentOutOfRangeException(nameof(read), read, "Unknown read."),
    };

    private sealed class ClientScope(ICantonLedgerClient client, ServiceProvider services) : IDisposable
    {
        public ICantonLedgerClient Client { get; } = client;

        public static ClientScope For(string transport)
        {
            var services = transport switch
            {
                Grpc => new ServiceCollection()
                    .AddLedgerClient(options => options.GrpcAddress = DeadAddress)
                    .BuildServiceProvider(),
                Rest => new ServiceCollection()
                    .AddRestLedgerClient(options => options.HttpAddress = DeadAddress)
                    .BuildServiceProvider(),
                _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
            };
            return new ClientScope(services.GetRequiredService<ICantonLedgerClient>(), services);
        }

        public void Dispose() => services.Dispose();
    }
}
