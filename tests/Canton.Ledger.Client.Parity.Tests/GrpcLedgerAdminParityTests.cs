// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerAdminParityTests : LiveLedgerAdminParityTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    protected override async Task<CapabilityLane<AdminCapability>> OpenAdminAsync(
        AdminParityScenario scenario, CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        ServiceProvider? services = null;
        try
        {
            var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddAdminClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                })
                .AddLedgerClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                })
                .BuildServiceProvider();

            var admin = services.GetRequiredService<IAdminClient>();
            var ledgerClient = services.GetRequiredService<ICantonLedgerClient>();
            var capability = await ResolveAdminCapabilityAsync(admin, ledgerClient, cancellationToken);
            return new CapabilityLane<AdminCapability>(capability, async () =>
            {
                try
                {
                    await services.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await fixture.DisposeAsync().ConfigureAwait(false);
                }
            });
        }
        catch
        {
            if (services is not null)
            {
                await services.DisposeAsync().ConfigureAwait(false);
            }

            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
