// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Represents a filter condition for PQS queries.
/// Filters are built via the <see cref="Filter"/> static class and generate
/// parameterized SQL WHERE clauses. Field names are derived from strongly-typed
/// expressions — never from user input — eliminating SQL injection by construction.
/// </summary>
public abstract record PqsFilter
{
    internal abstract string ToSqlClause(ICollection<(string Name, object Value)> parameters, ref int paramIndex);

    internal sealed record Predicate(SqlFragment Sql) : PqsFilter
    {
        internal override string ToSqlClause(ICollection<(string Name, object Value)> parameters, ref int paramIndex) =>
            Sql.Render(parameters, ref paramIndex);
    }

    internal sealed record OrFilter(PqsFilter[] Filters) : PqsFilter
    {
        internal override string ToSqlClause(ICollection<(string Name, object Value)> parameters, ref int paramIndex)
        {
            var parts = new string[Filters.Length];
            for (var i = 0; i < Filters.Length; i++)
                parts[i] = Filters[i].ToSqlClause(parameters, ref paramIndex);
            return $"({string.Join(" OR ", parts)})";
        }
    }

    internal sealed record AndFilter(PqsFilter[] Filters) : PqsFilter
    {
        internal override string ToSqlClause(ICollection<(string Name, object Value)> parameters, ref int paramIndex)
        {
            var parts = new string[Filters.Length];
            for (var i = 0; i < Filters.Length; i++)
                parts[i] = Filters[i].ToSqlClause(parameters, ref paramIndex);
            return $"({string.Join(" AND ", parts)})";
        }
    }
}
