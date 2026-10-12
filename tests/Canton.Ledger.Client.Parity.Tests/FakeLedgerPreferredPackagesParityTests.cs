// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime.Data;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerPreferredPackagesParityTests : LedgerPreferredPackagesParityTests
{
    protected override Task<CapabilityLane<PreferredPackagesCapability>> OpenPreferredPackagesAsync(
        CancellationToken cancellationToken)
    {
        var party = new Party("fake::preferred-packages");
        var synchronizer = new SynchronizerId("fake-sync");
        var package = new PackageReference("fake-package-id", "fake-package", "1.2.3");
        var client = FakeLedgerClient.Create()
            .WithPreferredPackages(new PreferredPackages([package], synchronizer))
            .Build();

        return Task.FromResult(new CapabilityLane<PreferredPackagesCapability>(
            new PreferredPackagesCapability(client, party, package.PackageName), client.DisposeAsync));
    }
}
