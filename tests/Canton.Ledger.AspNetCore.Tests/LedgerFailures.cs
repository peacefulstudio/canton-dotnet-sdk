// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.AspNetCore.Tests;

internal static class LedgerFailures
{
    private static readonly Identifier ConnectionRequest = new("pkg-1", "Connections.ConnectionRequest", "ConnectionRequest");

    internal static LedgerOperationException CommittedUndecodable(string? updateId, string? commandId) =>
        Unwrap(new ExerciseOutcome<int>.CommittedUndecodable(updateId, "bad payload") { CommandId = commandId });

    internal static LedgerOperationException Rejected(
        DamlErrorCategory category,
        string errorId = "SOME_ERROR",
        string message = "rejected",
        IReadOnlyDictionary<string, string>? metadata = null,
        string? commandId = null) =>
        Unwrap(new ExerciseOutcome<int>.DamlError(category, errorId, message, metadata ?? new Dictionary<string, string>())
        {
            CommandId = commandId,
        });

    internal static LedgerOperationException Committed(ExerciseOutcome<int> outcome) => Unwrap(outcome);

    internal static LedgerOperationException OutcomeUnknown(string? commandId) =>
        Unwrap(new ExerciseOutcome<int>.InfraError(new TransportStatus.NoResponse(), "connection reset") { CommandId = commandId });

    internal static LedgerOperationException RedactedRefusal(TransportStatus status, DamlErrorCategory? category = null) =>
        Unwrap(new ExerciseOutcome<int>.InfraError(status, "redacted by the participant", category));

    internal static LedgerOperationException RedactedRefusalAfterRetry(TransportStatus status, string commandId)
    {
        var exception = Unwrap(new ExerciseOutcome<int>.InfraError(status, "redacted by the participant")
        {
            CommandId = commandId,
        });
        exception.RejectedAfterRetry = true;
        return exception;
    }

    private static LedgerOperationException Unwrap(ExerciseOutcome<int> outcome)
    {
        var named = outcome with { Operation = new LedgerOperation.Command(ConnectionRequest, "Accept") };
        try
        {
            named.OneOrThrow();
        }
        catch (LedgerOperationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The outcome did not fail.");
    }
}
