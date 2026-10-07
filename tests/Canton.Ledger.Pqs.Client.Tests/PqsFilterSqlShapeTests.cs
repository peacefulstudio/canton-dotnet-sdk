// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsFilterSqlShapeTests
{
    [Fact]
    public void And_restarts_list_aliases_in_each_Where()
    {
        var (sql, parameters) = Render(Filter.And(
            Filter.Where<RichRecord>(r => r.Tags.Any()),
            Filter.Where<RichRecord>(r => r.HoldingCids.Any())));

        sql.Should().Be(
            "(EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'tags') = 'array' " +
            "THEN payload->'tags' END) AS e0(value)) AND " +
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'holdingCids') = 'array' " +
            "THEN payload->'holdingCids' END) AS e0(value)))");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void Or_restarts_map_aliases_and_continues_parameter_numbering_in_each_Where()
    {
        var (sql, parameters) = Render(Filter.Or(
            Filter.Where<RichRecord>(r => r.Attributes["tier"] == "gold"),
            Filter.Where<RichRecord>(r => r.Attributes["tier"] == "silver")));

        sql.Should().Be(
            "(COALESCE(payload->'attributes'->@p0, (SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN " +
            "jsonb_typeof(payload->'attributes') = 'array' THEN payload->'attributes' END) AS m0(value) " +
            "WHERE m0.value->>0 = @p1 LIMIT 1)) #>> '{}' = @p2 OR " +
            "COALESCE(payload->'attributes'->@p3, (SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN " +
            "jsonb_typeof(payload->'attributes') = 'array' THEN payload->'attributes' END) AS m0(value) " +
            "WHERE m0.value->>0 = @p4 LIMIT 1)) #>> '{}' = @p5)");
        parameters.Should().Equal(
            ("@p0", (object)"tier"), ("@p1", (object)"tier"), ("@p2", (object)"gold"),
            ("@p3", (object)"tier"), ("@p4", (object)"tier"), ("@p5", (object)"silver"));
    }

    [Fact]
    public void Where_numbers_map_and_list_aliases_from_one_counter_in_translation_order()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(
            r => r.Attributes["tier"] == "gold"
                && r.Tags.Any(tag => tag == "a" && r.Attributes["kind"] == "b")
                && r.HoldingCids.Any()));

        sql.Should().Be(
            "((COALESCE(payload->'attributes'->@p0, (SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN " +
            "jsonb_typeof(payload->'attributes') = 'array' THEN payload->'attributes' END) AS m0(value) " +
            "WHERE m0.value->>0 = @p1 LIMIT 1)) #>> '{}' = @p2 AND " +
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'tags') = 'array' " +
            "THEN payload->'tags' END) AS e1(value) WHERE (e1.value #>> '{}' = @p3 AND " +
            "COALESCE(payload->'attributes'->@p4, (SELECT m2.value->1 FROM jsonb_array_elements(CASE WHEN " +
            "jsonb_typeof(payload->'attributes') = 'array' THEN payload->'attributes' END) AS m2(value) " +
            "WHERE m2.value->>0 = @p5 LIMIT 1)) #>> '{}' = @p6))) AND " +
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'holdingCids') = 'array' " +
            "THEN payload->'holdingCids' END) AS e3(value)))");
        parameters.Should().Equal(
            ("@p0", (object)"tier"), ("@p1", (object)"tier"), ("@p2", (object)"gold"),
            ("@p3", (object)"a"),
            ("@p4", (object)"kind"), ("@p5", (object)"kind"), ("@p6", (object)"b"));
    }

    [Fact]
    public void Field_reads_a_TextMap_value()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.Attributes["tier"], "gold"));

        sql.Should().Be(
            "COALESCE(payload->'attributes'->@p0, (SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN " +
            "jsonb_typeof(payload->'attributes') = 'array' THEN payload->'attributes' END) AS m0(value) " +
            "WHERE m0.value->>0 = @p1 LIMIT 1)) #>> '{}' = @p2");
        parameters.Should().Equal(("@p0", (object)"tier"), ("@p1", (object)"tier"), ("@p2", (object)"gold"));
    }

    [Fact]
    public void Field_reads_an_Optional_inside_a_flat_Optional_record()
    {
        var (sql, parameters) = Render(Filter.Field<TypeCorners>(t => t.NestedNote!.Item, "inner"));

        sql.Should().Be("payload->'nestedNote'->>'item' = @p0");
        parameters.Should().Equal(("@p0", (object)"inner"));
    }

    [Fact]
    public void Field_defaults_an_absent_Optional_Int64_read_with_GetValueOrDefault()
    {
        var (sql, parameters) = Render(Filter.Field<ShapeCorners>(s => s.MaybeCount.GetValueOrDefault(), "3"));

        sql.Should().Be("COALESCE((payload->>'maybeCount')::bigint, @p0) = @p1");
        parameters.Should().Equal(("@p0", (object)0L), ("@p1", (object)3L));
    }

    [Fact]
    public void Field_guards_a_Numeric_read_through_a_variant_constructor()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => ((Outcome.Win)r.Outcome).Value.Prize, "10.5"));

        sql.Should().Be(
            "CASE WHEN payload->'outcome'->>'tag' = @p0 THEN (payload->'outcome'->'value'->>'prize')::numeric = @p1::numeric END");
        parameters.Should().Equal(("@p0", (object)"Win"), ("@p1", (object)"10.5"));
    }

    [Fact]
    public void Where_treats_a_missing_constructor_under_a_Some_guard_as_a_tag_mismatch()
    {
        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(
            s => (((Optional<Outcome>.Some)s.MaybeOutcome).Value as Outcome.Win) == null));

        sql.Should().Be(
            "CASE WHEN jsonb_typeof(payload->'maybeOutcome') <> 'null' " +
            "THEN payload->'maybeOutcome'->>'tag' IS DISTINCT FROM @p0 END");
        parameters.Should().Equal(("@p0", (object)"Win"));
    }

    [Fact]
    public void Where_treats_a_missing_constructor_under_a_constructor_guard_as_a_tag_mismatch()
    {
        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(
            s => (((Either<Outcome, long>.Left)s.OutcomeOrRank).Value as Outcome.Win) == null));

        sql.Should().Be(
            "CASE WHEN payload->'outcomeOrRank'->>'tag' = @p0 " +
            "THEN payload->'outcomeOrRank'->'value'->>'tag' IS DISTINCT FROM @p1 END");
        parameters.Should().Equal(("@p0", (object)"Left"), ("@p1", (object)"Win"));
    }

    [Fact]
    public void Where_tests_a_variant_constructor_under_a_constructor_guard()
    {
        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(
            s => ((Either<Outcome, long>.Left)s.OutcomeOrRank).Value is Outcome.Pending));

        sql.Should().Be(
            "CASE WHEN payload->'outcomeOrRank'->>'tag' = @p0 " +
            "THEN payload->'outcomeOrRank'->'value'->>'tag' = @p1 END");
        parameters.Should().Equal(("@p0", (object)"Left"), ("@p1", (object)"Pending"));
    }

    [Fact]
    public void Where_tests_List_membership_of_a_ContractId_by_its_identifier()
    {
        var holding = new ContractId<IHolding>("00def");

        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.HoldingCids.Contains(holding)));

        sql.Should().Be(
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'holdingCids') = 'array' " +
            "THEN payload->'holdingCids' END) AS e0(value) WHERE e0.value #>> '{}' = @p0)");
        parameters.Should().Equal(("@p0", (object)"00def"));
    }

    [Fact]
    public void Where_tests_List_membership_of_a_Time_in_utc()
    {
        var stamp = new DateTimeOffset(2026, 5, 29, 15, 30, 0, TimeSpan.FromHours(2));

        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(s => s.Stamps.Contains(stamp)));

        sql.Should().Be(
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'stamps') = 'array' " +
            "THEN payload->'stamps' END) AS e0(value) WHERE (e0.value #>> '{}')::timestamptz = @p0)");
        var bound = (DateTimeOffset)parameters.Single().Value;
        bound.Should().Be(new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero));
        bound.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Where_tests_List_membership_of_an_enum_by_its_Daml_constructor_name()
    {
        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(s => s.Suits.Contains(Suit.Hearts)));

        sql.Should().Be(
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'suits') = 'array' " +
            "THEN payload->'suits' END) AS e0(value) WHERE e0.value #>> '{}' = @p0)");
        parameters.Should().Equal(("@p0", (object)"Hearts"));
    }

    [Fact]
    public void Where_guards_a_List_predicate_read_through_a_cast_to_Some()
    {
        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(
            s => ((Optional<IReadOnlyList<string>>.Some)s.MaybeTags).Value.Contains("a")));

        sql.Should().Be(
            "CASE WHEN jsonb_typeof(payload->'maybeTags') <> 'null' THEN " +
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'maybeTags') = 'array' " +
            "THEN payload->'maybeTags' END) AS e0(value) WHERE e0.value #>> '{}' = @p0) END");
        parameters.Should().Equal(("@p0", (object)"a"));
    }

    [Fact]
    public void Where_guards_a_Map_key_presence_read_through_a_cast_to_Some()
    {
        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(
            s => ((Optional<IReadOnlyDictionary<long, string>>.Some)s.MaybeLabels).Value.ContainsKey(4)));

        sql.Should().Be(
            "CASE WHEN jsonb_typeof(payload->'maybeLabels') <> 'null' THEN " +
            "(SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'maybeLabels') = 'array' " +
            "THEN payload->'maybeLabels' END) AS m0(value) WHERE (m0.value->>0)::bigint = @p0 LIMIT 1) IS NOT NULL END");
        parameters.Should().Equal(("@p0", (object)4L));
    }

    [Fact]
    public void Where_tests_TextMap_key_presence_in_both_encodings()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Attributes.ContainsKey("tier")));

        sql.Should().Be(
            "COALESCE(payload->'attributes'->@p0, (SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN " +
            "jsonb_typeof(payload->'attributes') = 'array' THEN payload->'attributes' END) AS m0(value) " +
            "WHERE m0.value->>0 = @p1 LIMIT 1)) IS NOT NULL");
        parameters.Should().Equal(("@p0", (object)"tier"), ("@p1", (object)"tier"));
    }

    [Fact]
    public void Where_treats_a_missing_Map_key_as_distinct_from_any_value()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(t => t.LabelByRank[2] != "second"));

        sql.Should().Be(
            "(SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'labelByRank') = 'array' " +
            "THEN payload->'labelByRank' END) AS m0(value) WHERE (m0.value->>0)::bigint = @p0 LIMIT 1) #>> '{}' " +
            "IS DISTINCT FROM @p1");
        parameters.Should().Equal(("@p0", (object)2L), ("@p1", (object)"second"));
    }

    [Fact]
    public void Where_negates_a_List_predicate_with_absence_reading_as_false()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => !r.Tags.Any()));

        sql.Should().Be(
            "NOT COALESCE(EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'tags') = 'array' " +
            "THEN payload->'tags' END) AS e0(value)), FALSE)");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void Where_tests_a_flat_Optional_for_Some_by_type_pattern()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(t => t.Crate.Item is Optional<string>.Some));

        sql.Should().Be("jsonb_typeof(payload->'crate'->'item') <> 'null'");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void Where_compares_an_enum_converted_to_its_underlying_integer_by_its_Daml_constructor_name()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => (int)r.Suit == 1));

        sql.Should().Be("payload->>'suit' = @p0");
        parameters.Should().Equal(("@p0", (object)"Diamonds"));
    }

    [Fact]
    public void Where_defaults_an_absent_Optional_Int64_read_through_Optional_GetValueOrDefault()
    {
        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(s => s.MaybeRank.GetValueOrDefault() >= 2));

        sql.Should().Be("COALESCE((payload->>'maybeRank')::bigint, @p0) >= @p1");
        parameters.Should().Equal(("@p0", (object)0L), ("@p1", (object)2L));
    }

    [Fact]
    public void Where_reads_a_value_type_Optional_inside_a_nested_Optional_by_list_index()
    {
        var (sql, parameters) = Render(Filter.Where<ShapeCorners>(
            s => s.MaybeMaybeRank.GetValueOrDefault()!.GetValueOrDefault() == 5));

        sql.Should().Be("COALESCE((payload->'maybeMaybeRank'->0->>0)::bigint, @p0) = @p1");
        parameters.Should().Equal(("@p0", (object)0L), ("@p1", (object)5L));
    }

    private static (string Sql, List<(string Name, object Value)> Parameters) Render(PqsFilter filter)
    {
        var parameters = new List<(string Name, object Value)>();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(parameters, ref paramIndex);
        return (sql, parameters);
    }

    private sealed record ShapeCorners(
        [property: DamlFieldAttribute("maybeCount")] long? MaybeCount,
        [property: DamlFieldAttribute("maybeRank")] Optional<long> MaybeRank,
        [property: DamlFieldAttribute("maybeMaybeRank")] Optional<Optional<long>> MaybeMaybeRank,
        [property: DamlFieldAttribute("maybeOutcome")] Optional<Outcome> MaybeOutcome,
        [property: DamlFieldAttribute("outcomeOrRank")] Either<Outcome, long> OutcomeOrRank,
        [property: DamlFieldAttribute("stamps")] IReadOnlyList<DateTimeOffset> Stamps,
        [property: DamlFieldAttribute("suits")] IReadOnlyList<Suit> Suits,
        [property: DamlFieldAttribute("maybeTags")] Optional<IReadOnlyList<string>> MaybeTags,
        [property: DamlFieldAttribute("maybeLabels")] Optional<IReadOnlyDictionary<long, string>> MaybeLabels) : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "Test.Module", "ShapeCorners");
        public static string PackageId => "pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => throw new NotSupportedException();
    }
}
