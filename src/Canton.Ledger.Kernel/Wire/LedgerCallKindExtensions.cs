// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Kernel.Wire;

internal static class LedgerCallKindExtensions
{
    internal static LedgerOperationException NoAnswer(
        this LedgerCallKind kind, string message, TransportStatus status, Exception? cause) =>
        LedgerOperationException.FromFailedCall(
            message, status, category: null, errorId: null, metadata: null, cause, CommitStateWhenUnanswered(kind));

    internal static LedgerOperationException UnreadableResponse(
        this LedgerCallKind kind, string message, Exception? cause) =>
        LedgerOperationException.FromFailedCall(
            message,
            new TransportStatus.UndecodableBody(),
            category: null,
            errorId: null,
            metadata: null,
            cause,
            CommitStateWhenUnreadable(kind));

    private static CommitState CommitStateWhenUnanswered(LedgerCallKind kind) => kind switch
    {
        LedgerCallKind.Read => CommitState.NotCommitted,
        LedgerCallKind.AcceptedOnlyWrite or LedgerCallKind.EffectAppliedWrite => CommitState.Unknown,
        _ => throw new InvalidOperationException($"Unhandled ledger call kind: {kind}"),
    };

    private static CommitState CommitStateWhenUnreadable(LedgerCallKind kind) => kind switch
    {
        LedgerCallKind.Read => CommitState.NotCommitted,
        LedgerCallKind.AcceptedOnlyWrite => CommitState.Unknown,
        LedgerCallKind.EffectAppliedWrite => CommitState.Committed,
        _ => throw new InvalidOperationException($"Unhandled ledger call kind: {kind}"),
    };
}
