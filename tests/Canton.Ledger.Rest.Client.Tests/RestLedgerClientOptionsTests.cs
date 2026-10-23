// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
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

    [Fact]
    public void Timeout_defaults_to_thirty_seconds()
    {
        new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }.Timeout
            .Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_a_Timeout_that_is_not_positive(int milliseconds)
    {
        var options = new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            Timeout = TimeSpan.FromMilliseconds(milliseconds),
        };

        var results = options.Validate(new ValidationContext(options));

        results.Should().ContainSingle()
            .Which.MemberNames.Should().Equal("Timeout");
    }

    [Fact]
    public void Validate_accepts_a_null_Timeout_as_no_SDK_side_deadline()
    {
        var options = new RestLedgerClientOptions { HttpAddress = "http://localhost:7575", Timeout = null };

        options.Validate(new ValidationContext(options)).Should().BeEmpty();
    }
}
