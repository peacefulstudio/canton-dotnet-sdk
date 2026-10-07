// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Kernel.Wire;

internal static class ParsedLedgerErrorExtensions
{
    internal static LedgerOperationException ToException(
        this ParsedLedgerError parsed, LedgerCallKind kind, Exception? cause = null) => parsed switch
    {
        ParsedLedgerError.Structured structured => LedgerOperationException.FromFailedCall(
            structured.Message,
            structured.Status,
            structured.Category,
            structured.ErrorId,
            structured.Metadata,
            cause,
            CommitStateOf(structured, kind)),
        ParsedLedgerError.Unstructured unstructured => LedgerOperationException.FromFailedCall(
            unstructured.Message,
            unstructured.Status,
            unstructured.Category,
            errorId: null,
            metadata: null,
            cause,
            CommitStateOf(unstructured, kind)),
        _ => throw new InvalidOperationException($"Unhandled parsed ledger error: {parsed.GetType().Name}"),
    };

    private static CommitState CommitStateOf(ParsedLedgerError parsed, LedgerCallKind kind) => kind switch
    {
        LedgerCallKind.Read => CommitState.NotCommitted,
        LedgerCallKind.AcceptedOnlyWrite or LedgerCallKind.EffectAppliedWrite => CommitStateFromError(parsed),
        _ => throw new InvalidOperationException($"Unhandled ledger call kind: {kind}"),
    };

    private static CommitState CommitStateFromError(ParsedLedgerError parsed) => parsed switch
    {
        ParsedLedgerError.Structured structured => ParticipantRejectionCommitState.Of(
            structured.Category, structured.ErrorId, structured.Metadata),
        _ => CommitState.Unknown,
    };
}
