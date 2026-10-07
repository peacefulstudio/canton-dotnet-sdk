// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;

namespace Daml.Ledger.Abstractions;

/// <summary>The read capability: query participant ledger state.</summary>
public interface ILedgerReader
{
    /// <summary>Gets the current end of the participant's ledger.</summary>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport — see
    /// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(Daml.Runtime.Commands.ExerciseCommand, Daml.Runtime.Commands.SubmitterInfo, string?, Daml.Runtime.Commands.CommandId?, TimeSpan?, CancellationToken)"/>
    /// for the deadline contract.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The current ledger-end offset on the participant.</returns>
    /// <exception cref="LedgerOperationException">
    /// The call failed: the participant could not be reached, did not answer within the deadline, rejected the
    /// call, or answered with a body the client could not decode. A dead ledger surfaces here, not as a
    /// transport exception, with a transport-native <see cref="LedgerOperationException.Status"/> —
    /// <c>Grpc</c> with <c>Unavailable</c> or <c>DeadlineExceeded</c> on gRPC, <c>NoResponse</c> on the JSON Ledger
    /// API — and the transport's own exception as <see cref="Exception.InnerException"/>. A read changes nothing, so
    /// <see cref="LedgerOperationException.CommitState"/> is always <see cref="CommitState.NotCommitted"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
