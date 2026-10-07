// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Rest.Client;

internal abstract record RestCallFailure
{
    private RestCallFailure()
    {
    }

    internal sealed record Rejected(ParsedLedgerError Parsed, bool AfterRetry = false) : RestCallFailure;

    internal sealed record NoResponse(string Message, Exception Cause) : RestCallFailure;

    internal sealed record Undecodable(string Message, Exception? Cause, string? UpdateId) : RestCallFailure;
}
