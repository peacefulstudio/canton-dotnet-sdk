// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerKeyedExternalSigningParityTests : LedgerKeyedExternalSigningParityTests
{
    protected override Task<CapabilityLane<ExternalSigningCapability>> OpenKeyedExternalSigningAsync(
        ExternalSigningScenario scenario, CancellationToken cancellationToken) =>
        LiveTransports.OpenKeyedExternalSigningAsync(LiveTransports.Rest, cancellationToken);
}
