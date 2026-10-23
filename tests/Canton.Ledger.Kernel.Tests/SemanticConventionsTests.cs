// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class SemanticConventionsTests
{
    [Theory]
    [InlineData(SemanticConventions.ServerAddress, "server.address")]
    [InlineData(SemanticConventions.ServerPort, "server.port")]
    [InlineData(SemanticConventions.ErrorType, "error.type")]
    public void SemanticConventions_pins_the_opentelemetry_attribute_name(string constant, string canonicalName) =>
        constant.Should().Be(canonicalName);
}
