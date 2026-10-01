// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerAdminParityTests : LiveLedgerAdminParityTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
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
            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddRestLedgerRawApis(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .AddRestLedgerClient(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .BuildServiceProvider();

            await LedgerApiVersionSkewGuard.AssertConformableAsync(
                services.GetRequiredService<IVersionServiceApi>(), cancellationToken).ConfigureAwait(false);

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

    [Fact]
    public async Task GetCommandStatusAsync_is_not_served_by_the_JSON_API()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var act = () => lane.Capability.Admin.GetCommandStatusAsync(cancellationToken: cancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task UpdatePartyIdentityProviderIdAsync_is_not_served_by_the_JSON_API()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var act = () => lane.Capability.Admin.UpdatePartyIdentityProviderIdAsync(
            new Daml.Runtime.Data.Party("nobody::1220"), null, "idp", cancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task PruneAsync_is_not_served_by_the_JSON_API()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var act = () => lane.Capability.Admin.PruneAsync(1, cancellationToken: cancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
