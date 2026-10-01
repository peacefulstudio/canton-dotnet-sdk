// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Stdlib;

namespace Canton.Ledger.Abstractions;

internal sealed record PayloadNode(SqlFragment Json, SqlFragment Text, Type ClrType, SqlFragment? Guard)
{
    private static readonly SqlFragment FirstElement = SqlFragment.Of($"0");

    public bool InOptionalChain { get; private init; }

    public bool DefaultsWhenAbsent { get; private init; }

    public bool IsListEncodedOptional =>
        OptionalElementType(ClrType) is { } element && (InOptionalChain || OptionalElementType(element) is not null);

    public static PayloadNode Root(SqlFragment json, Type clrType) =>
        new(json, SqlFragment.Of($"{json} #>> '{{}}'"), clrType, null);

    public static Type? OptionalElementType(Type type)
    {
        for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(Optional<>))
                return candidate.GetGenericArguments()[0];
        }

        return null;
    }

    public PayloadNode Derived(SqlFragment json, Type clrType) =>
        new(json, SqlFragment.Of($"{json} #>> '{{}}'"), clrType, Guard);

    public PayloadNode Field(SqlFragment key, Type clrType) =>
        new(SqlFragment.Of($"{Json}->{key}"), SqlFragment.Of($"{Json}->>{key}"), clrType, Guard);

    public PayloadNode OptionalValue(Type clrType) =>
        IsListEncodedOptional ? Field(FirstElement, clrType) with { InOptionalChain = true } : As(clrType);

    public PayloadNode DefaultingWhenAbsent() => this with { DefaultsWhenAbsent = true };

    public SqlFragment IsSome() =>
        IsListEncodedOptional
            ? SqlFragment.Of($"(jsonb_typeof({Json}) = 'array' AND {Json} <> '[]'::jsonb)")
            : SqlFragment.Of($"jsonb_typeof({Json}) <> 'null'");

    public SqlFragment IsNone() =>
        IsListEncodedOptional
            ? SqlFragment.Of($"COALESCE({Json}, '[]'::jsonb) = '[]'::jsonb")
            : SqlFragment.Of($"COALESCE(jsonb_typeof({Json}), 'null') = 'null'");

    public PayloadNode As(Type clrType) => this with { ClrType = clrType };

    public PayloadNode Guarded(SqlFragment condition) =>
        this with { Guard = Guard is null ? condition : SqlFragment.Of($"{Guard} AND {condition}") };

    public SqlFragment Apply(SqlFragment predicate) =>
        Guard is null ? predicate : SqlFragment.Of($"CASE WHEN {Guard} THEN {predicate} END");
}
