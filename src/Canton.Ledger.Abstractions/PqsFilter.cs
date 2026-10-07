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
    internal abstract T Accept<T>(IPqsFilterVisitor<T> visitor);
}
