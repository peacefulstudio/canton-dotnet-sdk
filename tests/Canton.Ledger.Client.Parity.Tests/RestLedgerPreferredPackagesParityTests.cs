// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerPreferredPackagesParityTests : LedgerPreferredPackagesParityTests
{
    protected override Task<CapabilityLane<PreferredPackagesCapability>> OpenPreferredPackagesAsync(
        PackageLookup lookup, CancellationToken cancellationToken) =>
        LiveTransports.OpenPreferredPackagesAsync(
            LiveTransports.Rest, lookup, "rest-preferred-packages", cancellationToken);
}
