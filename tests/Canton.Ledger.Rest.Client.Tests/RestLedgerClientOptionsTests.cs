// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestLedgerClientOptionsTests
{
    [Fact]
    public void RestLedgerClientOptions_is_sealed()
    {
        typeof(RestLedgerClientOptions).IsSealed.Should().BeTrue(
            "an options POCO bound by the options pattern has no intended subtype");
    }
}
