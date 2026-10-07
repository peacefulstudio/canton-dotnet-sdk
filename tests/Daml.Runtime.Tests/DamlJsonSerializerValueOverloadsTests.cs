// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerValueOverloadsTests
{
    public static TheoryData<string, DamlValue, string> Serialize_value_overload_cases() => new()
    {
        { "DamlText", new DamlText("hello"), "\"hello\"" },
        { "DamlInt64", new DamlInt64(42), "\"42\"" },
        { "DamlBool_true", new DamlBool(true), "true" },
        { "DamlBool_false", new DamlBool(false), "false" },
        { "DamlParty", new DamlParty("Alice::12345"), "\"Alice::12345\"" },
        { "DamlDate", new DamlDate(new DateOnly(2023, 12, 25)), "\"2023-12-25\"" },
        { "DamlUnit", DamlUnit.Instance, "{}" },
        { "DamlGenMap", DamlGenMap.Create(
            (new DamlText("k1"), new DamlInt64(1)),
            (new DamlText("k2"), new DamlInt64(2))), "[[\"k1\",\"1\"],[\"k2\",\"2\"]]" },
        { "DamlOptional_Some", DamlOptional.Some(new DamlInt64(42)), "\"42\"" },
        { "DamlOptional_None", DamlOptional.None, "null" },
    };

    [Theory]
    [MemberData(nameof(Serialize_value_overload_cases))]
    public void Serialize_value_overload_should_emit_expected_json(string subtype, DamlValue value, string expectedJson)
    {
        var valueJson = DamlJsonSerializer.Serialize(value);

        valueJson.Should().Be(expectedJson, "Serialize<DamlValue> for {0} must emit the direct JSON shape", subtype);

        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("f", value)));
        recordJson.Should().Be($"{{\"f\":{expectedJson}}}", "record-path slice for {0} must equal the value-overload output", subtype);
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlNumeric()
    {
        var numeric = new DamlNumeric(123.456789m, 10);

        var valueJson = DamlJsonSerializer.Serialize(numeric);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("n", numeric)));

        valueJson.Should().Be("\"123.456789\"");
        recordJson.Should().Be($"{{\"n\":{valueJson}}}");
    }

    [Fact]
    public void Issue_155_Serialize_value_overload_should_not_stack_overflow_on_any_subtype()
    {
        Action act = () =>
        {
            DamlJsonSerializer.Serialize(new DamlText("hello"));
            DamlJsonSerializer.Serialize(new DamlInt64(42));
            DamlJsonSerializer.Serialize(new DamlBool(true));
            DamlJsonSerializer.Serialize(new DamlNumeric(1.5m));
            DamlJsonSerializer.Serialize(new DamlParty("Alice"));
            DamlJsonSerializer.Serialize(new DamlDate(new DateOnly(2024, 1, 1)));
            DamlJsonSerializer.Serialize(new DamlTimestamp(DateTimeOffset.UnixEpoch));
            DamlJsonSerializer.Serialize(new DamlContractId("cid"));
            DamlJsonSerializer.Serialize((DamlValue)DamlUnit.Instance);
            DamlJsonSerializer.Serialize(DamlOptional.Some(new DamlInt64(1)));
            DamlJsonSerializer.Serialize(DamlOptional.None);
            DamlJsonSerializer.Serialize(DamlList.Create(new DamlInt64(1)));
            DamlJsonSerializer.Serialize(DamlTextMap.Create(("k", new DamlInt64(1))));
            DamlJsonSerializer.Serialize(DamlGenMap.Create((new DamlText("k"), new DamlInt64(1))));
            DamlJsonSerializer.Serialize(DamlVariant.Create("Some", new DamlInt64(1)));
            DamlJsonSerializer.Serialize(DamlEnum.Create("Active"));
            DamlJsonSerializer.Serialize((DamlValue)DamlRecord.Create(
                DamlField.Create("x", new DamlInt64(1))));
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlUnit()
    {
        var valueJson = DamlJsonSerializer.Serialize((DamlValue)DamlUnit.Instance);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("u", DamlUnit.Instance)));

        valueJson.Should().Be("{}");
        recordJson.Should().Be($"{{\"u\":{valueJson}}}");
    }

    [Fact]
    public void RoundTrip_value_overload_decodes_DamlUnit_as_empty_DamlRecord()
    {
        var json = DamlJsonSerializer.Serialize((DamlValue)DamlUnit.Instance);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType<DamlRecord>();
        deserialized.As<DamlRecord>().Fields.Should().BeEmpty();
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlRecord()
    {
        var inner = DamlRecord.Create(DamlField.Create("x", new DamlInt64(1)));

        var valueJson = DamlJsonSerializer.Serialize((DamlValue)inner);
        var recordJson = DamlJsonSerializer.Serialize(inner);

        valueJson.Should().Be("{\"x\":\"1\"}");
        recordJson.Should().Be(valueJson);
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlEnum()
    {
        var enumValue = DamlEnum.Create("Active");

        var valueJson = DamlJsonSerializer.Serialize(enumValue);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("e", enumValue)));

        valueJson.Should().Be("\"Active\"");
        recordJson.Should().Be($"{{\"e\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlVariant()
    {
        var variant = DamlVariant.Create("Left", new DamlText("err"));

        var valueJson = DamlJsonSerializer.Serialize(variant);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("v", variant)));

        valueJson.Should().Be("{\"tag\":\"Left\",\"value\":\"err\"}");
        recordJson.Should().Be($"{{\"v\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlTextMap()
    {
        var textMap = DamlTextMap.Create(("k", new DamlInt64(7)));

        var valueJson = DamlJsonSerializer.Serialize(textMap);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("m", textMap)));

        valueJson.Should().Be("{\"k\":\"7\"}");
        recordJson.Should().Be($"{{\"m\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlList()
    {
        var list = DamlList.Create(new DamlInt64(1), new DamlInt64(2), new DamlInt64(3));

        var valueJson = DamlJsonSerializer.Serialize(list);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("l", list)));

        valueJson.Should().Be("[\"1\",\"2\",\"3\"]");
        recordJson.Should().Be($"{{\"l\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlContractId()
    {
        var contractId = new DamlContractId("00abcd1234");

        var valueJson = DamlJsonSerializer.Serialize(contractId);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("cid", contractId)));

        valueJson.Should().Be("\"00abcd1234\"");
        recordJson.Should().Be($"{{\"cid\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlTimestamp()
    {
        var timestamp = new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero);

        var valueJson = DamlJsonSerializer.Serialize(new DamlTimestamp(timestamp));
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("t", new DamlTimestamp(timestamp))));

        valueJson.Should().Be("\"2023-06-15T12:30:45Z\"");
        recordJson.Should().Be($"{{\"t\":{valueJson}}}");
    }

    private sealed record UnsupportedDamlValue : DamlValue;

    [Fact]
    public void Serialize_should_throw_JsonException_for_unsupported_DamlValue_subtype()
    {
        var act = () => DamlJsonSerializer.Serialize(new UnsupportedDamlValue());

        act.Should().Throw<JsonException>().WithMessage("*UnsupportedDamlValue*");
    }

    [Fact]
    public void Serialize_value_overload_should_throw_JsonException_for_duplicate_DamlField_labels()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(1.0m)),
            DamlField.Create("amount", new DamlNumeric(2.0m)));

        var act = () => DamlJsonSerializer.Serialize((DamlValue)record);

        act.Should().Throw<JsonException>().WithMessage("*amount*");
    }

    public static TheoryData<DamlValue> round_trippable_scalar_values() => new()
    {
        new DamlNumeric(1.5m),
        new DamlDate(new DateOnly(2026, 5, 26)),
        new DamlTimestamp(new DateTimeOffset(2026, 5, 26, 14, 30, 45, TimeSpan.Zero)),
    };

    [Theory]
    [MemberData(nameof(round_trippable_scalar_values))]
    public void Serialize_value_overload_round_trips_scalar_value(DamlValue original)
    {
        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType(original.GetType());
        deserialized.Should().Be(original);
    }

    public static TheoryData<DamlValue> round_trippable_temporal_values() => new()
    {
        new DamlDate(new DateOnly(2026, 5, 26)),
        new DamlTimestamp(new DateTimeOffset(2026, 5, 26, 14, 30, 45, TimeSpan.Zero)),
    };

    [Theory]
    [MemberData(nameof(round_trippable_temporal_values))]
    public void Serialize_value_overload_round_trips_temporal_value_under_non_invariant_culture(DamlValue original)
    {
        var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("th-TH");

            var json = DamlJsonSerializer.Serialize(original);
            var deserialized = DamlJsonSerializer.Deserialize(json);

            deserialized.Should().BeOfType(original.GetType());
            deserialized.Should().Be(original);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Serialize_refuses_a_carried_undecoded_json_value_by_name()
    {
        var action = () => DamlJsonSerializer.Serialize(new DamlUndecodedJson("{\"owner\":\"alice::ns\"}"));

        action.Should().Throw<JsonException>()
            .WithMessage("Cannot serialize DamlUndecodedJson to JSON");
    }

    [Fact]
    public void Serialize_refuses_a_carried_undecoded_json_value_nested_in_a_record()
    {
        var record = new DamlRecord(null, [new DamlField("inner", new DamlUndecodedJson("{}"))]);

        var action = () => DamlJsonSerializer.Serialize(record);

        action.Should().Throw<JsonException>()
            .WithMessage("Cannot serialize DamlUndecodedJson to JSON");
    }
}
