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

public class PqsFilterTranslationTests
{
    [Fact]
    public void Field_compares_a_Numeric_field_as_numeric()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.Amount, "42.5"));

        sql.Should().Be("(payload->>'amount')::numeric = @p0::numeric");
        parameters.Should().Equal(("@p0", (object)"42.5"));
    }

    [Fact]
    public void Field_keeps_every_digit_of_a_Numeric_37_value()
    {
        var (sql, parameters) = Render(Filter.Field<TypeCorners>(
            t => t.Finest, "0.1234567890123456789012345678901234567"));

        sql.Should().Be("(payload->>'finest')::numeric = @p0::numeric");
        parameters.Should().Equal(("@p0", (object)"0.1234567890123456789012345678901234567"));
    }

    [Fact]
    public void Field_parses_the_Numeric_value_with_the_invariant_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
        try
        {
            var (_, parameters) = Render(Filter.Field<RichRecord>(r => r.Amount, "-1234.5"));

            parameters.Should().Equal(("@p0", (object)"-1234.5"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("42,5")]
    [InlineData("forty-two")]
    [InlineData("1e3")]
    public void Field_rejects_a_value_that_is_not_a_Daml_Numeric(string value)
    {
        var act = () => Filter.Field<RichRecord>(r => r.Amount, value);

        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(value))
            .WithMessage($"'{value}' is not a valid Daml Numeric value.*");
    }

    [Fact]
    public void Field_compares_an_Int64_field_as_bigint()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.Count, "-7"));

        sql.Should().Be("(payload->>'count')::bigint = @p0");
        parameters.Should().Equal(("@p0", (object)(-7L)));
    }

    [Fact]
    public void Field_rejects_an_Int64_value_out_of_range()
    {
        var act = () => Filter.Field<RichRecord>(r => r.Count, "9223372036854775808");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("value")
            .WithMessage("'9223372036854775808' is out of range for a Daml Int64 value.*");
    }

    [Fact]
    public void Field_compares_a_Bool_field_as_boolean()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.Active, "true"));

        sql.Should().Be("(payload->>'active')::boolean = @p0");
        parameters.Should().Equal(("@p0", (object)true));
    }

    [Fact]
    public void Field_compares_a_Date_field_as_date()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.AsOf, "2026-05-29"));

        sql.Should().Be("(payload->>'asOf')::date = @p0");
        parameters.Should().Equal(("@p0", (object)new DateOnly(2026, 5, 29)));
    }

    [Fact]
    public void Field_compares_a_Time_field_as_timestamptz_in_utc()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.ObservedAt, "2026-05-29T15:30:00+02:00"));

        sql.Should().Be("(payload->>'observedAt')::timestamptz = @p0");
        var bound = (DateTimeOffset)parameters.Single().Value;
        bound.Should().Be(new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero));
        bound.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Field_compares_a_Party_field_as_text()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.Owner, "alice::1220"));

        sql.Should().Be("payload->>'owner' = @p0");
        parameters.Should().Equal(("@p0", (object)"alice::1220"));
    }

    [Fact]
    public void Field_compares_a_ContractId_field_as_text()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.Marker, "00abc"));

        sql.Should().Be("payload->>'marker' = @p0");
        parameters.Should().Equal(("@p0", (object)"00abc"));
    }

    [Fact]
    public void Field_compares_an_enum_field_by_its_Daml_constructor_name()
    {
        var (sql, parameters) = Render(Filter.Field<RichRecord>(r => r.Suit, "Hearts"));

        sql.Should().Be("payload->>'suit' = @p0");
        parameters.Should().Equal(("@p0", (object)"Hearts"));
    }

    [Fact]
    public void Where_compares_a_Numeric_field_against_a_captured_value()
    {
        var threshold = 42.5m;

        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Amount == threshold));

        sql.Should().Be("(payload->>'amount')::numeric = @p0");
        parameters.Should().Equal(("@p0", (object)42.5m));
    }

    [Theory]
    [InlineData(ComparisonCase.LessThan, "(payload->>'count')::bigint < @p0")]
    [InlineData(ComparisonCase.LessThanOrEqual, "(payload->>'count')::bigint <= @p0")]
    [InlineData(ComparisonCase.GreaterThan, "(payload->>'count')::bigint > @p0")]
    [InlineData(ComparisonCase.GreaterThanOrEqual, "(payload->>'count')::bigint >= @p0")]
    [InlineData(ComparisonCase.NotEqual, "(payload->>'count')::bigint IS DISTINCT FROM @p0")]
    public void Where_translates_each_comparison_operator(ComparisonCase comparison, string expectedSql)
    {
        var filter = comparison switch
        {
            ComparisonCase.LessThan => Filter.Where<RichRecord>(r => r.Count < 5),
            ComparisonCase.LessThanOrEqual => Filter.Where<RichRecord>(r => r.Count <= 5),
            ComparisonCase.GreaterThan => Filter.Where<RichRecord>(r => r.Count > 5),
            ComparisonCase.GreaterThanOrEqual => Filter.Where<RichRecord>(r => r.Count >= 5),
            _ => Filter.Where<RichRecord>(r => r.Count != 5),
        };

        var (sql, parameters) = Render(filter);

        sql.Should().Be(expectedSql);
        parameters.Should().Equal(("@p0", (object)5L));
    }

    [Fact]
    public void Where_flips_the_operator_when_the_value_is_on_the_left()
    {
        var (sql, _) = Render(Filter.Where<RichRecord>(r => 5 < r.Count));

        sql.Should().Be("(payload->>'count')::bigint > @p0");
    }

    [Fact]
    public void Where_compares_Date_and_Time_fields_in_order()
    {
        var since = new DateTimeOffset(2026, 5, 29, 15, 30, 0, TimeSpan.FromHours(2));

        var (sql, parameters) = Render(Filter.Where<RichRecord>(
            r => r.ObservedAt >= since && r.AsOf < new DateOnly(2026, 6, 1)));

        sql.Should().Be("((payload->>'observedAt')::timestamptz >= @p0 AND (payload->>'asOf')::date < @p1)");
        parameters.Should().Equal(
            ("@p0", (object)new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero)),
            ("@p1", (object)new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Where_compares_a_Party_field_by_its_identifier()
    {
        var owner = new Party("alice::1220");

        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Owner == owner));

        sql.Should().Be("payload->>'owner' = @p0");
        parameters.Should().Equal(("@p0", (object)"alice::1220"));
    }

    [Fact]
    public void Where_compares_a_ContractId_field_by_its_identifier()
    {
        var marker = new ContractId<Marker>("00abc");

        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Marker == marker));

        sql.Should().Be("payload->>'marker' = @p0");
        parameters.Should().Equal(("@p0", (object)"00abc"));
    }

    [Fact]
    public void Where_compares_an_enum_field_by_its_Daml_constructor_name()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Suit == Suit.Hearts));

        sql.Should().Be("payload->>'suit' = @p0");
        parameters.Should().Equal(("@p0", (object)"Hearts"));
    }

    [Fact]
    public void Where_rejects_ordering_on_a_field_without_a_Daml_order_in_sql()
    {
        var act = () => Filter.Where<RichRecord>(r => r.Suit > Suit.Diamonds);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("predicate")
            .WithMessage("*Enum*only supports == and !=*");
    }

    [Fact]
    public void Where_reads_a_bare_Bool_field_as_true()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Active));

        sql.Should().Be("(payload->>'active')::boolean = @p0");
        parameters.Should().Equal(("@p0", (object)true));
    }

    [Fact]
    public void Where_translates_and_or_and_not()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(
            r => (r.Label == "a" || r.Label == "b") && !(r.Count == 1)));

        sql.Should().Be(
            "((payload->>'label' = @p0 OR payload->>'label' = @p1) AND NOT COALESCE((payload->>'count')::bigint = @p2, FALSE))");
        parameters.Should().Equal(("@p0", (object)"a"), ("@p1", (object)"b"), ("@p2", (object)1L));
    }

    [Fact]
    public void Where_rejects_comparing_two_payload_fields()
    {
        var act = () => Filter.Where<RichRecord>(r => r.Fee == r.Amount);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("predicate")
            .WithMessage("*other side must be a captured value*");
    }

    [Fact]
    public void Where_rejects_comparing_a_whole_record()
    {
        var profile = new Profile("cdg", 7);

        var act = () => Filter.Where<RichRecord>(r => r.Profile == profile);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("predicate")
            .WithMessage("*'Profile' is not a Daml leaf type*");
    }

    [Fact]
    public void Where_throws_for_null_predicate()
    {
        var act = () => Filter.Where<RichRecord>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }

    [Fact]
    public void Where_navigates_a_nested_record_field()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Profile.Level >= 7));

        sql.Should().Be("(payload->'profile'->>'level')::bigint >= @p0");
        parameters.Should().Equal(("@p0", (object)7L));
    }

    [Fact]
    public void Where_tests_a_flat_Optional_for_None_with_null()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Note == null));

        sql.Should().Be("COALESCE(jsonb_typeof(payload->'note'), 'null') = 'null'");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void Where_tests_a_flat_Optional_for_Some_with_not_null()
    {
        var (sql, _) = Render(Filter.Where<RichRecord>(r => r.Note != null));

        sql.Should().Be("jsonb_typeof(payload->'note') <> 'null'");
    }

    [Fact]
    public void Where_compares_the_value_inside_a_flat_Optional()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Note == "hello"));

        sql.Should().Be("payload->>'note' = @p0");
        parameters.Should().Equal(("@p0", (object)"hello"));
    }

    [Fact]
    public void Where_treats_None_as_distinct_from_any_value()
    {
        var (sql, _) = Render(Filter.Where<RichRecord>(r => r.Note != "hello"));

        sql.Should().Be("payload->>'note' IS DISTINCT FROM @p0");
    }

    [Fact]
    public void Where_navigates_through_a_flat_Optional_record()
    {
        var (sql, _) = Render(Filter.Where<TypeCorners>(t => t.NestedNote!.Item.HasValue));

        sql.Should().Be("jsonb_typeof(payload->'nestedNote'->'item') <> 'null'");
    }

    [Fact]
    public void Where_reads_a_flat_Optional_value_with_GetValueOrDefault()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(t => t.Crate.Item.GetValueOrDefault() == "crated"));

        sql.Should().Be("payload->'crate'->>'item' = @p0");
        parameters.Should().Equal(("@p0", (object)"crated"));
    }

    [Fact]
    public void Where_guards_a_cast_to_Some_on_the_Optional_being_present()
    {
        var (sql, _) = Render(Filter.Where<TypeCorners>(t => ((Optional<string>.Some)t.Crate.Item).Value != "crated"));

        sql.Should().Be(
            "CASE WHEN jsonb_typeof(payload->'crate'->'item') <> 'null' THEN payload->'crate'->>'item' IS DISTINCT FROM @p0 END");
    }

    [Fact]
    public void Where_tests_a_nested_Optional_for_Some_by_its_list_encoding()
    {
        var (sql, _) = Render(Filter.Where<TypeCorners>(t => t.MaybeMaybeNote.HasValue));

        sql.Should().Be("(jsonb_typeof(payload->'maybeMaybeNote') = 'array' AND payload->'maybeMaybeNote' <> '[]'::jsonb)");
    }

    [Fact]
    public void Where_tests_a_nested_Optional_for_None_by_type_pattern()
    {
        var (sql, _) = Render(Filter.Where<TypeCorners>(t => t.MaybeMaybeNote is Optional<Optional<string>>.None));

        sql.Should().Be("COALESCE(payload->'maybeMaybeNote', '[]'::jsonb) = '[]'::jsonb");
    }

    [Fact]
    public void Where_navigates_into_a_nested_Optional_by_list_index()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(
            t => t.MaybeMaybeNote.GetValueOrDefault()!.GetValueOrDefault() == "deep"));

        sql.Should().Be("payload->'maybeMaybeNote'->0->>0 = @p0");
        parameters.Should().Equal(("@p0", (object)"deep"));
    }

    [Fact]
    public void Where_tests_the_inner_level_of_a_nested_Optional_in_its_list_encoding()
    {
        var (sql, _) = Render(Filter.Where<TypeCorners>(
            t => t.MaybeMaybeNote is Optional<Optional<string>>.Some
                && ((Optional<Optional<string>>.Some)t.MaybeMaybeNote).Value.HasValue));

        sql.Should().Be(
            "((jsonb_typeof(payload->'maybeMaybeNote') = 'array' AND payload->'maybeMaybeNote' <> '[]'::jsonb) AND " +
            "CASE WHEN (jsonb_typeof(payload->'maybeMaybeNote') = 'array' AND payload->'maybeMaybeNote' <> '[]'::jsonb) " +
            "THEN (jsonb_typeof(payload->'maybeMaybeNote'->0) = 'array' AND payload->'maybeMaybeNote'->0 <> '[]'::jsonb) END)");
    }

    [Fact]
    public void Where_reads_through_a_nullable_value_type_Optional()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(
            n => n.MaybeCount.HasValue && n.MaybeCount.Value > 3 && n.MaybeCount < 9));

        sql.Should().Be(
            "((jsonb_typeof(payload->'maybeCount') <> 'null' AND (payload->>'maybeCount')::bigint > @p0) " +
            "AND (payload->>'maybeCount')::bigint < @p1)");
        parameters.Should().Equal(("@p0", (object)3L), ("@p1", (object)9L));
    }

    [Fact]
    public void Where_compares_an_Optional_enum_field_for_equality()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeSuit == Suit.Hearts));

        sql.Should().Be("payload->>'maybeSuit' = @p0");
        parameters.Should().Equal(("@p0", (object)"Hearts"));
    }

    [Fact]
    public void Where_compares_an_Optional_enum_field_for_inequality_keeping_an_absent_value()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeSuit != Suit.Hearts));

        sql.Should().Be("payload->>'maybeSuit' IS DISTINCT FROM @p0");
        parameters.Should().Equal(("@p0", (object)"Hearts"));
    }

    [Fact]
    public void Where_compares_an_Optional_enum_field_with_a_captured_enum()
    {
        var wanted = Suit.Spades;

        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeSuit == wanted));

        sql.Should().Be("payload->>'maybeSuit' = @p0");
        parameters.Should().Equal(("@p0", (object)"Spades"));
    }

    [Fact]
    public void Where_compares_an_Optional_enum_field_with_a_captured_Optional_enum()
    {
        Suit? wanted = Suit.Diamonds;

        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeSuit == wanted));

        sql.Should().Be("payload->>'maybeSuit' = @p0");
        parameters.Should().Equal(("@p0", (object)"Diamonds"));
    }

    [Fact]
    public void Where_tests_an_Optional_enum_field_against_a_captured_absent_value()
    {
        Suit? absent = null;

        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeSuit == absent));

        sql.Should().Be("COALESCE(jsonb_typeof(payload->'maybeSuit'), 'null') = 'null'");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void Where_rejects_ordering_against_null()
    {
        long? missing = null;

        var act = () => Filter.Where<NullableFields>(n => n.MaybeCount > missing);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("predicate")
            .WithMessage("*null*only with == and !=*");
    }

    [Fact]
    public void Where_tests_a_variant_constructor_by_its_tag()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Outcome is Outcome.Pending));

        sql.Should().Be("payload->'outcome'->>'tag' = @p0");
        parameters.Should().Equal(("@p0", (object)"Pending"));
    }

    [Fact]
    public void Where_navigates_into_a_variant_constructor_payload_under_a_tag_guard()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => ((Outcome.Win)r.Outcome).Value.Prize > 10m));

        sql.Should().Be(
            "CASE WHEN payload->'outcome'->>'tag' = @p0 THEN (payload->'outcome'->'value'->>'prize')::numeric > @p1 END");
        parameters.Should().Equal(("@p0", (object)"Win"), ("@p1", (object)10m));
    }

    [Fact]
    public void Where_navigates_into_a_variant_with_as()
    {
        var (sql, _) = Render(Filter.Where<RichRecord>(r => (r.Outcome as Outcome.Win)!.Value.Tier == "gold"));

        sql.Should().Be("CASE WHEN payload->'outcome'->>'tag' = @p0 THEN payload->'outcome'->'value'->>'tier' = @p1 END");
    }

    [Fact]
    public void Where_replaces_an_absent_Optional_Int64_by_zero_under_GetValueOrDefault()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeCount.GetValueOrDefault() == 0));

        sql.Should().Be("COALESCE((payload->>'maybeCount')::bigint, @p0) = @p1");
        parameters.Should().Equal(("@p0", (object)0L), ("@p1", (object)0L));
    }

    [Fact]
    public void Where_orders_the_defaulted_Optional_Int64()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeCount.GetValueOrDefault() < 5));

        sql.Should().Be("COALESCE((payload->>'maybeCount')::bigint, @p0) < @p1");
        parameters.Should().Equal(("@p0", (object)0L), ("@p1", (object)5L));
    }

    [Fact]
    public void Where_defaults_an_absent_Optional_Numeric_to_zero()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeAmount.GetValueOrDefault() > 1m));

        sql.Should().Be("COALESCE((payload->>'maybeAmount')::numeric, @p0) > @p1");
        parameters.Should().Equal(("@p0", (object)0m), ("@p1", (object)1m));
    }

    [Fact]
    public void Where_defaults_an_absent_Optional_Bool_to_false()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeFlag.GetValueOrDefault()));

        sql.Should().Be("COALESCE((payload->>'maybeFlag')::boolean, @p0) = @p1");
        parameters.Should().Equal(("@p0", (object)false), ("@p1", (object)true));
    }

    [Fact]
    public void Where_defaults_an_absent_Optional_enum_to_its_first_constructor()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeSuit.GetValueOrDefault() == Suit.Hearts));

        sql.Should().Be("COALESCE(payload->>'maybeSuit', @p0) = @p1");
        parameters.Should().Equal(("@p0", (object)"Clubs"), ("@p1", (object)"Hearts"));
    }

    [Fact]
    public void Where_defaults_an_absent_Optional_Date_to_the_minimum_date()
    {
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeDate.GetValueOrDefault() == new DateOnly(2026, 1, 2)));

        sql.Should().Be("COALESCE((payload->>'maybeDate')::date, @p0) = @p1");
        parameters.Should().Equal(("@p0", (object)new DateOnly(1, 1, 1)), ("@p1", (object)new DateOnly(2026, 1, 2)));
    }

    [Fact]
    public void Where_defaults_an_absent_Optional_Time_to_the_minimum_instant()
    {
        var instant = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var (sql, parameters) = Render(Filter.Where<NullableFields>(n => n.MaybeTime.GetValueOrDefault() == instant));

        sql.Should().Be("COALESCE((payload->>'maybeTime')::timestamptz, @p0) = @p1");
        parameters.Should().Equal(("@p0", (object)new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero)), ("@p1", (object)instant));
    }

    [Fact]
    public void Where_rejects_a_missing_Optional_constructor_test_as_never_null()
    {
        var act = () => Filter.Where<TypeCorners>(t => (t.MaybeMaybeNote as Optional<Optional<string>>.Some) == null);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("predicate")
            .WithMessage("*Optional<T> field is never null*");
    }

    [Fact]
    public void Where_rejects_GetValueOrDefault_on_an_Optional_Party_whose_C_sharp_default_has_no_SQL_form()
    {
        var act = () => Filter.Where<NullableFields>(n => n.MaybeParty.GetValueOrDefault() == default(Party));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("predicate")
            .WithMessage("*GetValueOrDefault*Party*");
    }

    [Fact]
    public void Where_treats_a_missing_constructor_as_a_tag_mismatch()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => (r.Outcome as Outcome.Win) == null));

        sql.Should().Be("payload->'outcome'->>'tag' IS DISTINCT FROM @p0");
        parameters.Should().Equal(("@p0", (object)"Win"));
    }

    [Fact]
    public void Where_treats_a_present_constructor_as_a_tag_match()
    {
        var (sql, _) = Render(Filter.Where<RichRecord>(r => (r.Outcome as Outcome.Win) != null));

        sql.Should().Be("CASE WHEN payload->'outcome'->>'tag' = @p0 THEN jsonb_typeof(payload->'outcome') <> 'null' END");
    }

    [Fact]
    public void Where_navigates_into_a_generic_variant_constructor()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(t => ((Slot<long>.Filled)t.Slot).Value >= 5));

        sql.Should().Be("CASE WHEN payload->'slot'->>'tag' = @p0 THEN (payload->'slot'->>'value')::bigint >= @p1 END");
        parameters.Should().Equal(("@p0", (object)"Filled"), ("@p1", (object)5L));
    }

    [Fact]
    public void Where_tests_and_navigates_an_Either_by_its_Left_and_Right_tags()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(
            t => t.RankOrLabel is Either<long, string>.Right
                && ((Either<long, string>.Right)t.RankOrLabel).Value == "gold"));

        sql.Should().Be(
            "(payload->'rankOrLabel'->>'tag' = @p0 AND " +
            "CASE WHEN payload->'rankOrLabel'->>'tag' = @p1 THEN payload->'rankOrLabel'->>'value' = @p2 END)");
        parameters.Should().Equal(("@p0", (object)"Right"), ("@p1", (object)"Right"), ("@p2", (object)"gold"));
    }

    [Fact]
    public void Where_reads_an_Optional_inside_a_variant_payload()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(
            t => ((Either<Optional<string>, long>.Left)t.NoteOrRank).Value.HasValue));

        sql.Should().Be("CASE WHEN payload->'noteOrRank'->>'tag' = @p0 THEN jsonb_typeof(payload->'noteOrRank'->'value') <> 'null' END");
        parameters.Should().Equal(("@p0", (object)"Left"));
    }

    [Fact]
    public void Where_reads_tuple_components_by_their_positional_field_names()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(t => t.Pair._1 == "a" && t.Triple._3));

        sql.Should().Be("(payload->'pair'->>'_1' = @p0 AND (payload->'triple'->>'_3')::boolean = @p1)");
        parameters.Should().Equal(("@p0", (object)"a"), ("@p1", (object)true));
    }

    [Fact]
    public void Where_tests_List_membership_with_Contains()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Tags.Contains("urgent")));

        sql.Should().Be(
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'tags') = 'array' " +
            "THEN payload->'tags' END) AS e0(value) WHERE e0.value #>> '{}' = @p0)");
        parameters.Should().Equal(("@p0", (object)"urgent"));
    }

    [Fact]
    public void Where_tests_List_non_emptiness_with_Any()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Tags.Any()));

        sql.Should().Be(
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'tags') = 'array' " +
            "THEN payload->'tags' END) AS e0(value))");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void Where_translates_nested_List_element_predicates_with_distinct_aliases()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(
            t => t.Branch.Children.Any(child => child.Label == "leaf" && child.Children.Any())));

        sql.Should().Be(
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'branch'->'children') = 'array' " +
            "THEN payload->'branch'->'children' END) AS e0(value) WHERE (e0.value->>'label' = @p0 AND " +
            "EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(e0.value->'children') = 'array' " +
            "THEN e0.value->'children' END) AS e1(value))))");
        parameters.Should().Equal(("@p0", (object)"leaf"));
    }

    [Fact]
    public void Where_translates_All_as_no_element_failing_the_predicate()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Tags.All(tag => tag != "blocked")));

        sql.Should().Be(
            "NOT EXISTS (SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'tags') = 'array' " +
            "THEN payload->'tags' END) AS e0(value) WHERE NOT COALESCE(e0.value #>> '{}' IS DISTINCT FROM @p0, FALSE))");
        parameters.Should().Equal(("@p0", (object)"blocked"));
    }

    [Fact]
    public void Where_rejects_a_List_element_compared_with_another_payload_field()
    {
        var act = () => Filter.Where<RichRecord>(r => r.Tags.Any(tag => tag == r.Label));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("predicate")
            .WithMessage("*other side must be a captured value*");
    }

    [Fact]
    public void Where_looks_up_a_Text_keyed_Map_in_both_TextMap_and_GenMap_encodings()
    {
        var (sql, parameters) = Render(Filter.Where<RichRecord>(r => r.Attributes["tier"] == "gold"));

        sql.Should().Be(
            "COALESCE(payload->'attributes'->@p0, (SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN " +
            "jsonb_typeof(payload->'attributes') = 'array' THEN payload->'attributes' END) AS m0(value) " +
            "WHERE m0.value->>0 = @p1 LIMIT 1)) #>> '{}' = @p2");
        parameters.Should().Equal(("@p0", (object)"tier"), ("@p1", (object)"tier"), ("@p2", (object)"gold"));
    }

    [Fact]
    public void Where_looks_up_an_Int64_keyed_GenMap_by_its_typed_key()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(t => t.LabelByRank[1] == "first"));

        sql.Should().Be(
            "(SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'labelByRank') = 'array' " +
            "THEN payload->'labelByRank' END) AS m0(value) WHERE (m0.value->>0)::bigint = @p0 LIMIT 1) #>> '{}' = @p1");
        parameters.Should().Equal(("@p0", (object)1L), ("@p1", (object)"first"));
    }

    [Fact]
    public void Where_compares_a_GenMap_value_by_its_Daml_type()
    {
        var alice = new Party("alice::1220");

        var (sql, parameters) = Render(Filter.Where<TypeCorners>(t => t.QuotaByParty[alice] > 3));

        sql.Should().Be(
            "((SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'quotaByParty') = 'array' " +
            "THEN payload->'quotaByParty' END) AS m0(value) WHERE m0.value->>0 = @p0 LIMIT 1) #>> '{}')::bigint > @p1");
        parameters.Should().Equal(("@p0", (object)"alice::1220"), ("@p1", (object)3L));
    }

    [Fact]
    public void Where_tests_Map_key_presence_with_ContainsKey()
    {
        var (sql, parameters) = Render(Filter.Where<TypeCorners>(t => t.LabelByRank.ContainsKey(7)));

        sql.Should().Be(
            "(SELECT m0.value->1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(payload->'labelByRank') = 'array' " +
            "THEN payload->'labelByRank' END) AS m0(value) WHERE (m0.value->>0)::bigint = @p0 LIMIT 1) IS NOT NULL");
        parameters.Should().Equal(("@p0", (object)7L));
    }

    [Fact]
    public void Where_rejects_a_Map_key_read_from_the_payload()
    {
        var act = () => Filter.Where<RichRecord>(r => r.Attributes[r.Label] == "x");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("predicate")
            .WithMessage("*Map key must be a captured value*");
    }

    public enum ComparisonCase
    {
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        NotEqual,
    }

    private static (string Sql, List<(string Name, object Value)> Parameters) Render(PqsFilter filter)
    {
        var parameters = new List<(string Name, object Value)>();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(parameters, ref paramIndex);
        return (sql, parameters);
    }
    private sealed record NullableFields(
        [property: DamlFieldAttribute("maybeCount")] long? MaybeCount,
        [property: DamlFieldAttribute("maybeAmount")] decimal? MaybeAmount,
        [property: DamlFieldAttribute("maybeFlag")] bool? MaybeFlag,
        [property: DamlFieldAttribute("maybeParty")] Party? MaybeParty,
        [property: DamlFieldAttribute("maybeSuit")] Suit? MaybeSuit,
        [property: DamlFieldAttribute("maybeDate")] DateOnly? MaybeDate,
        [property: DamlFieldAttribute("maybeTime")] DateTimeOffset? MaybeTime) : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "Test.Module", "NullableFields");
        public static string PackageId => "pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => throw new NotSupportedException();
    }
}
