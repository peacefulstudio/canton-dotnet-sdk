// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;

namespace Canton.Ledger.Kernel.Wire;

internal static class ParsedLedgerErrorExtensions
{
    internal static LedgerOperationException ToException(this ParsedLedgerError parsed) => parsed switch
    {
        ParsedLedgerError.Structured structured => new LedgerOperationException(
            structured.Message, structured.Category, structured.ErrorId, structured.Metadata),
        ParsedLedgerError.Unstructured unstructured => new LedgerOperationException(
            unstructured.Message, unstructured.Status, unstructured.Category),
        _ => throw new InvalidOperationException($"Unhandled parsed ledger error: {parsed.GetType().Name}"),
    };
}
