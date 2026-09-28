// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonDeserializationLimitsTests
{
    private const int DocumentedDefaultMaxInputCharacters = 16 * 1024 * 1024;
    private const int DocumentedDefaultMaxArrayElements = 100_000;

    public sealed record Marker(long Value) : IDamlRecord<Marker>
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("value", new DamlInt64(Value)));

        public static Marker FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("value").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var valueJson = DamlLfJsonDecoders.RequireField(json, context, "value");
            return DamlRecord.Create(DamlField.Create(
                "value", DamlLfJsonDecoders.ReadInt64(valueJson, context.Field("value"))));
        }
    }

    [Fact]
    public void New_should_carry_the_documented_default_limits()
    {
        var limits = new DamlJsonDeserializationLimits();

        limits.MaxInputCharacters.Should().Be(DocumentedDefaultMaxInputCharacters);
        limits.MaxArrayElements.Should().Be(DocumentedDefaultMaxArrayElements);
    }

    [Fact]
    public void Deserialize_with_default_limits_should_accept_ordinary_input()
    {
        DamlJsonDeserializationLimits? unsetLimits = default;

        var act = () => DamlJsonSerializer.Deserialize("""{"value":"1"}""", unsetLimits);

        act.Should().NotThrow();
    }

    [Fact]
    public void Deserialize_with_default_limits_should_reject_an_array_one_past_100000_elements()
    {
        DamlJsonDeserializationLimits? unsetLimits = default;
        var oversizedArray = "[" + string.Join(",", Enumerable.Repeat("1", 100_001)) + "]";

        var act = () => DamlJsonSerializer.Deserialize(oversizedArray, unsetLimits);

        act.Should().Throw<JsonException>()
            .WithMessage("JSON array length 100001 exceeds the maximum supported JSON array length of 100000");
    }

    [Fact]
    public void DeserializeRecord_with_default_limits_should_accept_ordinary_input()
    {
        DamlJsonDeserializationLimits? unsetLimits = default;

        var act = () => DamlJsonSerializer.DeserializeRecord("""{"value":"1"}""", unsetLimits);

        act.Should().NotThrow();
    }

    [Fact]
    public void ReadRecord_generic_from_text_with_default_limits_should_accept_ordinary_input()
    {
        DamlJsonDeserializationLimits? unsetLimits = default;

        var act = () => DamlLfJsonReader.ReadRecord<Marker>("""{"value":"1"}""", unsetLimits);

        act.Should().NotThrow();
    }

    [Fact]
    public void Deserialize_with_a_new_limits_instance_should_accept_ordinary_input()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"value":"1"}""", new DamlJsonDeserializationLimits());

        act.Should().NotThrow();
    }

    [Fact]
    public void DeserializeRecord_with_a_new_limits_instance_should_accept_ordinary_input()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("""{"value":"1"}""", new DamlJsonDeserializationLimits());

        act.Should().NotThrow();
    }

    [Fact]
    public void ReadRecord_generic_from_element_with_a_new_limits_instance_should_accept_ordinary_input()
    {
        using var document = JsonDocument.Parse("""{"value":"1"}""");

        var act = () => DamlLfJsonReader.ReadRecord<Marker>(document.RootElement, new DamlJsonDeserializationLimits());

        act.Should().NotThrow();
    }

    [Fact]
    public void ReadRecord_generic_from_text_with_a_new_limits_instance_should_accept_ordinary_input()
    {
        var act = () => DamlLfJsonReader.ReadRecord<Marker>("""{"value":"1"}""", new DamlJsonDeserializationLimits());

        act.Should().NotThrow();
    }

}
