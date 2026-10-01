// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerReaderParityTests : LedgerReaderParityTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    protected override async Task<CapabilityLane<ILedgerReader>> OpenReaderAsync(CancellationToken cancellationToken)
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
                .AddLedgerClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                })
                .BuildServiceProvider();

            var reader = services.GetRequiredService<ILedgerReader>();
            return new CapabilityLane<ILedgerReader>(reader, async () =>
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

    private static string DarPath() => RichTypesDar.Path;

    protected override async Task<CapabilityLane<TypedReadsProbe>> OpenTypedReadsAsync(CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        var actAsRights = ActAsRightsLease.ForValidator(fixture);
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
        ServiceProvider? services = null;
        try
        {
            await fixture.UploadDarAsync(DarPath(), cancellationToken).ConfigureAwait(false);
            var party = await fixture.AllocatePartyAsync(
                "grpc-reads-parity", cancellationToken: cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(party.PartyId, cancellationToken).ConfigureAwait(false);

            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddLedgerClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                })
                .BuildServiceProvider();

            var client = services.GetRequiredService<ICantonLedgerClient>();
            var probe = await SeedAsync(client, new Party(party.PartyId), cancellationToken).ConfigureAwait(false);
            return new CapabilityLane<TypedReadsProbe>(
                probe,
                async () =>
                {
                    try
                    {
                        await services.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await LaneTeardown.ReleaseAsync(actAsRights, fixture).ConfigureAwait(false);
                    }
                },
                rightsGatedUserId: fixture.ValidatorUserId);
        }
        catch (Exception openFailure)
        {
            await LaneTeardown.ReleaseAsync(openFailure, services, actAsRights, fixture)
                .ConfigureAwait(false);
            throw;
        }
    }
}
