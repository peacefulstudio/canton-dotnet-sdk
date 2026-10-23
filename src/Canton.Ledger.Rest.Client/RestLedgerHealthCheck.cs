// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerHealthCheck(
    RestLedgerClient restLedgerClient,
    ILogger<RestLedgerHealthCheck>? logger = null,
    IOptions<RestLedgerClientOptions>? options = null) : IHealthCheck
{
    private readonly RestLedgerClient _restLedgerClient = restLedgerClient ?? throw new ArgumentNullException(nameof(restLedgerClient));
    private readonly bool _emitPartyAndContractSpanTags = options?.Value.EmitPartyAndContractSpanTags ?? false;
    private readonly ILogger _logger = (logger ?? NullLogger<RestLedgerHealthCheck>.Instance)
        .RedactingExceptionsUnless(options?.Value.EmitPartyAndContractSpanTags ?? false);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ledgerEnd = (await _restLedgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Value;

            LogHealthy(_logger, ledgerEnd);

            return HealthCheckResult.Healthy(
                description: $"Canton participant is reachable over HTTP (ledger end offset {ledgerEnd}).",
                data: new Dictionary<string, object> { ["ledgerEnd"] = ledgerEnd });
        }
        catch (Exception ex) when (StreamEventClassifier.IsNotCancellation(ex))
        {
            return Unhealthy(context, ex);
        }
    }

    private HealthCheckResult Unhealthy(HealthCheckContext context, Exception ex)
    {
        LogUnhealthy(_logger, ex);

        return new HealthCheckResult(
            context.Registration.FailureStatus,
            description: "Canton participant node is unreachable over HTTP.",
            exception: ex.RedactingUnless(_emitPartyAndContractSpanTags));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "REST ledger health check passed at ledger end offset {LedgerEnd}")]
    private static partial void LogHealthy(ILogger logger, long ledgerEnd);

    [LoggerMessage(Level = LogLevel.Warning, Message = "REST ledger health check failed")]
    private static partial void LogUnhealthy(ILogger logger, Exception ex);
}
