// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderVariantTests
{
    public abstract record Signal : IDamlVariant<Signal>
    {
        private static readonly string[] ExpectedConstructors = ["Alert", "Clear"];

        public abstract DamlVariant ToVariant();

        public static DamlVariant __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var tag = DamlLfJsonDecoders.ReadVariantTag(json, context);
            return tag switch
            {
                "Alert" => DamlVariant.Create("Alert", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                "Clear" => DamlVariant.Create("Clear", DamlLfJsonDecoders.ReadUnit(
                    DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, context, ExpectedConstructors)
            };
        }

        public sealed record Alert(string Message) : Signal
        {
            public override DamlVariant ToVariant() => DamlVariant.Create("Alert", new DamlText(Message));
        }

        public sealed record Clear : Signal
        {
            public override DamlVariant ToVariant() => DamlVariant.Create("Clear", DamlUnit.Instance);
        }
    }

    public abstract record NestedSignal : IDamlVariant<NestedSignal>
    {
        private static readonly string[] ExpectedConstructors = ["Wrap", "Leaf"];

        public abstract DamlVariant ToVariant();

        public static DamlVariant __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var tag = DamlLfJsonDecoders.ReadVariantTag(json, context);
            return tag switch
            {
                "Wrap" => DamlVariant.Create("Wrap", __ReadDamlLfJson(
                    DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                "Leaf" => DamlVariant.Create("Leaf", DamlLfJsonDecoders.ReadUnit(
                    DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, context, ExpectedConstructors)
            };
        }
    }

    private const string AlertJson = """{"tag":"Alert","value":"smoke detected"}""";
    private const string ClearJson = """{"tag":"Clear","value":{}}""";

    [Fact]
    public void ReadVariant_should_decode_a_payload_arm_from_json_text()
    {
        var variant = DamlLfJsonReader.ReadVariant<Signal>(AlertJson);

        variant.Constructor.Should().Be("Alert");
        variant.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("smoke detected");
    }

    [Fact]
    public void ReadVariant_should_decode_a_payload_arm_from_a_parsed_element()
    {
        using var document = JsonDocument.Parse(AlertJson);

        var variant = DamlLfJsonReader.ReadVariant<Signal>(document.RootElement);

        variant.Constructor.Should().Be("Alert");
        variant.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("smoke detected");
    }

    [Fact]
    public void ReadVariant_should_decode_a_nullary_arm_from_json_text()
    {
        var variant = DamlLfJsonReader.ReadVariant<Signal>(ClearJson);

        variant.Constructor.Should().Be("Clear");
        variant.Value.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadVariant_should_decode_a_nullary_arm_from_a_parsed_element()
    {
        using var document = JsonDocument.Parse(ClearJson);

        var variant = DamlLfJsonReader.ReadVariant<Signal>(document.RootElement);

        variant.Constructor.Should().Be("Clear");
        variant.Value.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadVariant_should_reject_an_unknown_constructor()
    {
        var act = () => DamlLfJsonReader.ReadVariant<Signal>("""{"tag":"Panic","value":{}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Panic' at 'Signal'; expected one of Alert, Clear");
    }

    [Fact]
    public void ReadVariant_should_throw_ArgumentNullException_for_null_json_text()
    {
        var act = () => DamlLfJsonReader.ReadVariant<Signal>((string)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("json");
    }

    [Fact]
    public void ReadVariant_should_reject_json_text_larger_than_the_configured_limit()
    {
        var limits = new DamlJsonDeserializationLimits(MaxInputCharacters: AlertJson.Length - 1);

        var act = () => DamlLfJsonReader.ReadVariant<Signal>(AlertJson, limits);

        act.Should().Throw<JsonException>().WithMessage("*maximum supported JSON input size*");
    }

    [Fact]
    public void ReadVariant_should_decode_a_document_over_the_size_limit_when_the_caller_already_parsed_it()
    {
        using var document = JsonDocument.Parse(AlertJson);
        var limits = new DamlJsonDeserializationLimits(MaxInputCharacters: 1);

        var variant = DamlLfJsonReader.ReadVariant<Signal>(document.RootElement, limits);

        variant.Constructor.Should().Be("Alert");
        variant.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("smoke detected");
    }

    private const int WrapLevelsExactlyFillingTheDepthBound = 127;

    private static string NestedJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"tag":"Wrap","value":""", levels))
        + """{"tag":"Leaf","value":{}}"""
        + string.Concat(Enumerable.Repeat("}", levels));

    [Fact]
    public void ReadVariant_should_decode_value_nesting_exactly_at_the_supported_depth()
    {
        var variant = DamlLfJsonReader.ReadVariant<NestedSignal>(
            NestedJson(WrapLevelsExactlyFillingTheDepthBound));

        variant.Constructor.Should().Be("Wrap");
    }

    [Fact]
    public void ReadVariant_should_reject_value_nesting_one_level_beyond_the_supported_depth()
    {
        var act = () => DamlLfJsonReader.ReadVariant<NestedSignal>(
            NestedJson(WrapLevelsExactlyFillingTheDepthBound + 1));

        act.Should().Throw<JsonException>().WithMessage("*maximum supported depth*");
    }

    private static readonly DamlJsonDeserializationLimits UnusableLimits = new(MaxInputCharacters: 0);

    [Fact]
    public void ReadVariant_should_reject_an_unusable_limit_configuration_from_json_text()
    {
        var act = () => DamlLfJsonReader.ReadVariant<Signal>(AlertJson, UnusableLimits);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("limits");
    }

    [Fact]
    public void ReadVariant_should_reject_an_unusable_limit_configuration_from_a_parsed_element()
    {
        using var document = JsonDocument.Parse(AlertJson);

        var act = () => DamlLfJsonReader.ReadVariant<Signal>(document.RootElement, UnusableLimits);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("limits");
    }

    [Fact]
    public void ReadVariant_should_decode_a_payload_arm_when_the_limits_argument_is_explicitly_null()
    {
        var variant = DamlLfJsonReader.ReadVariant<Signal>(AlertJson, limits: null);

        variant.Constructor.Should().Be("Alert");
        variant.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("smoke detected");
    }
}
