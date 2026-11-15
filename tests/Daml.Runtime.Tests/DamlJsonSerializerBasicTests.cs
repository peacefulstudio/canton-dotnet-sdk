// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerBasicTests
{
    [Fact]
    public void Serialize_should_convert_DamlRecord_to_json()
    {
        var record = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Bob")),
            DamlField.Create("age", new DamlInt64(30))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"name\"");
        json.Should().Contain("\"Bob\"");
        json.Should().Contain("\"age\"");
        json.Should().Contain("30");
    }

    [Fact]
    public void DeserializeRecord_should_parse_json_to_DamlRecord()
    {
        var json = """{"name":"Charlie","score":95}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.Fields.Should().HaveCount(2);
        record.GetField("name")!.As<DamlText>().Value.Should().Be("Charlie");
        record.GetField("score")!.As<DamlInt64>().Value.Should().Be(95);
    }

    [Fact]
    public void Serialize_should_handle_nested_DamlRecord()
    {
        var inner = DamlRecord.Create(
            DamlField.Create("x", new DamlInt64(10)),
            DamlField.Create("y", new DamlInt64(20))
        );
        var outer = DamlRecord.Create(
            DamlField.Create("point", inner)
        );

        var json = DamlJsonSerializer.Serialize(outer);

        json.Should().Contain("\"point\"");
        json.Should().Contain("\"x\"");
        json.Should().Contain("\"y\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlList()
    {
        var list = DamlList.Create(
            new DamlInt64(1),
            new DamlInt64(2),
            new DamlInt64(3)
        );
        var record = DamlRecord.Create(
            DamlField.Create("numbers", list)
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("[\"1\",\"2\",\"3\"]");
    }

    [Fact]
    public void Serialize_should_handle_DamlVariant()
    {
        var variant = DamlVariant.Create("Left", new DamlText("error"));
        var record = DamlRecord.Create(
            DamlField.Create("result", variant)
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"tag\"");
        json.Should().Contain("\"Left\"");
        json.Should().Contain("\"value\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlBool()
    {
        var record = DamlRecord.Create(
            DamlField.Create("isActive", new DamlBool(true)),
            DamlField.Create("isDisabled", new DamlBool(false))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"isActive\":true");
        json.Should().Contain("\"isDisabled\":false");
    }

    [Fact]
    public void Serialize_should_handle_DamlDate()
    {
        var record = DamlRecord.Create(
            DamlField.Create("createdDate", new DamlDate(new DateOnly(2023, 12, 25)))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("2023-12-25");
    }

    [Fact]
    public void Serialize_should_handle_DamlTimestamp()
    {
        var timestamp = new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero);
        var record = DamlRecord.Create(
            DamlField.Create("updatedAt", new DamlTimestamp(timestamp))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("2023-06-15");
        json.Should().Contain("12:30:45");
    }

    [Fact]
    public void Serialize_should_emit_DamlTimestamp_with_Z_utc_designator()
    {
        var timestamp = new DateTimeOffset(2023, 6, 15, 14, 30, 45, TimeSpan.FromHours(2));

        var json = DamlJsonSerializer.Serialize(new DamlTimestamp(timestamp));

        json.Should().Be("\"2023-06-15T12:30:45Z\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlParty()
    {
        var record = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty("Alice::12345"))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"owner\":\"Alice::12345\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlOptional_None()
    {
        var record = DamlRecord.Create(
            DamlField.Create("maybeValue", DamlOptional.None)
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"maybeValue\":null");
    }

    [Fact]
    public void Serialize_should_handle_DamlOptional_Some()
    {
        var record = DamlRecord.Create(
            DamlField.Create("maybeValue", DamlOptional.Some(new DamlInt64(42)))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"maybeValue\":\"42\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlTextMap()
    {
        var textMap = DamlTextMap.Create(
            ("key1", new DamlText("value1")),
            ("key2", new DamlInt64(42))
        );
        var record = DamlRecord.Create(
            DamlField.Create("settings", textMap)
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"key1\":\"value1\"");
        json.Should().Contain("\"key2\":\"42\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlEnum()
    {
        var enumValue = DamlEnum.Create("Active");
        var record = DamlRecord.Create(
            DamlField.Create("status", enumValue)
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"status\":\"Active\"");
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlList()
    {
        var json = """{"items":[1,2,3]}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var items = record.GetField("items")!.As<DamlList>();
        items.Count.Should().Be(3);
        items[0].As<DamlInt64>().Value.Should().Be(1);
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlBool()
    {
        var json = """{"active":true,"disabled":false}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.GetField("active")!.As<DamlBool>().Value.Should().BeTrue();
        record.GetField("disabled")!.As<DamlBool>().Value.Should().BeFalse();
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlVariant()
    {
        var json = """{"result":{"tag":"Left","value":"error message"}}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var variant = record.GetField("result")!.As<DamlVariant>();
        variant.Constructor.Should().Be("Left");
        variant.Value.As<DamlText>().Value.Should().Be("error message");
    }

    [Fact]
    public void DeserializeRecord_should_keep_object_with_properties_beyond_tag_and_value_as_DamlRecord()
    {
        var json = """{"payload":{"tag":"Left","value":"err","extra":true}}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var payload = record.GetRequiredField("payload").As<DamlRecord>();
        payload.Fields.Should().HaveCount(3);
        payload.GetRequiredField("tag").Should().Be(new DamlText("Left"));
        payload.GetRequiredField("extra").Should().Be(new DamlBool(true));
    }

    [Fact]
    public void DeserializeRecord_should_keep_object_with_non_string_tag_as_DamlRecord()
    {
        var json = """{"payload":{"tag":7,"value":"err"}}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var payload = record.GetRequiredField("payload").As<DamlRecord>();
        payload.GetRequiredField("tag").Should().Be(new DamlInt64(7));
        payload.GetRequiredField("value").Should().Be(new DamlText("err"));
    }

    [Fact]
    public void DeserializeRecord_should_handle_nested_DamlRecord()
    {
        var json = """{"person":{"name":"Alice","age":30}}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var person = record.GetField("person")!.As<DamlRecord>();
        person.GetField("name")!.As<DamlText>().Value.Should().Be("Alice");
        person.GetField("age")!.As<DamlInt64>().Value.Should().Be(30);
    }

    [Fact]
    public void DeserializeRecord_should_read_DamlVariant_null_value_as_DamlOptional_None()
    {
        var json = """{"result":{"tag":"A","value":null}}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var variant = record.GetField("result")!.As<DamlVariant>();
        variant.Constructor.Should().Be("A");
        variant.Value.Should().Be(DamlOptional.None);
    }

    [Fact]
    public void Serialize_should_round_trip_DamlVariant_null_value_as_null()
    {
        var json = """{"tag":"A","value":null}""";

        var value = DamlJsonSerializer.Deserialize(json);

        DamlJsonSerializer.Serialize(value).Should().Be("""{"tag":"A","value":null}""");
    }

    [Fact]
    public void DeserializeRecord_should_map_explicit_null_field_to_DamlOptional_None()
    {
        var record = DamlJsonSerializer.DeserializeRecord("""{"maybeValue":null}""");

        record.GetRequiredField("maybeValue").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void Deserialize_should_map_explicit_null_field_to_DamlOptional_None_in_nested_record()
    {
        var deserialized = DamlJsonSerializer.Deserialize("""{"person":{"nickname":null}}""");

        var person = deserialized.As<DamlRecord>().GetRequiredField("person").As<DamlRecord>();
        person.GetRequiredField("nickname").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void RoundTrip_should_reconstruct_DamlOptional_None_field()
    {
        var original = DamlRecord.Create(DamlField.Create("maybeValue", DamlOptional.None));

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        deserialized.GetRequiredField("maybeValue").AsOptional().Should().Be(DamlOptional.None);
    }

    [Fact]
    public void RoundTrip_should_recover_DamlOptional_Some_field_through_AsOptional()
    {
        var original = DamlRecord.Create(DamlField.Create("maybeValue", DamlOptional.Some(new DamlInt64(42))));

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        var optional = deserialized.GetRequiredField("maybeValue").AsOptional();
        optional.HasValue.Should().BeTrue();
        optional.Value.Should().Be(new DamlInt64(42));
    }

    [Fact]
    public void RoundTrip_should_preserve_DamlRecord_data()
    {
        var original = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Alice")),
            DamlField.Create("age", new DamlInt64(30)),
            DamlField.Create("active", new DamlBool(true))
        );

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        deserialized.GetField("name")!.As<DamlText>().Value.Should().Be("Alice");
        deserialized.GetField("age")!.As<DamlInt64>().Value.Should().Be(30);
        deserialized.GetField("active")!.As<DamlBool>().Value.Should().BeTrue();
    }

    [Fact]
    public void Serialize_DamlDate_should_be_locale_independent()
    {
        var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("ar-SA");

            var record = DamlRecord.Create(
                DamlField.Create("createdDate", new DamlDate(new DateOnly(2024, 3, 15)))
            );

            var json = DamlJsonSerializer.Serialize(record);

            json.Should().Contain("\"createdDate\":\"2024-03-15\"");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Serialize_DamlTimestamp_should_be_locale_independent()
    {
        var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("ar-SA");

            var timestamp = new DateTimeOffset(2024, 3, 15, 9, 8, 7, TimeSpan.Zero);
            var record = DamlRecord.Create(
                DamlField.Create("updatedAt", new DamlTimestamp(timestamp))
            );

            var json = DamlJsonSerializer.Serialize(record);

            json.Should().Contain("\"updatedAt\":\"2024-03-15T09:08:07Z\"");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }
}
