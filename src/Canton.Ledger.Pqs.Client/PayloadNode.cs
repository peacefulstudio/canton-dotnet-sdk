// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Pqs.Client;

internal sealed record PayloadNode(SqlFragment Json, SqlFragment Text, SqlFragment? Guard, SqlFragment? AbsentDefault)
{
    public static PayloadNode Root(SqlFragment json) => new(json, TextOf(json), null, null);

    public PayloadNode Derived(SqlFragment json) => new(json, TextOf(json), Guard, null);

    public PayloadNode Field(SqlFragment key) =>
        new(SqlFragment.Of($"{Json}->{key}"), SqlFragment.Of($"{Json}->>{key}"), Guard, null);

    public PayloadNode DefaultingTo(SqlFragment absentDefault) => this with { AbsentDefault = absentDefault };

    public SqlFragment IsSome(bool listEncoded) =>
        listEncoded
            ? SqlFragment.Of($"(jsonb_typeof({Json}) = 'array' AND {Json} <> '[]'::jsonb)")
            : SqlFragment.Of($"jsonb_typeof({Json}) <> 'null'");

    public SqlFragment IsNone(bool listEncoded) =>
        listEncoded
            ? SqlFragment.Of($"COALESCE({Json}, '[]'::jsonb) = '[]'::jsonb")
            : SqlFragment.Of($"COALESCE(jsonb_typeof({Json}), 'null') = 'null'");

    public PayloadNode Guarded(SqlFragment condition) =>
        this with { Guard = Guard is null ? condition : SqlFragment.Of($"{Guard} AND {condition}") };

    public SqlFragment Apply(SqlFragment predicate) =>
        Guard is null ? predicate : SqlFragment.Of($"CASE WHEN {Guard} THEN {predicate} END");

    private static SqlFragment TextOf(SqlFragment json) => SqlFragment.Of($"{json} #>> '{{}}'");
}
