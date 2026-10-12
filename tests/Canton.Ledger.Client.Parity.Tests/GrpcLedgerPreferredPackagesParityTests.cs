// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerPreferredPackagesParityTests : LedgerPreferredPackagesParityTests
{
    protected override Task<CapabilityLane<PreferredPackagesCapability>> OpenPreferredPackagesAsync(
        CancellationToken cancellationToken) =>
        LiveTransports.OpenPreferredPackagesAsync(
            LiveTransports.Grpc, "grpc-preferred-packages", cancellationToken);
}
