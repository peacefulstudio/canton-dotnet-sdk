// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Localnet;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

internal sealed record LiveLaneContext(
    ServiceProvider Services, LocalnetFixture Fixture, ActAsRightsLease ActAsRights);

internal static class LiveLane
{
    internal static async Task<CapabilityLane<TCapability>> OpenAsync<TCapability>(
        string skipMessage,
        Func<LocalnetFixture, ServiceProvider> buildServices,
        Func<LiveLaneContext, CancellationToken, Task<TCapability>> resolveCapability,
        CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(skipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        var actAsRights = ActAsRightsLease.ForValidator(fixture);
        ServiceProvider? services = null;
        try
        {
            services = buildServices(fixture);
            var capability = await resolveCapability(
                new LiveLaneContext(services, fixture, actAsRights), cancellationToken).ConfigureAwait(false);
            return new CapabilityLane<TCapability>(
                capability,
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
                fixture.ValidatorUserId);
        }
        catch (Exception openFailure)
        {
            await LaneTeardown.ReleaseAsync(openFailure, services, actAsRights, fixture)
                .ConfigureAwait(false);
            throw;
        }
    }
}
