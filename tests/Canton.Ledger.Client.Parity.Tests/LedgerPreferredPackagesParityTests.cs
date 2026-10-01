// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Grpc.Core;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>Whether a preferred-package lookup names a package the participant hosts.</summary>
public enum PackageLookup
{
    /// <summary>The package name of the uploaded conformance DAR.</summary>
    UploadedPackage,

    /// <summary>A package name no DAR on the participant carries.</summary>
    UnknownPackage,
}

/// <summary>
/// An <see cref="ICantonLedgerClient"/> with a party the participant knows and the package name a
/// lookup resolves.
/// </summary>
/// <param name="Client">The provider's ledger client.</param>
/// <param name="Party">A party known on the participant.</param>
/// <param name="PackageName">The package name the shared body asks for.</param>
public sealed record PreferredPackagesCapability(ICantonLedgerClient Client, Party Party, string PackageName);

/// <summary>
/// Behavioral parity suite over <see cref="ICantonLedgerClient.GetPreferredPackagesAsync"/> and
/// <see cref="ICantonLedgerClient.GetPreferredPackageVersionAsync"/>. The bodies pin only what
/// <c>interactive_submission_service.proto</c> documents: a resolved preference names the requested
/// package and carries a synchronizer, and an unknown package name never resolves — the participant rejects
/// it as a missing resource, PACKAGE_NAMES_NOT_FOUND, rather than answering with an empty preference.
/// </summary>
public abstract class LedgerPreferredPackagesParityTests
{
    /// <summary>Opens a lane over this provider's preferred-package lookups.</summary>
    protected abstract Task<CapabilityLane<PreferredPackagesCapability>> OpenPreferredPackagesAsync(
        PackageLookup lookup, CancellationToken cancellationToken);

    [Fact]
    public async Task GetPreferredPackagesAsync_names_the_requested_package()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenPreferredPackagesAsync(PackageLookup.UploadedPackage, cancellationToken);
        var (client, party, packageName) = lane.Capability;

        var preferred = await client.GetPreferredPackagesAsync(
            [new PackageVettingRequirement([party], packageName)], cancellationToken: cancellationToken);

        preferred.PackageReferences.Should().ContainSingle().Which.PackageName.Should().Be(packageName);
        preferred.PackageReferences[0].PackageId.Should().NotBeNullOrWhiteSpace();
        preferred.PackageReferences[0].PackageVersion.Should().NotBeNullOrWhiteSpace();
        ((string)preferred.SynchronizerId).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_names_the_requested_package()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenPreferredPackagesAsync(PackageLookup.UploadedPackage, cancellationToken);
        var (client, party, packageName) = lane.Capability;

        var preference = await client.GetPreferredPackageVersionAsync(
            [party], packageName, cancellationToken: cancellationToken);

        preference.Should().NotBeNull();
        preference!.Package.PackageName.Should().Be(packageName);
        preference.Package.PackageId.Should().NotBeNullOrWhiteSpace();
        preference.Package.PackageVersion.Should().NotBeNullOrWhiteSpace();
        ((string)preference.SynchronizerId).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_does_not_resolve_an_unknown_package_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenPreferredPackagesAsync(PackageLookup.UnknownPackage, cancellationToken);
        var (client, party, packageName) = lane.Capability;
        PackagePreference? preference = null;

        var failure = await Record.ExceptionAsync(async () => preference = await client.GetPreferredPackageVersionAsync(
            [party], packageName, cancellationToken: cancellationToken));

        preference.Should().BeNull();
        if (failure is not null)
        {
            var reportedAsMissingResource =
                failure is LedgerOperationException or RpcException { StatusCode: StatusCode.NotFound };
            reportedAsMissingResource.Should().BeTrue(
                "a participant that rejects an unknown package name reports it as a missing resource, but this was {0}",
                failure);
            failure.Message.Should().Contain(
                "do not match upgradable packages",
                "the participant's own PACKAGE_NAMES_NOT_FOUND text names the reason on both transports");
        }
    }
}
