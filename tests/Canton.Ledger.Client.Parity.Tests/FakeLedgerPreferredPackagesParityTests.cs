// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime.Data;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerPreferredPackagesParityTests : LedgerPreferredPackagesParityTests
{
    protected override Task<CapabilityLane<PreferredPackagesCapability>> OpenPreferredPackagesAsync(
        PackageLookup lookup, CancellationToken cancellationToken)
    {
        var party = new Party("fake::preferred-packages");
        var synchronizer = new SynchronizerId("fake-sync");
        var package = new PackageReference("fake-package-id", "fake-package", "1.2.3");
        var builder = FakeLedgerClient.Create();
        var packageName = lookup == PackageLookup.UploadedPackage ? package.PackageName : "fake-unknown-package";
        var client = (lookup == PackageLookup.UploadedPackage
                ? builder
                    .WithPreferredPackages(new PreferredPackages([package], synchronizer))
                    .WithPackagePreference(new PackagePreference(package, synchronizer))
                : builder.WithPackagePreference(null))
            .Build();

        return Task.FromResult(new CapabilityLane<PreferredPackagesCapability>(
            new PreferredPackagesCapability(client, party, packageName), client.DisposeAsync));
    }
}
