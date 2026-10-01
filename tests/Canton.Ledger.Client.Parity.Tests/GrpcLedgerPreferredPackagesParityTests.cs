// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerPreferredPackagesParityTests : LedgerPreferredPackagesParityTests
{
    protected override Task<CapabilityLane<PreferredPackagesCapability>> OpenPreferredPackagesAsync(
        PackageLookup lookup, CancellationToken cancellationToken) =>
        LiveTransports.OpenPreferredPackagesAsync(
            LiveTransports.Grpc, lookup, "grpc-preferred-packages", cancellationToken);
}
