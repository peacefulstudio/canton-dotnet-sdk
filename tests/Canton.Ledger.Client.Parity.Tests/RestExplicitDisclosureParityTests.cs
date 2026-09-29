// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestExplicitDisclosureParityTests : ExplicitDisclosureParityTests
{
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
        ServiceProvider? services = null;
        try
        {
            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddRestLedgerRawApis(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .AddRestLedgerClient(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .BuildServiceProvider();

            await LedgerApiVersionSkewGuard.AssertConformableAsync(
                services.GetRequiredService<IVersionServiceApi>(), cancellationToken).ConfigureAwait(false);

            await fixture.UploadDarAsync(ContractKeysDar.Path, cancellationToken).ConfigureAwait(false);
            var issuer = await fixture.AllocatePartyAsync(
                "rest-disclosure-issuer", cancellationToken: cancellationToken).ConfigureAwait(false);
            var reader = await fixture.AllocatePartyAsync(
                "rest-disclosure-reader", cancellationToken: cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(issuer.PartyId, cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(reader.PartyId, cancellationToken).ConfigureAwait(false);

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
