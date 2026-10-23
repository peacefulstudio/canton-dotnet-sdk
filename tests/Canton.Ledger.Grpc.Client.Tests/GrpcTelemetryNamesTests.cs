// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcTelemetryNamesTests
{
    [Theory]
    [InlineData(ActivityHelper.RpcSystem, "rpc.system")]
    [InlineData(ActivityHelper.RpcService, "rpc.service")]
    [InlineData(ActivityHelper.RpcMethod, "rpc.method")]
    [InlineData(ActivityHelper.RpcGrpcStatusCode, "rpc.grpc.status_code")]
    [InlineData(LedgerCallInvoker.RetryAttemptActivityName, "LedgerClient.RetryAttempt")]
    [InlineData(CallContextHelper.AuthorizationHeaderName, "authorization")]
    public void Grpc_client_pins_the_telemetry_and_wire_name(string constant, string literal) =>
        constant.Should().Be(literal);
}
