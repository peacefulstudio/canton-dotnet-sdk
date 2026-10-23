// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Grpc.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerHealthCheckTests
{
    private static HealthCheckContext CreateContext(HealthStatus failureStatus = HealthStatus.Unhealthy) =>
        new()
        {
            Registration = new HealthCheckRegistration("canton-ledger", Substitute.For<IHealthCheck>(), failureStatus, null)
        };

    [Fact]
    public async Task CheckHealth_returns_healthy_with_ledger_end_offset()
    {
        var ledgerClient = Substitute.For<ILedgerClient>();
        ledgerClient.GetLedgerEndAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(LedgerOffset.At(42));

        var healthCheck = new LedgerHealthCheck(ledgerClient);

        var result = await healthCheck.CheckHealthAsync(CreateContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("ledgerEnd")
            .WhoseValue.Should().Be(42L);
    }

    [Fact]
    public async Task CheckHealth_reports_healthy_for_a_caller_with_only_actAs_and_readAs_rights()
    {
        var ledgerClient = Substitute.For<ILedgerClient>();
        ledgerClient.GetLedgerEndAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(LedgerOffset.At(7));

        var healthCheck = new LedgerHealthCheck(ledgerClient);

        var result = await healthCheck.CheckHealthAsync(CreateContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_returns_failure_status_when_ledger_client_throws()
    {
        var ledgerClient = Substitute.For<ILedgerClient>();
        ledgerClient.GetLedgerEndAsync(cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        var healthCheck = new LedgerHealthCheck(ledgerClient);

        var result = await healthCheck.CheckHealthAsync(CreateContext(HealthStatus.Degraded), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception!.Message.Should().Be("System.InvalidOperationException");
    }

    [Fact]
    public async Task CheckHealth_propagates_operation_canceled_exception()
    {
        var ledgerClient = Substitute.For<ILedgerClient>();
        ledgerClient.GetLedgerEndAsync(cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var healthCheck = new LedgerHealthCheck(ledgerClient);

        var act = () => healthCheck.CheckHealthAsync(CreateContext(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckHealth_propagates_caller_cancellation_surfaced_as_RpcException_Cancelled()
    {
        var ledgerClient = Substitute.For<ILedgerClient>();
        ledgerClient.GetLedgerEndAsync(cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client.")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var healthCheck = new LedgerHealthCheck(ledgerClient);

        var act = () => healthCheck.CheckHealthAsync(CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckHealth_returns_failure_status_for_RpcException_Cancelled_when_caller_token_not_cancelled()
    {
        var ledgerClient = Substitute.For<ILedgerClient>();
        ledgerClient.GetLedgerEndAsync(cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new RpcException(new Status(StatusCode.Cancelled, "server closed the call")));

        var healthCheck = new LedgerHealthCheck(ledgerClient);

        var result = await healthCheck.CheckHealthAsync(CreateContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception!.Message.Should().Be("Grpc.Core.RpcException");
    }

    [Fact]
    public void Constructor_throws_for_null_ledger_client()
    {
        var act = () => new LedgerHealthCheck(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ledgerClient");
    }

    private static async Task<(Exception? Logged, Exception? InResult)> ExceptionsOfAFailureNaming(string userId, bool emitIdentifiers)
    {
        var ledgerClient = Substitute.For<ILedgerClient>();
        ledgerClient.GetLedgerEndAsync(cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException($"user '{userId}' has no rights"));
        using var logs = new CapturingLoggerFactory();
        var options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001", EmitPartyAndContractSpanTags = emitIdentifiers };
        var healthCheck = new LedgerHealthCheck(ledgerClient, logs.CreateLogger<LedgerHealthCheck>(), Options.Create(options));

        var result = await healthCheck.CheckHealthAsync(CreateContext(), TestContext.Current.CancellationToken);

        return (logs.Records.Single(record => record.Level == LogLevel.Warning).Exception, result.Exception);
    }

    [Fact]
    public async Task CheckHealth_logs_only_the_exception_type_of_a_failure_by_default()
    {
        var (logged, _) = await ExceptionsOfAFailureNaming("alice-user", emitIdentifiers: false);

        logged!.Message.Should().Be("System.InvalidOperationException");
    }

    [Fact]
    public async Task CheckHealth_logs_the_exception_of_a_failure_when_party_and_contract_tags_are_opted_in()
    {
        var (logged, _) = await ExceptionsOfAFailureNaming("alice-user", emitIdentifiers: true);

        logged.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("user 'alice-user' has no rights");
    }

    [Fact]
    public async Task CheckHealth_reports_only_the_exception_type_in_the_result_of_a_failure_by_default()
    {
        var (_, inResult) = await ExceptionsOfAFailureNaming("alice-user", emitIdentifiers: false);

        inResult!.Message.Should().Be("System.InvalidOperationException");
        inResult.ToString().Should().NotContain("alice-user");
    }

    [Fact]
    public async Task CheckHealth_reports_the_exception_in_the_result_of_a_failure_when_party_and_contract_tags_are_opted_in()
    {
        var (_, inResult) = await ExceptionsOfAFailureNaming("alice-user", emitIdentifiers: true);

        inResult.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("user 'alice-user' has no rights");
    }
}
