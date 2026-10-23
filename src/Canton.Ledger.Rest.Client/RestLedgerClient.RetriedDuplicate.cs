// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerClient
{
    private async Task<ExerciseOutcome<TProjection>> ResolveRetriedDuplicateAsync<TProjection>(
        string commandId,
        Raw.TransactionFormat? pointReadFormat,
        RetriedDuplicateVerdict verdict,
        ExerciseOutcome<TProjection> duplicate,
        Func<Raw.Transaction, TProjection> project,
        CancellationToken cancellationToken)
    {
        if (pointReadFormat is null
            || verdict is not RetriedDuplicateVerdict.CommittedAt { CompletionOffset: var completionOffset })
        {
            LogRetriedDuplicateUnresolved(_logger, commandId);
            return duplicate;
        }

        var request = new Raw.GetUpdateByOffsetRequest
        {
            Offset = completionOffset.ToString(CultureInfo.InvariantCulture),
            UpdateFormat = RestSubscribeRequestBuilder.BuildTransactionUpdateFormat(pointReadFormat),
        };

        try
        {
            var projected = await GetUpdateAsync(
                UpdateByOffsetPath, request, $"offset {completionOffset}", OnlyOfCommand(commandId, completionOffset, project),
                timeout: null, cancellationToken).ConfigureAwait(false);
            LogRetriedDuplicateResolved(_logger, commandId, completionOffset);
            return new ExerciseOutcome<TProjection>.One(projected);
        }
        catch (Exception pointReadFailure) when (StreamEventClassifier.IsNotCancellation(pointReadFailure))
        {
            LogRetriedDuplicatePointReadFailed(commandId, completionOffset, pointReadFailure);
            return duplicate;
        }
    }

    private void LogRetriedDuplicatePointReadFailed(string commandId, long completionOffset, Exception pointReadFailure)
    {
        if (_emitPartyAndContractSpanTags)
        {
            LogRetriedDuplicatePointReadFailedWithDetail(_logger, commandId, completionOffset, pointReadFailure.Message, pointReadFailure);
        }
        else
        {
            LogRetriedDuplicatePointReadFailed(_logger, commandId, completionOffset, StatusOf(pointReadFailure));
        }
    }

    private static string StatusOf(Exception failure) =>
        failure is LedgerOperationException { Status: TransportStatus.Http(var statusCode) }
            ? ((int)statusCode).ToString(CultureInfo.InvariantCulture)
            : failure.GetType().Name;

    private static Func<Raw.Transaction, TProjection> OnlyOfCommand<TProjection>(
        string commandId,
        long completionOffset,
        Func<Raw.Transaction, TProjection> project) =>
        transaction => transaction.CommandId == commandId
            ? project(transaction)
            : throw new ProjectionRefusalException(
                $"The transaction at offset {completionOffset} was not recorded for command '{commandId}'.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND because the first attempt already committed; resolved the committed transaction at completion offset {CompletionOffset} and surfaced it as success")]
    private static partial void LogRetriedDuplicateResolved(ILogger logger, string commandId, long completionOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND but the rejection carried no usable completion_offset to resolve the committed transaction; surfacing the DUPLICATE_COMMAND error")]
    private static partial void LogRetriedDuplicateUnresolved(ILogger logger, string commandId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND but the point read at completion offset {CompletionOffset} failed with {Status}; surfacing the DUPLICATE_COMMAND error")]
    private static partial void LogRetriedDuplicatePointReadFailed(ILogger logger, string commandId, long completionOffset, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND but the point read at completion offset {CompletionOffset} failed ({Detail}); surfacing the DUPLICATE_COMMAND error")]
    private static partial void LogRetriedDuplicatePointReadFailedWithDetail(ILogger logger, string commandId, long completionOffset, string detail, Exception exception);
}
