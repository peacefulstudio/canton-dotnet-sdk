// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerExternalSigningParityTests : LedgerExternalSigningParityTests
{
    protected override Task<CapabilityLane<ExternalSigningCapability>> OpenExternalSigningAsync(
        ExternalSigningScenario scenario, CancellationToken cancellationToken) =>
        LiveTransports.OpenExternalSigningAsync(LiveTransports.Rest, cancellationToken);
}
