// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Kernel.Wire;

internal static class LedgerCallOutcome
{
    private const string DuplicateCommandErrorId = "DUPLICATE_COMMAND";
    private const string CompletionOffsetMetadataKey = "completion_offset";

    internal static LedgerOperationException ToException(
        this LedgerCallEnding ending, LedgerCallKind kind, CommandId? commandId = null)
    {
        var exception = ExceptionOf(ending, kind);
        exception.CommandId = commandId;
        return exception;
    }

    private static LedgerOperationException ExceptionOf(LedgerCallEnding ending, LedgerCallKind kind) =>
        ending switch
        {
            LedgerCallEnding.Rejected { Error: ParsedLedgerError.Structured structured } rejected =>
                LedgerOperationException.FromFailedCall(
                    structured.Message,
                    structured.Status,
                    structured.Category,
                    structured.ErrorId,
                    structured.Metadata,
                    rejected.Cause,
                    CommitStateOf(ending, kind)),
            LedgerCallEnding.Rejected { Error: ParsedLedgerError.Unstructured unstructured } rejected =>
                LedgerOperationException.FromFailedCall(
                    unstructured.Message,
                    unstructured.Status,
                    unstructured.Category,
                    errorId: null,
                    metadata: null,
                    rejected.Cause,
                    CommitStateOf(ending, kind)),
            LedgerCallEnding.Unanswered unanswered => LedgerOperationException.FromFailedCall(
                unanswered.Message,
                new TransportStatus.NoResponse(),
                category: null,
                errorId: null,
                metadata: null,
                unanswered.Cause,
                CommitStateOf(ending, kind)),
            LedgerCallEnding.NeverSent neverSent => LedgerOperationException.FromFailedCall(
                neverSent.Message,
                new TransportStatus.NoResponse(),
                category: null,
                errorId: null,
                metadata: null,
                neverSent.Cause,
                CommitStateOf(ending, kind)),
            LedgerCallEnding.Unreadable unreadable => LedgerOperationException.FromFailedCall(
                unreadable.Message,
                new TransportStatus.UndecodableBody(),
                category: null,
                errorId: null,
                metadata: null,
                unreadable.Cause,
                CommitStateOf(ending, kind)),
            _ => throw Unhandled(ending),
        };

    internal static ExerciseOutcome<T> ToOutcome<T>(this LedgerCallEnding ending) => ending switch
    {
        LedgerCallEnding.Rejected
        {
            AfterRetry: true,
            Error: ParsedLedgerError.Structured { ErrorId: not DuplicateCommandErrorId } structured,
        } rejected =>
            new ExerciseOutcome<T>.InfraError(
                structured.Status, structured.Message, structured.Category, rejected.Cause),
        LedgerCallEnding.Rejected { Error: ParsedLedgerError.Structured structured } =>
            new ExerciseOutcome<T>.DamlError(
                structured.Category, structured.ErrorId, structured.Message, structured.Metadata),
        LedgerCallEnding.Rejected { Error: ParsedLedgerError.Unstructured unstructured } rejected =>
            new ExerciseOutcome<T>.InfraError(
                unstructured.Status, unstructured.Message, unstructured.Category, rejected.Cause),
        LedgerCallEnding.Unanswered unanswered =>
            new ExerciseOutcome<T>.InfraError(
                new TransportStatus.NoResponse(), unanswered.Message, SourceException: unanswered.Cause),
        LedgerCallEnding.NeverSent neverSent =>
            new ExerciseOutcome<T>.InfraError(
                new TransportStatus.NoResponse(), neverSent.Message, SourceException: neverSent.Cause),
        LedgerCallEnding.Unreadable unreadable =>
            new ExerciseOutcome<T>.CommittedUndecodable(
                unreadable.UpdateId,
                unreadable.Message,
                unreadable.Cause ?? new InvalidOperationException(unreadable.Message)),
        _ => throw Unhandled(ending),
    };

    internal static StreamFault ToStreamFault(this LedgerCallEnding ending) => ending switch
    {
        LedgerCallEnding.Rejected rejected => StreamFault.FromTransport(
            rejected.Error.Status,
            rejected.Error.Message,
            rejected.Error.ClassifiedCategory,
            rejected.Error.ReportedErrorId,
            rejected.Cause),
        LedgerCallEnding.Unanswered unanswered => StreamFault.FromTransport(
            new TransportStatus.NoResponse(), unanswered.Message, category: null, errorId: null, unanswered.Cause),
        LedgerCallEnding.NeverSent neverSent => StreamFault.FromTransport(
            new TransportStatus.NoResponse(), neverSent.Message, category: null, errorId: null, neverSent.Cause),
        LedgerCallEnding.Unreadable unreadable => StreamFault.FromTransport(
            new TransportStatus.UndecodableBody(), unreadable.Message, category: null, errorId: null, unreadable.Cause),
        _ => throw Unhandled(ending),
    };

    internal static RetriedDuplicateVerdict ToRetriedDuplicateVerdict(this LedgerCallEnding ending) => ending switch
    {
        LedgerCallEnding.Rejected
        {
            AfterRetry: true,
            Error: ParsedLedgerError.Structured { ErrorId: DuplicateCommandErrorId } duplicate,
        } =>
            CommitStateOf(ending, LedgerCallKind.EffectAppliedWrite) is CommitState.Committed
            && CompletionOffsetOf(duplicate) is { } completionOffset
                ? new RetriedDuplicateVerdict.CommittedAt(completionOffset)
                : new RetriedDuplicateVerdict.Unresolvable(),
        _ => new RetriedDuplicateVerdict.NotARetriedDuplicate(),
    };

    internal static bool ReportsAbsence(TransportStatus? status, DamlErrorCategory? category) => status switch
    {
        TransportStatus.Grpc grpc => grpc.StatusCode is GrpcStatusCode.NotFound,
        TransportStatus.Http http => http.StatusCode is HttpStatusCode.NotFound
            || category is DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
        _ => false,
    };

    private static long? CompletionOffsetOf(ParsedLedgerError.Structured duplicate) =>
        duplicate.Metadata.TryGetValue(CompletionOffsetMetadataKey, out var raw)
        && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var completionOffset)
        && completionOffset > 0
            ? completionOffset
            : null;

    private static CommitState CommitStateOf(LedgerCallEnding ending, LedgerCallKind kind) => (kind, ending) switch
    {
        (LedgerCallKind.Read, _) => CommitState.NotCommitted,
        (_, LedgerCallEnding.NeverSent) => CommitState.NotCommitted,
        (_, LedgerCallEnding.Unanswered) => CommitState.Unknown,
        (_, LedgerCallEnding.Rejected
        {
            AfterRetry: true,
            Error: ParsedLedgerError.Structured { ErrorId: not DuplicateCommandErrorId },
        }) => CommitState.Unknown,
        (_, LedgerCallEnding.Rejected { Error: ParsedLedgerError.Structured structured }) =>
            ParticipantRejectionCommitState.Of(structured.Category, structured.ErrorId, structured.Metadata),
        (_, LedgerCallEnding.Rejected) => CommitState.Unknown,
        (LedgerCallKind.AcceptedOnlyWrite, LedgerCallEnding.Unreadable) => CommitState.Unknown,
        (LedgerCallKind.EffectAppliedWrite, LedgerCallEnding.Unreadable) => CommitState.Committed,
        _ => throw new InvalidOperationException($"Unhandled ledger call kind: {kind}"),
    };

    private static InvalidOperationException Unhandled(LedgerCallEnding ending) =>
        new($"Unhandled ledger call ending: {ending.GetType().Name}");
}
