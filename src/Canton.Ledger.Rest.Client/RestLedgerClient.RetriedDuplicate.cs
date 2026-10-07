// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerClient
{
    private async Task<ExerciseOutcome<TProjection>> ResolveRetriedDuplicateAsync<TProjection>(
        string commandId,
        Raw.TransactionFormat? pointReadFormat,
        ExerciseOutcome<TProjection>.DamlError duplicate,
        Func<Raw.Transaction, TProjection> project,
        CancellationToken cancellationToken)
    {
        if (pointReadFormat is null
            || !RetriedDuplicateCommand.TryReadCompletionOffset(duplicate.Metadata, out var completionOffset))
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
                UpdateByOffsetPath, request, $"offset {completionOffset}", project,
                timeout: null, cancellationToken).ConfigureAwait(false);
            LogRetriedDuplicateResolved(_logger, commandId, completionOffset);
            return new ExerciseOutcome<TProjection>.One(projected);
        }
        catch (Exception pointReadFailure) when (pointReadFailure is not OperationCanceledException)
        {
            LogRetriedDuplicatePointReadFailed(_logger, commandId, completionOffset, pointReadFailure.Message, pointReadFailure);
            return duplicate;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND because the first attempt already committed; resolved the committed transaction at completion offset {CompletionOffset} and surfaced it as success")]
    private static partial void LogRetriedDuplicateResolved(ILogger logger, string commandId, long completionOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND but the rejection carried no usable completion_offset to resolve the committed transaction; surfacing the DUPLICATE_COMMAND error")]
    private static partial void LogRetriedDuplicateUnresolved(ILogger logger, string commandId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retried submission {CommandId} was rejected as DUPLICATE_COMMAND but the point read at completion offset {CompletionOffset} failed ({Detail}); surfacing the DUPLICATE_COMMAND error")]
    private static partial void LogRetriedDuplicatePointReadFailed(ILogger logger, string commandId, long completionOffset, string detail, Exception exception);
}
