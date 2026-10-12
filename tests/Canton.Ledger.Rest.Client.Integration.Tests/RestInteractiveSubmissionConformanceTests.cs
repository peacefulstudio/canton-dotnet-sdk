// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Canton.Ledger.Rest.Client.Raw;
using Refit;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for <see cref="IInteractiveSubmissionApi"/>. Canton 3.6 disables
/// <c>GET /v2/interactive-submission/preferred-package-version</c> by default and Canton 3.7 removes
/// it, so a 3.6 participant answers it with a refusal rather than a package preference. The raw tier
/// keeps the route for the releases that still serve it, and this suite pins the refusal a 3.6
/// participant actually gives, so the day Canton changes it the lane says so.
/// </summary>
[Trait("Category", "Integration")]
public class RestInteractiveSubmissionConformanceTests
{
    private const string LocalnetPackageName = "splice-amulet";

    [Fact]
    public async Task GetPreferredPackageVersion_on_a_participant_with_deprecated_endpoints_disabled_answers_400_DEPRECATED_API_DISABLED()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var party = await lane.Fixture.AllocatePartyAsync(
            "rest-preferred-package", cancellationToken: TestContext.Current.CancellationToken);
        await lane.GrantActAsAsync(party.PartyId, TestContext.Current.CancellationToken);
        var synchronizer = Assert.Single(await lane.Fixture.GetConnectedSynchronizersAsync(
            party.PartyId, TestContext.Current.CancellationToken));
        Assert.Equal("global", synchronizer.Alias);

        var refusal = await Assert.ThrowsAsync<ApiException>(() =>
            lane.Api<IInteractiveSubmissionApi>().GetPreferredPackageVersion(
                [party.PartyId],
                LocalnetPackageName,
                synchronizer.Id,
                DateTimeOffset.UtcNow,
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
        Assert.Contains("DEPRECATED_API_DISABLED", refusal.Content);
    }
}
