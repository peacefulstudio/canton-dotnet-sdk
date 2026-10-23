// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Testing.Localnet.Tests;

public class LocalnetEnvironmentTests
{
    [Theory]
    [InlineData(LocalnetEnvironment.GrpcUrl, "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL")]
    [InlineData(LocalnetEnvironment.PqsConnectionString, "CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING")]
    [InlineData(LocalnetEnvironment.ValidatorApiUrl, "CANTON_LOCALNET_A_VALIDATOR_1_VALIDATOR_API_URL")]
    [InlineData(LocalnetEnvironment.DefaultGrpcUrl, "http://localhost:11901")]
    public void Localnet_environment_pins_the_variable_name_and_default_address(string constant, string literal) =>
        constant.Should().Be(literal);
}
