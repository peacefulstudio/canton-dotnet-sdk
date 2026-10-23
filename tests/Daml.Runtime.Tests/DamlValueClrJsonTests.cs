// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for a value declared as <see cref="DamlValue"/>:
/// the concrete arm's own object with a <c>"$case"</c> discriminator naming the arm first, so each
/// arm reads back as itself on default options and on <c>AddDamlConverters()</c> options.
/// </summary>
public class DamlValueClrJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private static readonly Identifier Iou = new("pkg", "Main", "Iou");

    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions CamelCaseKeys = new() { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private static readonly JsonSerializerOptions Preserving = new() { ReferenceHandler = ReferenceHandler.Preserve };

    private static readonly JsonSerializerOptions IgnoringCycles = new() { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    private static readonly JsonSerializerOptions Deep = new() { MaxDepth = 128 };

    private static readonly JsonSerializerOptions Unlimited = new() { MaxDepth = 1000 };

    private static readonly JsonSerializerOptions Shallow = new() { MaxDepth = 8 };

    private static readonly JsonSerializerOptions StrictPreset = new(JsonSerializerOptions.Strict);

    private static readonly JsonSerializerOptions IgnoringNulls = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private sealed record Holder(DamlValue Payload);

    public static TheoryData<DamlValue, string> ArmsAndTheirJson() => new()
    {
        { new DamlInt64(42), """{"$case":"DamlInt64","Value":42}""" },
        { new DamlText("hello"), """{"$case":"DamlText","Value":"hello"}""" },
        { new DamlBool(true), """{"$case":"DamlBool","Value":true}""" },
        { new DamlParty("Alice::1220"), """{"$case":"DamlParty","Value":"Alice::1220"}""" },
        { DamlUnit.Instance, """{"$case":"DamlUnit"}""" },
        { new DamlDate(new DateOnly(2026, 10, 5)), """{"$case":"DamlDate","Value":"2026-10-05","DaysSinceEpoch":20731}""" },
        { new DamlEnum(Iou, "Red"), """{"$case":"DamlEnum","EnumId":{"PackageId":"pkg","ModuleName":"Main","EntityName":"Iou","FullyQualifiedName":"Main:Iou"},"Constructor":"Red"}""" },
        { new DamlContractId("00c0ffee"), """{"$case":"DamlContractId","Value":"00c0ffee","TemplateId":null}""" },
        { new DamlUndecodedJson("[1,2]") { PackageId = "pkg" }, """{"$case":"DamlUndecodedJson","LfJson":"[1,2]","PackageId":"pkg"}""" },
        { DamlOptional.Some(new DamlInt64(7)), """{"$case":"DamlOptional","Value":{"$case":"DamlInt64","Value":7}}""" },
        { DamlOptional.None, """{"$case":"DamlOptional","Value":null}""" },
        { DamlOptionalChain.Some(new DamlText("c")), """{"$case":"DamlOptionalChain","Value":{"$case":"DamlText","Value":"c"}}""" },
        { DamlOptionalChain.None, """{"$case":"DamlOptionalChain","Value":null}""" },
        { DamlList.Create(new DamlInt64(1), new DamlText("two")), """{"$case":"DamlList","Values":[{"$case":"DamlInt64","Value":1},{"$case":"DamlText","Value":"two"}]}""" },
        { DamlList.Create(), """{"$case":"DamlList","Values":[]}""" },
        { DamlTextMap.Create(("b", new DamlInt64(2)), ("a", new DamlInt64(1))), """{"$case":"DamlTextMap","Entries":[{"Key":"b","Value":{"$case":"DamlInt64","Value":2}},{"Key":"a","Value":{"$case":"DamlInt64","Value":1}}]}""" },
        { DamlGenMap.Create((new DamlText("k"), new DamlBool(false))), """{"$case":"DamlGenMap","Entries":[{"Key":{"$case":"DamlText","Value":"k"},"Value":{"$case":"DamlBool","Value":false}}]}""" },
        { DamlRecord.Create(DamlField.Create("n", new DamlInt64(1))), """{"$case":"DamlRecord","RecordId":null,"Fields":[{"Label":"n","Value":{"$case":"DamlInt64","Value":1}}]}""" },
        { DamlVariant.Create("Left", new DamlText("x")), """{"$case":"DamlVariant","VariantId":null,"Constructor":"Left","Value":{"$case":"DamlText","Value":"x"}}""" },
    };

    [Theory]
    [MemberData(nameof(ArmsAndTheirJson))]
    public void A_value_declared_as_DamlValue_is_written_as_its_arm_with_a_case_discriminator(DamlValue value, string expectedJson)
    {
        JsonSerializer.Serialize<DamlValue>(value).Should().Be(expectedJson);
    }

    [Theory]
    [MemberData(nameof(ArmsAndTheirJson))]
    public void A_value_declared_as_DamlValue_reads_back_as_the_same_arm_on_default_options(DamlValue value, string json)
    {
        var read = JsonSerializer.Deserialize<DamlValue>(json);

        read.Should().Be(value);
        read!.GetType().Should().Be(value.GetType());
    }

    [Theory]
    [MemberData(nameof(ArmsAndTheirJson))]
    public void A_value_declared_as_DamlValue_reads_back_as_the_same_arm_on_AddDamlConverters_options(DamlValue value, string json)
    {
        var read = JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize<DamlValue>(value, Registered), Registered);

        read.Should().Be(value);
        json.Should().Be(JsonSerializer.Serialize<DamlValue>(value, Registered));
    }

    [Fact]
    public void A_DamlOptionalChain_does_not_read_back_as_a_DamlOptional()
    {
        var read = JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize<DamlValue>(DamlOptionalChain.Some(DamlOptional.Some(new DamlInt64(1)))));

        read.Should().Be(DamlOptionalChain.Some(DamlOptional.Some(new DamlInt64(1))));
    }

    [Fact]
    public void A_DamlParty_does_not_read_back_as_a_DamlText()
    {
        var read = JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize<DamlValue>(new DamlParty("Alice")));

        read.Should().BeOfType<DamlParty>();
    }

    [Fact]
    public void A_member_declared_as_DamlValue_reads_back_through_its_holder()
    {
        var holder = new Holder(DamlRecord.Create(Iou, DamlField.Create("owner", new DamlParty("Alice"))));

        var read = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(holder));

        read.Should().Be(holder);
    }

    [Fact]
    public void A_DamlRecord_declared_as_itself_keeps_its_own_shape_without_a_discriminator()
    {
        var json = JsonSerializer.Serialize(DamlRecord.Create(DamlField.Create("n", new DamlInt64(1))));

        json.Should().Be("""{"RecordId":null,"Fields":[{"Label":"n","Value":{"$case":"DamlInt64","Value":1}}]}""");
    }

    [Fact]
    public void Members_may_arrive_in_any_order()
    {
        var read = JsonSerializer.Deserialize<DamlValue>(
            """{"Fields":[{"Value":{"Value":1,"$case":"DamlInt64"},"Label":"n"}],"RecordId":null,"$case":"DamlRecord"}""");

        read.Should().Be(DamlRecord.Create(DamlField.Create("n", new DamlInt64(1))));
    }

    [Fact]
    public void A_camel_case_naming_policy_round_trips_and_keeps_the_discriminator_spelling()
    {
        var options = CamelCase;
        var value = DamlVariant.Create("Left", new DamlInt64(1));

        var json = JsonSerializer.Serialize<DamlValue>(value, options);

        json.Should().Be("""{"$case":"DamlVariant","variantId":null,"constructor":"Left","value":{"$case":"DamlInt64","value":1}}""");
        JsonSerializer.Deserialize<DamlValue>(json, options).Should().Be(value);
    }

    [Fact]
    public void A_dictionary_key_policy_leaves_text_map_keys_untouched()
    {
        var options = CamelCaseKeys;
        var value = DamlTextMap.Create(("MixedCase", new DamlInt64(1)));

        JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize<DamlValue>(value, options), options).Should().Be(value);
    }

    [Fact]
    public void Disallowing_unmapped_members_still_reads_every_arm()
    {
        var options = Strict;
        var value = DamlList.Create(DamlOptional.Some(new DamlText("x")), DamlRecord.Create(DamlField.Create("n", new DamlInt64(1))));

        JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize<DamlValue>(value, options), options).Should().Be(value);
    }

    [Fact]
    public void An_unmapped_member_on_a_composite_arm_is_refused_when_unmapped_members_are_disallowed()
    {
        var options = Strict;

        var act = () => JsonSerializer.Deserialize<DamlValue>("""{"$case":"DamlList","Values":[],"Extra":1}""", options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void An_unmapped_member_on_a_composite_arm_is_skipped_by_default()
    {
        var read = JsonSerializer.Deserialize<DamlValue>("""{"$case":"DamlList","Values":[],"Extra":{"deep":[1]}}""");

        read.Should().Be(DamlList.Create());
    }

    [Theory]
    [InlineData("""{"Value":1}""", "$case")]
    [InlineData("""{"$case":"Nope"}""", "Nope")]
    [InlineData("""{"$case":1}""", "$case")]
    [InlineData("""[1]""", "object")]
    [InlineData("""{"$case":"DamlList"}""", "Values")]
    [InlineData("""{"$case":"DamlList","Values":[null]}""", "null")]
    [InlineData("""{"$case":"DamlTextMap","Entries":[{"Key":"a","Value":{"$case":"DamlUnit"}},{"Key":"a","Value":{"$case":"DamlUnit"}}]}""", "duplicate")]
    [InlineData("""{"$case":"DamlGenMap","Entries":[{"Key":{"$case":"DamlUnit"},"Value":{"$case":"DamlUnit"}},{"Key":{"$case":"DamlUnit"},"Value":{"$case":"DamlUnit"}}]}""", "duplicate")]
    [InlineData("""{"$case":"DamlVariant","Constructor":"Left"}""", "Value")]
    public void A_malformed_value_is_refused_with_a_JsonException_naming_the_problem(string json, string namedInMessage)
    {
        var act = () => JsonSerializer.Deserialize<DamlValue>(json);

        act.Should().Throw<JsonException>().Where(ex => ex.Message.Contains(namedInMessage, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_ReferenceHandler_is_refused_on_write_and_on_read(bool preserve)
    {
        var options = preserve ? Preserving : IgnoringCycles;

        var write = () => JsonSerializer.Serialize<DamlValue>(DamlList.Create(), options);
        var read = () => JsonSerializer.Deserialize<DamlValue>("""{"$case":"DamlList","Values":[]}""", options);

        write.Should().Throw<JsonException>().Where(ex => ex.Message.Contains("ReferenceHandler", StringComparison.Ordinal));
        read.Should().Throw<JsonException>().Where(ex => ex.Message.Contains("ReferenceHandler", StringComparison.Ordinal));
    }

    [Fact]
    public void Twelve_nested_lists_round_trip_when_MaxDepth_is_raised()
    {
        DamlValue value = new DamlInt64(1);
        for (var level = 0; level < 12; level++)
        {
            value = DamlList.Create(value);
        }
        var options = Deep;

        JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize(value, options), options).Should().Be(value);
    }

    [Fact]
    public void Nesting_past_MaxDepth_fails_with_a_JsonException_on_write_and_on_read()
    {
        var value = NestedOptional(100);
        var json = NestedOptionalJson(100);

        var write = () => JsonSerializer.Serialize(value);
        var read = () => JsonSerializer.Deserialize<DamlValue>(json);

        write.Should().Throw<JsonException>();
        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void Sixty_three_nested_optionals_round_trip_when_MaxDepth_is_raised()
    {
        var value = NestedOptional(63);

        var read = JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize(value, Unlimited), Unlimited);

        read.Should().Be(value);
    }

    public static TheoryData<string, int> NestingsAtTheDefaultMaxDepth() => new()
    {
        { nameof(DamlOptional), 63 },
        { nameof(DamlList), 31 },
        { nameof(DamlRecord), 21 },
    };

    public static TheoryData<string, int> NestingsUnderARaisedMaxDepth() => new()
    {
        { nameof(DamlOptional), 300 },
        { nameof(DamlList), 300 },
        { nameof(DamlRecord), 300 },
    };

    [Theory]
    [MemberData(nameof(NestingsAtTheDefaultMaxDepth))]
    public void A_bad_case_at_the_deepest_level_default_options_allow_is_refused_with_a_JsonException_on_a_256_KB_stack(string arm, int levels)
    {
        var json = NestedJson(arm, levels, """{"$case":"Nope"}""");

        var thrown = StackProbe.OnStackOf(256, () => JsonSerializer.Deserialize<DamlValue>(json));

        thrown.Should().BeOfType<JsonException>().Which.Message.Should().Contain("\"Nope\"");
    }

    [Theory]
    [MemberData(nameof(NestingsAtTheDefaultMaxDepth))]
    public void The_deepest_nesting_default_options_allow_reads_and_writes_on_a_256_KB_stack(string arm, int levels)
    {
        var value = Nested(arm, levels);
        var json = NestedJson(arm, levels, """{"$case":"DamlInt64","Value":1}""");
        DamlValue? read = null;
        string? written = null;

        var thrown = StackProbe.OnStackOf(256, () =>
        {
            read = JsonSerializer.Deserialize<DamlValue>(json);
            written = JsonSerializer.Serialize(value);
        });

        thrown.Should().BeNull();
        read.Should().Be(value);
        written.Should().Be(json);
    }

    [Theory]
    [MemberData(nameof(NestingsUnderARaisedMaxDepth))]
    public void Three_hundred_levels_round_trip_on_a_one_megabyte_stack_when_MaxDepth_is_raised(string arm, int levels)
    {
        var value = Nested(arm, levels);
        DamlValue? read = null;

        var thrown = StackProbe.OnStackOf(1024, () => read = JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize(value, Unlimited), Unlimited));

        thrown.Should().BeNull();
        read.Should().Be(value);
    }

    [Theory]
    [MemberData(nameof(NestingsUnderARaisedMaxDepth))]
    public void A_bad_case_three_hundred_levels_deep_is_refused_with_a_JsonException_on_a_one_megabyte_stack_when_MaxDepth_is_raised(string arm, int levels)
    {
        var json = NestedJson(arm, levels, """{"$case":"Nope"}""");

        var thrown = StackProbe.OnStackOf(1024, () => JsonSerializer.Deserialize<DamlValue>(json, Unlimited));

        thrown.Should().BeOfType<JsonException>().Which.Message.Should().Contain("\"Nope\"");
    }

    public static TheoryData<string> ArmsCarryingAnIdentifier() =>
        [nameof(DamlRecord), nameof(DamlVariant), nameof(DamlEnum)];

    [Theory]
    [MemberData(nameof(ArmsCarryingAnIdentifier))]
    public void An_identifier_at_the_deepest_level_default_options_allow_writes_and_reads_back_on_a_256_KB_stack(string arm)
    {
        var value = OptionalsAround(62, CarryingAnIdentifier(arm));
        DamlValue? read = null;

        var thrown = StackProbe.OnStackOf(256, () => read = JsonSerializer.Deserialize<DamlValue>(JsonSerializer.Serialize(value)));

        thrown.Should().BeNull();
        read.Should().Be(value);
    }

    [Theory]
    [MemberData(nameof(ArmsCarryingAnIdentifier))]
    public void An_identifier_one_level_past_the_deepest_default_level_is_refused_with_a_JsonException_on_write_on_a_256_KB_stack(string arm)
    {
        var value = OptionalsAround(63, CarryingAnIdentifier(arm));

        var thrown = StackProbe.OnStackOf(256, () => JsonSerializer.Serialize(value));

        thrown.Should().BeOfType<JsonException>().Which.Message.Should().Contain("maximum depth of 64");
    }

    [Theory]
    [MemberData(nameof(ArmsCarryingAnIdentifier))]
    public void An_identifier_one_level_past_the_deepest_default_level_is_refused_with_a_JsonException_through_a_callers_writer(string arm)
    {
        var value = OptionalsAround(63, CarryingAnIdentifier(arm));
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        var thrown = StackProbe.OnStackOf(256, () => JsonSerializer.Serialize(writer, value));

        thrown.Should().BeOfType<JsonException>().Which.Message.Should().Contain("maximum depth of 64");
    }

    [Fact]
    public void A_lowered_MaxDepth_bounds_nesting_on_read_and_on_write()
    {
        var options = Shallow;

        JsonSerializer.Deserialize<DamlValue>(NestedJson(nameof(DamlOptional), 7, """{"$case":"DamlInt64","Value":1}"""), options)
            .Should().Be(NestedOptional(7));
        var read = () => JsonSerializer.Deserialize<DamlValue>(NestedJson(nameof(DamlOptional), 8, """{"$case":"DamlInt64","Value":1}"""), options);
        var write = () => JsonSerializer.Serialize(NestedOptional(8), options);

        read.Should().Throw<JsonException>();
        write.Should().Throw<JsonException>();
    }

    [Theory]
    [MemberData(nameof(ArmsAndTheirJson))]
    public void A_value_declared_as_DamlValue_round_trips_on_the_strict_preset(DamlValue value, string json)
    {
        var options = StrictPreset;

        JsonSerializer.Serialize<DamlValue>(value, options).Should().Be(json);
        JsonSerializer.Deserialize<DamlValue>(json, options).Should().Be(value);
    }

    [Theory]
    [InlineData("""{"$case":"DamlOptional"}""", "Value")]
    [InlineData("""{"$case":"DamlVariant","VariantId":null,"Constructor":"L","Value":null}""", "Value")]
    [InlineData("""{"$case":"DamlVariant","VariantId":null,"Constructor":null,"Value":{"$case":"DamlUnit"}}""", "Constructor")]
    [InlineData("""{"$case":"DamlRecord","RecordId":null,"Fields":[{"Value":{"$case":"DamlUnit"}}]}""", "Label")]
    [InlineData("""{"$case":"DamlRecord","RecordId":null,"Fields":[{"Label":"n","Value":null}]}""", "Value")]
    [InlineData("""{"$case":"DamlTextMap","Entries":[{"Key":"a","Value":null}]}""", "Value")]
    [InlineData("""{"$case":"DamlGenMap","Entries":[{"Key":null,"Value":{"$case":"DamlUnit"}}]}""", "Key")]
    [InlineData("""{"$case":"DamlList","Values":[],"Values":[]}""", "Values")]
    [InlineData("""{"$case":"DamlList","$case":"DamlList","Values":[]}""", "$case")]
    public void The_strict_preset_refuses_a_missing_member_a_null_member_and_a_duplicate_member(string json, string namedInMessage)
    {
        var options = StrictPreset;

        var act = () => JsonSerializer.Deserialize<DamlValue>(json, options);

        act.Should().Throw<JsonException>().Where(ex => ex.Message.Contains(namedInMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void An_absent_optional_value_reads_as_None_on_default_options()
    {
        JsonSerializer.Deserialize<DamlValue>("""{"$case":"DamlOptional"}""").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void Ignoring_null_members_on_write_leaves_them_out_and_reads_back()
    {
        var options = IgnoringNulls;
        var value = DamlList.Create(DamlOptional.None, DamlRecord.Create(), DamlVariant.Create("L", DamlUnit.Instance));

        var json = JsonSerializer.Serialize<DamlValue>(value, options);

        json.Should().Be("""{"$case":"DamlList","Values":[{"$case":"DamlOptional"},{"$case":"DamlRecord","Fields":[]},{"$case":"DamlVariant","Constructor":"L","Value":{"$case":"DamlUnit"}}]}""");
        JsonSerializer.Deserialize<DamlValue>(json, options).Should().Be(value);
    }

    [Fact]
    public void A_member_name_in_another_case_binds_only_when_names_are_case_insensitive()
    {
        const string json = """{"$case":"DamlOptional","value":{"$case":"DamlUnit"}}""";

        JsonSerializer.Deserialize<DamlValue>(json).Should().Be(DamlOptional.None);
        JsonSerializer.Deserialize<DamlValue>(json, CaseInsensitive).Should().Be(DamlOptional.Some(DamlUnit.Instance));
    }

    [Fact]
    public void An_arm_the_runtime_does_not_define_is_refused_on_write()
    {
        var act = () => JsonSerializer.Serialize<DamlValue>(new ForeignArm());

        act.Should().Throw<JsonException>().Where(ex => ex.Message.Contains("ForeignArm", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Daml_LF_serializer_still_writes_the_untyped_wire_spelling()
    {
        DamlJsonSerializer.Serialize(new DamlParty("Alice")).Should().Be("\"Alice\"");
    }

    private static DamlValue CarryingAnIdentifier(string arm) =>
        arm switch
        {
            nameof(DamlRecord) => DamlRecord.Create(Iou),
            nameof(DamlVariant) => DamlVariant.Create(Iou, "Left", DamlUnit.Instance),
            _ => DamlEnum.Create(Iou, "Red"),
        };

    private static DamlValue OptionalsAround(int levels, DamlValue inner)
    {
        var value = inner;
        for (var level = 0; level < levels; level++)
        {
            value = DamlOptional.Some(value);
        }

        return value;
    }

    private static DamlValue NestedOptional(int levels)
    {
        DamlValue value = new DamlInt64(1);
        for (var level = 0; level < levels; level++)
        {
            value = DamlOptional.Some(value);
        }

        return value;
    }

    private static string NestedOptionalJson(int levels) =>
        NestedJson(nameof(DamlOptional), levels, """{"$case":"DamlInt64","Value":1}""");

    private static DamlValue Nested(string arm, int levels)
    {
        DamlValue value = new DamlInt64(1);
        for (var level = 0; level < levels; level++)
        {
            value = arm switch
            {
                nameof(DamlOptional) => DamlOptional.Some(value),
                nameof(DamlList) => DamlList.Create(value),
                _ => DamlRecord.Create(DamlField.Create("f", value)),
            };
        }

        return value;
    }

    private static string NestedJson(string arm, int levels, string innermost)
    {
        var (open, close) = arm switch
        {
            nameof(DamlOptional) => ("""{"$case":"DamlOptional","Value":""", "}"),
            nameof(DamlList) => ("""{"$case":"DamlList","Values":[""", "]}"),
            _ => ("""{"$case":"DamlRecord","RecordId":null,"Fields":[{"Label":"f","Value":""", "}]}"),
        };

        return string.Concat(Enumerable.Repeat(open, levels)) + innermost + string.Concat(Enumerable.Repeat(close, levels));
    }

    private sealed record ForeignArm : DamlValue;
}
