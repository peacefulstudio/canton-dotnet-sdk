// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class MixedTransportAdminClientRegistrationTests
{
    [Fact]
    public void AddAdminClient_after_AddRestLedgerClient_resolves_the_gRPC_AdminClient()
    {
        using var provider = new ServiceCollection()
            .AddRestLedgerClient(static options => options.HttpAddress = "http://ledger.example:7575")
            .AddAdminClient(static options => options.GrpcAddress = "https://ledger.example:5001")
            .BuildServiceProvider();

        provider.GetRequiredService<IAdminClient>().Should().BeOfType<AdminClient>();
    }

    [Fact]
    public void AddAdminClient_before_AddRestLedgerClient_resolves_the_gRPC_AdminClient()
    {
        using var provider = new ServiceCollection()
            .AddAdminClient(static options => options.GrpcAddress = "https://ledger.example:5001")
            .AddRestLedgerClient(static options => options.HttpAddress = "http://ledger.example:7575")
            .BuildServiceProvider();

        provider.GetRequiredService<IAdminClient>().Should().BeOfType<AdminClient>();
    }

    [Fact]
    public void AddCantonLedger_after_AddRestLedgerClient_resolves_the_gRPC_AdminClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Canton:Ledger:GrpcAddress"] = "https://ledger.example:5001",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddRestLedgerClient(static options => options.HttpAddress = "http://ledger.example:7575")
            .AddCantonLedger(configuration)
            .BuildServiceProvider();

        provider.GetRequiredService<IAdminClient>().Should().BeOfType<AdminClient>();
    }

    [Fact]
    public void AddRestLedgerClient_alone_resolves_the_RestAdminClient()
    {
        using var provider = new ServiceCollection()
            .AddRestLedgerClient(static options => options.HttpAddress = "http://ledger.example:7575")
            .BuildServiceProvider();

        provider.GetRequiredService<IAdminClient>().Should().BeOfType<RestAdminClient>();
    }
}
