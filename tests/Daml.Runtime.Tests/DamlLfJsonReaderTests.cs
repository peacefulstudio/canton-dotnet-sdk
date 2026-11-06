// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderTests
{
    private const string OwnerJson = """{"owner":"alice::1220ab"}""";
    private const string OwnerParty = "alice::1220ab";

    public sealed record PartyHolder([property: DamlFieldAttribute("owner")] Party Owner) : IDamlRecord<PartyHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

        public static PartyHolder FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("owner", DamlLfJsonDecoders.ReadParty(
                DamlLfJsonDecoders.RequireField(json, context, "owner"), context.Field("owner"))));
        }
    }

    public sealed record PartyHolderEnvelope([property: DamlFieldAttribute("holder")] PartyHolder Holder)
        : IDamlRecord<PartyHolderEnvelope>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("holder", Holder.ToRecord()));

        public static PartyHolderEnvelope FromRecord(DamlRecord record) =>
            new(PartyHolder.FromRecord(record.GetRequiredField("holder").As<DamlRecord>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("holder", PartyHolder.__ReadDamlLfJson(
                DamlLfJsonDecoders.RequireField(json, context, "holder"), context.Field("holder"))));
        }
    }

    private static void ShouldCarryTheOwnerParty(DamlRecord record)
    {
        record.RecordId.Should().BeNull();
        record.Fields.Should().ContainSingle().Which.Label.Should().Be("owner");
        record.GetRequiredField("owner").Should().BeOfType<DamlParty>()
            .Which.Value.Should().Be(OwnerParty);
    }

    [Fact]
    public void ReadRecord_should_arm_a_party_field_when_given_json_text_and_a_type_argument()
    {
        ShouldCarryTheOwnerParty(DamlLfJsonReader.ReadRecord<PartyHolder>(OwnerJson));
    }

    [Fact]
    public void ReadRecord_should_arm_a_party_field_when_given_a_parsed_element_and_a_type_argument()
    {
        using var document = JsonDocument.Parse(OwnerJson);

        ShouldCarryTheOwnerParty(DamlLfJsonReader.ReadRecord<PartyHolder>(document.RootElement));
    }

    [Fact]
    public void ReadRecord_should_arm_a_party_field_nested_inside_a_record_field()
    {
        var record = DamlLfJsonReader.ReadRecord<PartyHolderEnvelope>("""{"holder":{"owner":"alice::1220ab"}}""");

        record.Fields.Should().ContainSingle().Which.Label.Should().Be("holder");
        ShouldCarryTheOwnerParty(record.GetRequiredField("holder").Should().BeOfType<DamlRecord>().Which);
    }

    [Fact]
    public void ReadRecord_should_throw_ArgumentNullException_for_null_json_text_and_a_type_argument()
    {
        var act = () => DamlLfJsonReader.ReadRecord<PartyHolder>((string)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("json");
    }

    [Fact]
    public void Read_should_return_the_generated_record_when_given_json_text()
    {
        var holder = DamlLfJsonReader.Read<PartyHolder>(OwnerJson);

        holder.Should().Be(new PartyHolder(new Party(OwnerParty)));
    }

    [Fact]
    public void Read_should_return_the_generated_record_when_given_a_parsed_element()
    {
        using var document = JsonDocument.Parse(OwnerJson);

        var holder = DamlLfJsonReader.Read<PartyHolder>(document.RootElement);

        holder.Should().Be(new PartyHolder(new Party(OwnerParty)));
    }

    [Fact]
    public void Read_should_return_a_nested_generated_record()
    {
        var envelope = DamlLfJsonReader.Read<PartyHolderEnvelope>("""{"holder":{"owner":"alice::1220ab"}}""");

        envelope.Should().Be(new PartyHolderEnvelope(new PartyHolder(new Party(OwnerParty))));
    }

    [Fact]
    public void Read_should_round_trip_a_record_through_its_serialized_form()
    {
        var original = new PartyHolder(new Party("bob::1220cd"));

        var decoded = DamlLfJsonReader.Read<PartyHolder>(DamlJsonSerializer.Serialize(original.ToRecord()));

        decoded.Should().Be(original);
    }

    [Fact]
    public void Read_should_throw_the_same_JsonException_as_ReadRecord_for_a_missing_field_in_json_text()
    {
        var readRecord = () => DamlLfJsonReader.ReadRecord<PartyHolder>("{}");
        var read = () => DamlLfJsonReader.Read<PartyHolder>("{}");

        var expected = readRecord.Should().Throw<JsonException>().Which;
        read.Should().Throw<JsonException>().WithMessage(expected.Message);
    }

    [Fact]
    public void Read_should_throw_the_same_JsonException_as_ReadRecord_for_a_non_object_element()
    {
        using var document = JsonDocument.Parse("[]");
        var readRecord = () => DamlLfJsonReader.ReadRecord<PartyHolder>(document.RootElement);
        var read = () => DamlLfJsonReader.Read<PartyHolder>(document.RootElement);

        var expected = readRecord.Should().Throw<JsonException>().Which;
        read.Should().Throw<JsonException>().WithMessage(expected.Message);
    }

    [Fact]
    public void Read_should_throw_JsonException_for_malformed_json_text()
    {
        var act = () => DamlLfJsonReader.Read<PartyHolder>("""{"owner":""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_should_throw_ArgumentNullException_for_null_json_text()
    {
        var act = () => DamlLfJsonReader.Read<PartyHolder>((string)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("json");
    }
}
