// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientOptionsAddressTests
{
    [Theory]
    [InlineData("localhost:5001")]
    [InlineData("localhost")]
    [InlineData("/ledger")]
    [InlineData("ftp://localhost:5001")]
    [InlineData("unix:///tmp/ledger.sock")]
    public void GrpcAddress_that_is_not_an_absolute_http_or_https_uri_is_rejected(string address)
    {
        var results = ValidationResultsFor(address);

        var failure = results.Should().ContainSingle().Subject;
        failure.MemberNames.Should().Equal("GrpcAddress");
        failure.ErrorMessage.Should().Be(
            $"GrpcAddress '{address}' must be an absolute http or https URI, for example 'https://localhost:5001'.");
    }

    [Theory]
    [InlineData("http://localhost:5001")]
    [InlineData("https://localhost:5001")]
    [InlineData("HTTPS://ledger.example.com")]
    public void GrpcAddress_that_is_an_absolute_http_or_https_uri_is_accepted(string address)
    {
        ValidationResultsFor(address).Should().BeEmpty();
    }

    private static List<ValidationResult> ValidationResultsFor(string address)
    {
        var options = new LedgerClientOptions { GrpcAddress = address };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
