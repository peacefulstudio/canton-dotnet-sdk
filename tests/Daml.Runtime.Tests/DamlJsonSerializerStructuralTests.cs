// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerStructuralTests
{
    [Fact]
    public void Deserialize_should_reject_json_larger_than_configured_limit()
    {
        var json = """{"field":"abcdef"}""";
        var limits = new DamlJsonDeserializationLimits(MaxInputCharacters: json.Length - 1);

        var act = () => DamlJsonSerializer.DeserializeRecord(json, limits);

        act.Should().Throw<JsonException>().WithMessage("*maximum supported JSON input size*");
    }

    [Fact]
    public void Deserialize_should_reject_arrays_wider_than_configured_limit()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 2);

        var act = () => DamlJsonSerializer.Deserialize("[1,2,3]", limits);

        act.Should().Throw<JsonException>().WithMessage("*maximum supported JSON array length*");
    }

    [Fact]
    public void DeserializeRecord_with_limits_overload_should_reject_a_top_level_JSON_null()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 2);

        var act = () => DamlJsonSerializer.DeserializeRecord("null", limits);

        act.Should().Throw<JsonException>().WithMessage("Expected a JSON object for a Daml record but found null");
    }

    [Fact]
    public void DeserializeRecord_with_limits_overload_should_reject_a_non_object_JSON_value()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 2);

        var act = () => DamlJsonSerializer.DeserializeRecord("[1,2]", limits);

        act.Should().Throw<JsonException>().WithMessage("Expected a JSON object for a Daml record but found Array");
    }

    [Fact]
    public void DeserializeRecord_with_limits_overload_should_parse_a_JSON_object()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 2);

        var record = DamlJsonSerializer.DeserializeRecord("""{"amount":"42"}""", limits);

        record.Should().Be(DamlRecord.Create(DamlField.Create("amount", new DamlInt64(42))));
    }

    [Fact]
    public void Serialize_should_render_DamlGenMap_as_array_of_two_element_arrays()
    {
        var genMap = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );
        var record = DamlRecord.Create(DamlField.Create("entries", genMap));

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"entries\":[[\"Alice\",\"1\"],[\"Bob\",\"2\"]]");
    }

    [Fact]
    public void RoundTrip_should_preserve_DamlGenMap_entries()
    {
        var original = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );
        var record = DamlRecord.Create(DamlField.Create("entries", original));

        var json = DamlJsonSerializer.Serialize(record);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        var entries = deserialized.GetField("entries")!;
        entries.Should().BeOfType<DamlGenMap>();
        entries.As<DamlGenMap>().Entries.Should().BeEquivalentTo(original.Entries);
    }

    [Fact]
    public void RoundTrip_top_level_Deserialize_should_preserve_DamlGenMap()
    {
        var original = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType<DamlGenMap>();
        deserialized.As<DamlGenMap>().Entries.Should().BeEquivalentTo(original.Entries);
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_DamlGenMap_duplicate_keys()
    {
        var act = () => DamlJsonSerializer.Deserialize("""[["k",1],["k",2]]""");

        act.Should().Throw<JsonException>().WithMessage("*duplicate key*");
    }

    [Fact]
    public void Deserialize_empty_array_should_resolve_to_DamlList_per_documented_contract()
    {
        var deserialized = DamlJsonSerializer.Deserialize("[]");

        deserialized.Should().BeOfType<DamlList>();
        deserialized.As<DamlList>().Values.Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_top_level_Deserialize_should_resolve_a_DamlOptionalChain_to_DamlList_per_documented_contract()
    {
        const string because =
            "the untyped entry point has no type schema, and the array form a chain writes is "
            + "indistinguishable on the wire from a list, so the chain is the seventh member of "
            + "the lossy family DamlJsonSerializer.Deserialize documents";

        var none = DamlJsonSerializer.Deserialize(DamlJsonSerializer.Serialize(DamlOptionalChain.None));
        var carryingNone = DamlJsonSerializer.Deserialize(
            DamlJsonSerializer.Serialize(DamlOptionalChain.Some(DamlOptionalChain.None)));
        var carryingValue = DamlJsonSerializer.Deserialize(
            DamlJsonSerializer.Serialize(
                DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep")))));

        none.Should().BeOfType<DamlList>(because).Subject.Values.Should().BeEmpty();

        carryingNone.Should().BeOfType<DamlList>(because)
            .Subject.Values.Single().Should().BeOfType<DamlList>(because)
            .Subject.Values.Should().BeEmpty();

        carryingValue.Should().BeOfType<DamlList>(because)
            .Subject.Values.Single().Should().BeOfType<DamlList>(because)
            .Subject.Values.Single().Should().BeOfType<DamlText>()
            .Subject.Value.Should().Be("deep");
    }

    [Fact]
    public void Deserialize_pair_with_null_key_should_surface_array_null_error_not_GenMap_error()
    {
        var act = () => DamlJsonSerializer.Deserialize("[[null, 5]]");

        act.Should().Throw<JsonException>()
            .WithMessage("Null array elements not supported");
    }

    [Fact]
    public void Deserialize_pair_with_null_value_should_surface_array_null_error_not_GenMap_error()
    {
        var act = () => DamlJsonSerializer.Deserialize("[[5, null]]");

        act.Should().Throw<JsonException>()
            .WithMessage("Null array elements not supported");
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_when_top_level_json_is_not_an_object()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("[1,2,3]");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_naming_null_for_top_level_null_literal()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("null");

        act.Should().Throw<JsonException>().WithMessage("*null*");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_duplicate_DamlField_labels()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(1.0m)),
            DamlField.Create("amount", new DamlNumeric(2.0m)));

        var act = () => DamlJsonSerializer.Serialize(record);

        act.Should().Throw<JsonException>().WithMessage("*amount*");
    }

    [Fact]
    public void Serialize_should_pin_the_duplicate_label_message_naming_the_label()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(1.0m)),
            DamlField.Create("amount", new DamlNumeric(2.0m)));

        var act = () => DamlJsonSerializer.Serialize(record);

        act.Should().Throw<JsonException>().WithMessage(
            "Duplicate field label 'amount' in Daml record; refusing to serialize last-wins");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_an_empty_record_field_label()
    {
        var record = DamlRecord.Create(DamlField.Create("", new DamlText("x")));

        var act = () => DamlJsonSerializer.Serialize(record);

        act.Should().Throw<JsonException>().WithMessage("A Daml record field label must not be empty");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_an_empty_record_field_label_in_a_nested_record()
    {
        var inner = DamlRecord.Create(DamlField.Create("", new DamlText("x")));
        var outer = DamlRecord.Create(DamlField.Create("inner", inner));

        var act = () => DamlJsonSerializer.Serialize(outer);

        act.Should().Throw<JsonException>().WithMessage("A Daml record field label must not be empty");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_an_empty_record_field_label_through_the_value_overload()
    {
        DamlValue record = DamlRecord.Create(DamlField.Create("", new DamlText("x")));

        var act = () => DamlJsonSerializer.Serialize(record);

        act.Should().Throw<JsonException>().WithMessage("A Daml record field label must not be empty");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_an_empty_variant_constructor()
    {
        var act = () => DamlJsonSerializer.Serialize(DamlVariant.Create("", new DamlInt64(1)));

        act.Should().Throw<JsonException>().WithMessage("A Daml variant constructor must not be empty");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_an_empty_variant_constructor_inside_a_record_field()
    {
        var record = DamlRecord.Create(DamlField.Create("outcome", DamlVariant.Create("", DamlUnit.Instance)));

        var act = () => DamlJsonSerializer.Serialize(record);

        act.Should().Throw<JsonException>().WithMessage("A Daml variant constructor must not be empty");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_an_empty_enum_constructor()
    {
        var act = () => DamlJsonSerializer.Serialize(DamlEnum.Create(""));

        act.Should().Throw<JsonException>().WithMessage("A Daml enum constructor must not be empty");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_an_empty_enum_constructor_inside_a_list()
    {
        var list = DamlList.Create(DamlEnum.Create("Hearts"), DamlEnum.Create(""));

        var act = () => DamlJsonSerializer.Serialize(list);

        act.Should().Throw<JsonException>().WithMessage("A Daml enum constructor must not be empty");
    }

    [Fact]
    public void Serialize_should_keep_accepting_an_empty_TextMap_key()
    {
        var map = DamlTextMap.Create(("", new DamlInt64(1)));

        DamlJsonSerializer.Serialize(map).Should().Be("""{"":"1"}""");
    }

    [Fact]
    public void Serialize_should_keep_accepting_an_empty_TextMap_key_inside_a_record()
    {
        var record = DamlRecord.Create(DamlField.Create("attributes", DamlTextMap.Create(("", new DamlInt64(1)))));

        DamlJsonSerializer.Serialize(record).Should().Be("""{"attributes":{"":"1"}}""");
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_for_duplicate_json_properties()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("""{"amount":"1.0","amount":"2.0"}""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_duplicate_json_properties()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"amount":"1.0","amount":"2.0"}""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_duplicate_json_properties_in_nested_object()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"outer":{"a":1,"a":2}}""");

        act.Should().Throw<JsonException>();
    }

    private const int SupportedNestingDepth = 128;
    private const int DamlLfValueDepthLimit = 100;

    private static DamlRecord NestRecord(int levels)
    {
        DamlValue value = new DamlInt64(1);
        for (var i = 0; i < levels; i++)
        {
            value = DamlRecord.Create(DamlField.Create("inner", value));
        }
        return (DamlRecord)value;
    }

    private static string NestJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"inner":""", levels))
        + "1"
        + new string('}', levels);

    [Fact]
    public void Serialize_should_allow_nesting_at_exactly_the_supported_depth()
    {
        var act = () => DamlJsonSerializer.Serialize(NestRecord(SupportedNestingDepth));

        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_should_throw_JsonException_one_level_beyond_the_supported_depth()
    {
        var act = () => DamlJsonSerializer.Serialize(NestRecord(SupportedNestingDepth + 1));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_when_nesting_exceeds_supported_depth()
    {
        var act = () => DamlJsonSerializer.Deserialize(NestJson(SupportedNestingDepth + 1));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void RoundTrip_should_support_values_at_the_DamlLf_depth_limit()
    {
        var json = DamlJsonSerializer.Serialize(NestRecord(DamlLfValueDepthLimit));
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().Be(NestRecord(DamlLfValueDepthLimit));
    }

    [Fact]
    public void DeserializeRecord_should_support_json_at_the_DamlLf_depth_limit()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord(NestJson(DamlLfValueDepthLimit));

        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_should_render_DamlOptionalChain_none_as_an_empty_array()
    {
        DamlJsonSerializer.Serialize(DamlOptionalChain.None).Should().Be("[]");
    }

    [Fact]
    public void Serialize_should_render_a_chain_carrying_none_as_an_array_of_an_empty_array()
    {
        DamlJsonSerializer.Serialize(DamlOptionalChain.Some(DamlOptionalChain.None))
            .Should().Be("[[]]");
    }

    [Fact]
    public void Serialize_should_render_a_chain_carrying_a_value_as_an_array_of_an_array()
    {
        DamlJsonSerializer.Serialize(
                DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep"))))
            .Should().Be("""[["deep"]]""");
    }

    [Fact]
    public void Serialize_should_leave_a_flat_DamlOptional_on_its_existing_encoding()
    {
        DamlJsonSerializer.Serialize(DamlOptional.None).Should().Be("null");
        DamlJsonSerializer.Serialize(DamlOptional.Some(new DamlText("present")))
            .Should().Be("\"present\"");
    }

    [Fact]
    public void Serialize_should_render_a_flat_Some_of_None_as_an_array_of_an_empty_array()
    {
        DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.None)).Should().Be("[[]]");
    }

    [Fact]
    public void Serialize_should_render_a_flat_Some_of_Some_as_an_array_of_an_array()
    {
        DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.Some(new DamlText("x"))))
            .Should().Be("""[["x"]]""");
    }

    [Fact]
    public void Serialize_should_render_a_flat_Some_of_Some_of_None_with_one_array_level_per_optional()
    {
        DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.Some(DamlOptional.None)))
            .Should().Be("[[[]]]");
    }

    [Fact]
    public void Serialize_should_write_a_flat_Some_of_None_differently_from_None()
    {
        DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.None))
            .Should().NotBe(DamlJsonSerializer.Serialize(DamlOptional.None));
        DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.None))
            .Should().NotBe(DamlJsonSerializer.Serialize(DamlOptionalChain.None));
    }

    [Fact]
    public void Serialize_should_write_a_flat_optional_carrying_a_chain_level_with_one_array_level_per_optional()
    {
        DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptionalChain.Some(new DamlText("x"))))
            .Should().Be("""[["x"]]""");
    }

    [Fact]
    public void Serialize_should_write_a_flat_optional_inside_a_chain_level_as_an_array()
    {
        DamlJsonSerializer.Serialize(DamlOptionalChain.Some(DamlOptional.Some(new DamlText("x"))))
            .Should().Be("""[["x"]]""");
        DamlJsonSerializer.Serialize(DamlOptionalChain.Some(DamlOptional.None))
            .Should().Be("[[]]");
    }

    [Fact]
    public void Serialize_should_leave_a_flat_Some_of_a_non_optional_payload_unchanged()
    {
        DamlJsonSerializer.Serialize(DamlOptional.Some(DamlList.Create(new DamlText("a"))))
            .Should().Be("""["a"]""");
        DamlJsonSerializer.Serialize(DamlOptional.Some(new DamlInt64(7))).Should().Be("\"7\"");
    }

    [Fact]
    public void Serialize_should_nest_a_flat_optional_found_inside_a_record_field()
    {
        var record = new DamlRecord(null, [new DamlField("maybeMaybeNote", DamlOptional.Some(DamlOptional.None))]);

        DamlJsonSerializer.Serialize(record).Should().Be("""{"maybeMaybeNote":[[]]}""");
    }

    [Fact]
    public void Serialize_should_not_collapse_an_optional_inside_a_list_element_of_a_flat_optional()
    {
        var list = DamlList.Create(DamlOptional.Some(DamlOptional.None), DamlOptional.None);

        DamlJsonSerializer.Serialize(DamlOptional.Some(list)).Should().Be("[[[]],null]");
    }

    [Fact]
    public void Serialize_should_write_every_flat_nested_shape_as_the_encoding_the_participant_accepted()
    {
        using var matrix = DamlLfJsonReaderWireSamplesTests.LoadWireSample("probe_nested_optional_matrix.json");
        var acceptedSomeNone = DamlLfJsonReaderWireSamplesTests.ResolvePayload(
            matrix.RootElement, "response/array_of_empty_array/sent");
        var acceptedSomeSome = DamlLfJsonReaderWireSamplesTests.ResolvePayload(
            matrix.RootElement, "response/array_of_array_of_text/sent");

        SameJson(DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.None)), acceptedSomeNone)
            .Should().BeTrue();
        SameJson(
                DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.Some(new DamlText("deep")))),
                acceptedSomeSome)
            .Should().BeTrue();
    }

    private static bool SameJson(string written, JsonElement captured) =>
        JsonNode.DeepEquals(JsonNode.Parse(written), JsonNode.Parse(captured.GetRawText()));

    [Theory]
    [MemberData(nameof(FlatNestedShapesWithTheirChainEquivalent))]
    public void Serialize_then_ReadRecord_should_recover_the_chain_equivalent_of_a_flat_nested_optional(
        DamlValue flat, DamlValue expectedChain)
    {
        var json = DamlJsonSerializer.Serialize(new DamlRecord(null, [new DamlField("nestedNote", flat)]));

        var read = DamlLfJsonReader.ReadRecord<DamlLfJsonReaderWireSamplesTests.NestedNoteHolder>(json);

        read.GetRequiredField("nestedNote").Should().Be(expectedChain);
    }

    public static TheoryData<DamlValue, DamlValue> FlatNestedShapesWithTheirChainEquivalent => new()
    {
        { DamlOptional.Some(DamlOptional.None), DamlOptionalChain.Some(DamlOptionalChain.None) },
        {
            DamlOptional.Some(DamlOptional.Some(new DamlText("deep"))),
            DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep")))
        },
    };

    [Fact]
    public void Serialize_should_never_emit_a_nested_optional_encoding_the_participant_rejected()
    {
        var rejected = new[] { "null", "[null]", """["deep"]""" };

        var emitted = new[]
        {
            DamlJsonSerializer.Serialize(DamlOptionalChain.None),
            DamlJsonSerializer.Serialize(DamlOptionalChain.Some(DamlOptionalChain.None)),
            DamlJsonSerializer.Serialize(
                DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep")))),
        };

        emitted.Should().HaveCount(3,
            "a shorter array would satisfy the disjointness below without serializing anything");
        emitted.Should().NotIntersectWith(rejected,
            "the participant answered HTTP 500 to each of these forms in a nested position");
    }
}
