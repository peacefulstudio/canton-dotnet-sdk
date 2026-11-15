// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Canton.Ledger.Kernel.Streams;

internal static partial class StreamEventClassifier
{
    public static bool TryAdmit<TSynchronizerScope>(
        in DecodedStreamEvent<TSynchronizerScope> decoded,
        out TSynchronizerScope synchronizerScope,
        out StreamEntryRefusal refusal)
        where TSynchronizerScope : struct
    {
        if (!decoded.MatchesMarker)
        {
            synchronizerScope = default;
            refusal = new StreamEntryRefusal(LedgerOffset.At(decoded.Offset), decoded.UnmatchedKind);
            return false;
        }
        if (decoded.SynchronizerScope is not { } admitted)
        {
            synchronizerScope = default;
            refusal = new StreamEntryRefusal(LedgerOffset.At(decoded.Offset), UnclassifiedKind.MissingSynchronizerId);
            return false;
        }
        synchronizerScope = admitted;
        refusal = default;
        return true;
    }

    public static SynchronizerId? Synchronizer(string? wireSynchronizerId) =>
        string.IsNullOrWhiteSpace(wireSynchronizerId) ? null : new SynchronizerId(wireSynchronizerId);

    public static ReassignmentScope? ReassignmentSynchronizers(string? wireSource, string? wireTarget) =>
        string.IsNullOrWhiteSpace(wireSource) || string.IsNullOrWhiteSpace(wireTarget)
            ? null
            : new ReassignmentScope(new SynchronizerId(wireSource), new SynchronizerId(wireTarget));

    public static bool IsNotCancellation(Exception exception) => exception is not OperationCanceledException;

    /// <summary>
    /// Logs an event the transport could not decode and returns the refusal that surfaces it in
    /// band as an <c>Unclassified</c> carrying <see cref="UnclassifiedKind.DecodeFailure"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="offset"/> is the resume point the refusal reports. On the update streams it
    /// is the offset of the containing update — never an offset read off the individual event,
    /// which the wire leaves unset often enough that reading it would hand a consumer the beginning
    /// of the ledger. A consumer resuming from it therefore re-reads every event in that update,
    /// including the ones it already handled, so it must be idempotent across the whole update
    /// rather than across the single event that failed. On the active-contract snapshot, where
    /// there is no containing update, it is the entry's own offset or the snapshot offset it falls
    /// back to.
    /// </remarks>
    public static StreamEntryRefusal DecodeFailure(string markerName, long offset, ILogger? logger, Exception cause) =>
        DecodeFailure(markerName, LedgerOffset.At(offset), logger, cause);

    public static StreamEntryRefusal DecodeFailure(string markerName, LedgerOffset? offset, ILogger? logger, Exception cause)
    {
        LogEventDecodeFailed(logger ?? NullLogger.Instance, markerName, offset?.Value, cause);
        return new StreamEntryRefusal(offset, UnclassifiedKind.DecodeFailure);
    }

    /// <summary>
    /// Logs an update whose own offset the transport could not decode and returns the refusal that
    /// surfaces it in band as an <c>Unclassified</c> carrying <see cref="UnclassifiedKind.DecodeFailure"/>
    /// and no offset.
    /// </summary>
    /// <remarks>
    /// The offset is withheld rather than guessed: the begin-of-ledger offset would silently rewind a
    /// consumer that persists it as a resume point. One refusal stands for the whole update.
    /// </remarks>
    public static StreamEntryRefusal UndecodableUpdateOffset(
        string markerName, ILogger? logger, MalformedResponseException cause)
    {
        LogUpdateOffsetUndecodable(logger ?? NullLogger.Instance, markerName, cause);
        return new StreamEntryRefusal(null, UnclassifiedKind.DecodeFailure);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode the offset of an update on the {TemplateType} stream — surfaced as Unclassified (decode-failure) carrying no offset, so a consumer must not persist this event as a resume point")]
    private static partial void LogUpdateOffsetUndecodable(ILogger logger, string templateType, Exception exception);

    /// <summary>
    /// Logs an offset checkpoint whose offset the transport could not decode and returns the fault
    /// that ends the update stream with <see cref="TransportStatus.UndecodableBody"/>.
    /// </summary>
    /// <remarks>
    /// The checkpoint offset is the resume point itself, so the stream cannot continue past it: a
    /// consumer persisting a guessed offset would rewind or skip.
    /// </remarks>
    public static StreamFault UndecodableCheckpoint(string markerName, ILogger? logger, MalformedResponseException cause)
    {
        LogCheckpointUndecodable(logger ?? NullLogger.Instance, markerName, cause);
        return StreamFault.FromUndecodableBody(
            $"Could not decode the offset checkpoint on the update stream: {cause.Detail}", cause);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode the offset checkpoint on the {TemplateType} update stream — ended with StreamError (undecodable-body)")]
    private static partial void LogCheckpointUndecodable(ILogger logger, string templateType, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode event at offset {Offset} on the {TemplateType} stream — surfaced as Unclassified (decode-failure)")]
    private static partial void LogEventDecodeFailed(ILogger logger, string templateType, long? offset, Exception exception);
}
