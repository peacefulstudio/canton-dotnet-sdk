// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Pqs.Client;

internal sealed class PqsSqlRenderer : IPqsFilterVisitor<SqlFragment>, IPqsPathVisitor<PayloadNode>
{
    private static readonly SqlFragment PayloadColumn = SqlFragment.Of($"payload");
    private static readonly SqlFragment VariantTagKey = SqlFragment.JsonKey("tag");
    private static readonly SqlFragment FirstElement = SqlFragment.Of($"0");
    private static readonly SqlFragment AndSeparator = SqlFragment.Of($" AND ");
    private static readonly SqlFragment OrSeparator = SqlFragment.Of($" OR ");

    private PqsSqlRenderer()
    {
    }

    public static PqsSqlRenderer Instance { get; } = new();

    public SqlFragment Visit(PqsAll filter) => Junction(filter.Filters, AndSeparator);

    public SqlFragment Visit(PqsAny filter) => Junction(filter.Filters, OrSeparator);

    public SqlFragment Visit(PqsNot filter) => SqlFragment.Of($"NOT COALESCE({filter.Operand.Accept(this)}, FALSE)");

    public SqlFragment Visit(PqsCompare filter)
    {
        var node = filter.Path.Accept(this);
        var typed = Typed(filter.Operand.Kind, node.Text);
        var value = node.AbsentDefault is { } absentDefault ? SqlFragment.Of($"COALESCE({typed}, {absentDefault})") : typed;
        return node.Apply(SqlFragment.Of($"{value} {Operator(filter.Comparison)} {Operand(filter.Operand)}"));
    }

    public SqlFragment Visit(PqsOptionalPresence filter)
    {
        var node = filter.Optional.Accept(this);
        return node.Apply(filter.IsSome ? node.IsSome(filter.ListEncoded) : node.IsNone(filter.ListEncoded));
    }

    public SqlFragment Visit(PqsNotNull filter)
    {
        var node = filter.Path.Accept(this);
        return node.Apply(SqlFragment.Of($"{node.Json} IS NOT NULL"));
    }

    public SqlFragment Visit(PqsListAny filter)
    {
        var (list, elements) = Elements(filter.List, filter.Element);
        return list.Apply(filter.Condition is { } condition
            ? SqlFragment.Of($"EXISTS ({elements} WHERE {condition.Accept(this)})")
            : SqlFragment.Of($"EXISTS ({elements})"));
    }

    public SqlFragment Visit(PqsListAll filter)
    {
        var (list, elements) = Elements(filter.List, filter.Element);
        return list.Apply(SqlFragment.Of($"NOT EXISTS ({elements} WHERE NOT COALESCE({filter.Condition.Accept(this)}, FALSE))"));
    }

    public PayloadNode Visit(PqsScopeRoot path) =>
        PayloadNode.Root(path.Scope.AliasOrdinal is { } ordinal
            ? SqlFragment.Of($"{SqlFragment.Alias('e', ordinal)}.value")
            : PayloadColumn);

    public PayloadNode Visit(PqsField path) => path.Parent.Accept(this).Field(SqlFragment.JsonKey(path.Name));

    public PayloadNode Visit(PqsFirstElement path) => path.Optional.Accept(this).Field(FirstElement);

    public PayloadNode Visit(PqsSomeCast path)
    {
        var optional = path.Optional.Accept(this);
        return optional.Guarded(optional.IsSome(path.ListEncoded));
    }

    public PayloadNode Visit(PqsConstructorCast path)
    {
        var variant = path.Variant.Accept(this);
        return variant.Guarded(
            SqlFragment.Of($"{variant.Field(VariantTagKey).Text} = {SqlFragment.Parameter(path.Tag)}"));
    }

    public PayloadNode Visit(PqsMapEntry path)
    {
        var map = path.Map.Accept(this);
        var alias = SqlFragment.Alias('m', path.AliasOrdinal);
        var genMapLookup = SqlFragment.Of(
            $"(SELECT {alias}.value->1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof({map.Json}) = 'array' THEN {map.Json} END) " +
            $"AS {alias}(value) WHERE {Typed(path.Key.Kind, SqlFragment.Of($"{alias}.value->>0"))} = {Operand(path.Key)} LIMIT 1)");
        var lookup = path.Key.Kind == PqsLeafKind.Text
            ? SqlFragment.Of($"COALESCE({map.Json}->{SqlFragment.Parameter(path.Key.Value)}, {genMapLookup})")
            : genMapLookup;
        return map.Derived(lookup);
    }

    public PayloadNode Visit(PqsDefaulted path) => path.Value.Accept(this).DefaultingTo(Operand(path.Default));

    private (PayloadNode List, SqlFragment Elements) Elements(PqsPath listPath, PqsScope element)
    {
        var list = listPath.Accept(this);
        var alias = SqlFragment.Alias('e', element.AliasOrdinal!.Value);
        return (list, SqlFragment.Of(
            $"SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof({list.Json}) = 'array' THEN {list.Json} END) AS {alias}(value)"));
    }

    private SqlFragment Junction(ImmutableArray<PqsFilter> filters, SqlFragment separator) =>
        SqlFragment.Of($"({SqlFragment.Join(separator, filters.Select(filter => filter.Accept(this)))})");

    private static SqlFragment Typed(PqsLeafKind kind, SqlFragment text) =>
        kind switch
        {
            PqsLeafKind.Int64 => SqlFragment.Of($"({text})::bigint"),
            PqsLeafKind.Numeric => SqlFragment.Of($"({text})::numeric"),
            PqsLeafKind.Bool => SqlFragment.Of($"({text})::boolean"),
            PqsLeafKind.Date => SqlFragment.Of($"({text})::date"),
            PqsLeafKind.Time => SqlFragment.Of($"({text})::timestamptz"),
            _ => text,
        };

    private static SqlFragment Operand(PqsOperand operand) =>
        operand is { Kind: PqsLeafKind.Numeric, Value: string literal }
            ? SqlFragment.Of($"{SqlFragment.Parameter(literal)}::numeric")
            : SqlFragment.Parameter(operand.Value);

    private static SqlFragment Operator(PqsComparison comparison) =>
        comparison switch
        {
            PqsComparison.Equal => SqlFragment.Of($"="),
            PqsComparison.NotEqual => SqlFragment.Of($"IS DISTINCT FROM"),
            PqsComparison.LessThan => SqlFragment.Of($"<"),
            PqsComparison.LessThanOrEqual => SqlFragment.Of($"<="),
            PqsComparison.GreaterThan => SqlFragment.Of($">"),
            PqsComparison.GreaterThanOrEqual => SqlFragment.Of($">="),
            _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null),
        };
}
