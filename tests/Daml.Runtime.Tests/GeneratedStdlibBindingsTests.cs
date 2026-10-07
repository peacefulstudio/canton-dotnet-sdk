// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class GeneratedStdlibBindingsTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static DamlLfJsonDecodeContext Root => DamlLfJsonDecodeContext.Root("test");

    [Fact]
    public void Tuple5_round_trips_five_differently_typed_components_through_a_record()
    {
        var tuple = new Tuple5<long, string, bool, string, decimal>(42L, "gold", true, "Alice::ns", 3.5m);

        var record = tuple.ToRecord(
            value => new DamlInt64(value),
            value => new DamlText(value),
            value => new DamlBool(value),
            value => new DamlParty(value),
            value => new DamlNumeric(value));

        record.Fields.Select(field => field.Label).Should().Equal("_1", "_2", "_3", "_4", "_5");
        Tuple5<long, string, bool, string, decimal>.FromRecord(
            record,
            value => value.As<DamlInt64>().Value,
            null,
            value => value.As<DamlText>().Value,
            null,
            value => value.As<DamlBool>().Value,
            null,
            value => value.As<DamlParty>().Value,
            null,
            value => value.As<DamlNumeric>().Value,
            null).Should().Be(tuple);
    }

    [Fact]
    public void Tuple20_round_trips_its_last_component()
    {
        var tuple = new Tuple20<long, long, long, long, long, long, long, long, long, long, long, long, long, long, long, long, long, long, long, string>(
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, "last");

        var record = tuple.ToRecord(
            v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v),
            v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v),
            v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v),
            v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlInt64(v), v => new DamlText(v));

        record.GetRequiredField("_20").As<DamlText>().Value.Should().Be("last");
        record.Fields.Should().HaveCount(20);
    }

    [Fact]
    public void Unit_wrapper_is_a_one_element_record_and_not_the_daml_unit_value()
    {
        var wrapped = new Unit<long>(7L);

        var record = wrapped.ToRecord(value => new DamlInt64(value));

        record.Fields.Should().ContainSingle().Which.Label.Should().Be("_1");
        Unit<long>.FromRecord(record, value => value.As<DamlInt64>().Value, null).Should().Be(wrapped);
    }

    [Fact]
    public void Down_round_trips_through_its_unpack_field()
    {
        var down = new Down<string>("x");

        var record = down.ToRecord(value => new DamlText(value));

        record.Fields.Should().ContainSingle().Which.Label.Should().Be("unpack");
        Down<string>.FromRecord(record, value => value.As<DamlText>().Value, null).Should().Be(down);
    }

    [Fact]
    public void Validation_success_round_trips_through_a_variant()
    {
        Validation<string, long> validation = new Validation<string, long>.Success(9L);

        var variant = validation.ToVariant(value => new DamlText(value), value => new DamlInt64(value));

        variant.Constructor.Should().Be("Success");
        Validation<string, long>.FromVariant(
            variant, value => value.As<DamlText>().Value, null, value => value.As<DamlInt64>().Value, null)
            .Should().Be(validation);
    }

    [Fact]
    public void Validation_errors_round_trips_a_non_empty_list_of_errors()
    {
        Validation<string, long> validation = new Validation<string, long>.Errors(new NonEmpty<string>("e1", ["e2"]));

        var variant = validation.ToVariant(value => new DamlText(value), value => new DamlInt64(value));

        variant.Constructor.Should().Be("Errors");
        Validation<string, long>.FromVariant(
            variant, value => value.As<DamlText>().Value, null, value => value.As<DamlInt64>().Value, null)
            .Should().Be(validation);
    }

    [Theory]
    [InlineData(Ordering.LT, "LT")]
    [InlineData(Ordering.EQ, "EQ")]
    [InlineData(Ordering.GT, "GT")]
    public void Ordering_round_trips_through_its_enum_constructor(Ordering ordering, string constructor)
    {
        var value = ordering.ToDamlEnum();

        value.Constructor.Should().Be(constructor);
        OrderingExtensions.FromDamlEnum(value).Should().Be(ordering);
    }

    [Fact]
    public void Minstd_round_trips_through_its_single_int64_constructor()
    {
        Minstd minstd = new Minstd.Minstd_(1234L);

        var variant = minstd.ToVariant();

        variant.Constructor.Should().Be("Minstd");
        Minstd.FromVariant(variant).Should().Be(minstd);
    }

    [Fact]
    public void Tuple5_json_decodes_each_component_with_its_own_reader()
    {
        var json = Json("""{"_1":"42","_2":"gold","_3":true,"_4":"Alice::ns","_5":"2026-10-02"}""");

        var record = Tuple5<long, string, bool, string, DateOnly>.__ReadDamlLfJson(
            json,
            Root,
            DamlLfJsonDecoders.ReadInt64,
            null,
            DamlLfJsonDecoders.ReadText,
            null,
            DamlLfJsonDecoders.ReadBool,
            null,
            DamlLfJsonDecoders.ReadParty,
            null,
            DamlLfJsonDecoders.ReadDate,
            null);

        record.GetRequiredField("_1").Should().Be(new DamlInt64(42L));
        record.GetRequiredField("_2").Should().Be(new DamlText("gold"));
        record.GetRequiredField("_3").Should().Be(new DamlBool(true));
        record.GetRequiredField("_4").Should().Be(new DamlParty("Alice::ns"));
        record.GetRequiredField("_5").Should().BeOfType<DamlDate>();
    }

    [Fact]
    public void Tuple5_json_decoding_fails_when_the_readers_for_positions_one_and_two_are_swapped()
    {
        var json = Json("""{"_1":"42","_2":"gold","_3":true,"_4":"Alice::ns","_5":"2026-10-02"}""");

        var act = () => Tuple5<string, long, bool, string, DateOnly>.__ReadDamlLfJson(
            json,
            Root,
            DamlLfJsonDecoders.ReadText,
            null,
            DamlLfJsonDecoders.ReadInt64,
            null,
            DamlLfJsonDecoders.ReadBool,
            null,
            DamlLfJsonDecoders.ReadParty,
            null,
            DamlLfJsonDecoders.ReadDate,
            null);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Tuple5_json_decoding_fails_when_the_readers_for_positions_three_and_four_are_swapped()
    {
        var json = Json("""{"_1":"42","_2":"gold","_3":true,"_4":"Alice::ns","_5":"2026-10-02"}""");

        var act = () => Tuple5<long, string, string, bool, DateOnly>.__ReadDamlLfJson(
            json,
            Root,
            DamlLfJsonDecoders.ReadInt64,
            null,
            DamlLfJsonDecoders.ReadText,
            null,
            DamlLfJsonDecoders.ReadParty,
            null,
            DamlLfJsonDecoders.ReadBool,
            null,
            DamlLfJsonDecoders.ReadDate,
            null);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Validation_json_decodes_a_success_with_the_value_reader_and_an_errors_list_with_the_error_reader()
    {
        var success = Validation<string, long>.__ReadDamlLfJson(
            Json("""{"tag":"Success","value":"5"}"""), Root, DamlLfJsonDecoders.ReadText, null, DamlLfJsonDecoders.ReadInt64, null);
        var errors = Validation<string, long>.__ReadDamlLfJson(
            Json("""{"tag":"Errors","value":{"hd":"bad","tl":["worse"]}}"""), Root, DamlLfJsonDecoders.ReadText, null, DamlLfJsonDecoders.ReadInt64, null);

        success.Value.Should().Be(new DamlInt64(5L));
        errors.Constructor.Should().Be("Errors");
    }

    [Fact]
    public void Validation_json_decoding_fails_when_the_error_and_value_readers_are_swapped()
    {
        var act = () => Validation<long, string>.__ReadDamlLfJson(
            Json("""{"tag":"Success","value":"5"}"""), Root, DamlLfJsonDecoders.ReadInt64, null, DamlLfJsonDecoders.ReadBool, null);

        act.Should().Throw<JsonException>();
    }
}
