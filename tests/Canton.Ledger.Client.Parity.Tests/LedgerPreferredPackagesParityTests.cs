// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// An <see cref="ICantonLedgerClient"/> with a party the participant knows and the package name a
/// lookup resolves.
/// </summary>
/// <param name="Client">The provider's ledger client.</param>
/// <param name="Party">A party known on the participant.</param>
/// <param name="PackageName">The package name the shared body asks for.</param>
public sealed record PreferredPackagesCapability(ICantonLedgerClient Client, Party Party, string PackageName);

/// <summary>
/// Behavioral parity suite over <see cref="ICantonLedgerClient.GetPreferredPackagesAsync"/>. The body pins
/// only what <c>interactive_submission_service.proto</c> documents: a resolved preference names the
/// requested package and carries a synchronizer.
/// </summary>
public abstract class LedgerPreferredPackagesParityTests
{
    /// <summary>Opens a lane over this provider's preferred-package lookups.</summary>
    protected abstract Task<CapabilityLane<PreferredPackagesCapability>> OpenPreferredPackagesAsync(
        CancellationToken cancellationToken);

    [Fact]
    public async Task GetPreferredPackagesAsync_names_the_requested_package()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenPreferredPackagesAsync(cancellationToken);
        var (client, party, packageName) = lane.Capability;

        var preferred = await client.GetPreferredPackagesAsync(
            [new PackageVettingRequirement([party], packageName)], cancellationToken: cancellationToken);

        preferred.PackageReferences.Should().ContainSingle().Which.PackageName.Should().Be(packageName);
        preferred.PackageReferences[0].PackageId.Should().NotBeNullOrWhiteSpace();
        preferred.PackageReferences[0].PackageVersion.Should().NotBeNullOrWhiteSpace();
        ((string)preferred.SynchronizerId).Should().NotBeNullOrWhiteSpace();
    }
}
