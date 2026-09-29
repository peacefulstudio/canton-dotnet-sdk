// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcExplicitDisclosureParityTests : ExplicitDisclosureParityTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    protected override async Task<CapabilityLane<(ICantonLedgerClient Client, Party Issuer, Party Reader)>>
        OpenDisclosureLaneAsync(CancellationToken cancellationToken)
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
            await fixture.UploadDarAsync(ContractKeysDar.Path, cancellationToken).ConfigureAwait(false);
            var issuer = await fixture.AllocatePartyAsync(
                "grpc-disclosure-issuer", cancellationToken: cancellationToken).ConfigureAwait(false);
            var reader = await fixture.AllocatePartyAsync(
                "grpc-disclosure-reader", cancellationToken: cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(issuer.PartyId, cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(reader.PartyId, cancellationToken).ConfigureAwait(false);

            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddLedgerClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                })
                .BuildServiceProvider();

            var client = services.GetRequiredService<ICantonLedgerClient>();
            return new CapabilityLane<(ICantonLedgerClient, Party, Party)>(
                (client, new Party(issuer.PartyId), new Party(reader.PartyId)),
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
