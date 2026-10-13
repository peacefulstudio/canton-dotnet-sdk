// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Kernel.Wire;

internal abstract record LedgerCallEnding
{
    private LedgerCallEnding()
    {
    }

    internal sealed record Rejected(ParsedLedgerError Error, Exception? Cause, bool AfterRetry = false) : LedgerCallEnding;

    internal sealed record Unanswered(string Message, Exception Cause) : LedgerCallEnding;

    internal sealed record NeverSent(string Message, Exception Cause) : LedgerCallEnding
    {
        internal NeverSent(TokenAcquisitionException tokenFailure)
            : this(tokenFailure.Message, tokenFailure)
        {
        }
    }

    internal sealed record Unreadable(string Message, Exception? Cause, string? UpdateId) : LedgerCallEnding;
}
