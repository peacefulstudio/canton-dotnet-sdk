// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Testing;

/// <summary>
/// Static factories for the <see cref="CompletionStreamEvent"/> variants the command completion
/// stream yields. Each thinly wraps the corresponding public record constructor so a test can
/// stage completion events without hand-writing the constructor calls. Pair these with
/// <see cref="FakeLedgerClientBuilder.WithCompletionEvents"/>.
/// </summary>
public static class CompletionEvents
{
    /// <summary>Builds a <see cref="CompletionStreamEvent.CommandAccepted"/> event.</summary>
    /// <param name="completion">The neutral completion payload of the accepted command.</param>
    /// <param name="updateId">The id of the update the accepted command produced.</param>
    /// <returns>The accepted event.</returns>
    public static CompletionStreamEvent CommandAccepted(Completion completion, string updateId) =>
        new CompletionStreamEvent.CommandAccepted(completion, updateId);

    /// <summary>Builds a <see cref="CompletionStreamEvent.CommandRejected"/> event.</summary>
    /// <param name="completion">The neutral completion payload of the rejected command.</param>
    /// <param name="status">The rejection verdict: a non-zero <c>google.rpc.Code</c> and its detail.</param>
    /// <returns>The rejected event.</returns>
    public static CompletionStreamEvent CommandRejected(Completion completion, CompletionStatus status) =>
        new CompletionStreamEvent.CommandRejected(completion, status);

    /// <summary>Builds a <see cref="CompletionStreamEvent.Checkpoint"/> event.</summary>
    /// <param name="offset">The participant's current ledger offset.</param>
    /// <returns>The checkpoint event.</returns>
    public static CompletionStreamEvent Checkpoint(LedgerOffset offset) =>
        new CompletionStreamEvent.Checkpoint(offset);

    /// <summary>Builds an in-band <see cref="CompletionStreamEvent.StreamError"/> event.</summary>
    /// <param name="status">What the transport reported for the fault.</param>
    /// <param name="message">Status detail from the participant or transport.</param>
    /// <param name="category">Classification of the transport failure, or <c>null</c> to stage a
    /// fault the transport could not classify.</param>
    /// <param name="errorId">The participant's own Canton error code, or <c>null</c> to stage a
    /// fault that carried no structured error to read one from.</param>
    /// <param name="sourceException">Transport exception that ended the stream, or <c>null</c> to
    /// stage a fault carried in-band rather than thrown.</param>
    /// <returns>The stream-error event.</returns>
    public static CompletionStreamEvent StreamError(
        TransportStatus status,
        string message,
        DamlErrorCategory? category = null,
        string? errorId = null,
        Exception? sourceException = null) =>
        new CompletionStreamEvent.StreamError(status, message, category, errorId, sourceException);
}
