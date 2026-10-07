// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;

namespace Canton.Ledger.Abstractions;

internal sealed record PqsAll(ImmutableArray<PqsFilter> Filters) : PqsFilter
{
    internal override T Accept<T>(IPqsFilterVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed record PqsAny(ImmutableArray<PqsFilter> Filters) : PqsFilter
{
    internal override T Accept<T>(IPqsFilterVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed record PqsNot(PqsFilter Operand) : PqsFilter
{
    internal override T Accept<T>(IPqsFilterVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed record PqsCompare(PqsPath Path, PqsComparison Comparison, PqsOperand Operand) : PqsFilter
{
    internal override T Accept<T>(IPqsFilterVisitor<T> visitor) => visitor.Visit(this);

    protected override bool PrintMembers(StringBuilder builder) => false;
}

internal sealed record PqsOptionalPresence(PqsPath Optional, bool IsSome, bool ListEncoded) : PqsFilter
{
    internal override T Accept<T>(IPqsFilterVisitor<T> visitor) => visitor.Visit(this);

    protected override bool PrintMembers(StringBuilder builder) => false;
}

internal sealed record PqsNotNull(PqsPath Path) : PqsFilter
{
    internal override T Accept<T>(IPqsFilterVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed record PqsListAny(PqsPath List, PqsScope Element, PqsFilter? Condition) : PqsFilter
{
    internal override T Accept<T>(IPqsFilterVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed record PqsListAll(PqsPath List, PqsScope Element, PqsFilter Condition) : PqsFilter
{
    internal override T Accept<T>(IPqsFilterVisitor<T> visitor) => visitor.Visit(this);
}

internal enum PqsComparison
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}
