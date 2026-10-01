// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class AdminParityScenarioTests
{
    [Fact]
    public void UpdatedIssuer_is_derived_from_the_identity_provider_id()
    {
        var scenario = new AdminParityScenario("hint", "user", "unknown", "idp-42");

        scenario.UpdatedIssuer.Should().Be("https://updated-idp-42.invalid");
    }

    [Fact]
    public void NewIdentityProviderConfig_issuer_is_derived_from_the_identity_provider_id()
    {
        var scenario = new AdminParityScenario("hint", "user", "unknown", "idp-42");

        scenario.NewIdentityProviderConfig().Issuer.Should().Be("https://idp-42.invalid");
    }

    [Fact]
    public void CreateUnique_yields_distinct_issuers_for_each_call()
    {
        var first = AdminParityScenario.CreateUnique();
        var second = AdminParityScenario.CreateUnique();

        first.UpdatedIssuer.Should().NotBe(second.UpdatedIssuer);
        first.NewIdentityProviderConfig().Issuer.Should().NotBe(second.NewIdentityProviderConfig().Issuer);
    }
}
