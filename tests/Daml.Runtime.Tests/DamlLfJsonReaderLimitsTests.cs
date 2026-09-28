// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderLimitsTests
{
    private const string ThreeOwnersJson = """{"owners":["a::1220ab","b::1220cd","c::1220ef"]}""";

    public sealed record OwnerListHolder([property: DamlFieldAttribute("owners")] IReadOnlyList<Party> Owners)
        : IDamlRecord<OwnerListHolder>
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create(
                "owners",
                new DamlList(Owners.Select(owner => (DamlValue)owner.ToDamlValue()).ToList())));

        public static OwnerListHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("owners").As<DamlList>().Values
                .Select(value => Party.FromDamlValue(value.As<DamlParty>()))
                .ToList());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var ownersJson = DamlLfJsonDecoders.RequireField(json, context, "owners");
            var ownersContext = context.Field("owners");
            return DamlRecord.Create(DamlField.Create(
                "owners", DamlLfJsonDecoders.ReadList(ownersJson, ownersContext, DamlLfJsonDecoders.ReadParty)));
        }
    }

    public sealed record NestingHolder([property: DamlFieldAttribute("nested")] IReadOnlyList<NestingHolder> Nested)
        : IDamlRecord<NestingHolder>
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create(
                "nested",
                new DamlList(Nested.Select(child => (DamlValue)child.ToRecord()).ToList())));

        public static NestingHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("nested").As<DamlList>().Values
                .Select(value => FromRecord(value.As<DamlRecord>()))
                .ToList());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var nestedJson = DamlLfJsonDecoders.RequireField(json, context, "nested");
            var nestedContext = context.Field("nested");
            return DamlRecord.Create(DamlField.Create(
                "nested",
                DamlLfJsonDecoders.ReadList(
                    nestedJson, nestedContext, (element, elementContext) => __ReadDamlLfJson(element, elementContext))));
        }
    }

    private const int LevelsOverflowingTheDepthBoundAtTwoDepthUnitsEach = 128;
    private const int LevelsExactlyFillingTheDepthBound = 64;

    private static string NestedJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"nested":[""", levels))
        + string.Concat(Enumerable.Repeat("]}", levels));

    [Fact]
    public void ReadRecord_should_reject_arrays_wider_than_the_configured_limit_from_a_parsed_element()
    {
        using var document = JsonDocument.Parse(ThreeOwnersJson);
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 2);

        var act = () => DamlLfJsonReader.ReadRecord<OwnerListHolder>(document.RootElement, limits);

        act.Should().Throw<JsonException>().WithMessage("*maximum supported JSON array length*");
    }

    [Fact]
    public void ReadRecord_should_reject_arrays_wider_than_the_configured_limit_from_json_text()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 2);

        var act = () => DamlLfJsonReader.ReadRecord<OwnerListHolder>(ThreeOwnersJson, limits);

        act.Should().Throw<JsonException>().WithMessage("*maximum supported JSON array length*");
    }

    [Fact]
    public void ReadRecord_should_decode_an_array_exactly_at_the_configured_limit()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 3);

        var record = DamlLfJsonReader.ReadRecord<OwnerListHolder>(ThreeOwnersJson, limits);

        record.GetRequiredField("owners").Should().BeOfType<DamlList>()
            .Which.Values.Should().HaveCount(3);
    }

    [Fact]
    public void ReadRecord_should_reject_json_text_larger_than_the_configured_limit()
    {
        var limits = new DamlJsonDeserializationLimits(MaxInputCharacters: ThreeOwnersJson.Length - 1);

        var act = () => DamlLfJsonReader.ReadRecord<OwnerListHolder>(ThreeOwnersJson, limits);

        act.Should().Throw<JsonException>().WithMessage("*maximum supported JSON input size*");
    }

    [Fact]
    public void ReadRecord_should_reject_value_nesting_beyond_the_supported_depth()
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestingHolder>(
            NestedJson(LevelsOverflowingTheDepthBoundAtTwoDepthUnitsEach));

        act.Should().Throw<JsonException>().WithMessage("*maximum supported depth*");
    }

    [Fact]
    public void ReadRecord_should_decode_value_nesting_exactly_at_the_supported_depth()
    {
        var record = DamlLfJsonReader.ReadRecord<NestingHolder>(NestedJson(LevelsExactlyFillingTheDepthBound));

        record.GetRequiredField("nested").Should().BeOfType<DamlList>();
    }

    [Fact]
    public void ReadRecord_should_reject_value_nesting_one_level_beyond_the_supported_depth()
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestingHolder>(NestedJson(LevelsExactlyFillingTheDepthBound + 1));

        act.Should().Throw<JsonException>().WithMessage("*maximum supported depth*");
    }

    [Fact]
    public void ReadRecord_should_throw_JsonException_for_duplicate_json_properties()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OwnerListHolder>(
            """{"owners":["a::1220ab"],"owners":["b::1220cd"]}""");

        act.Should().Throw<JsonException>();
    }

    public sealed record AttributeMapHolder(
        [property: DamlFieldAttribute("attributes")] IReadOnlyDictionary<string, string> Attributes)
        : IDamlRecord<AttributeMapHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "attributes",
            new DamlTextMap(Attributes.ToDictionary(entry => entry.Key, entry => (DamlValue)new DamlText(entry.Value)))));

        public static AttributeMapHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("attributes").As<DamlTextMap>().Values
                .ToDictionary(entry => entry.Key, entry => entry.Value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var attributesJson = DamlLfJsonDecoders.RequireField(json, context, "attributes");
            var attributesContext = context.Field("attributes");
            return DamlRecord.Create(DamlField.Create(
                "attributes",
                DamlLfJsonDecoders.ReadTextMap(attributesJson, attributesContext, DamlLfJsonDecoders.ReadText)));
        }
    }

    [Fact]
    public void ReadRecord_should_reject_text_maps_wider_than_the_configured_limit()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 2);

        var act = () => DamlLfJsonReader.ReadRecord<AttributeMapHolder>(
            """{"attributes":{"a":"1","b":"2","c":"3"}}""", limits);

        act.Should().Throw<JsonException>()
            .WithMessage("JSON object property count 3 exceeds the maximum supported Daml TextMap entry count of 2");
    }

    [Fact]
    public void ReadRecord_should_decode_a_text_map_exactly_at_the_configured_limit()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 3);

        var record = DamlLfJsonReader.ReadRecord<AttributeMapHolder>(
            """{"attributes":{"a":"1","b":"2","c":"3"}}""", limits);

        record.GetRequiredField("attributes").Should().BeOfType<DamlTextMap>()
            .Which.Values.Should().HaveCount(3);
    }

    public sealed record GenMapBalanceHolder(
        [property: DamlFieldAttribute("balances")] IReadOnlyDictionary<Party, long> Balances)
        : IDamlRecord<GenMapBalanceHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "balances",
            new DamlGenMap(Balances
                .Select(entry => ((DamlValue)entry.Key.ToDamlValue(), (DamlValue)new DamlInt64(entry.Value)))
                .ToList())));

        public static GenMapBalanceHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("balances").As<DamlGenMap>().Entries
                .ToDictionary(
                    entry => Party.FromDamlValue(entry.Key.As<DamlParty>()),
                    entry => entry.Value.As<DamlInt64>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var balancesJson = DamlLfJsonDecoders.RequireField(json, context, "balances");
            var balancesContext = context.Field("balances");
            return DamlRecord.Create(DamlField.Create(
                "balances",
                DamlLfJsonDecoders.ReadGenMap(
                    balancesJson, balancesContext, DamlLfJsonDecoders.ReadParty, DamlLfJsonDecoders.ReadInt64)));
        }
    }

    [Fact]
    public void ReadRecord_should_reject_gen_maps_wider_than_the_configured_limit()
    {
        var limits = new DamlJsonDeserializationLimits(MaxArrayElements: 2);

        var act = () => DamlLfJsonReader.ReadRecord<GenMapBalanceHolder>(
            """{"balances":[["a::1220ab","1"],["b::1220cd","2"],["c::1220ef","3"]]}""", limits);

        act.Should().Throw<JsonException>().WithMessage("*maximum supported JSON array length*");
    }

    public sealed record NestedMapHolder(
        [property: DamlFieldAttribute("nested")] IReadOnlyDictionary<string, NestedMapHolder> Nested)
        : IDamlRecord<NestedMapHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "nested",
            new DamlTextMap(Nested.ToDictionary(entry => entry.Key, entry => (DamlValue)entry.Value.ToRecord()))));

        public static NestedMapHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("nested").As<DamlTextMap>().Values
                .ToDictionary(entry => entry.Key, entry => FromRecord(entry.Value.As<DamlRecord>())));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var nestedJson = DamlLfJsonDecoders.RequireField(json, context, "nested");
            var nestedContext = context.Field("nested");
            return DamlRecord.Create(DamlField.Create(
                "nested",
                DamlLfJsonDecoders.ReadTextMap(
                    nestedJson, nestedContext, (element, elementContext) => __ReadDamlLfJson(element, elementContext))));
        }
    }

    private static string NestedMapJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"nested":{"a":""", levels))
        + """{"nested":{}}"""
        + string.Concat(Enumerable.Repeat("}}", levels));

    [Fact]
    public void ReadRecord_should_reject_value_nesting_beyond_the_supported_depth_through_a_text_map()
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestedMapHolder>(
            NestedMapJson(LevelsOverflowingTheDepthBoundAtTwoDepthUnitsEach));

        act.Should().Throw<JsonException>().WithMessage("*maximum supported depth*");
    }

    public sealed record NestedStdlibMapHolder(
        [property: DamlFieldAttribute("nested")] Map<string, NestedStdlibMapHolder> Nested)
        : IDamlRecord<NestedStdlibMapHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "nested",
            Nested.ToRecord(key => new DamlText(key), child => child.ToRecord())));

        public static NestedStdlibMapHolder FromRecord(DamlRecord record) =>
            new(Map<string, NestedStdlibMapHolder>.FromRecord(
                record.GetRequiredField("nested").As<DamlRecord>(),
                value => value.As<DamlText>().Value,
                value => FromRecord(value.As<DamlRecord>())));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var nestedJson = DamlLfJsonDecoders.RequireField(json, context, "nested");
            var nestedContext = context.Field("nested");
            return DamlRecord.Create(DamlField.Create(
                "nested",
                DamlLfJsonDecoders.ReadStdlibMap(
                    nestedJson,
                    nestedContext,
                    DamlLfJsonDecoders.ReadText,
                    (element, elementContext) => __ReadDamlLfJson(element, elementContext))));
        }
    }

    private const int StdlibMapLevelsExactlyFillingTheDepthBound = 42;

    private static string NestedStdlibMapJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"nested":{"map":[["a",""", levels))
        + """{"nested":{"map":[]}}"""
        + string.Concat(Enumerable.Repeat("]]}}", levels));

    [Fact]
    public void ReadRecord_should_decode_stdlib_map_nesting_exactly_at_the_supported_depth()
    {
        var record = DamlLfJsonReader.ReadRecord<NestedStdlibMapHolder>(
            NestedStdlibMapJson(StdlibMapLevelsExactlyFillingTheDepthBound));

        record.GetRequiredField("nested").Should().BeOfType<DamlRecord>();
    }

    [Fact]
    public void ReadRecord_should_reject_stdlib_map_nesting_one_level_beyond_the_supported_depth()
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestedStdlibMapHolder>(
            NestedStdlibMapJson(StdlibMapLevelsExactlyFillingTheDepthBound + 1));

        act.Should().Throw<JsonException>().WithMessage("*maximum supported depth*");
    }

    public sealed record NestedChainHolder(
        [property: DamlFieldAttribute("nested")] Optional<Optional<NestedChainHolder>> Nested)
        : IDamlRecord<NestedChainHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "nested",
            Nested.ToChainValue(carried => carried.ToChainValue(child => child.ToRecord()))));

        public static NestedChainHolder FromRecord(DamlRecord record) =>
            new(Optional<Optional<NestedChainHolder>>.FromChainValue(
                record.GetRequiredField("nested"),
                carried => Optional<NestedChainHolder>.FromChainValue(
                    carried, child => FromRecord(child.As<DamlRecord>()))));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var nestedJson = DamlLfJsonDecoders.RequireField(json, context, "nested");
            var nestedContext = context.Field("nested");
            var nested = DamlLfJsonDecoders.ReadOptionalChain(nestedJson, nestedContext, (outer, outerContext) =>
                DamlLfJsonDecoders.ReadOptionalChain(outer, outerContext, (inner, innerContext) =>
                    __ReadDamlLfJson(inner, innerContext)));
            return DamlRecord.Create(DamlField.Create("nested", nested));
        }
    }

    private const int ChainLevelsExactlyFillingTheDepthBoundAtThreeDepthUnitsEach = 42;

    private static string NestedChainJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"nested":[[""", levels))
        + """{"nested":[]}"""
        + string.Concat(Enumerable.Repeat("]]}", levels));

    [Fact]
    public void ReadRecord_should_decode_nested_optional_chain_nesting_exactly_at_the_supported_depth()
    {
        var record = DamlLfJsonReader.ReadRecord<NestedChainHolder>(
            NestedChainJson(ChainLevelsExactlyFillingTheDepthBoundAtThreeDepthUnitsEach));

        record.GetRequiredField("nested").Should().BeOfType<DamlOptionalChain>();
    }

    [Fact]
    public void ReadRecord_should_reject_nested_optional_chain_nesting_one_level_beyond_the_supported_depth()
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestedChainHolder>(
            NestedChainJson(ChainLevelsExactlyFillingTheDepthBoundAtThreeDepthUnitsEach + 1));

        act.Should().Throw<JsonException>().WithMessage("*maximum supported depth*");
    }

    [Fact]
    public void ReadRecord_should_decode_a_document_over_the_size_limit_when_the_caller_already_parsed_it()
    {
        using var document = JsonDocument.Parse(ThreeOwnersJson);
        var limits = new DamlJsonDeserializationLimits(MaxInputCharacters: 1);

        var record = DamlLfJsonReader.ReadRecord<OwnerListHolder>(document.RootElement, limits);

        record.GetRequiredField("owners").Should().BeOfType<DamlList>()
            .Which.Values.Should().Equal(
                new DamlParty("a::1220ab"),
                new DamlParty("b::1220cd"),
                new DamlParty("c::1220ef"));
    }

    private static readonly DamlJsonDeserializationLimits UnusableLimits = new(MaxInputCharacters: 0);

    [Fact]
    public void ReadRecord_should_reject_an_unusable_limit_configuration_from_json_text()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OwnerListHolder>(ThreeOwnersJson, UnusableLimits);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("limits");
    }

    [Fact]
    public void ReadRecord_should_reject_an_unusable_limit_configuration_from_a_parsed_element()
    {
        using var document = JsonDocument.Parse(ThreeOwnersJson);

        var act = () => DamlLfJsonReader.ReadRecord<OwnerListHolder>(document.RootElement, UnusableLimits);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("limits");
    }
}
