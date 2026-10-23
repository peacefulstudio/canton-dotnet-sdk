// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

[Collection("PqsClient global ActivitySource")]
public class PqsClientTelemetryPrivacyTests
{
    private const string FailureMessage = "contract '00abc123' of party 'alice::1220ff' rejected";

    private static PqsClient ClientFailingWith(
        Exception failure,
        bool emitIdentifiers,
        ILogger<PqsClient> logger) =>
        new(
            new PqsClientOptions
            {
                ConnectionString = "Host=localhost;Database=pqs",
                EmitPartyAndContractSpanTags = emitIdentifiers,
            },
            _ => throw failure,
            logger);

    private static async Task<(Activity Span, Exception? Logged)> QueryFailingWith(Exception failure, bool emitIdentifiers)
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.PqsClient);
        using var logs = new CapturingLoggerFactory();
        var client = ClientFailingWith(failure, emitIdentifiers, logs.CreateLogger<PqsClient>());

        var act = async () => await client.QueryAsync<FilterTests.SampleTemplate>(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<Exception>();

        var logged = logs.Records.Single(record => record.Level == LogLevel.Error).Exception;
        return (capture.Activities.Single(a => a.OperationName == "PqsQuery"), logged);
    }

    [Fact]
    public async Task QueryAsync_span_status_carries_only_the_exception_type_when_identifiers_are_off()
    {
        var (span, _) = await QueryFailingWith(new InvalidOperationException(FailureMessage), emitIdentifiers: false);

        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task QueryAsync_span_status_carries_the_exception_message_when_identifiers_are_on()
    {
        var (span, _) = await QueryFailingWith(new InvalidOperationException(FailureMessage), emitIdentifiers: true);

        span.StatusDescription.Should().Be("contract '00abc123' of party 'alice::1220ff' rejected");
    }

    [Fact]
    public async Task QueryAsync_logs_a_redacted_exception_when_identifiers_are_off()
    {
        var (_, logged) = await QueryFailingWith(new InvalidOperationException(FailureMessage), emitIdentifiers: false);

        logged!.Message.Should().Be("System.InvalidOperationException");
        logged.ToString().Should().NotContain("alice::1220ff");
    }

    [Fact]
    public async Task QueryAsync_logs_the_original_exception_when_identifiers_are_on()
    {
        var (_, logged) = await QueryFailingWith(new InvalidOperationException(FailureMessage), emitIdentifiers: true);

        logged.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be(
            "contract '00abc123' of party 'alice::1220ff' rejected");
    }

    [Fact]
    public async Task QueryAsync_span_status_hides_a_postgres_error_detail_when_identifiers_are_off()
    {
        var failure = new PostgresException(
            "duplicate key", "ERROR", "ERROR", "23505", detail: "Key (contract_id)=(00abc123) already exists.");

        var (span, logged) = await QueryFailingWith(failure, emitIdentifiers: false);

        span.StatusDescription.Should().Be("PostgresException");
        logged!.ToString().Should().NotContain("00abc123");
    }

    [Fact]
    public async Task ExistsAsync_span_status_hides_the_exception_message_when_identifiers_are_off()
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.PqsClient);
        var client = ClientFailingWith(
            new InvalidOperationException(FailureMessage), emitIdentifiers: false, new CapturingLoggerFactory().CreateLogger<PqsClient>());

        var act = async () => await client.ExistsAsync(
            new ContractId<FilterTests.SampleTemplate>("00abc123"), TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>();

        capture.Activities.Single(a => a.OperationName == "PqsExists").StatusDescription
            .Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task QueryOneAsync_span_status_carries_the_exception_message_when_identifiers_are_on()
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.PqsClient);
        var client = ClientFailingWith(
            new InvalidOperationException(FailureMessage), emitIdentifiers: true, new CapturingLoggerFactory().CreateLogger<PqsClient>());
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "alice");

        var act = async () => await client.QueryOneAsync<FilterTests.SampleTemplate>(
            filter, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>();

        capture.Activities.Single(a => a.OperationName == "PqsQueryOne").StatusDescription
            .Should().Be("contract '00abc123' of party 'alice::1220ff' rejected");
    }
}
