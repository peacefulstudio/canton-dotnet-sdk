// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class PqsFilterEvaluatorTests
{
    private const string RichJson = """
        {"owner":"alice","count":42,"amount":"12.34","label":"alpha","active":true,"asOf":"2026-05-29",
         "observedAt":"2026-05-29T13:30:00Z","note":"hello","tags":["urgent","blue"],"attributes":{"tier":"gold"},
         "holdingCids":["h1","h2"],"profile":{"nickname":"cdg","level":7},
         "outcome":{"tag":"Win","value":{"prize":"250.50","tier":"gold"}},"suit":"Hearts","fee":"0.05"}
        """;

    [Fact]
    public void Compare_Text_matches_equal_and_rejects_different()
    {
        Matches(Filter.Field<RichRecord>(r => r.Label, "alpha"), RichJson).Should().BeTrue();
        Matches(Filter.Field<RichRecord>(r => r.Label, "beta"), RichJson).Should().BeFalse();
    }

    [Fact]
    public void Compare_Text_is_case_sensitive()
    {
        Matches(Filter.Field<RichRecord>(r => r.Label, "ALPHA"), RichJson).Should().BeFalse();
    }

    [Fact]
    public void Compare_Party_matches_by_identifier()
    {
        Matches(Filter.Where<RichRecord>(r => r.Owner == new Party("alice")), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Owner == new Party("bob")), RichJson).Should().BeFalse();
    }

    [Fact]
    public void Compare_Int64_orders_numerically_not_lexically()
    {
        Matches(Filter.Where<RichRecord>(r => r.Count > 5), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Count >= 42 && r.Count <= 42), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Count < 5), RichJson).Should().BeFalse();
    }

    [Fact]
    public void Compare_Numeric_ignores_the_stored_scale()
    {
        Matches(Filter.Field<RichRecord>(r => r.Amount, "12.3400000000"), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Amount > 12.3m && r.Amount < 12.35m), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Amount > 12.34m), RichJson).Should().BeFalse();
    }

    [Fact]
    public void Compare_Numeric_is_exact_beyond_decimal_precision()
    {
        const string json = """{"finest":"0.1234567890123456789012345678901234567"}""";

        Matches(Filter.Field<TypeCorners>(t => t.Finest, "0.1234567890123456789012345678901234567"), json).Should().BeTrue();
        Matches(Filter.Field<TypeCorners>(t => t.Finest, "0.1234567890123456789012345678901234568"), json).Should().BeFalse();
    }

    [Fact]
    public void Compare_Bool_matches_and_a_bare_Bool_member_reads_as_equal_to_true()
    {
        Matches(Filter.Where<RichRecord>(r => r.Active), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Active), RichJson.Replace("\"active\":true", "\"active\":false")).Should().BeFalse();
    }

    [Fact]
    public void Compare_Date_and_Time_order_by_value()
    {
        var asOfBefore = new DateOnly(2026, 6, 1);
        var observedFrom = new DateTimeOffset(2026, 5, 29, 15, 0, 0, TimeSpan.FromHours(2));

        Matches(Filter.Where<RichRecord>(r => r.AsOf < asOfBefore && r.ObservedAt >= observedFrom), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.AsOf > asOfBefore), RichJson).Should().BeFalse();
    }

    [Fact]
    public void Compare_Time_ignores_sub_microsecond_ticks_of_the_operand()
    {
        var instant = new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero).AddTicks(9);

        Matches(Filter.Where<RichRecord>(r => r.ObservedAt == instant), RichJson).Should().BeTrue();
    }

    [Fact]
    public void Compare_Enum_matches_by_Daml_constructor_name()
    {
        Matches(Filter.Where<RichRecord>(r => r.Suit == Suit.Hearts), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Suit == Suit.Clubs), RichJson).Should().BeFalse();
    }

    [Fact]
    public void Compare_reads_nested_record_fields()
    {
        Matches(Filter.Where<RichRecord>(r => r.Profile.Level == 7 && r.Profile.Nickname == "cdg"), RichJson).Should().BeTrue();
    }

    [Fact]
    public void Compare_on_a_missing_field_is_unknown_so_equality_does_not_match()
    {
        Matches(Filter.Where<RichRecord>(r => r.Profile.Level == 7), """{"profile":{}}""").Should().BeFalse();
    }

    [Fact]
    public void Compare_on_a_JSON_null_field_is_unknown_so_equality_does_not_match()
    {
        Matches(Filter.Where<RichRecord>(r => r.Note == "hello"), """{"note":null}""").Should().BeFalse();
    }

    [Fact]
    public void NotEqual_is_distinct_from_so_an_absent_field_differs_from_any_value()
    {
        Matches(Filter.Where<RichRecord>(r => r.Label != "alpha"), """{}""").Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Label != "alpha"), """{"label":null}""").Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Label != "alpha"), RichJson).Should().BeFalse();
    }

    [Fact]
    public void Ordering_on_an_absent_field_is_unknown_in_both_directions()
    {
        Matches(Filter.Where<RichRecord>(r => r.Count < 5), """{}""").Should().BeFalse();
        Matches(Filter.Where<RichRecord>(r => r.Count >= 5), """{}""").Should().BeFalse();
    }

    [Fact]
    public void Not_reads_an_unknown_operand_as_false_so_the_negation_matches()
    {
        Matches(Filter.Where<RichRecord>(r => !(r.Count == 5)), """{}""").Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => !(r.Count == 5)), """{"count":5}""").Should().BeFalse();
    }

    [Fact]
    public void And_with_an_unknown_operand_does_not_match_even_when_the_rest_is_true()
    {
        Matches(Filter.Where<RichRecord>(r => r.Label == "alpha" && r.Count == 42), """{"label":"alpha"}""").Should().BeFalse();
    }

    [Fact]
    public void And_with_a_false_operand_is_false_even_beside_an_unknown_one()
    {
        Matches(Filter.Where<RichRecord>(r => !(r.Label == "alpha" && r.Count == 42)), """{"label":"beta"}""").Should().BeTrue();
    }

    [Fact]
    public void Or_with_a_true_operand_matches_beside_an_unknown_one()
    {
        Matches(Filter.Where<RichRecord>(r => r.Count == 1 || r.Label == "alpha"), """{"label":"alpha"}""").Should().BeTrue();
    }

    [Fact]
    public void Or_with_only_unknown_and_false_operands_does_not_match_and_its_negation_does()
    {
        Matches(Filter.Where<RichRecord>(r => r.Count == 1 || r.Label == "zzz"), """{"label":"alpha"}""").Should().BeFalse();
        Matches(Filter.Where<RichRecord>(r => !(r.Count == 1 || r.Label == "alpha")), """{"label":"beta"}""").Should().BeTrue();
    }

    [Fact]
    public void Compare_Int64_on_text_Postgres_cannot_cast_throws_naming_contract_and_field_path()
    {
        var act = () => Matches(Filter.Where<RichRecord>(r => r.Profile.Level == 7), """{"profile":{"level":"seven"}}""");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("cid-1").And.Contain("payload.profile.level").And.Contain("seven");
    }

    [Fact]
    public void Compare_Int64_on_a_fractional_JSON_number_throws_like_the_bigint_cast()
    {
        var act = () => Matches(Filter.Where<RichRecord>(r => r.Count == 7), """{"count":7.5}""");

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("payload.count");
    }

    [Fact]
    public void Defaulted_Optional_Int64_reads_absent_and_null_as_the_default()
    {
        var filter = Filter.Where<OptionalCounts>(c => c.MaybeCount.GetValueOrDefault() == 0);

        Matches(filter, """{}""").Should().BeTrue();
        Matches(filter, """{"maybeCount":null}""").Should().BeTrue();
        Matches(filter, """{"maybeCount":0}""").Should().BeTrue();
        Matches(filter, """{"maybeCount":7}""").Should().BeFalse();
    }

    [Fact]
    public void Defaulted_Optional_Int64_orders_with_the_default_in_place_of_absence()
    {
        var filter = Filter.Where<OptionalCounts>(c => c.MaybeCount.GetValueOrDefault() < 5);

        Matches(filter, """{"maybeCount":null}""").Should().BeTrue();
        Matches(filter, """{"maybeCount":7}""").Should().BeFalse();
    }

    [Fact]
    public void Compare_Optional_Int64_without_default_treats_null_as_unknown()
    {
        Matches(Filter.Where<OptionalCounts>(c => c.MaybeCount == 7), """{"maybeCount":null}""").Should().BeFalse();
        Matches(Filter.Where<OptionalCounts>(c => c.MaybeCount == 7), """{"maybeCount":7}""").Should().BeTrue();
    }

    [Fact]
    public void Compare_Optional_enum_equality_matches_only_the_named_constructor()
    {
        var filter = Filter.Where<ShapeCorners>(s => s.MaybeSuit == Suit.Hearts);

        Matches(filter, """{"maybeSuit":"Hearts"}""").Should().BeTrue();
        Matches(filter, """{"maybeSuit":"Clubs"}""").Should().BeFalse();
        Matches(filter, """{"maybeSuit":null}""").Should().BeFalse();
        Matches(filter, """{}""").Should().BeFalse();
    }

    [Fact]
    public void Compare_Optional_enum_inequality_keeps_an_absent_value()
    {
        var filter = Filter.Where<ShapeCorners>(s => s.MaybeSuit != Suit.Hearts);

        Matches(filter, """{"maybeSuit":"Hearts"}""").Should().BeFalse();
        Matches(filter, """{"maybeSuit":"Clubs"}""").Should().BeTrue();
        Matches(filter, """{"maybeSuit":null}""").Should().BeTrue();
        Matches(filter, """{}""").Should().BeTrue();
    }

    [Fact]
    public void Compare_Optional_enum_with_a_captured_Optional_enum_matches_by_constructor()
    {
        Suit? wanted = Suit.Spades;

        var filter = Filter.Where<ShapeCorners>(s => s.MaybeSuit == wanted);

        Matches(filter, """{"maybeSuit":"Spades"}""").Should().BeTrue();
        Matches(filter, """{"maybeSuit":null}""").Should().BeFalse();
    }

    [Fact]
    public void Variant_constructor_test_matches_only_the_tagged_constructor()
    {
        var filter = Filter.Where<RichRecord>(r => r.Outcome is Outcome.Win);

        Matches(filter, RichJson).Should().BeTrue();
        Matches(filter, """{"outcome":{"tag":"Pending","value":{}}}""").Should().BeFalse();
        Matches(filter, """{}""").Should().BeFalse();
    }

    [Fact]
    public void Variant_payload_read_is_guarded_by_the_constructor_tag()
    {
        var filter = Filter.Field<RichRecord>(r => ((Outcome.Win)r.Outcome).Value.Prize, "250.5");

        Matches(filter, RichJson).Should().BeTrue();
        Matches(filter, """{"outcome":{"tag":"Pending","value":{"prize":"250.5"}}}""").Should().BeFalse();
    }

    [Fact]
    public void Variant_payload_read_does_not_cast_when_the_guard_fails()
    {
        var filter = Filter.Field<RichRecord>(r => ((Outcome.Win)r.Outcome).Value.Prize, "250.5");

        Matches(filter, """{"outcome":{"tag":"Pending","value":{"prize":"not-a-number"}}}""").Should().BeFalse();
    }

    [Fact]
    public void Variant_constructor_cast_compared_to_null_is_true_when_the_variant_is_missing()
    {
        var filter = Filter.Where<ShapeCorners>(
            s => (((Optional<Outcome>.Some)s.MaybeOutcome).Value as Outcome.Win) == null);

        Matches(filter, """{"maybeOutcome":{"tag":"Pending","value":{}}}""").Should().BeTrue();
        Matches(filter, """{"maybeOutcome":{"tag":"Win","value":{}}}""").Should().BeFalse();
        Matches(filter, """{"maybeOutcome":null}""").Should().BeFalse();
        Matches(filter, """{}""").Should().BeFalse();
    }

    [Fact]
    public void Variant_constructor_cast_compared_to_non_null_tests_for_the_tag()
    {
        var filter = Filter.Where<RichRecord>(r => (r.Outcome as Outcome.Win) != null);

        Matches(filter, RichJson).Should().BeTrue();
        Matches(filter, """{"outcome":{"tag":"Pending","value":{}}}""").Should().BeFalse();
    }

    [Fact]
    public void Flat_Optional_Some_test_distinguishes_present_null_and_missing()
    {
        var filter = Filter.Where<TypeCorners>(t => t.Crate.Item is Optional<string>.Some);

        Matches(filter, """{"crate":{"item":"x"}}""").Should().BeTrue();
        Matches(filter, """{"crate":{"item":null}}""").Should().BeFalse();
        Matches(filter, """{"crate":{}}""").Should().BeFalse();
    }

    [Fact]
    public void Flat_Optional_None_test_treats_missing_as_none()
    {
        var filter = Filter.Where<TypeCorners>(t => t.Crate.Item is Optional<string>.None);

        Matches(filter, """{"crate":{"item":null}}""").Should().BeTrue();
        Matches(filter, """{"crate":{}}""").Should().BeTrue();
        Matches(filter, """{"crate":{"item":"x"}}""").Should().BeFalse();
    }

    [Fact]
    public void Nested_Optional_reads_its_inner_value_by_list_index()
    {
        var filter = Filter.Where<ShapeCorners>(
            s => ((Optional<Optional<long>>.Some)s.MaybeMaybeRank).Value.GetValueOrDefault() == 3);

        Matches(filter, """{"maybeMaybeRank":[[3]]}""").Should().BeTrue();
        Matches(filter, """{"maybeMaybeRank":[[4]]}""").Should().BeFalse();
    }

    [Fact]
    public void Optional_inside_an_Optional_record_reads_through_the_flat_encoding()
    {
        var filter = Filter.Field<TypeCorners>(t => t.NestedNote!.Item, "inner");

        Matches(filter, """{"nestedNote":{"item":"inner"}}""").Should().BeTrue();
        Matches(filter, """{"nestedNote":{"item":"other"}}""").Should().BeFalse();
    }

    [Fact]
    public void List_Any_without_predicate_tests_non_emptiness()
    {
        Matches(Filter.Where<RichRecord>(r => r.Tags.Any()), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Tags.Any()), """{"tags":[]}""").Should().BeFalse();
    }

    [Fact]
    public void List_Any_on_a_missing_or_non_array_value_is_false_and_its_negation_true()
    {
        Matches(Filter.Where<RichRecord>(r => r.Tags.Any()), """{}""").Should().BeFalse();
        Matches(Filter.Where<RichRecord>(r => r.Tags.Any()), """{"tags":"urgent"}""").Should().BeFalse();
        Matches(Filter.Where<RichRecord>(r => !r.Tags.Any()), """{}""").Should().BeTrue();
    }

    [Fact]
    public void List_Any_with_predicate_needs_one_element_to_satisfy_it()
    {
        Matches(Filter.Where<RichRecord>(r => r.Tags.Any(tag => tag == "blue")), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.Tags.Any(tag => tag == "red")), RichJson).Should().BeFalse();
    }

    [Fact]
    public void List_All_is_true_for_an_empty_missing_or_non_array_list()
    {
        var filter = Filter.Where<RichRecord>(r => r.Tags.All(tag => tag == "blue"));

        Matches(filter, """{"tags":[]}""").Should().BeTrue();
        Matches(filter, """{}""").Should().BeTrue();
        Matches(filter, """{"tags":"red"}""").Should().BeTrue();
    }

    [Fact]
    public void List_All_fails_when_any_element_is_false_or_unknown()
    {
        var filter = Filter.Where<ShapeCorners>(s => s.Stamps.All(stamp => stamp > new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        Matches(filter, """{"stamps":["2026-02-01T00:00:00Z","2026-03-01T00:00:00Z"]}""").Should().BeTrue();
        Matches(filter, """{"stamps":["2026-02-01T00:00:00Z","2025-03-01T00:00:00Z"]}""").Should().BeFalse();
        Matches(filter, """{"stamps":["2026-02-01T00:00:00Z",null]}""").Should().BeFalse();
    }

    [Fact]
    public void List_Any_ignores_elements_whose_condition_is_unknown()
    {
        var filter = Filter.Where<ShapeCorners>(s => s.Stamps.Any(stamp => stamp > new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        Matches(filter, """{"stamps":[null]}""").Should().BeFalse();
        Matches(filter, """{"stamps":[null,"2026-02-01T00:00:00Z"]}""").Should().BeTrue();
    }

    [Fact]
    public void List_Contains_matches_ContractId_Time_and_enum_elements_by_value()
    {
        Matches(Filter.Where<RichRecord>(r => r.HoldingCids.Contains(new ContractId<IHolding>("h2"))), RichJson).Should().BeTrue();
        Matches(Filter.Where<RichRecord>(r => r.HoldingCids.Contains(new ContractId<IHolding>("h9"))), RichJson).Should().BeFalse();
        Matches(Filter.Where<ShapeCorners>(s => s.Suits.Contains(Suit.Hearts)), """{"suits":["Clubs","Hearts"]}""").Should().BeTrue();
        Matches(
            Filter.Where<ShapeCorners>(s => s.Stamps.Contains(new DateTimeOffset(2026, 2, 1, 1, 0, 0, TimeSpan.FromHours(1)))),
            """{"stamps":["2026-02-01T00:00:00Z"]}""").Should().BeTrue();
    }

    [Fact]
    public void List_predicate_read_through_a_Some_cast_is_guarded_by_the_optional_being_set()
    {
        var filter = Filter.Where<ShapeCorners>(s => ((Optional<IReadOnlyList<string>>.Some)s.MaybeTags).Value.Contains("a"));

        Matches(filter, """{"maybeTags":["a","b"]}""").Should().BeTrue();
        Matches(filter, """{"maybeTags":["b"]}""").Should().BeFalse();
        Matches(filter, """{"maybeTags":null}""").Should().BeFalse();
        Matches(filter, """{}""").Should().BeFalse();
    }

    [Fact]
    public void Nested_list_scopes_resolve_to_their_own_element()
    {
        var filter = Filter.Where<RichRecord>(r => r.Tags.Any(tag => r.HoldingCids.Any(cid => cid == new ContractId<IHolding>("h1")) && tag == "blue"));

        Matches(filter, RichJson).Should().BeTrue();
        Matches(filter, RichJson.Replace("\"h1\",", "")).Should().BeFalse();
    }

    [Fact]
    public void TextMap_lookup_reads_an_object_encoded_map()
    {
        Matches(Filter.Field<RichRecord>(r => r.Attributes["tier"], "gold"), RichJson).Should().BeTrue();
        Matches(Filter.Field<RichRecord>(r => r.Attributes["tier"], "silver"), RichJson).Should().BeFalse();
    }

    [Fact]
    public void TextMap_lookup_falls_back_to_an_entry_array_encoding()
    {
        var filter = Filter.Field<RichRecord>(r => r.Attributes["tier"], "gold");

        Matches(filter, """{"attributes":[["tier","gold"]]}""").Should().BeTrue();
        Matches(filter, """{"attributes":[["other","gold"]]}""").Should().BeFalse();
    }

    [Fact]
    public void TextMap_lookup_uses_the_first_matching_entry_of_an_array()
    {
        var filter = Filter.Field<RichRecord>(r => r.Attributes["tier"], "gold");

        Matches(filter, """{"attributes":[["tier","gold"],["tier","silver"]]}""").Should().BeTrue();
        Matches(filter, """{"attributes":[["tier","silver"],["tier","gold"]]}""").Should().BeFalse();
    }

    [Fact]
    public void TextMap_object_entry_wins_over_the_array_fallback_and_missing_keys_are_unknown()
    {
        var filter = Filter.Where<RichRecord>(r => r.Attributes["tier"] != "gold");

        Matches(filter, RichJson).Should().BeFalse();
        Matches(filter, """{"attributes":{}}""").Should().BeTrue();
        Matches(filter, """{}""").Should().BeTrue();
    }

    [Fact]
    public void TextMap_ContainsKey_counts_a_JSON_null_value_as_present()
    {
        var filter = Filter.Where<RichRecord>(r => r.Attributes.ContainsKey("tier"));

        Matches(filter, """{"attributes":{"tier":null}}""").Should().BeTrue();
        Matches(filter, """{"attributes":[["tier",null]]}""").Should().BeTrue();
        Matches(filter, """{"attributes":{}}""").Should().BeFalse();
        Matches(filter, """{}""").Should().BeFalse();
    }

    [Fact]
    public void GenMap_lookup_reads_the_value_of_the_first_entry_with_the_typed_key()
    {
        var filter = Filter.Where<TypeCorners>(t => t.LabelByRank[1] == "gold");

        Matches(filter, """{"labelByRank":[[1,"gold"],[2,"silver"]]}""").Should().BeTrue();
        Matches(filter, """{"labelByRank":[[2,"silver"],[1,"gold"]]}""").Should().BeTrue();
        Matches(filter, """{"labelByRank":[[2,"silver"]]}""").Should().BeFalse();
    }

    [Fact]
    public void GenMap_missing_key_is_distinct_from_any_value()
    {
        var filter = Filter.Where<TypeCorners>(t => t.LabelByRank[2] != "second");

        Matches(filter, """{"labelByRank":[[1,"gold"]]}""").Should().BeTrue();
        Matches(filter, """{"labelByRank":[[2,"second"]]}""").Should().BeFalse();
        Matches(filter, """{"labelByRank":{"2":"first"}}""").Should().BeTrue();
    }

    [Fact]
    public void GenMap_key_cast_failure_names_the_entry_path()
    {
        var act = () => Matches(Filter.Where<TypeCorners>(t => t.LabelByRank[1] == "gold"), """{"labelByRank":[["one","gold"]]}""");

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("labelByRank");
    }

    [Fact]
    public void GenMap_ContainsKey_through_a_Some_cast_is_guarded_by_the_optional()
    {
        var filter = Filter.Where<ShapeCorners>(s => ((Optional<IReadOnlyDictionary<long, string>>.Some)s.MaybeLabels).Value.ContainsKey(4));

        Matches(filter, """{"maybeLabels":[[4,"x"]]}""").Should().BeTrue();
        Matches(filter, """{"maybeLabels":[[5,"x"]]}""").Should().BeFalse();
        Matches(filter, """{"maybeLabels":null}""").Should().BeFalse();
    }

    [Fact]
    public void Time_comparison_reads_stored_infinity_as_beyond_every_instant()
    {
        var filter = Filter.Where<RichRecord>(r => r.ObservedAt > DateTimeOffset.UtcNow);

        Matches(filter, """{"observedAt":"infinity"}""").Should().BeTrue();
    }

    [Fact]
    public void Time_comparison_against_the_minimum_operand_reads_it_as_negative_infinity()
    {
        var filter = Filter.Where<RichRecord>(r => r.ObservedAt > DateTimeOffset.MinValue);

        Matches(filter, RichJson).Should().BeTrue();
    }

    private static bool Matches(PqsFilter filter, string payloadJson)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        return PqsFilterEvaluator.Matches(filter, "cid-1", payload.RootElement);
    }

    private sealed record ShapeCorners(
        [property: DamlFieldAttribute("maybeCount")] long? MaybeCount,
        [property: DamlFieldAttribute("maybeSuit")] Suit? MaybeSuit,
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
