// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Data;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class RestValueEncoderTests
{
    [Fact]
    public void ToWireValue_carries_the_runtime_writers_text_as_the_only_idiomatic_entry()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlList([new DamlInt64(1), new DamlInt64(2)]));

        wire.AdditionalProperties.Should().Equal(new Dictionary<string, object> { ["idiomatic"] = """["1","2"]""" });
        wire.List.Should().BeNull();
    }

    [Fact]
    public void ToWireValue_sends_a_carried_undecoded_json_value_verbatim_as_the_idiomatic_entry()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlUndecodedJson("{\"owner\": \"alice::ns1\",\"amount\":\"10\"}"));

        wire.AdditionalProperties.Should().Equal(
            new Dictionary<string, object> { ["idiomatic"] = "{\"owner\": \"alice::ns1\",\"amount\":\"10\"}" });
    }

    [Fact]
    public void ToWireValue_refuses_a_carried_undecoded_json_value_nested_in_a_list()
    {
        var action = () => RestValueEncoder.ToWireValue(new DamlList([new DamlUndecodedJson("{}")]));

        action.Should().Throw<JsonException>().WithMessage("Cannot serialize DamlUndecodedJson to JSON");
    }

    [Fact]
    public void ToWireRecord_carries_the_runtime_writers_text_as_the_only_idiomatic_entry()
    {
        var record = new DamlRecord(
            new RuntimeIdentifier("pkg", "Mod", "Ent"),
            [new DamlField("owner", new DamlParty("alice::ns1")), new DamlField("amount", new DamlInt64(10))]);

        var wire = RestValueEncoder.ToWireRecord(record);

        wire.AdditionalProperties.Should().Equal(
            new Dictionary<string, object> { ["idiomatic"] = """{"owner":"alice::ns1","amount":"10"}""" });
        wire.Fields.Should().BeNull();
    }

    [Fact]
    public void ToWireValue_round_trips_through_RestValueDecoder_for_a_nested_record()
    {
        var original = new DamlRecord(
            null,
            [new DamlField("tags", new DamlList([new DamlText("a"), new DamlText("b")]))]);

        var wireJson = JsonSerializer.Serialize(RestValueEncoder.ToWireRecord(original), RestRefitSettings.SerializerOptions);
        var wire = JsonSerializer.Deserialize<Raw.Record>(wireJson, RestRefitSettings.SerializerOptions)!;
        var decoded = RestValueDecoder.ToDamlRecord<TagsRecord>(wire);

        decoded.Should().BeEquivalentTo(original, options => options.PreferringRuntimeMemberTypes());
    }

    private sealed record TagsRecord(
        [property: DamlFieldAttribute("tags")] IReadOnlyList<string> Tags) : IDamlRecord<TagsRecord>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("tags", (element, elementContext) => DamlLfJsonDecoders.ReadList(element, elementContext, DamlLfJsonDecoders.ReadText)));

        public static TagsRecord FromRecord(DamlRecord record) => throw new NotSupportedException();
    }
}
