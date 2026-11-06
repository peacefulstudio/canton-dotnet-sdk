// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Canton.Ledger.Testing.LiveConformance;
using Canton.Ledger.Testing.Localnet;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>Reaches the participant over the JSON Ledger API; the faulting client cuts the snapshot page mid-body.</summary>
internal sealed class RestLiveTransport : ILiveTransport
{
    private const int RowsKeptBeforeSnapshotFault = 1;

    private readonly ServiceProvider _wholeBodyServices;
    private readonly ServiceProvider _truncatingServices;

    internal RestLiveTransport(LocalnetFixture localnet)
    {
        _wholeBodyServices = BuildServices(localnet, truncateSnapshotPages: false);
        _truncatingServices = BuildServices(localnet, truncateSnapshotPages: true);
    }

    public ICantonLedgerClient CreateClient() => _wholeBodyServices.GetRequiredService<RestLedgerClient>();

    public ICantonLedgerClient CreateFaultingSnapshotClient() => _truncatingServices.GetRequiredService<RestLedgerClient>();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _truncatingServices.DisposeAsync();
        }
        finally
        {
            await _wholeBodyServices.DisposeAsync();
        }
    }

    private static ServiceProvider BuildServices(LocalnetFixture localnet, bool truncateSnapshotPages)
    {
        var registrations = new ServiceCollection()
            .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(localnet.TokenProvider.GetAccessTokenAsync))
            .AddRestLedgerClient(options => options.HttpAddress = localnet.Endpoints.JsonLedgerApi.ToString());

        if (truncateSnapshotPages)
        {
            registrations
                .AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                .AddHttpMessageHandler(() => new TruncatedActiveContractsPageHandler(RowsKeptBeforeSnapshotFault));
        }

        return registrations.BuildServiceProvider();
    }
}
