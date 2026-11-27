// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace Canton.Ledger.AspNetCore;

internal static partial class ErrorCodeId
{
    internal static string? OrNull(string? errorId) =>
        errorId is not null && Shape().IsMatch(errorId) ? errorId : null;

    [GeneratedRegex(@"\A[A-Z][A-Z0-9_]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
