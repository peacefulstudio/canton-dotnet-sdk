// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestTelemetryNamesTests
{
    [Theory]
    [InlineData(RestActivityHandler.HttpRequestMethod, "http.request.method")]
    [InlineData(RestActivityHandler.HttpResponseStatusCode, "http.response.status_code")]
    [InlineData(RestActivityHandler.UrlFull, "url.full")]
    [InlineData(RestActivityHandler.UrlTemplate, "url.template")]
    [InlineData(RestRetryHandler.RetryAttemptActivityName, "RestLedgerClient.RetryAttempt")]
    public void Rest_client_pins_the_telemetry_name(string constant, string literal) =>
        constant.Should().Be(literal);
}
