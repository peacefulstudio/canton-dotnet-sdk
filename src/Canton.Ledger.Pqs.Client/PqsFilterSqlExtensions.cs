// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Pqs.Client;

internal static class PqsFilterSqlExtensions
{
    public static string ToSqlClause(
        this PqsFilter filter,
        ICollection<(string Name, object Value)> parameters,
        ref int paramIndex) =>
        filter.Accept(PqsSqlRenderer.Instance).Render(parameters, ref paramIndex);
}
