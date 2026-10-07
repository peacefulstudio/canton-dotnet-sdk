// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonDecodersTests
{
    private const string ReferenceContractId =
        "00c35a913ec4d5ed3d9c407da7dcaf99ecb7956c3930ebe89dd3370075d4de7c7";

    public sealed record WidgetRecord([property: DamlFieldAttribute("count")] long Count) : IDamlRecord<WidgetRecord>
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("count", new DamlInt64(Count)));

        public static WidgetRecord FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("count").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("count", DamlLfJsonDecoders.ReadInt64(
                DamlLfJsonDecoders.RequireField(json, context, "count"), context.Field("count"))));
        }
    }

    public abstract record Rating : IDamlVariant<Rating>
    {
        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        private static readonly string[] ExpectedConstructors = ["Stars", "Unrated"];

        public static DamlVariant __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var tag = DamlLfJsonDecoders.ReadVariantTag(json, context);
            var value = DamlLfJsonDecoders.RequireVariantValue(json, context);
            var valueContext = context.Field("value");
            return tag switch
            {
                "Stars" => DamlVariant.Create("Stars", DamlLfJsonDecoders.ReadInt64(value, valueContext)),
                "Unrated" => DamlVariant.Create("Unrated", DamlLfJsonDecoders.ReadUnit(value, valueContext)),
                _ => throw DamlLfJsonDecoders.UnknownConstructor("variant constructor", tag, context, ExpectedConstructors),
            };
        }

        public sealed record Stars(long Value) : Rating
        {
            public override string Tag => "Stars";

            public override DamlVariant ToVariant() => DamlVariant.Create("Stars", new DamlInt64(Value));
        }

        public sealed record Unrated : Rating
        {
            public override string Tag => "Unrated";

            public override DamlVariant ToVariant() => DamlVariant.Create("Unrated", DamlUnit.Instance);
        }
    }

    public sealed record BundleRecord([property: DamlFieldAttribute("amounts")] IReadOnlyList<long> Amounts)
        : IDamlRecord<BundleRecord>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "amounts",
            new DamlList(Amounts.Select(amount => (DamlValue)new DamlInt64(amount)).ToList())));

        public static BundleRecord FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("amounts").As<DamlList>().Values.Select(value => value.As<DamlInt64>().Value).ToList());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("amounts", DamlLfJsonDecoders.ReadList(
                DamlLfJsonDecoders.RequireField(json, context, "amounts"), context.Field("amounts"),
                DamlLfJsonDecoders.ReadInt64)));
        }
    }

    private static JsonElement Parse(string json, out JsonDocument document)
    {
        document = JsonDocument.Parse(json);
        return document.RootElement;
    }

    [Fact]
    public void ReadInt64_should_decode_a_canonical_wire_string()
    {
        using var document = JsonDocument.Parse("\"42\"");
        var context = DamlLfJsonDecodeContext.Root("Count");

        DamlLfJsonDecoders.ReadInt64(document.RootElement, context).Should().Be(new DamlInt64(42));
    }

    [Fact]
    public void ReadInt64_should_reject_a_non_string_shape_at_its_context_path()
    {
        using var document = JsonDocument.Parse("42");
        var context = DamlLfJsonDecodeContext.Root("Widget").Field("count");

        var act = () => DamlLfJsonDecoders.ReadInt64(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'Widget.count' but found Number");
    }

    [Fact]
    public void ReadInt64_should_reject_a_malformed_wire_string()
    {
        using var document = JsonDocument.Parse("\"twelve\"");
        var context = DamlLfJsonDecodeContext.Root("Count");

        var act = () => DamlLfJsonDecoders.ReadInt64(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Value 'twelve' at 'Count' is not a valid Daml Int64");
    }

    [Fact]
    public void ReadNumeric_should_decode_the_participant_scale_zero_wire_string()
    {
        using var document = JsonDocument.Parse("\"42.\"");
        var context = DamlLfJsonDecodeContext.Root("TypeCorners").Field("whole");

        DamlLfJsonDecoders.ReadNumeric(document.RootElement, context).Should().Be(new DamlNumeric(42m));
    }

    [Fact]
    public void ReadNumeric_should_decode_a_canonical_wire_string()
    {
        using var document = JsonDocument.Parse("\"1.25\"");
        var context = DamlLfJsonDecodeContext.Root("Amount");

        DamlLfJsonDecoders.ReadNumeric(document.RootElement, context).Should().Be(new DamlNumeric(1.25m));
    }

    [Fact]
    public void ReadNumeric_should_reject_a_malformed_wire_string()
    {
        using var document = JsonDocument.Parse("\"not-a-number\"");
        var context = DamlLfJsonDecodeContext.Root("Amount");

        var act = () => DamlLfJsonDecoders.ReadNumeric(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Value 'not-a-number' at 'Amount' is not a valid Daml Numeric");
    }

    [Fact]
    public void ReadText_should_decode_a_string()
    {
        using var document = JsonDocument.Parse("\"gold\"");
        var context = DamlLfJsonDecodeContext.Root("Tier");

        DamlLfJsonDecoders.ReadText(document.RootElement, context).Should().Be(new DamlText("gold"));
    }

    [Fact]
    public void ReadText_should_reject_a_non_string_shape()
    {
        using var document = JsonDocument.Parse("true");
        var context = DamlLfJsonDecodeContext.Root("Tier");

        var act = () => DamlLfJsonDecoders.ReadText(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'Tier' but found True");
    }

    [Fact]
    public void ReadBool_should_decode_a_boolean()
    {
        using var document = JsonDocument.Parse("true");
        var context = DamlLfJsonDecodeContext.Root("Flag");

        DamlLfJsonDecoders.ReadBool(document.RootElement, context).Should().Be(new DamlBool(true));
    }

    [Fact]
    public void ReadBool_should_reject_a_non_boolean_shape()
    {
        using var document = JsonDocument.Parse("1");
        var context = DamlLfJsonDecodeContext.Root("Flag");

        var act = () => DamlLfJsonDecoders.ReadBool(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON boolean at 'Flag' but found Number");
    }

    [Fact]
    public void ReadParty_should_decode_a_string()
    {
        using var document = JsonDocument.Parse("\"alice::1220ab\"");
        var context = DamlLfJsonDecodeContext.Root("Owner");

        DamlLfJsonDecoders.ReadParty(document.RootElement, context).Should().Be(new DamlParty("alice::1220ab"));
    }

    [Fact]
    public void ReadParty_should_reject_a_non_string_shape()
    {
        using var document = JsonDocument.Parse("null");
        var context = DamlLfJsonDecodeContext.Root("Owner");

        var act = () => DamlLfJsonDecoders.ReadParty(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'Owner' but found Null");
    }

    [Fact]
    public void ReadContractId_should_decode_a_string()
    {
        using var document = JsonDocument.Parse($"\"{ReferenceContractId}\"");
        var context = DamlLfJsonDecodeContext.Root("Reference");

        DamlLfJsonDecoders.ReadContractId(document.RootElement, context).Should().Be(new DamlContractId(ReferenceContractId));
    }

    [Fact]
    public void ReadContractId_should_reject_a_non_string_shape()
    {
        using var document = JsonDocument.Parse("7");
        var context = DamlLfJsonDecodeContext.Root("Reference");

        var act = () => DamlLfJsonDecoders.ReadContractId(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'Reference' but found Number");
    }

    [Fact]
    public void ReadDate_should_decode_a_canonical_wire_string()
    {
        using var document = JsonDocument.Parse("\"2026-09-05\"");
        var context = DamlLfJsonDecodeContext.Root("RecordedOn");

        DamlLfJsonDecoders.ReadDate(document.RootElement, context).Should().Be(new DamlDate(new DateOnly(2026, 9, 5)));
    }

    [Fact]
    public void ReadDate_should_reject_a_malformed_wire_string()
    {
        using var document = JsonDocument.Parse("\"05-09-2026\"");
        var context = DamlLfJsonDecodeContext.Root("RecordedOn");

        var act = () => DamlLfJsonDecoders.ReadDate(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Value '05-09-2026' at 'RecordedOn' is not a valid Daml Date");
    }

    [Fact]
    public void ReadTimestamp_should_decode_a_canonical_wire_string()
    {
        using var document = JsonDocument.Parse("\"2026-09-05T12:34:56Z\"");
        var context = DamlLfJsonDecodeContext.Root("RecordedAt");

        DamlLfJsonDecoders.ReadTimestamp(document.RootElement, context).Should()
            .Be(new DamlTimestamp(new DateTimeOffset(2026, 9, 5, 12, 34, 56, TimeSpan.Zero)));
    }

    [Fact]
    public void ReadTimestamp_should_reject_a_malformed_wire_string()
    {
        using var document = JsonDocument.Parse("\"not-a-timestamp\"");
        var context = DamlLfJsonDecodeContext.Root("RecordedAt");

        var act = () => DamlLfJsonDecoders.ReadTimestamp(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Value 'not-a-timestamp' at 'RecordedAt' is not a valid Daml Timestamp");
    }

    [Fact]
    public void ReadUnit_should_decode_an_empty_object()
    {
        using var document = JsonDocument.Parse("{}");
        var context = DamlLfJsonDecodeContext.Root("Marker");

        DamlLfJsonDecoders.ReadUnit(document.RootElement, context).Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadUnit_should_reject_a_non_object_shape()
    {
        using var document = JsonDocument.Parse("\"x\"");
        var context = DamlLfJsonDecodeContext.Root("Marker");

        var act = () => DamlLfJsonDecoders.ReadUnit(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Object at 'Marker' but found String");
    }

    [Fact]
    public void ReadUnit_should_reject_a_non_empty_object()
    {
        using var document = JsonDocument.Parse("""{"unexpected":1}""");
        var context = DamlLfJsonDecodeContext.Root("Marker");

        var act = () => DamlLfJsonDecoders.ReadUnit(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected an empty JSON Object at 'Marker' (Daml Unit) but found 1 property");
    }

    [Fact]
    public void ReadUnit_should_report_a_plural_property_count_when_rejecting_a_non_empty_object()
    {
        using var document = JsonDocument.Parse("""{"a":1,"b":2}""");
        var context = DamlLfJsonDecodeContext.Root("Marker");

        var act = () => DamlLfJsonDecoders.ReadUnit(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected an empty JSON Object at 'Marker' (Daml Unit) but found 2 properties");
    }

    [Fact]
    public void ReadRecord_should_report_the_context_path_in_a_shape_mismatch()
    {
        using var document = JsonDocument.Parse("\"not-an-object\"");
        var context = DamlLfJsonDecodeContext.Root("WidgetRecord");

        var act = () => DamlLfJsonDecoders.ReadRecord<WidgetRecord>(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Object at 'WidgetRecord' but found String");
    }

    [Fact]
    public void ReadRecord_should_allow_a_field_exactly_at_the_maximum_supported_depth()
    {
        using var document = JsonDocument.Parse("""{"count":"7"}""");
        var context = DamlLfJsonDecodeContext.Root("WidgetRecord");
        for (var level = 0; level < 127; level++)
        {
            context = context.Field("f");
        }
        context.Depth.Should().Be(127);

        var decoded = DamlLfJsonDecoders.ReadRecord<WidgetRecord>(document.RootElement, context);

        decoded.Should().Be(DamlRecord.Create(DamlField.Create("count", new DamlInt64(7))));
    }

    [Fact]
    public void ReadVariant_should_report_the_context_path_in_a_shape_mismatch()
    {
        using var document = JsonDocument.Parse("\"not-an-object\"");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var act = () => DamlLfJsonDecoders.ReadVariant<Rating>(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Object at 'Rating' but found String");
    }

    [Fact]
    public void ReadVariant_should_allow_a_nullary_arm_exactly_at_the_maximum_supported_depth()
    {
        using var document = JsonDocument.Parse("""{"tag":"Unrated","value":{}}""");
        var context = DamlLfJsonDecodeContext.Root("Rating");
        for (var level = 0; level < 127; level++)
        {
            context = context.Field("f");
        }
        context.Depth.Should().Be(127);

        var decoded = DamlLfJsonDecoders.ReadVariant<Rating>(document.RootElement, context);

        decoded.Should().Be(DamlVariant.Create("Unrated", DamlUnit.Instance));
    }

    [Fact]
    public void ReadVariant_should_reject_a_nullary_arm_beyond_the_maximum_supported_depth()
    {
        using var document = JsonDocument.Parse("""{"tag":"Unrated","value":{}}""");
        var context = DamlLfJsonDecodeContext.Root("Rating");
        for (var level = 0; level < 128; level++)
        {
            context = context.Field("f");
        }
        context.Depth.Should().Be(128);

        var act = () => DamlLfJsonDecoders.ReadVariant<Rating>(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Value nesting exceeds the maximum supported depth of 128");
    }

    [Fact]
    public void ReadList_should_decode_each_element_at_a_bracketed_index_path()
    {
        using var document = JsonDocument.Parse("""["1","2","3"]""");
        var context = DamlLfJsonDecodeContext.Root("Amounts");

        var decoded = DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(new DamlList([new DamlInt64(1), new DamlInt64(2), new DamlInt64(3)]));
    }

    [Fact]
    public void ReadList_should_report_the_bracketed_index_of_a_malformed_element()
    {
        using var document = JsonDocument.Parse("""["1","two"]""");
        var context = DamlLfJsonDecodeContext.Root("Amounts");

        var act = () => DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Value 'two' at 'Amounts[1]' is not a valid Daml Int64");
    }

    [Fact]
    public void ReadList_should_reject_a_non_array_shape()
    {
        using var document = JsonDocument.Parse("\"not-a-list\"");
        var context = DamlLfJsonDecodeContext.Root("Amounts");

        var act = () => DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Array at 'Amounts' but found String");
    }

    [Fact]
    public void ReadList_should_reject_an_array_wider_than_the_configured_limit()
    {
        using var document = JsonDocument.Parse("""["1","2","3"]""");
        var context = DamlLfJsonDecodeContext.Root("Amounts", new DamlJsonDeserializationLimits(MaxArrayElements: 2));

        var act = () => DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("JSON array length 3 exceeds the maximum supported JSON array length of 2");
    }

    [Fact]
    public void ReadList_should_decode_an_empty_list_when_MaxArrayElements_is_explicitly_zero()
    {
        using var document = JsonDocument.Parse("[]");
        var context = DamlLfJsonDecodeContext.Root("Amounts", new DamlJsonDeserializationLimits(MaxArrayElements: 0));

        var decoded = DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(new DamlList([]));
    }

    [Fact]
    public void ReadList_should_reject_a_non_empty_list_when_MaxArrayElements_is_explicitly_zero()
    {
        using var document = JsonDocument.Parse("""["1"]""");
        var context = DamlLfJsonDecodeContext.Root("Amounts", new DamlJsonDeserializationLimits(MaxArrayElements: 0));

        var act = () => DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("JSON array length 1 exceeds the maximum supported JSON array length of 0");
    }

    [Fact]
    public void ReadList_should_decode_a_non_empty_list_starting_from_a_default_context()
    {
        using var document = JsonDocument.Parse("""["1","2","3"]""");
        var context = default(DamlLfJsonDecodeContext);

        var decoded = DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(new DamlList([new DamlInt64(1), new DamlInt64(2), new DamlInt64(3)]));
    }

    [Fact]
    public void ReadRecord_should_decode_a_record_containing_a_list_starting_from_a_default_context()
    {
        using var document = JsonDocument.Parse("""{"amounts":["1","2","3"]}""");
        var context = default(DamlLfJsonDecodeContext);

        var decoded = DamlLfJsonDecoders.ReadRecord<BundleRecord>(document.RootElement, context);

        decoded.Should().Be(DamlRecord.Create(DamlField.Create(
            "amounts",
            new DamlList([new DamlInt64(1), new DamlInt64(2), new DamlInt64(3)]))));
    }

    [Fact]
    public void ReadInt64_should_report_the_fallback_root_path_in_a_shape_mismatch_starting_from_a_default_context()
    {
        using var document = JsonDocument.Parse("42");
        var context = default(DamlLfJsonDecodeContext);

        var act = () => DamlLfJsonDecoders.ReadInt64(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at '$' but found Number");
    }

    [Fact]
    public void ReadOptional_should_decode_null_as_None()
    {
        using var document = JsonDocument.Parse("null");
        var context = DamlLfJsonDecodeContext.Root("Nickname");

        DamlLfJsonDecoders.ReadOptional(document.RootElement, context, DamlLfJsonDecoders.ReadText).Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadOptional_should_decode_a_bare_value_as_Some()
    {
        using var document = JsonDocument.Parse("\"ren\"");
        var context = DamlLfJsonDecodeContext.Root("Nickname");

        DamlLfJsonDecoders.ReadOptional(document.RootElement, context, DamlLfJsonDecoders.ReadText).Should()
            .Be(DamlOptional.Some(new DamlText("ren")));
    }

    [Fact]
    public void ReadOptionalChain_should_decode_an_empty_array_as_None()
    {
        using var document = JsonDocument.Parse("[]");
        var context = DamlLfJsonDecodeContext.Root("Nested");

        DamlLfJsonDecoders.ReadOptionalChain(document.RootElement, context, DamlLfJsonDecoders.ReadInt64).Should()
            .Be(DamlOptionalChain.None);
    }

    [Fact]
    public void ReadOptionalChain_should_decode_a_one_element_array_as_Some()
    {
        using var document = JsonDocument.Parse("""["5"]""");
        var context = DamlLfJsonDecodeContext.Root("Nested");

        DamlLfJsonDecoders.ReadOptionalChain(document.RootElement, context, DamlLfJsonDecoders.ReadInt64).Should()
            .Be(DamlOptionalChain.Some(new DamlInt64(5)));
    }

    [Fact]
    public void ReadOptionalChain_should_compose_to_decode_None_at_the_outer_level()
    {
        using var document = JsonDocument.Parse("[]");
        var context = DamlLfJsonDecodeContext.Root("Nested");

        var decoded = DamlLfJsonDecoders.ReadOptionalChain(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadOptionalChain(element, elementContext, DamlLfJsonDecoders.ReadText));

        decoded.Should().Be(DamlOptionalChain.None);
    }

    [Fact]
    public void ReadOptionalChain_should_compose_to_decode_Some_of_None_at_the_inner_level()
    {
        using var document = JsonDocument.Parse("[[]]");
        var context = DamlLfJsonDecodeContext.Root("Nested");

        var decoded = DamlLfJsonDecoders.ReadOptionalChain(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadOptionalChain(element, elementContext, DamlLfJsonDecoders.ReadText));

        decoded.Should().Be(DamlOptionalChain.Some(DamlOptionalChain.None));
    }

    [Fact]
    public void ReadOptionalChain_should_compose_to_decode_Some_of_Some_at_the_inner_level()
    {
        using var document = JsonDocument.Parse("""[["ink"]]""");
        var context = DamlLfJsonDecodeContext.Root("Nested");

        var decoded = DamlLfJsonDecoders.ReadOptionalChain(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadOptionalChain(element, elementContext, DamlLfJsonDecoders.ReadText));

        decoded.Should().Be(DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("ink"))));
    }

    [Fact]
    public void ReadOptionalChain_should_reject_a_non_array_shape()
    {
        using var document = JsonDocument.Parse("null");
        var context = DamlLfJsonDecodeContext.Root("Nested");

        var act = () => DamlLfJsonDecoders.ReadOptionalChain(document.RootElement, context, DamlLfJsonDecoders.ReadText);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Array at 'Nested' but found Null");
    }

    [Fact]
    public void ReadOptionalChain_should_reject_an_array_of_more_than_one_element()
    {
        using var document = JsonDocument.Parse("""["a","b"]""");
        var context = DamlLfJsonDecodeContext.Root("Nested");

        var act = () => DamlLfJsonDecoders.ReadOptionalChain(document.RootElement, context, DamlLfJsonDecoders.ReadText);

        act.Should().Throw<JsonException>().WithMessage(
            "A nested Daml Optional at 'Nested' encodes as an array of at most one element but found 2");
    }

    [Fact]
    public void ReadTextMap_should_decode_each_entry_value_at_a_quoted_key_path()
    {
        using var document = JsonDocument.Parse("""{"gold":"1","silver":"2"}""");
        var context = DamlLfJsonDecodeContext.Root("Balances");

        var decoded = DamlLfJsonDecoders.ReadTextMap(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(new DamlTextMap(new Dictionary<string, DamlValue>
        {
            ["gold"] = new DamlInt64(1),
            ["silver"] = new DamlInt64(2),
        }));
    }

    [Fact]
    public void ReadTextMap_should_report_the_quoted_key_path_of_a_malformed_entry()
    {
        using var document = JsonDocument.Parse("""{"gold":"one"}""");
        var context = DamlLfJsonDecodeContext.Root("Balances");

        var act = () => DamlLfJsonDecoders.ReadTextMap(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Value 'one' at 'Balances['gold']' is not a valid Daml Int64");
    }

    [Fact]
    public void ReadTextMap_should_reject_a_non_object_shape()
    {
        using var document = JsonDocument.Parse("[]");
        var context = DamlLfJsonDecodeContext.Root("Balances");

        var act = () => DamlLfJsonDecoders.ReadTextMap(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Object at 'Balances' but found Array");
    }

    [Fact]
    public void ReadTextMap_should_reject_more_entries_than_the_configured_limit()
    {
        using var document = JsonDocument.Parse("""{"a":"1","b":"2","c":"3"}""");
        var context = DamlLfJsonDecodeContext.Root("Balances", new DamlJsonDeserializationLimits(MaxArrayElements: 2));

        var act = () => DamlLfJsonDecoders.ReadTextMap(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage(
            "JSON object property count 3 exceeds the maximum supported Daml TextMap entry count of 2");
    }

    [Fact]
    public void ReadGenMap_should_decode_each_key_value_pair()
    {
        using var document = JsonDocument.Parse("""[[{"party":"alice::1220ab"},"1"]]""");
        var context = DamlLfJsonDecodeContext.Root("Ledger");

        var decoded = DamlLfJsonDecoders.ReadGenMap(
            document.RootElement,
            context,
            DamlLfJsonDecoders.ReadRecord<PartyKeyRecord>,
            DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(new DamlGenMap([
            (DamlRecord.Create(DamlField.Create("party", new DamlParty("alice::1220ab"))), new DamlInt64(1))
        ]));
    }

    public sealed record PartyKeyRecord([property: DamlFieldAttribute("party")] Party Party) : IDamlRecord<PartyKeyRecord>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("party", Party.ToDamlValue()));

        public static PartyKeyRecord FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("party").As<DamlParty>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("party", DamlLfJsonDecoders.ReadParty(
                DamlLfJsonDecoders.RequireField(json, context, "party"), context.Field("party"))));
        }
    }

    [Fact]
    public void ReadGenMap_should_reject_a_non_array_shape()
    {
        using var document = JsonDocument.Parse("{}");
        var context = DamlLfJsonDecodeContext.Root("Ledger");

        var act = () => DamlLfJsonDecoders.ReadGenMap(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Array at 'Ledger' but found Object");
    }

    [Fact]
    public void ReadGenMap_should_reject_an_entry_that_is_not_a_two_element_pair()
    {
        using var document = JsonDocument.Parse("""[["only-one"]]""");
        var context = DamlLfJsonDecodeContext.Root("Ledger");

        var act = () => DamlLfJsonDecoders.ReadGenMap(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Expected a two-element key/value pair at 'Ledger[0]' but found 1 element(s)");
    }

    [Fact]
    public void ReadGenMap_should_reject_a_duplicate_key()
    {
        using var document = JsonDocument.Parse("""[["gold","1"],["gold","2"]]""");
        var context = DamlLfJsonDecodeContext.Root("Ledger");

        var act = () => DamlLfJsonDecoders.ReadGenMap(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Duplicate key at 'Ledger[1]' in a Daml GenMap");
    }

    [Fact]
    public void ReadGenMap_should_reject_more_entries_than_the_configured_limit()
    {
        using var document = JsonDocument.Parse("""[["a","1"],["b","2"],["c","3"]]""");
        var context = DamlLfJsonDecodeContext.Root("Ledger", new DamlJsonDeserializationLimits(MaxArrayElements: 2));

        var act = () => DamlLfJsonDecoders.ReadGenMap(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("JSON array length 3 exceeds the maximum supported JSON array length of 2");
    }

    [Fact]
    public void ReadSet_should_decode_its_elements_from_the_wrapped_map_field()
    {
        using var document = JsonDocument.Parse("""{"map":[["1",{}],["2",{}]]}""");
        var context = DamlLfJsonDecodeContext.Root("Members");

        var decoded = DamlLfJsonDecoders.ReadSet(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(DamlRecord.Create(DamlField.Create("map", new DamlGenMap([
            (new DamlInt64(1), DamlUnit.Instance),
            (new DamlInt64(2), DamlUnit.Instance),
        ]))));
    }

    [Fact]
    public void ReadSet_should_reject_a_missing_map_field()
    {
        using var document = JsonDocument.Parse("{}");
        var context = DamlLfJsonDecodeContext.Root("Members");

        var act = () => DamlLfJsonDecoders.ReadSet(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Members.map' is missing from the JSON object");
    }

    [Fact]
    public void ReadSet_should_reject_a_duplicate_element()
    {
        using var document = JsonDocument.Parse("""{"map":[["1",{}],["1",{}]]}""");
        var context = DamlLfJsonDecodeContext.Root("Members");

        var act = () => DamlLfJsonDecoders.ReadSet(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Duplicate key at 'Members.map[1]' in a Daml Set");
    }

    [Fact]
    public void ReadStdlibMap_should_decode_its_entries_from_the_wrapped_map_field()
    {
        using var document = JsonDocument.Parse("""{"map":[["gold","1"]]}""");
        var context = DamlLfJsonDecodeContext.Root("Balances");

        var decoded = DamlLfJsonDecoders.ReadStdlibMap(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(DamlRecord.Create(DamlField.Create("map", new DamlGenMap([
            (new DamlText("gold"), new DamlInt64(1))
        ]))));
    }

    [Fact]
    public void ReadStdlibMap_should_reject_a_missing_map_field()
    {
        using var document = JsonDocument.Parse("{}");
        var context = DamlLfJsonDecodeContext.Root("Balances");

        var act = () => DamlLfJsonDecoders.ReadStdlibMap(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Balances.map' is missing from the JSON object");
    }

    [Fact]
    public void ReadStdlibMap_should_reject_a_duplicate_key()
    {
        using var document = JsonDocument.Parse("""{"map":[["gold","1"],["gold","2"]]}""");
        var context = DamlLfJsonDecodeContext.Root("Balances");

        var act = () => DamlLfJsonDecoders.ReadStdlibMap(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Duplicate key at 'Balances.map[1]' in a Daml Map");
    }

    [Fact]
    public void ReadNonEmpty_should_decode_its_head_and_tail()
    {
        using var document = JsonDocument.Parse("""{"hd":"1","tl":["2","3"]}""");
        var context = DamlLfJsonDecodeContext.Root("Queue");

        var decoded = DamlLfJsonDecoders.ReadNonEmpty(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(new DamlRecord(null, [
            new DamlField("hd", new DamlInt64(1)),
            new DamlField("tl", new DamlList([new DamlInt64(2), new DamlInt64(3)])),
        ]));
    }

    [Fact]
    public void ReadNonEmpty_should_reject_a_missing_head_field()
    {
        using var document = JsonDocument.Parse("""{"tl":[]}""");
        var context = DamlLfJsonDecodeContext.Root("Queue");

        var act = () => DamlLfJsonDecoders.ReadNonEmpty(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Queue.hd' is missing from the JSON object");
    }

    [Fact]
    public void ReadNonEmpty_should_reject_a_missing_tail_field()
    {
        using var document = JsonDocument.Parse("""{"hd":"1"}""");
        var context = DamlLfJsonDecodeContext.Root("Queue");

        var act = () => DamlLfJsonDecoders.ReadNonEmpty(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Queue.tl' is missing from the JSON object");
    }

    [Fact]
    public void ReadNonEmpty_should_reject_a_tail_wider_than_the_configured_limit()
    {
        using var document = JsonDocument.Parse("""{"hd":"1","tl":["2","3","4"]}""");
        var context = DamlLfJsonDecodeContext.Root("Queue", new DamlJsonDeserializationLimits(MaxArrayElements: 2));

        var act = () => DamlLfJsonDecoders.ReadNonEmpty(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("JSON array length 3 exceeds the maximum supported JSON array length of 2");
    }

    [Fact]
    public void ReadNonEmpty_should_allow_a_tail_element_exactly_at_the_maximum_supported_depth()
    {
        using var document = JsonDocument.Parse("""{"hd":"1","tl":["2"]}""");
        var context = DamlLfJsonDecodeContext.Root("Queue");
        for (var level = 0; level < 126; level++)
        {
            context = context.Field("f");
        }
        context.Depth.Should().Be(126);

        var decoded = DamlLfJsonDecoders.ReadNonEmpty(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(new DamlRecord(null, [
            new DamlField("hd", new DamlInt64(1)),
            new DamlField("tl", new DamlList([new DamlInt64(2)])),
        ]));
    }

    [Fact]
    public void ReadNonEmpty_should_nest_a_tail_element_one_level_deeper_than_the_head()
    {
        using var document = JsonDocument.Parse("""{"hd":"1","tl":["2"]}""");
        var context = DamlLfJsonDecodeContext.Root("Queue");
        for (var level = 0; level < 127; level++)
        {
            context = context.Field("f");
        }
        context.Depth.Should().Be(127);

        var act = () => DamlLfJsonDecoders.ReadNonEmpty(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Value nesting exceeds the maximum supported depth of 128");
    }

    private static readonly DamlLfElementReader ReaderThatMustNotBeCalled =
        (_, _) => throw new Xunit.Sdk.XunitException("the component reader must not be called");

    private static readonly DamlLfElementReader ReadOptionalText =
        (element, elementContext) => DamlLfJsonDecoders.ReadOptional(element, elementContext, DamlLfJsonDecoders.ReadText);

    [Fact]
    public void ReadTuple2_should_decode_both_components()
    {
        using var document = JsonDocument.Parse("""{"_1":"gold","_2":"42"}""");
        var context = DamlLfJsonDecodeContext.Root("Pair");

        var decoded = DamlLfJsonDecoders.ReadTuple2(
            document.RootElement, context, DamlLfJsonDecoders.ReadText, null, DamlLfJsonDecoders.ReadInt64, null);

        decoded.Should().Be(new DamlRecord(null, [
            new DamlField("_1", new DamlText("gold")),
            new DamlField("_2", new DamlInt64(42)),
        ]));
    }

    [Fact]
    public void ReadTuple2_should_reject_a_missing_required_component()
    {
        using var document = JsonDocument.Parse("""{"_1":"gold"}""");
        var context = DamlLfJsonDecodeContext.Root("Pair");

        var act = () => DamlLfJsonDecoders.ReadTuple2(
            document.RootElement, context, DamlLfJsonDecoders.ReadText, null, DamlLfJsonDecoders.ReadInt64, null);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Pair._2' is missing from the JSON object");
    }

    [Fact]
    public void ReadTuple2_should_keep_no_field_for_an_omitted_Optional_component()
    {
        using var document = JsonDocument.Parse("""{"_1":"alice"}""");
        var context = DamlLfJsonDecodeContext.Root("Key");

        var decoded = DamlLfJsonDecoders.ReadTuple2(
            document.RootElement, context, DamlLfJsonDecoders.ReadParty, null, ReadOptionalText, DamlOptional.None);

        decoded.Should().Be(new DamlRecord(null, [new DamlField("_1", new DamlParty("alice"))]));
    }

    [Fact]
    public void ReadTuple2_should_keep_a_present_null_Optional_component_as_a_None_field()
    {
        using var document = JsonDocument.Parse("""{"_1":"alice","_2":null}""");
        var context = DamlLfJsonDecodeContext.Root("Key");

        var decoded = DamlLfJsonDecoders.ReadTuple2(
            document.RootElement, context, DamlLfJsonDecoders.ReadParty, null, ReadOptionalText, DamlOptional.None);

        decoded.Should().Be(new DamlRecord(null, [
            new DamlField("_1", new DamlParty("alice")),
            new DamlField("_2", DamlOptional.None),
        ]));
    }

    [Fact]
    public void ReadTuple2_should_reject_an_omitted_required_first_component_without_calling_its_reader()
    {
        using var document = JsonDocument.Parse("""{"_2":"note"}""");
        var context = DamlLfJsonDecodeContext.Root("key");

        var act = () => DamlLfJsonDecoders.ReadTuple2(
            document.RootElement, context, ReaderThatMustNotBeCalled, null, ReadOptionalText, DamlOptional.None);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'key._1' is missing from the JSON object");
    }

    [Fact]
    public void ReadTuple2_should_reject_a_non_object_shape()
    {
        using var document = JsonDocument.Parse("""["gold","42"]""");
        var context = DamlLfJsonDecodeContext.Root("Pair");

        var act = () => DamlLfJsonDecoders.ReadTuple2(
            document.RootElement, context, DamlLfJsonDecoders.ReadText, null, DamlLfJsonDecoders.ReadInt64, null);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Object at 'Pair' but found Array");
    }

    [Fact]
    public void ReadTuple3_should_decode_all_three_components()
    {
        using var document = JsonDocument.Parse("""{"_1":"gold","_2":"42","_3":true}""");
        var context = DamlLfJsonDecodeContext.Root("Triple");

        var decoded = DamlLfJsonDecoders.ReadTuple3(
            document.RootElement,
            context,
            DamlLfJsonDecoders.ReadText,
            null,
            DamlLfJsonDecoders.ReadInt64,
            null,
            DamlLfJsonDecoders.ReadBool,
            null);

        decoded.Should().Be(new DamlRecord(null, [
            new DamlField("_1", new DamlText("gold")),
            new DamlField("_2", new DamlInt64(42)),
            new DamlField("_3", new DamlBool(true)),
        ]));
    }

    [Fact]
    public void ReadTuple3_should_reject_a_missing_required_third_component()
    {
        using var document = JsonDocument.Parse("""{"_1":"gold","_2":"42"}""");
        var context = DamlLfJsonDecodeContext.Root("Triple");

        var act = () => DamlLfJsonDecoders.ReadTuple3(
            document.RootElement,
            context,
            DamlLfJsonDecoders.ReadText,
            null,
            DamlLfJsonDecoders.ReadInt64,
            null,
            DamlLfJsonDecoders.ReadBool,
            null);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Triple._3' is missing from the JSON object");
    }

    [Fact]
    public void ReadTuple3_should_keep_no_field_for_an_omitted_Optional_component()
    {
        using var document = JsonDocument.Parse("""{"_1":"gold","_2":"42"}""");
        var context = DamlLfJsonDecodeContext.Root("Triple");

        var decoded = DamlLfJsonDecoders.ReadTuple3(
            document.RootElement,
            context,
            DamlLfJsonDecoders.ReadText,
            null,
            DamlLfJsonDecoders.ReadInt64,
            null,
            ReadOptionalText,
            DamlOptional.None);

        decoded.Should().Be(new DamlRecord(null, [
            new DamlField("_1", new DamlText("gold")),
            new DamlField("_2", new DamlInt64(42)),
        ]));
    }

    [Fact]
    public void ReadTuple3_should_reject_an_omitted_required_first_component_without_calling_its_reader()
    {
        using var document = JsonDocument.Parse("""{"_2":"42","_3":"note"}""");
        var context = DamlLfJsonDecodeContext.Root("key");

        var act = () => DamlLfJsonDecoders.ReadTuple3(
            document.RootElement,
            context,
            ReaderThatMustNotBeCalled,
            null,
            DamlLfJsonDecoders.ReadInt64,
            null,
            ReadOptionalText,
            DamlOptional.None);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'key._1' is missing from the JSON object");
    }

    [Fact]
    public void ReadEither_should_decode_a_Left_tag()
    {
        using var document = JsonDocument.Parse("""{"tag":"Left","value":"oops"}""");
        var context = DamlLfJsonDecodeContext.Root("Outcome");

        var decoded = DamlLfJsonDecoders.ReadEither(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(DamlVariant.Create("Left", new DamlText("oops")));
    }

    [Fact]
    public void ReadEither_should_decode_a_Right_tag()
    {
        using var document = JsonDocument.Parse("""{"tag":"Right","value":"7"}""");
        var context = DamlLfJsonDecodeContext.Root("Outcome");

        var decoded = DamlLfJsonDecoders.ReadEither(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        decoded.Should().Be(DamlVariant.Create("Right", new DamlInt64(7)));
    }

    [Fact]
    public void ReadEither_should_reject_an_unknown_tag()
    {
        using var document = JsonDocument.Parse("""{"tag":"Neither","value":"7"}""");
        var context = DamlLfJsonDecodeContext.Root("Outcome");

        var act = () => DamlLfJsonDecoders.ReadEither(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage(
            "Unknown Daml variant constructor 'Neither' at 'Outcome'; expected one of Left, Right");
    }

    [Fact]
    public void ReadEither_should_reject_a_missing_value_member()
    {
        using var document = JsonDocument.Parse("""{"tag":"Left"}""");
        var context = DamlLfJsonDecodeContext.Root("Outcome");

        var act = () => DamlLfJsonDecoders.ReadEither(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Required Daml variant field 'Outcome.value' is missing from the JSON object");
    }

    [Fact]
    public void ReadEither_should_reject_a_non_object_shape()
    {
        using var document = JsonDocument.Parse("\"Left\"");
        var context = DamlLfJsonDecodeContext.Root("Outcome");

        var act = () => DamlLfJsonDecoders.ReadEither(document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Object at 'Outcome' but found String");
    }

    [Fact]
    public void ReadList_of_ReadTuple2_should_nest_the_bracketed_index_ahead_of_the_component_label()
    {
        using var document = JsonDocument.Parse("""[{"_1":"gold","_2":"42"},{"_1":"silver","_2":"seven"}]""");
        var context = DamlLfJsonDecodeContext.Root("Pairs");

        var act = () => DamlLfJsonDecoders.ReadList(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadTuple2(
                element, elementContext, DamlLfJsonDecoders.ReadText, null, DamlLfJsonDecoders.ReadInt64, null));

        act.Should().Throw<JsonException>().WithMessage("Value 'seven' at 'Pairs[1]._2' is not a valid Daml Int64");
    }

    [Fact]
    public void ReadList_of_ReadList_should_enforce_the_shared_depth_bound_across_composed_readers()
    {
        const int levelsBeforeTheOuterList = 127;
        using var document = JsonDocument.Parse("""[[{}]]""");
        var context = DamlLfJsonDecodeContext.Root("Nested");
        for (var level = 0; level < levelsBeforeTheOuterList; level++)
        {
            context = context.Field("f");
        }
        context.Depth.Should().Be(127);

        var act = () => DamlLfJsonDecoders.ReadList(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadList(element, elementContext, DamlLfJsonDecoders.ReadUnit));

        act.Should().Throw<JsonException>().WithMessage("Value nesting exceeds the maximum supported depth of 128");
    }

    [Fact]
    public void RequireObject_should_return_the_element_when_it_is_a_JSON_object()
    {
        using var document = JsonDocument.Parse("""{"count":"1"}""");
        var context = DamlLfJsonDecodeContext.Root("Widget");

        var result = DamlLfJsonDecoders.RequireObject(document.RootElement, context);

        result.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void RequireObject_should_reject_a_non_object_shape_at_its_context_path()
    {
        using var document = JsonDocument.Parse("42");
        var context = DamlLfJsonDecodeContext.Root("Widget");

        var act = () => DamlLfJsonDecoders.RequireObject(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Object at 'Widget' but found Number");
    }

    [Fact]
    public void RequireField_should_return_the_named_property_value()
    {
        using var document = JsonDocument.Parse("""{"count":"1"}""");
        var context = DamlLfJsonDecodeContext.Root("Widget");

        var result = DamlLfJsonDecoders.RequireField(document.RootElement, context, "count");

        result.GetString().Should().Be("1");
    }

    [Fact]
    public void RequireField_should_report_the_field_path_not_the_record_path_when_missing()
    {
        using var document = JsonDocument.Parse("{}");
        var context = DamlLfJsonDecodeContext.Root("Widget");

        var act = () => DamlLfJsonDecoders.RequireField(document.RootElement, context, "count");

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Widget.count' is missing from the JSON object");
    }

    [Fact]
    public void AddFieldIfPresent_should_add_the_decoded_field_when_the_property_is_present()
    {
        using var document = JsonDocument.Parse("""{"note":"ren"}""");
        var context = DamlLfJsonDecodeContext.Root("Widget");
        var fields = new List<DamlField>();

        DamlLfJsonDecoders.AddFieldIfPresent(
            fields, document.RootElement, "note",
            present => DamlLfJsonDecoders.ReadOptional(present, context.Field("note"), DamlLfJsonDecoders.ReadText));

        fields.Should().Equal(DamlField.Create("note", DamlOptional.Some(new DamlText("ren"))));
    }

    [Fact]
    public void AddFieldIfPresent_should_add_nothing_when_the_property_is_absent()
    {
        using var document = JsonDocument.Parse("""{"other":"x"}""");
        var fields = new List<DamlField>();

        DamlLfJsonDecoders.AddFieldIfPresent(
            fields, document.RootElement, "note", _ => throw new InvalidOperationException("must not decode an absent property"));

        fields.Should().BeEmpty();
    }

    [Fact]
    public void AddFieldIfPresent_should_add_a_flat_None_field_for_a_present_null()
    {
        using var document = JsonDocument.Parse("""{"note":null}""");
        var context = DamlLfJsonDecodeContext.Root("Widget");
        var fields = new List<DamlField>();

        DamlLfJsonDecoders.AddFieldIfPresent(
            fields, document.RootElement, "note",
            present => DamlLfJsonDecoders.ReadOptional(present, context.Field("note"), DamlLfJsonDecoders.ReadText));

        fields.Should().Equal(DamlField.Create("note", DamlOptional.None));
    }

    [Fact]
    public void AddFieldIfPresent_should_append_after_the_fields_already_collected()
    {
        using var document = JsonDocument.Parse("""{"note":"ren"}""");
        var fields = new List<DamlField> { DamlField.Create("owner", new DamlParty("alice")) };

        DamlLfJsonDecoders.AddFieldIfPresent(fields, document.RootElement, "note", present => new DamlText(present.GetString()!));

        fields.Select(field => field.Label).Should().Equal("owner", "note");
    }

    [Fact]
    public void AddTypeParameterField_should_decode_a_present_field_with_the_instantiation_reader()
    {
        using var document = JsonDocument.Parse("""{"item":"gold"}""");
        var context = DamlLfJsonDecodeContext.Root("Box");
        var fields = new List<DamlField>();

        DamlLfJsonDecoders.AddTypeParameterField(
            fields, document.RootElement, context, "item", DamlLfJsonDecoders.ReadText, null);

        fields.Should().Equal(DamlField.Create("item", new DamlText("gold")));
    }

    [Fact]
    public void AddTypeParameterField_should_add_no_field_for_an_omitted_field_at_an_Optional_instantiation()
    {
        using var document = JsonDocument.Parse("{}");
        var context = DamlLfJsonDecodeContext.Root("Box");
        var fields = new List<DamlField>();

        DamlLfJsonDecoders.AddTypeParameterField(
            fields, document.RootElement, context, "item", ReaderThatMustNotBeCalled, DamlOptional.None);

        fields.Should().BeEmpty();
    }

    [Fact]
    public void AddTypeParameterField_should_keep_a_present_null_as_a_None_field_at_an_Optional_instantiation()
    {
        using var document = JsonDocument.Parse("""{"item":null}""");
        var context = DamlLfJsonDecodeContext.Root("Box");
        var fields = new List<DamlField>();

        DamlLfJsonDecoders.AddTypeParameterField(
            fields, document.RootElement, context, "item", ReadOptionalText, DamlOptional.None);

        fields.Should().Equal(DamlField.Create("item", DamlOptional.None));
    }

    [Fact]
    public void AddTypeParameterField_should_report_an_omitted_field_missing_at_a_required_instantiation_without_calling_the_reader()
    {
        using var document = JsonDocument.Parse("{}");
        var context = DamlLfJsonDecodeContext.Root("Box");

        var act = () => DamlLfJsonDecoders.AddTypeParameterField(
            [], document.RootElement, context, "item", ReaderThatMustNotBeCalled, null);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Box.item' is missing from the JSON object");
    }

    [Fact]
    public void AddTypeParameterField_should_surface_the_JsonException_a_reader_throws_on_a_present_value()
    {
        using var document = JsonDocument.Parse("""{"item":5}""");
        var context = DamlLfJsonDecodeContext.Root("Box");

        var act = () => DamlLfJsonDecoders.AddTypeParameterField(
            [], document.RootElement, context, "item", DamlLfJsonDecoders.ReadText, null);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'Box.item' but found Number");
    }

    [Fact]
    public void ReadVariantTag_with_context_should_decode_the_tag_after_guarding_object_shape()
    {
        using var document = JsonDocument.Parse("""{"tag":"Stars","value":"3"}""");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        DamlLfJsonDecoders.ReadVariantTag(document.RootElement, context).Should().Be("Stars");
    }

    [Fact]
    public void ReadVariantTag_with_context_should_reject_a_non_object_shape_at_its_context_path()
    {
        using var document = JsonDocument.Parse("\"Stars\"");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var act = () => DamlLfJsonDecoders.ReadVariantTag(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Object at 'Rating' but found String");
    }

    [Fact]
    public void RequireVariantValue_should_return_the_value_property()
    {
        using var document = JsonDocument.Parse("""{"tag":"Stars","value":"3"}""");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var result = DamlLfJsonDecoders.RequireVariantValue(document.RootElement, context);

        result.GetString().Should().Be("3");
    }

    [Fact]
    public void RequireVariantValue_should_reject_a_missing_value_member()
    {
        using var document = JsonDocument.Parse("""{"tag":"Stars"}""");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var act = () => DamlLfJsonDecoders.RequireVariantValue(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage("Required Daml variant field 'Rating.value' is missing from the JSON object");
    }

    [Fact]
    public void UnknownConstructor_with_context_should_report_the_context_path()
    {
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var exception = DamlLfJsonDecoders.UnknownConstructor("variant constructor", "Bogus", context, ["Stars", "Unrated"]);

        exception.Message.Should().Be("Unknown Daml variant constructor 'Bogus' at 'Rating'; expected one of Stars, Unrated");
    }

    [Fact]
    public void ReadEnumConstructor_should_decode_a_known_constructor()
    {
        using var document = JsonDocument.Parse("\"Stars\"");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        DamlLfJsonDecoders.ReadEnumConstructor(document.RootElement, context, ["Stars", "Unrated"])
            .Should().Be(DamlEnum.Create("Stars"));
    }

    [Fact]
    public void ReadEnumConstructor_should_reject_a_constructor_outside_the_expected_set()
    {
        using var document = JsonDocument.Parse("\"Bogus\"");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var act = () => DamlLfJsonDecoders.ReadEnumConstructor(document.RootElement, context, ["Stars", "Unrated"]);

        act.Should().Throw<JsonException>().WithMessage("Unknown Daml enum constructor 'Bogus' at 'Rating'; expected one of Stars, Unrated");
    }

    [Fact]
    public void ReadEnumConstructor_should_reject_a_non_string_shape()
    {
        using var document = JsonDocument.Parse("42");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var act = () => DamlLfJsonDecoders.ReadEnumConstructor(document.RootElement, context, ["Stars", "Unrated"]);

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'Rating' but found Number");
    }

    [Fact]
    public void DirectionExtensions_ReadDamlLfJson_should_report_the_context_path_in_an_unknown_constructor_error()
    {
        using var document = JsonDocument.Parse("\"Sideways\"");
        var context = DamlLfJsonDecodeContext.Root("Direction");

        var act = () => DirectionExtensions.__ReadDamlLfJson(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage(
            "Unknown Daml enum constructor 'Sideways' at 'Direction'; expected one of Forward, U$u0020Turn");
    }

    [Fact]
    public void ReadEnumConstructor_should_reject_a_constructor_differing_only_by_case()
    {
        using var document = JsonDocument.Parse("\"hearts\"");
        var context = DamlLfJsonDecodeContext.Root("Suit");

        var act = () => DamlLfJsonDecoders.ReadEnumConstructor(document.RootElement, context, ["Hearts", "Spades"]);

        act.Should().Throw<JsonException>().WithMessage("Unknown Daml enum constructor 'hearts' at 'Suit'; expected one of Hearts, Spades");
    }

    [Fact]
    public void ReadEnumConstructor_should_elide_a_65_character_constructor_name_at_the_64_character_boundary()
    {
        var oversizedName = new string('B', 65);
        using var document = JsonDocument.Parse($"\"{oversizedName}\"");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var act = () => DamlLfJsonDecoders.ReadEnumConstructor(document.RootElement, context, ["Stars", "Unrated"]);

        act.Should().Throw<JsonException>().WithMessage(
            $"Unknown Daml enum constructor '{new string('B', 64)}…' at 'Rating'; expected one of Stars, Unrated");
    }

    [Fact]
    public void ReadEnumConstructor_should_elide_one_character_earlier_when_the_64th_character_is_a_high_surrogate()
    {
        var surrogateSplitName = new string('B', 63) + "😀";
        using var document = JsonDocument.Parse($"\"{surrogateSplitName}\"");
        var context = DamlLfJsonDecodeContext.Root("Rating");

        var act = () => DamlLfJsonDecoders.ReadEnumConstructor(document.RootElement, context, ["Stars", "Unrated"]);

        act.Should().Throw<JsonException>().WithMessage(
            $"Unknown Daml enum constructor '{new string('B', 63)}…' at 'Rating'; expected one of Stars, Unrated");
    }

    [Fact]
    public void ReadUnsupported_should_always_throw_naming_the_unresolved_Daml_type_and_the_context_path()
    {
        using var document = JsonDocument.Parse("""{"anything":"here"}""");
        var context = DamlLfJsonDecodeContext.Root("GenericResults").Field("split");

        var act = () => DamlLfJsonDecoders.ReadUnsupported(document.RootElement, context, "RichTypes.Holding");

        act.Should().Throw<NotSupportedException>().WithMessage(
            "Daml type 'RichTypes.Holding' at 'GenericResults.split' lies outside the emitted Daml-LF JSON decoders");
    }

    [Fact]
    public void ReadList_over_an_empty_array_should_never_invoke_the_element_reader()
    {
        using var document = JsonDocument.Parse("[]");
        var context = DamlLfJsonDecodeContext.Root("Root");

        var act = () => DamlLfJsonDecoders.ReadList(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadUnsupported(element, elementContext, "RichTypes.Holding"));

        act.Should().NotThrow();
    }

    [Fact]
    public void ReadOptional_over_a_null_value_should_never_invoke_the_element_reader()
    {
        using var document = JsonDocument.Parse("null");
        var context = DamlLfJsonDecodeContext.Root("Root");

        var act = () => DamlLfJsonDecoders.ReadOptional(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadUnsupported(element, elementContext, "RichTypes.Holding"));

        act.Should().NotThrow();
    }

    [Fact]
    public void ReadList_over_a_single_element_array_should_invoke_the_element_reader_at_the_indexed_path()
    {
        using var document = JsonDocument.Parse("""["x"]""");
        var context = DamlLfJsonDecodeContext.Root("Root");

        var act = () => DamlLfJsonDecoders.ReadList(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadUnsupported(element, elementContext, "RichTypes.Holding"));

        act.Should().Throw<NotSupportedException>().WithMessage(
            "Daml type 'RichTypes.Holding' at 'Root[0]' lies outside the emitted Daml-LF JSON decoders");
    }

    public sealed record SentinelRecord(long Value) : IDamlRecord<SentinelRecord>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("value", new DamlInt64(Value)));

        public static SentinelRecord FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("value").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var record = DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create(
                "value",
                DamlLfJsonDecoders.ReadInt64(
                    DamlLfJsonDecoders.RequireField(record, context, "value"), context.Field("value"))));
        }
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadRecordT_should_route_through_the_type_s_own_emitted_reader()
    {
        using var document = JsonDocument.Parse("""{"value":"7"}""");
        var context = DamlLfJsonDecodeContext.Root("SentinelRecord");

        var record = DamlLfJsonDecoders.ReadRecord<SentinelRecord>(document.RootElement, context);

        record.GetRequiredField("value").As<DamlInt64>().Value.Should().Be(7);
    }

    [Fact]
    public void DamlLfJsonReader_ReadRecordT_from_a_JsonElement_should_route_through_the_type_s_own_emitted_reader()
    {
        using var document = JsonDocument.Parse("""{"value":"7"}""");

        var record = DamlLfJsonReader.ReadRecord<SentinelRecord>(document.RootElement);

        record.GetRequiredField("value").As<DamlInt64>().Value.Should().Be(7);
    }

    [Fact]
    public void DamlLfJsonReader_ReadRecordT_from_a_string_should_route_through_the_type_s_own_emitted_reader()
    {
        var record = DamlLfJsonReader.ReadRecord<SentinelRecord>("""{"value":"7"}""");

        record.GetRequiredField("value").As<DamlInt64>().Value.Should().Be(7);
    }

    public sealed record SentinelVariant(long Value) : IDamlVariant<SentinelVariant>
    {
        public DamlVariant ToVariant() => DamlVariant.Create("Value", new DamlInt64(Value));

        public static DamlVariant __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var tag = DamlLfJsonDecoders.ReadVariantTag(json, context);
            return tag switch
            {
                "Value" => DamlVariant.Create(
                    tag,
                    DamlLfJsonDecoders.ReadInt64(
                        DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                _ => throw DamlLfJsonDecoders.UnknownConstructor("variant", tag, context, ["Value"])
            };
        }
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadVariantT_should_route_through_the_type_s_own_emitted_reader()
    {
        using var document = JsonDocument.Parse("""{"tag":"Value","value":"7"}""");
        var context = DamlLfJsonDecodeContext.Root("SentinelVariant");

        var variant = DamlLfJsonDecoders.ReadVariant<SentinelVariant>(document.RootElement, context);

        variant.Constructor.Should().Be("Value");
        variant.Value.As<DamlInt64>().Value.Should().Be(7);
    }

    private static readonly JsonDocumentOptions DeepChainDocumentOptions = new() { MaxDepth = 256 };

    private static string BuildOptionalChainJson(int layers, bool innermostPresent)
    {
        var json = innermostPresent ? "[\"1\"]" : "[]";
        for (var remaining = 1; remaining < layers; remaining++)
        {
            json = $"[{json}]";
        }
        return json;
    }

    private static DamlValue ReadOptionalChainOfDepth(JsonElement json, DamlLfJsonDecodeContext context, int layers) =>
        DamlLfJsonDecoders.ReadOptionalChain(json, context, (element, elementContext) =>
            layers == 1
                ? DamlLfJsonDecoders.ReadInt64(element, elementContext)
                : ReadOptionalChainOfDepth(element, elementContext, layers - 1));

    [Fact]
    public void An_Optional_chain_nested_to_the_maximum_supported_depth_should_decode()
    {
        using var document = JsonDocument.Parse(BuildOptionalChainJson(layers: 128, innermostPresent: true), DeepChainDocumentOptions);
        var context = DamlLfJsonDecodeContext.Root("Chain");

        var act = () => ReadOptionalChainOfDepth(document.RootElement, context, layers: 128);

        act.Should().NotThrow();
    }

    [Fact]
    public void An_Optional_chain_nested_one_level_past_the_maximum_should_throw_when_the_innermost_value_is_present()
    {
        using var document = JsonDocument.Parse(BuildOptionalChainJson(layers: 129, innermostPresent: true), DeepChainDocumentOptions);
        var context = DamlLfJsonDecodeContext.Root("Chain");

        var act = () => ReadOptionalChainOfDepth(document.RootElement, context, layers: 129);

        act.Should().Throw<JsonException>().WithMessage(
            "Value nesting exceeds the maximum supported depth of 128");
    }

    [Fact]
    public void An_Optional_chain_nested_one_level_past_the_maximum_should_decode_when_the_innermost_value_is_absent()
    {
        using var document = JsonDocument.Parse(BuildOptionalChainJson(layers: 129, innermostPresent: false), DeepChainDocumentOptions);
        var context = DamlLfJsonDecodeContext.Root("Chain");

        var act = () => ReadOptionalChainOfDepth(document.RootElement, context, layers: 129);

        act.Should().NotThrow();
    }

    public sealed record Branch : IDamlRecord<Branch>
    {
        [DamlFieldAttribute("next")]
        public Branch? Next { get; init; }

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "next", Next is null ? DamlOptional.None : DamlOptional.Some(Next.ToRecord())));

        public static Branch FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var record = DamlLfJsonDecoders.RequireObject(json, context);
            var nextJson = DamlLfJsonDecoders.RequireField(record, context, "next");
            return DamlRecord.Create(DamlField.Create(
                "next",
                DamlLfJsonDecoders.ReadOptional(
                    nextJson,
                    context.Field("next"),
                    (element, elementContext) => __ReadDamlLfJson(element, elementContext))));
        }
    }

    private static string BuildNestedBranchJson(int levels)
    {
        var json = "null";
        for (var remaining = 0; remaining < levels; remaining++)
        {
            json = $$"""{"next":{{json}}}""";
        }
        return json;
    }

    [Fact]
    public void The_emitted_path_should_decode_a_recursive_Optional_record_64_levels_deep()
    {
        using var document = JsonDocument.Parse(BuildNestedBranchJson(levels: 64), DeepChainDocumentOptions);
        var context = DamlLfJsonDecodeContext.Root("Branch");

        var act = () => Branch.__ReadDamlLfJson(document.RootElement, context);

        act.Should().NotThrow();
    }

    [Fact]
    public void The_emitted_path_should_throw_decoding_a_recursive_Optional_record_65_levels_deep()
    {
        using var document = JsonDocument.Parse(BuildNestedBranchJson(levels: 65), DeepChainDocumentOptions);
        var context = DamlLfJsonDecodeContext.Root("Branch");

        var act = () => Branch.__ReadDamlLfJson(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage(
            "Value nesting exceeds the maximum supported depth of 128");
    }

    private static string BuildNestedListArrayJson(int layers, string leaf)
    {
        var json = leaf;
        for (var level = 0; level < layers; level++)
        {
            json = $"[{json}]";
        }
        return json;
    }

    private static DamlValue ReadNestedListOfDepth(JsonElement json, DamlLfJsonDecodeContext context, int layers) =>
        layers == 0
            ? DamlLfJsonDecoders.ReadOptional(json, context, DamlLfJsonDecoders.ReadInt64)
            : DamlLfJsonDecoders.ReadList(json, context, (element, elementContext) => ReadNestedListOfDepth(element, elementContext, layers - 1));

    [Fact]
    public void The_emitted_path_should_decode_126_nested_lists_ending_in_a_present_Optional_leaf()
    {
        using var document = JsonDocument.Parse(BuildNestedListArrayJson(layers: 126, leaf: "\"1\""), DeepChainDocumentOptions);
        var context = DamlLfJsonDecodeContext.Root("NestedListDepth126Record").Field("x");

        var act = () => ReadNestedListOfDepth(document.RootElement, context, layers: 126);

        act.Should().NotThrow();
    }

    [Fact]
    public void The_emitted_path_should_throw_decoding_127_nested_lists_ending_in_a_present_Optional_leaf()
    {
        using var document = JsonDocument.Parse(BuildNestedListArrayJson(layers: 127, leaf: "\"1\""), DeepChainDocumentOptions);
        var context = DamlLfJsonDecodeContext.Root("NestedListDepth127Record").Field("x");

        var act = () => ReadNestedListOfDepth(document.RootElement, context, layers: 127);

        act.Should().Throw<JsonException>().WithMessage(
            "Value nesting exceeds the maximum supported depth of 128");
    }

    [Fact]
    public void The_emitted_path_should_decode_127_nested_lists_ending_in_an_absent_Optional_leaf()
    {
        using var document = JsonDocument.Parse(BuildNestedListArrayJson(layers: 127, leaf: "null"), DeepChainDocumentOptions);
        var context = DamlLfJsonDecodeContext.Root("NestedListDepth127Record").Field("x");

        var act = () => ReadNestedListOfDepth(document.RootElement, context, layers: 127);

        act.Should().NotThrow();
    }

    public sealed record ChoiceOwner : ITemplate
    {
        public static Identifier TemplateId { get; } = new("choice-owner-pkg", "WP1.Fakes", "ChoiceOwner");

        public static string PackageId => "choice-owner-pkg";

        public static string PackageName => "WP1FakesPackage";

        public static Version PackageVersion { get; } = new(1, 0, 0);

        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    [Fact]
    public void Choice_json_reading_and_decoding_should_compose_through_the_erased_IChoice_facet()
    {
        var choice = new Choice<ChoiceOwner, long, long>
        {
            Name = new ChoiceName("DoIt"),
            Consuming = true,
            ArgumentEncoder = value => new DamlInt64(value),
            ArgumentDecoder = value => value.As<DamlInt64>().Value,
            ResultDecoder = value => value.As<DamlInt64>().Value,
            ArgumentJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
            ResultJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
        };
        IChoice erased = choice;
        using var document = JsonDocument.Parse("\"9\"");
        var context = DamlLfJsonDecodeContext.Root("DoIt");

        erased.ReadArgumentJson(document.RootElement, context).Should().Be(new DamlInt64(9));
        erased.ReadResultJson(document.RootElement, context).Should().Be(new DamlInt64(9));
        erased.DecodeArgumentJson(document.RootElement, context).Should().Be(9L);
        erased.DecodeResultJson(document.RootElement, context).Should().Be(9L);
        choice.ArgumentJsonDecoder(document.RootElement, context).Should().Be(9L);
        choice.ResultJsonDecoder(document.RootElement, context).Should().Be(9L);
    }

    public sealed record KeyedFakeTemplate : ITemplate, IHasKey<KeyedFakeTemplate, string>
    {
        public static Identifier TemplateId { get; } = new("keyed-fake-pkg", "WP1.Fakes", "KeyedFakeTemplate");

        public static string PackageId => "keyed-fake-pkg";

        public static string PackageName => "WP1FakesPackage";

        public static Version PackageVersion { get; } = new(1, 0, 0);

        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<KeyedFakeTemplate, string> Key { get; } = new()
        {
            KeyEncoder = value => new DamlText(value),
            KeyDecoder = value => value.As<DamlText>().Value,
            KeyJsonReader = (json, context) => DamlLfJsonDecoders.ReadText(json, context),
        };

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    [Fact]
    public void KeyDescriptor_json_reading_and_decoding_should_compose_through_the_erased_IKeyDescriptor_facet()
    {
        IKeyDescriptor descriptor = KeyedFakeTemplate.Key;
        using var document = JsonDocument.Parse("\"alice\"");
        var context = DamlLfJsonDecodeContext.Root("Key");

        descriptor.TemplateType.Should().Be<KeyedFakeTemplate>();
        descriptor.KeyType.Should().Be<string>();
        descriptor.ReadKeyJson(document.RootElement, context).Should().Be(new DamlText("alice"));
        descriptor.DecodeKey(new DamlText("alice")).Should().Be("alice");
        KeyedFakeTemplate.Key.KeyJsonDecoder(document.RootElement, context).Should().Be("alice");
    }

    private sealed record RegistryExactRecord : IDamlType, IDamlRecord<RegistryExactRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-registry-exact", "Registry.Module", "ExactRecord"),
            DamlTypeKind.Template,
            "RegistryPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static RegistryExactRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    [Fact]
    public void GeneratedTypeReaders_ForRecord_should_be_found_by_its_exact_identifier()
    {
        GeneratedTypeReaders.ForRecord<RegistryExactRecord>();

        var lookup = GeneratedTypeReaders.FindRecordReader(RegistryExactRecord.DamlTypeId.Identifier);

        var resolved = lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>().Subject;
        resolved.Value.Should().Be((DamlLfElementReader)RegistryExactRecord.__ReadDamlLfJson);
    }

    [Fact]
    public void GeneratedTypeReaders_ForRecord_lookup_should_be_missing_for_an_unregistered_identifier()
    {
        var unregistered = new Identifier("pkg-never-registered", "Registry.Module", "NeverRegistered");

        var lookup = GeneratedTypeReaders.FindRecordReader(unregistered);

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Missing>();
    }

    private sealed record RegistryFallbackRecord : IDamlType, IDamlRecord<RegistryFallbackRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-registry-fallback-registered", "Registry.Module", "FallbackRecord"),
            DamlTypeKind.Template,
            "RegistryPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static RegistryFallbackRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    [Fact]
    public void GeneratedTypeReaders_ForRecord_lookup_should_fall_back_to_the_module_and_entity_when_the_package_differs_but_only_one_type_is_registered()
    {
        GeneratedTypeReaders.ForRecord<RegistryFallbackRecord>();
        var lookupUnderADifferentPackage = new Identifier(
            "pkg-registry-fallback-lookup", "Registry.Module", "FallbackRecord");

        var lookup = GeneratedTypeReaders.FindRecordReader(lookupUnderADifferentPackage);

        var resolved = lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>().Subject;
        resolved.Value.Should().Be((DamlLfElementReader)RegistryFallbackRecord.__ReadDamlLfJson);
    }

    private sealed record RegistryAmbiguousRecordA : IDamlType, IDamlRecord<RegistryAmbiguousRecordA>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-registry-ambiguous-a", "Registry.Module", "AmbiguousRecord"),
            DamlTypeKind.Template,
            "RegistryPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static RegistryAmbiguousRecordA FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    private sealed record RegistryAmbiguousRecordB : IDamlType, IDamlRecord<RegistryAmbiguousRecordB>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-registry-ambiguous-b", "Registry.Module", "AmbiguousRecord"),
            DamlTypeKind.Template,
            "RegistryPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static RegistryAmbiguousRecordB FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    [Fact]
    public void GeneratedTypeReaders_ForRecord_lookup_should_be_ambiguous_naming_both_declaring_types_when_the_module_and_entity_are_ambiguous()
    {
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordA>();
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordB>();
        var lookupUnderAThirdPackage = new Identifier(
            "pkg-registry-ambiguous-lookup", "Registry.Module", "AmbiguousRecord");

        var lookup = GeneratedTypeReaders.FindRecordReader(lookupUnderAThirdPackage);

        var ambiguous = lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Ambiguous>().Subject;
        ambiguous.Candidates.Should().Equal(
            "Daml.Runtime.Tests.DamlLfJsonDecodersTests+RegistryAmbiguousRecordA",
            "Daml.Runtime.Tests.DamlLfJsonDecodersTests+RegistryAmbiguousRecordB");
    }

    [Fact]
    public void GeneratedTypeReaders_ForRecord_lookup_by_each_registered_type_s_own_identifier_should_resolve_despite_the_shared_module_and_entity_ambiguity()
    {
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordA>();
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordB>();

        var lookupA = GeneratedTypeReaders.FindRecordReader(RegistryAmbiguousRecordA.DamlTypeId.Identifier);
        var lookupB = GeneratedTypeReaders.FindRecordReader(RegistryAmbiguousRecordB.DamlTypeId.Identifier);

        lookupA.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>().Subject
            .Value.Should().Be((DamlLfElementReader)RegistryAmbiguousRecordA.__ReadDamlLfJson);
        lookupB.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>().Subject
            .Value.Should().Be((DamlLfElementReader)RegistryAmbiguousRecordB.__ReadDamlLfJson);
    }

    [Fact]
    public void GeneratedTypeReaders_ForRecord_lookup_by_a_structurally_equal_but_distinct_Identifier_instance_should_still_resolve()
    {
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordA>();
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordB>();
        var structurallyEqualIdentifier = new Identifier("pkg-registry-ambiguous-a", "Registry.Module", "AmbiguousRecord");
        structurallyEqualIdentifier.Should().NotBeSameAs(RegistryAmbiguousRecordA.DamlTypeId.Identifier);

        var lookup = GeneratedTypeReaders.FindRecordReader(structurallyEqualIdentifier);

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>().Subject
            .Value.Should().Be((DamlLfElementReader)RegistryAmbiguousRecordA.__ReadDamlLfJson);
    }

    [Fact]
    public void GeneratedTypeReaders_ForRecord_re_registering_the_same_type_should_not_grow_its_module_and_entity_bucket()
    {
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordA>();
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordA>();
        GeneratedTypeReaders.ForRecord<RegistryAmbiguousRecordB>();
        var lookupUnderAThirdPackage = new Identifier(
            "pkg-registry-ambiguous-lookup", "Registry.Module", "AmbiguousRecord");

        var lookup = GeneratedTypeReaders.FindRecordReader(lookupUnderAThirdPackage);

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Ambiguous>().Subject.Candidates.Should().Equal(
            "Daml.Runtime.Tests.DamlLfJsonDecodersTests+RegistryAmbiguousRecordA",
            "Daml.Runtime.Tests.DamlLfJsonDecodersTests+RegistryAmbiguousRecordB");
    }

    private sealed record RegistryConcurrentRecord : IDamlType, IDamlRecord<RegistryConcurrentRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-registry-concurrent", "Registry.Module", "ConcurrentRecord"),
            DamlTypeKind.Template,
            "RegistryPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static RegistryConcurrentRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    [Fact]
    public async Task GeneratedTypeReaders_ForRecord_concurrent_registration_of_the_same_type_should_settle_on_one_entry()
    {
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(GeneratedTypeReaders.ForRecord<RegistryConcurrentRecord>)));

        var exact = GeneratedTypeReaders.FindRecordReader(RegistryConcurrentRecord.DamlTypeId.Identifier);
        var lookupUnderADifferentPackage = new Identifier(
            "pkg-registry-concurrent-lookup", "Registry.Module", "ConcurrentRecord");
        var byFallback = GeneratedTypeReaders.FindRecordReader(lookupUnderADifferentPackage);

        exact.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>();
        byFallback.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>();
    }

    private sealed record RegistryChoiceOwnerV1 : IDamlType, IHasChoices<RegistryChoiceOwnerV1>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-registry-choice-v1", "Registry.ChoiceModule", "ChoiceRecord"),
            DamlTypeKind.Template,
            "RegistryPackage");

        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            new Choice<RegistryChoiceOwnerV1, long, long>
            {
                Name = new ChoiceName("Common"),
                Consuming = true,
                ArgumentEncoder = value => new DamlInt64(value),
                ArgumentDecoder = value => value.As<DamlInt64>().Value,
                ResultDecoder = value => value.As<DamlInt64>().Value,
                ArgumentJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
                ResultJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
            },
        ];
    }

    private sealed record RegistryChoiceOwnerV2 : IDamlType, IHasChoices<RegistryChoiceOwnerV2>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-registry-choice-v2", "Registry.ChoiceModule", "ChoiceRecord"),
            DamlTypeKind.Template,
            "RegistryPackage");

        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            new Choice<RegistryChoiceOwnerV2, long, long>
            {
                Name = new ChoiceName("Common"),
                Consuming = true,
                ArgumentEncoder = value => new DamlInt64(value),
                ArgumentDecoder = value => value.As<DamlInt64>().Value,
                ResultDecoder = value => value.As<DamlInt64>().Value,
                ArgumentJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
                ResultJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
            },
            new Choice<RegistryChoiceOwnerV2, long, long>
            {
                Name = new ChoiceName("Added"),
                Consuming = true,
                ArgumentEncoder = value => new DamlInt64(value),
                ArgumentDecoder = value => value.As<DamlInt64>().Value,
                ResultDecoder = value => value.As<DamlInt64>().Value,
                ArgumentJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
                ResultJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
            },
        ];
    }

    [Fact]
    public void GeneratedTypeReaders_ForChoices_lookup_should_be_ambiguous_naming_both_declaring_types_when_the_module_and_entity_are_ambiguous_even_for_a_choice_only_one_of_them_declares()
    {
        GeneratedTypeReaders.ForChoices<RegistryChoiceOwnerV1>();
        GeneratedTypeReaders.ForChoices<RegistryChoiceOwnerV2>();
        var lookupUnderAThirdPackage = new Identifier(
            "pkg-registry-choice-lookup", "Registry.ChoiceModule", "ChoiceRecord");

        var lookup = GeneratedTypeReaders.FindChoice(lookupUnderAThirdPackage, new ChoiceName("Added"));

        lookup.Should().BeOfType<RegistryLookup<IChoice>.Ambiguous>().Subject.Candidates.Should().Equal(
            "Daml.Runtime.Tests.DamlLfJsonDecodersTests+RegistryChoiceOwnerV1",
            "Daml.Runtime.Tests.DamlLfJsonDecodersTests+RegistryChoiceOwnerV2");
    }

    [Fact]
    public void The_constrained_entry_points_should_not_carry_the_obsolete_diagnostic()
    {
        var constrainedEntryPoints = new[]
        {
            typeof(DamlLfJsonDecoders).GetMethod(
                nameof(DamlLfJsonDecoders.ReadRecord), 1, [typeof(JsonElement), typeof(DamlLfJsonDecodeContext)]),
            typeof(DamlLfJsonDecoders).GetMethod(
                nameof(DamlLfJsonDecoders.ReadVariant), 1, [typeof(JsonElement), typeof(DamlLfJsonDecodeContext)]),
            typeof(DamlLfJsonReader).GetMethod(
                nameof(DamlLfJsonReader.ReadRecord), 1, [typeof(JsonElement), typeof(DamlJsonDeserializationLimits)]),
            typeof(DamlLfJsonReader).GetMethod(
                nameof(DamlLfJsonReader.ReadRecord), 1, [typeof(string), typeof(DamlJsonDeserializationLimits)]),
        };

        constrainedEntryPoints.Should().NotContainNulls();
        constrainedEntryPoints.Should().OnlyContain(method => method!.GetCustomAttribute<ObsoleteAttribute>() == null);
    }

    private static bool AllInterfaceMembersAreAbstract(Type interfaceType) =>
        interfaceType
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OfType<MethodInfo>()
            .All(method => method.IsAbstract);

    [Fact]
    public void The_generated_dispatch_interfaces_should_declare_every_member_as_abstract()
    {
        AllInterfaceMembersAreAbstract(typeof(IDamlRecord<>)).Should().BeTrue();
        AllInterfaceMembersAreAbstract(typeof(IDamlVariant<>)).Should().BeTrue();
        AllInterfaceMembersAreAbstract(typeof(IKeyDescriptor)).Should().BeTrue();
    }
}
