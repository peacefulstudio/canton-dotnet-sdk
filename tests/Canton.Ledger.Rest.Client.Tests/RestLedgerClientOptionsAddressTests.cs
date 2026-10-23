// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestLedgerClientOptionsAddressTests
{
    [Theory]
    [InlineData("localhost:7575")]
    [InlineData("localhost")]
    [InlineData("/ledger")]
    [InlineData("ftp://localhost:7575")]
    public void HttpAddress_that_is_not_an_absolute_http_or_https_uri_is_rejected(string address)
    {
        var results = ValidationResultsFor(address);

        var failure = results.Should().ContainSingle().Subject;
        failure.MemberNames.Should().Equal("HttpAddress");
        failure.ErrorMessage.Should().Be(
            $"HttpAddress '{address}' must be an absolute http or https URI, for example 'http://localhost:7575'.");
    }

    [Theory]
    [InlineData("http://localhost:7575")]
    [InlineData("https://localhost:7575")]
    [InlineData("HTTPS://ledger.example.com")]
    public void HttpAddress_that_is_an_absolute_http_or_https_uri_is_accepted(string address)
    {
        ValidationResultsFor(address).Should().BeEmpty();
    }

    private static List<ValidationResult> ValidationResultsFor(string address)
    {
        var options = new RestLedgerClientOptions { HttpAddress = address };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
