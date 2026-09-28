// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class LocalnetValidatorWalletTests
{
    private static readonly Uri TokenEndpoint =
        new("http://localhost:8082/realms/AValidator1/protocol/openid-connect/token");

    [Fact]
    public void ForAValidator1_defaults_the_tap_to_the_published_splice_validator_admin_port()
    {
        var wallet = LocalnetValidatorWallet.ForAValidator1(TokenEndpoint, _ => null);

        wallet.TapUri.Should().Be(new Uri("http://localhost:11903/api/validator/v0/wallet/tap"));
    }

    [Fact]
    public void ForAValidator1_takes_the_validator_api_base_from_its_environment_override()
    {
        var wallet = LocalnetValidatorWallet.ForAValidator1(
            TokenEndpoint,
            name => name == "CANTON_LOCALNET_A_VALIDATOR_1_VALIDATOR_API_URL" ? "http://validator.example:4000" : null);

        wallet.TapUri.Should().Be(new Uri("http://validator.example:4000/api/validator/v0/wallet/tap"));
    }

    [Fact]
    public void WalletAdminPasswordGrant_authenticates_as_the_a_validator_1_wallet_admin_through_the_unsafe_client()
    {
        LocalnetValidatorWallet.WalletAdminPasswordGrant.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "a-validator-1-unsafe",
            ["username"] = "a-validator-1",
            ["password"] = "abc123",
        });
    }
}
