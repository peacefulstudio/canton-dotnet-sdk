// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Resilience;

/// <summary>
/// Stops a retry pipeline after an attempt was already sent, carrying the failure of that sent attempt
/// so the transport can surface it instead of the later failure that ended the retries.
/// </summary>
internal sealed class RetryAbandonedException(Exception sentAttemptFailure) : Exception
{
    internal Exception SentAttemptFailure { get; } = sentAttemptFailure;
}
