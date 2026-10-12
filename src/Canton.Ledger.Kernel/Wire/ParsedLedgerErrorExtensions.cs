// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Kernel.Wire;

internal static class ParsedLedgerErrorExtensions
{
    internal static ParsedLedgerError WithMessage(this ParsedLedgerError parsed, string message) => parsed switch
    {
        ParsedLedgerError.Structured structured => new ParsedLedgerError.Structured(
            structured.Category, structured.ErrorId, message, structured.Metadata, structured.Status),
        ParsedLedgerError.Unstructured unstructured => new ParsedLedgerError.Unstructured(
            message, unstructured.Status, unstructured.Category),
        _ => throw new InvalidOperationException($"Unhandled parsed ledger error: {parsed.GetType().Name}"),
    };
}
