// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;

namespace Canton.Ledger.Client.Parity.Tests;

internal enum PqsFilterTarget
{
    RichRecord,
    OptionalCounts,
    TypeCorners,
}

internal sealed record PqsFilterParityCase(
    string Name,
    PqsFilterTarget Target,
    Func<PqsFilterSeed, PqsFilter> Filter,
    string[] Expected);

internal sealed record PqsFilterRejectedCase(string Name, Func<PqsFilterSeed, PqsFilter> Build);

internal static class PqsFilterParityCases
{
    private static readonly DateTimeOffset WinObservedAt = new(2026, 5, 29, 13, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PendingObservedAt = new(2026, 6, 15, 9, 15, 30, TimeSpan.Zero);

    public static IReadOnlyList<PqsFilterParityCase> All { get; } =
    [
        .. RichRecordNumericCases(),
        .. RichRecordScalarCases(),
        .. RichRecordTimeCases(),
        .. RichRecordOptionalAndVariantCases(),
        .. RichRecordCollectionCases(),
        .. RichRecordCombinatorCases(),
        .. OptionalCountsCases(),
        .. TypeCornersOptionalCases(),
        .. TypeCornersVariantCases(),
        .. TypeCornersMapAndListCases(),
        .. TypeCornersNumericCases(),
    ];

    public static IReadOnlyList<PqsFilterRejectedCase> Rejected { get; } =
    [
        new("arithmetic_on_a_field", _ => Filter.Where<RichRecord>(r => r.Count + 1 > 43)),
        new("string_method_call", _ => Filter.Where<RichRecord>(r => r.Label.StartsWith("abc"))),
        new("conditional_expression", _ => Filter.Where<RichRecord>(r => r.Active ? r.Count > 1 : r.Count < 1)),
        new("string_method_on_a_field_in_a_comparison", _ => Filter.Where<RichRecord>(r => r.Label.Trim() == "x")),
        new("string_length_member", _ => Filter.Where<RichRecord>(r => r.Label.Length > 3)),
        new("list_count_member", _ => Filter.Where<RichRecord>(r => r.Tags.Count > 1)),
        new("party_value_member", _ => Filter.Where<RichRecord>(r => r.Owner.Value == "alice::0000")),
        new("date_time_offset_year_member", _ => Filter.Where<RichRecord>(r => r.ObservedAt.Year == 2026)),
        new("variant_tag_member", _ => Filter.Where<RichRecord>(r => r.Outcome.Tag == "Win")),
    ];

    public static IReadOnlyList<string> Names { get; } = [.. All.Select(c => c.Name)];

    public static IReadOnlyList<string> RejectedNames { get; } = [.. Rejected.Select(c => c.Name)];

    private static PqsFilterParityCase Rich(string name, Func<PqsFilterSeed, PqsFilter> filter, params string[] expected) =>
        new(name, PqsFilterTarget.RichRecord, filter, expected);

    private static PqsFilterParityCase Counts(string name, Func<PqsFilterSeed, PqsFilter> filter, params string[] expected) =>
        new(name, PqsFilterTarget.OptionalCounts, filter, expected);

    private static PqsFilterParityCase Corners(string name, Func<PqsFilterSeed, PqsFilter> filter, params string[] expected) =>
        new(name, PqsFilterTarget.TypeCorners, filter, expected);

    private static IEnumerable<PqsFilterParityCase> RichRecordNumericCases()
    {
        yield return Rich("rich_numeric_field_padded_string", _ => Filter.Field<RichRecord>(r => r.Amount, "12.34"), "win");
        yield return Rich("rich_numeric_field_full_scale_string", _ => Filter.Field<RichRecord>(r => r.Amount, "12.3400000000"), "win");
        yield return Rich("rich_numeric_where_equals_decimal", _ => Filter.Where<RichRecord>(r => r.Amount == 12.34m), "win");
        yield return Rich("rich_numeric_where_equals_decimal_with_extra_scale", _ => Filter.Where<RichRecord>(r => r.Amount == 12.340m), "win");
        yield return Rich("rich_numeric_where_not_equals", _ => Filter.Where<RichRecord>(r => r.Amount != 12.34m), "pending", "mid");
        yield return Rich("rich_numeric_range", _ => Filter.Where<RichRecord>(r => r.Amount > 12.3m && r.Amount < 12.35m), "win");
        yield return Rich("rich_numeric_negative_equals", _ => Filter.Where<RichRecord>(r => r.Amount == -0.5m), "mid");
        yield return Rich("rich_numeric_non_negative", _ => Filter.Where<RichRecord>(r => r.Amount >= 0m), "win", "pending");
        yield return Rich("rich_numeric_scale_two_equals", _ => Filter.Where<RichRecord>(r => r.Fee == 0.05m), "win");
        yield return Rich("rich_numeric_scale_two_field", _ => Filter.Field<RichRecord>(r => r.Fee, "0.05"), "win");
        yield return Rich("rich_numeric_scale_two_greater", _ => Filter.Where<RichRecord>(r => r.Fee > 0.02m), "win", "mid");
        yield return Rich("rich_int64_greater_or_equal", _ => Filter.Where<RichRecord>(r => r.Count >= 42), "win");
        yield return Rich("rich_int64_negative_less_than", _ => Filter.Where<RichRecord>(r => r.Count < 0), "mid");
        yield return Rich("rich_int64_not_equals", _ => Filter.Where<RichRecord>(r => r.Count != 42), "pending", "mid");
        yield return Rich("rich_int64_field_string", _ => Filter.Field<RichRecord>(r => r.Count, "1"), "pending");
    }

    private static IEnumerable<PqsFilterParityCase> RichRecordScalarCases()
    {
        yield return Rich("rich_date_before", _ => Filter.Where<RichRecord>(r => r.AsOf < new DateOnly(2026, 6, 1)), "win", "mid");
        yield return Rich("rich_date_equals", _ => Filter.Where<RichRecord>(r => r.AsOf == new DateOnly(2026, 6, 15)), "pending");
        yield return Rich("rich_date_above_minimum_operand", _ => Filter.Where<RichRecord>(r => r.AsOf > DateOnly.MinValue), "win", "pending", "mid");
        yield return Rich("rich_date_below_maximum_operand", _ => Filter.Where<RichRecord>(r => r.AsOf < DateOnly.MaxValue), "win", "pending", "mid");
        yield return Rich("rich_date_at_or_below_minimum_operand", _ => Filter.Where<RichRecord>(r => r.AsOf <= DateOnly.MinValue));
        yield return Rich("rich_date_at_or_above_maximum_operand", _ => Filter.Where<RichRecord>(r => r.AsOf >= DateOnly.MaxValue));
        yield return Rich("rich_bool_bare", _ => Filter.Where<RichRecord>(r => r.Active), "win", "mid");
        yield return Rich("rich_bool_negated", _ => Filter.Where<RichRecord>(r => !r.Active), "pending");
        yield return Rich("rich_bool_equals_false", _ => Filter.Where<RichRecord>(r => r.Active == false), "pending");
        yield return Rich("rich_enum_equals", _ => Filter.Where<RichRecord>(r => r.Suit == Suit.Hearts), "win");
        yield return Rich("rich_enum_not_equals", _ => Filter.Where<RichRecord>(r => r.Suit != Suit.Hearts), "pending", "mid");
        yield return Rich("rich_enum_last_constructor", _ => Filter.Where<RichRecord>(r => r.Suit == Suit.Spades), "mid");
        yield return Rich("rich_nested_record_int64", _ => Filter.Where<RichRecord>(r => r.Profile.Level == 7), "win");
        yield return Rich("rich_nested_record_positive_level", _ => Filter.Where<RichRecord>(r => r.Profile.Level > 0), "win", "pending");
        yield return Rich("rich_nested_record_text", _ => Filter.Where<RichRecord>(r => r.Profile.Nickname == "x"), "mid");
        yield return Rich("rich_nested_record_field_string", _ => Filter.Field<RichRecord>(r => r.Profile.Level, "7"), "win");
        yield return Rich("rich_party_equals_owner", s => Filter.Where<RichRecord>(r => r.Owner == s.Owner), "win", "pending", "mid");
        yield return Rich("rich_party_not_equals_owner", s => Filter.Where<RichRecord>(r => r.Owner != s.Owner));
        yield return Rich("rich_party_equals_stranger", _ => Filter.Where<RichRecord>(r => r.Owner == new Party("stranger::0000")));
        yield return Rich("rich_party_not_equals_stranger", _ => Filter.Where<RichRecord>(r => r.Owner != new Party("stranger::0000")), "win", "pending", "mid");
        yield return Rich("rich_contract_id_equals", s => Filter.Where<RichRecord>(r => r.Marker == s.MarkerA), "win");
        yield return Rich("rich_contract_id_not_equals", s => Filter.Where<RichRecord>(r => r.Marker != s.MarkerA), "pending", "mid");
        yield return Rich("rich_contract_id_other_marker", s => Filter.Where<RichRecord>(r => r.Marker == s.MarkerB), "pending", "mid");
        yield return Rich("rich_interface_contract_id_equals", s => Filter.Where<RichRecord>(r => r.HoldingCid == s.Holding), "win", "pending", "mid");
    }

    private static IEnumerable<PqsFilterParityCase> RichRecordTimeCases()
    {
        yield return Rich("rich_time_with_offset_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt >= new DateTimeOffset(2026, 5, 29, 15, 0, 0, TimeSpan.FromHours(2))), "win", "pending");
        yield return Rich("rich_time_equals_sub_microsecond_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt == WinObservedAt.AddTicks(5)), "win");
        yield return Rich("rich_time_greater_than_sub_microsecond_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt > WinObservedAt.AddTicks(5)), "pending");
        yield return Rich("rich_time_less_than_sub_microsecond_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt < WinObservedAt.AddTicks(5)), "mid");
        yield return Rich("rich_time_equals_stored_microseconds", _ => Filter.Where<RichRecord>(r => r.ObservedAt == PendingObservedAt.AddTicks(1_234_560)), "pending");
        yield return Rich("rich_time_equals_stored_microseconds_with_sub_microsecond_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt == PendingObservedAt.AddTicks(1_234_567)), "pending");
        yield return Rich("rich_time_above_minimum_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt > DateTimeOffset.MinValue), "win", "pending", "mid");
        yield return Rich("rich_time_below_maximum_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt < DateTimeOffset.MaxValue), "win", "pending", "mid");
        yield return Rich("rich_time_at_or_below_minimum_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt <= DateTimeOffset.MinValue));
        yield return Rich("rich_time_at_or_above_maximum_operand", _ => Filter.Where<RichRecord>(r => r.ObservedAt >= DateTimeOffset.MaxValue));
    }

    private static IEnumerable<PqsFilterParityCase> RichRecordOptionalAndVariantCases()
    {
        yield return Rich("rich_optional_text_present", _ => Filter.Where<RichRecord>(r => r.Note != null), "win", "mid");
        yield return Rich("rich_optional_text_absent", _ => Filter.Where<RichRecord>(r => r.Note == null), "pending");
        yield return Rich("rich_optional_text_empty_string", _ => Filter.Where<RichRecord>(r => r.Note == ""), "mid");
        yield return Rich("rich_optional_text_equals", _ => Filter.Where<RichRecord>(r => r.Note == "hello"), "win");
        yield return Rich("rich_optional_text_not_equals_includes_none", _ => Filter.Where<RichRecord>(r => r.Note != "hello"), "pending", "mid");
        yield return Rich("rich_optional_text_negated_equals_includes_none", _ => Filter.Where<RichRecord>(r => !(r.Note == "hello")), "pending", "mid");
        yield return Rich("rich_optional_text_or_none", _ => Filter.Where<RichRecord>(r => r.Note == null || r.Note == "hello"), "win", "pending");
        yield return Rich("rich_variant_is_constructor", _ => Filter.Where<RichRecord>(r => r.Outcome is Outcome.Win), "win", "mid");
        yield return Rich("rich_variant_is_payloadless_constructor", _ => Filter.Where<RichRecord>(r => r.Outcome is Outcome.Pending), "pending");
        yield return Rich("rich_variant_negated_is_constructor", _ => Filter.Where<RichRecord>(r => !(r.Outcome is Outcome.Win)), "pending");
        yield return Rich("rich_variant_payload_greater_than", _ => Filter.Where<RichRecord>(r => ((Outcome.Win)r.Outcome).Value.Prize > 250m), "win");
        yield return Rich("rich_variant_payload_equals", _ => Filter.Where<RichRecord>(r => ((Outcome.Win)r.Outcome).Value.Prize == 1m), "mid");
        yield return Rich("rich_variant_payload_not_equals_skips_other_constructor", _ => Filter.Where<RichRecord>(r => ((Outcome.Win)r.Outcome).Value.Tier != "gold"), "mid");
        yield return Rich("rich_variant_cast_as_null_matches_other_constructor", _ => Filter.Where<RichRecord>(r => (r.Outcome as Outcome.Win) == null), "pending");
        yield return Rich("rich_variant_cast_as_not_null", _ => Filter.Where<RichRecord>(r => (r.Outcome as Outcome.Win) != null), "win", "mid");
    }

    private static IEnumerable<PqsFilterParityCase> RichRecordCollectionCases()
    {
        yield return Rich("rich_list_contains", _ => Filter.Where<RichRecord>(r => r.Tags.Contains("urgent")), "win");
        yield return Rich("rich_list_contains_absent_item", _ => Filter.Where<RichRecord>(r => r.Tags.Contains("nothing")));
        yield return Rich("rich_list_any", _ => Filter.Where<RichRecord>(r => r.Tags.Any()), "win", "mid");
        yield return Rich("rich_list_not_any_matches_empty", _ => Filter.Where<RichRecord>(r => !r.Tags.Any()), "pending");
        yield return Rich("rich_list_any_element", _ => Filter.Where<RichRecord>(r => r.Tags.Any(tag => tag == "blocked")), "mid");
        yield return Rich("rich_list_all_element_passes_empty", _ => Filter.Where<RichRecord>(r => r.Tags.All(tag => tag != "blocked")), "win", "pending");
        yield return Rich("rich_list_all_equal_only_empty", _ => Filter.Where<RichRecord>(r => r.Tags.All(tag => tag == "urgent")), "pending");
        yield return Rich("rich_list_contract_ids_any", _ => Filter.Where<RichRecord>(r => r.HoldingCids.Any()), "win", "mid");
        yield return Rich("rich_list_contract_ids_contains", s => Filter.Where<RichRecord>(r => r.HoldingCids.Contains(s.OtherHolding)), "mid");
        yield return Rich("rich_list_contract_ids_all_equal", s => Filter.Where<RichRecord>(r => r.HoldingCids.All(cid => cid == s.Holding)), "win", "pending");
        yield return Rich("rich_text_map_lookup_equals", _ => Filter.Where<RichRecord>(r => r.Attributes["k1"] == "v1"), "win");
        yield return Rich("rich_text_map_lookup_not_equals_includes_missing_key", _ => Filter.Where<RichRecord>(r => r.Attributes["k1"] != "v1"), "pending", "mid");
        yield return Rich("rich_text_map_lookup_equals_missing_key", _ => Filter.Where<RichRecord>(r => r.Attributes["k9"] == "x"));
        yield return Rich("rich_text_map_lookup_not_equals_missing_key", _ => Filter.Where<RichRecord>(r => r.Attributes["k9"] != "x"), "win", "pending", "mid");
        yield return Rich("rich_text_map_contains_key", _ => Filter.Where<RichRecord>(r => r.Attributes.ContainsKey("k2")), "win");
        yield return Rich("rich_text_map_contains_key_shared", _ => Filter.Where<RichRecord>(r => r.Attributes.ContainsKey("k1")), "win", "mid");
        yield return Rich("rich_text_map_negated_contains_key", _ => Filter.Where<RichRecord>(r => !r.Attributes.ContainsKey("k2")), "pending", "mid");
    }

    private static IEnumerable<PqsFilterParityCase> RichRecordCombinatorCases()
    {
        yield return Rich("rich_and_or_not_mixture", _ => Filter.Where<RichRecord>(r => (r.Active && r.Count > 10) || r.Suit == Suit.Spades), "win", "mid");
        yield return Rich("rich_negated_disjunction_matches_nothing", _ => Filter.Where<RichRecord>(r => !(r.Active || r.Suit == Suit.Clubs)));
        yield return Rich("rich_negated_conjunction", _ => Filter.Where<RichRecord>(r => !(r.Active && r.Suit == Suit.Hearts)), "pending", "mid");
        yield return Rich("rich_double_negation", _ => Filter.Where<RichRecord>(r => !!r.Active), "win", "mid");
        yield return Rich("rich_filter_and_of_or", _ => Filter.And(
            Filter.Where<RichRecord>(r => r.Active),
            Filter.Or(Filter.Field<RichRecord>(r => r.Count, "42"), Filter.Field<RichRecord>(r => r.Count, "-5"))), "win", "mid");
        yield return Rich("rich_filter_or_of_and", _ => Filter.Or(
            Filter.And(Filter.Where<RichRecord>(r => r.Active), Filter.Where<RichRecord>(r => r.Count > 0)),
            Filter.Where<RichRecord>(r => r.Suit == Suit.Clubs)), "win", "pending");
    }

    private static IEnumerable<PqsFilterParityCase> OptionalCountsCases()
    {
        yield return Counts("counts_default_zero_equals", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount.GetValueOrDefault() == 0), "none", "zero");
        yield return Counts("counts_default_zero_not_equals", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount.GetValueOrDefault() != 0), "seven", "neg");
        yield return Counts("counts_default_zero_less_than", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount.GetValueOrDefault() < 5), "none", "zero", "neg");
        yield return Counts("counts_default_zero_greater_than", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount.GetValueOrDefault() > 0), "seven");
        yield return Counts("counts_negated_default_comparison", _ => Filter.Where<OptionalCounts>(n => !(n.MaybeCount.GetValueOrDefault() == 0)), "seven", "neg");
        yield return Counts("counts_null_equals", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount == null), "none");
        yield return Counts("counts_not_null", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount != null), "zero", "seven", "neg");
        yield return Counts("counts_has_value", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount.HasValue), "zero", "seven", "neg");
        yield return Counts("counts_not_has_value", _ => Filter.Where<OptionalCounts>(n => !n.MaybeCount.HasValue), "none");
        yield return Counts("counts_negated_not_null", _ => Filter.Where<OptionalCounts>(n => !(n.MaybeCount != null)), "none");
        yield return Counts("counts_value_equals", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount == 7), "seven");
        yield return Counts("counts_value_not_equals_includes_none", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount != 7), "none", "zero", "neg");
        yield return Counts("counts_negated_value_equals_includes_none", _ => Filter.Where<OptionalCounts>(n => !(n.MaybeCount == 7)), "none", "zero", "neg");
        yield return Counts("counts_value_greater_than_skips_none", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount > 0), "seven");
        yield return Counts("counts_negated_greater_than_includes_none", _ => Filter.Where<OptionalCounts>(n => !(n.MaybeCount > 0)), "none", "zero", "neg");
        yield return Counts("counts_value_property_skips_none", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount!.Value >= 0), "zero", "seven");
        yield return Counts("counts_value_property_not_equals_includes_none", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount!.Value != 7), "none", "zero", "neg");
        yield return Counts("counts_null_or_seven", _ => Filter.Where<OptionalCounts>(n => n.MaybeCount == null || n.MaybeCount == 7), "none", "seven");
        yield return Counts("counts_field_string", _ => Filter.Field<OptionalCounts>(n => n.MaybeCount!, "7"), "seven");
    }

    private static IEnumerable<PqsFilterParityCase> TypeCornersOptionalCases()
    {
        yield return Corners("corners_nested_optional_has_value", _ => Filter.Where<TypeCorners>(t => t.MaybeMaybeNote.HasValue), "a", "b");
        yield return Corners("corners_nested_optional_is_none", _ => Filter.Where<TypeCorners>(t => t.MaybeMaybeNote is Optional<Optional<string>>.None), "c");
        yield return Corners("corners_nested_optional_is_some", _ => Filter.Where<TypeCorners>(t => t.MaybeMaybeNote is Optional<Optional<string>>.Some), "a", "b");
        yield return Corners("corners_nested_optional_inner_value", _ => Filter.Where<TypeCorners>(t => t.MaybeMaybeNote.GetValueOrDefault()!.GetValueOrDefault() == "deep"), "a");
        yield return Corners("corners_nested_optional_some_none", _ => Filter.Where<TypeCorners>(t => t.MaybeMaybeNote.HasValue && !t.MaybeMaybeNote.GetValueOrDefault()!.HasValue), "b");
        yield return Corners("corners_crate_optional_value", _ => Filter.Where<TypeCorners>(t => t.Crate.Item.GetValueOrDefault() == "crated"), "a");
        yield return Corners("corners_crate_optional_has_value", _ => Filter.Where<TypeCorners>(t => t.Crate.Item.HasValue), "a", "c");
        yield return Corners("corners_crate_optional_absent", _ => Filter.Where<TypeCorners>(t => !t.Crate.Item.HasValue), "b");
        yield return Corners("corners_optional_record_absent", _ => Filter.Where<TypeCorners>(t => t.NestedNote == null), "b");
        yield return Corners("corners_optional_record_present", _ => Filter.Where<TypeCorners>(t => t.NestedNote != null), "a", "c");
        yield return Corners("corners_optional_record_inner_has_value", _ => Filter.Where<TypeCorners>(t => t.NestedNote!.Item.HasValue), "c");
        yield return Corners("corners_optional_record_negated_inner_has_value_includes_none_outer", _ => Filter.Where<TypeCorners>(t => !t.NestedNote!.Item.HasValue), "a", "b");
        yield return Corners("corners_boxed_fields", _ => Filter.Where<TypeCorners>(t => t.BoxedProfile.Item.Level == 7 && t.BoxedText.Item == "boxed"), "a", "b", "c");
    }

    private static IEnumerable<PqsFilterParityCase> TypeCornersVariantCases()
    {
        yield return Corners("corners_either_is_left", _ => Filter.Where<TypeCorners>(t => t.RankOrLabel is Either<long, string>.Left), "b", "c");
        yield return Corners("corners_either_negated_is_left", _ => Filter.Where<TypeCorners>(t => !(t.RankOrLabel is Either<long, string>.Left)), "a");
        yield return Corners("corners_either_left_payload_equals", _ => Filter.Where<TypeCorners>(t => ((Either<long, string>.Left)t.RankOrLabel).Value == 5), "b");
        yield return Corners("corners_either_left_payload_not_equals_skips_right", _ => Filter.Where<TypeCorners>(t => ((Either<long, string>.Left)t.RankOrLabel).Value != 5), "c");
        yield return Corners("corners_either_left_payload_greater_than", _ => Filter.Where<TypeCorners>(t => ((Either<long, string>.Left)t.RankOrLabel).Value > 5), "c");
        yield return Corners("corners_either_right_payload_equals", _ => Filter.Where<TypeCorners>(t => ((Either<long, string>.Right)t.RankOrLabel).Value == "runner-up"), "a");
        yield return Corners("corners_either_cast_as_left_null", _ => Filter.Where<TypeCorners>(t => (t.RankOrLabel as Either<long, string>.Left) == null), "a");
        yield return Corners("corners_either_cast_as_left_not_null", _ => Filter.Where<TypeCorners>(t => (t.RankOrLabel as Either<long, string>.Left) != null), "b", "c");
        yield return Corners("corners_either_cast_as_right_not_null", _ => Filter.Where<TypeCorners>(t => (t.RankOrLabel as Either<long, string>.Right) != null), "a");
        yield return Corners("corners_either_with_optional_left", _ => Filter.Where<TypeCorners>(t => t.NoteOrRank is Either<Optional<string>, long>.Left), "a", "c");
        yield return Corners("corners_either_with_optional_right", _ => Filter.Where<TypeCorners>(t => t.NoteOrRank is Either<Optional<string>, long>.Right), "b");
        yield return Corners("corners_either_left_optional_value", _ => Filter.Where<TypeCorners>(t => ((Either<Optional<string>, long>.Left)t.NoteOrRank).Value.GetValueOrDefault() == "noted"), "a");
        yield return Corners("corners_either_left_optional_has_value", _ => Filter.Where<TypeCorners>(t => ((Either<Optional<string>, long>.Left)t.NoteOrRank).Value.HasValue), "a");
        yield return Corners("corners_either_left_optional_negated_has_value_includes_other_constructor", _ => Filter.Where<TypeCorners>(t => !((Either<Optional<string>, long>.Left)t.NoteOrRank).Value.HasValue), "b", "c");
        yield return Corners("corners_slot_is_filled", _ => Filter.Where<TypeCorners>(t => t.Slot is Slot<long>.Filled), "a", "c");
        yield return Corners("corners_slot_is_vacant", _ => Filter.Where<TypeCorners>(t => t.Slot is Slot<long>.Vacant), "b");
        yield return Corners("corners_slot_filled_payload_equals", _ => Filter.Where<TypeCorners>(t => ((Slot<long>.Filled)t.Slot).Value == 11), "a");
        yield return Corners("corners_slot_filled_payload_less_than", _ => Filter.Where<TypeCorners>(t => ((Slot<long>.Filled)t.Slot).Value < 11), "c");
        yield return Corners("corners_slot_filled_payload_not_equals_skips_vacant", _ => Filter.Where<TypeCorners>(t => ((Slot<long>.Filled)t.Slot).Value != 11), "c");
    }

    private static IEnumerable<PqsFilterParityCase> TypeCornersMapAndListCases()
    {
        yield return Corners("corners_gen_map_lookup_equals", _ => Filter.Where<TypeCorners>(t => t.LabelByRank[1] == "gold"), "a", "b");
        yield return Corners("corners_gen_map_lookup_second_entry", _ => Filter.Where<TypeCorners>(t => t.LabelByRank[2] == "silver"), "b");
        yield return Corners("corners_gen_map_lookup_not_equals_includes_missing_key", _ => Filter.Where<TypeCorners>(t => t.LabelByRank[2] != "silver"), "a", "c");
        yield return Corners("corners_gen_map_lookup_not_equals_missing_everywhere", _ => Filter.Where<TypeCorners>(t => t.LabelByRank[3] != "x"), "a", "b", "c");
        yield return Corners("corners_gen_map_contains_key", _ => Filter.Where<TypeCorners>(t => t.LabelByRank.ContainsKey(1)), "a", "b");
        yield return Corners("corners_gen_map_contains_second_key", _ => Filter.Where<TypeCorners>(t => t.LabelByRank.ContainsKey(2)), "b");
        yield return Corners("corners_gen_map_negated_contains_key_matches_empty_map", _ => Filter.Where<TypeCorners>(t => !t.LabelByRank.ContainsKey(1)), "c");
        yield return Corners("corners_party_map_lookup_greater_than", s => Filter.Where<TypeCorners>(t => t.QuotaByParty[s.Owner] > 4), "a");
        yield return Corners("corners_party_map_lookup_equals_zero", s => Filter.Where<TypeCorners>(t => t.QuotaByParty[s.Owner] == 0), "c");
        yield return Corners("corners_party_map_lookup_not_equals_includes_missing_key", s => Filter.Where<TypeCorners>(t => t.QuotaByParty[s.Owner] != 5), "b", "c");
        yield return Corners("corners_party_map_negated_lookup_equals_includes_missing_key", s => Filter.Where<TypeCorners>(t => !(t.QuotaByParty[s.Owner] == 5)), "b", "c");
        yield return Corners("corners_party_map_contains_key", s => Filter.Where<TypeCorners>(t => t.QuotaByParty.ContainsKey(s.Owner)), "a", "c");
        yield return Corners("corners_party_map_negated_contains_key", s => Filter.Where<TypeCorners>(t => !t.QuotaByParty.ContainsKey(s.Owner)), "b");
        yield return Corners("corners_tuple_bool_component", _ => Filter.Where<TypeCorners>(t => t.Triple._3), "a", "c");
        yield return Corners("corners_tuple_int_component_negative", _ => Filter.Where<TypeCorners>(t => t.Triple._2 < 0), "b");
        yield return Corners("corners_tuple_pair_component", _ => Filter.Where<TypeCorners>(t => t.Pair._2 == 3), "a", "b");
        yield return Corners("corners_recursive_list_any", _ => Filter.Where<TypeCorners>(t => t.Branch.Children.Any()), "a", "c");
        yield return Corners("corners_recursive_list_empty", _ => Filter.Where<TypeCorners>(t => !t.Branch.Children.Any()), "b");
        yield return Corners("corners_recursive_list_nested_condition", _ => Filter.Where<TypeCorners>(t => t.Branch.Children.Any(child => child.Label == "leaf" && !child.Children.Any())), "a");
        yield return Corners("corners_recursive_list_child_with_children", _ => Filter.Where<TypeCorners>(t => t.Branch.Children.Any(child => child.Children.Any())), "c");
        yield return Corners("corners_recursive_list_all_childless_passes_empty", _ => Filter.Where<TypeCorners>(t => t.Branch.Children.All(child => !child.Children.Any())), "a", "b");
    }

    private static IEnumerable<PqsFilterParityCase> TypeCornersNumericCases()
    {
        yield return Corners("corners_numeric_zero_scale_equals", _ => Filter.Where<TypeCorners>(t => t.Whole == 42m), "a", "c");
        yield return Corners("corners_numeric_zero_scale_field_string", _ => Filter.Field<TypeCorners>(t => t.Whole, "42"), "a", "c");
        yield return Corners("corners_numeric_zero_scale_greater_than", _ => Filter.Where<TypeCorners>(t => t.Whole > 10m), "a", "c");
        yield return Corners("corners_numeric_37_every_digit", _ => Filter.Field<TypeCorners>(t => t.Finest, "0.1234567890123456789012345678000000000"), "b");
        yield return Corners("corners_numeric_37_differs_in_last_digit", _ => Filter.Field<TypeCorners>(t => t.Finest, "0.1234567890123456789012345678901234567"));
        yield return Corners("corners_numeric_37_field_short_string", _ => Filter.Field<TypeCorners>(t => t.Finest, "0.5"), "a");
        yield return Corners("corners_numeric_37_where_equals", _ => Filter.Where<TypeCorners>(t => t.Finest == 0.5m), "a");
        yield return Corners("corners_numeric_37_where_range", _ => Filter.Where<TypeCorners>(t => t.Finest > 0.1m && t.Finest < 0.2m), "b");
        yield return Corners("corners_numeric_37_where_less_than", _ => Filter.Where<TypeCorners>(t => t.Finest < 0.2m), "b", "c");
    }
}
