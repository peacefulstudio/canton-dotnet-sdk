// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlVariantJsonConverterFactoryTests
{
    [JsonConverter(typeof(DamlVariantJsonConverterFactory))]
    public abstract record Shape
    {
        public sealed record Circle(long Radius) : Shape;

        public sealed record Empty() : Shape;

        public sealed record Unrelated(long Width);

        public sealed record Nested(Shape Inner) : Shape;
    }

    [JsonConverter(typeof(DamlVariantJsonConverterFactory))]
    public abstract record Box<T>
        where T : notnull
    {
        public sealed record Full(T Value) : Box<T>;

        public sealed record Hollow() : Box<T>;
    }

    public abstract record Unmarked
    {
        public sealed record Arm() : Unmarked;
    }

    [JsonConverter(typeof(ForeignJsonConverter))]
    public abstract record ForeignConverter;

    private sealed class ForeignJsonConverter : JsonConverter<ForeignConverter>
    {
        public override ForeignConverter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, ForeignConverter value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    [Theory]
    [InlineData(typeof(Shape), true)]
    [InlineData(typeof(Shape.Circle), true)]
    [InlineData(typeof(Shape.Empty), true)]
    [InlineData(typeof(Box<long>), true)]
    [InlineData(typeof(Box<long>.Full), true)]
    [InlineData(typeof(Box<>), false)]
    [InlineData(typeof(Shape.Unrelated), false)]
    [InlineData(typeof(Unmarked), false)]
    [InlineData(typeof(Unmarked.Arm), false)]
    [InlineData(typeof(ForeignConverter), false)]
    [InlineData(typeof(Optional<long>), false)]
    [InlineData(typeof(Optional<long>.Some), false)]
    public void CanConvert_matches_only_variants_naming_the_factory_and_their_arms(Type type, bool expected)
    {
        new DamlVariantJsonConverterFactory().CanConvert(type).Should().Be(expected);
    }

    [Fact]
    public void Serialize_writes_the_arm_with_its_case_first()
    {
        Shape shape = new Shape.Circle(3);

        JsonSerializer.Serialize(shape).Should().Be("""{"$case":"Circle","Radius":3}""");
    }

    [Fact]
    public void Deserialize_reads_each_arm_back()
    {
        JsonSerializer.Deserialize<Shape>("""{"$case":"Circle","Radius":3}""").Should().Be(new Shape.Circle(3));
        JsonSerializer.Deserialize<Shape>("""{"$case":"Empty"}""").Should().Be(new Shape.Empty());
    }

    [Fact]
    public void Deserialize_does_not_treat_a_nested_type_outside_the_variant_as_a_case()
    {
        var act = () => JsonSerializer.Deserialize<Shape>("""{"$case":"Unrelated","Width":1}""");

        act.Should().Throw<JsonException>()
            .WithMessage("DamlVariantJsonConverterFactoryTests.Shape names an unknown case \"Unrelated\"; expected one of Circle, Empty, Nested.");
    }

    [Fact]
    public void Serialize_discriminates_every_level_of_a_variant_nested_in_its_own_arm()
    {
        const string json = """{"$case":"Nested","Inner":{"$case":"Circle","Radius":2}}""";
        Shape shape = new Shape.Nested(new Shape.Circle(2));

        JsonSerializer.Serialize(shape).Should().Be(json);
        JsonSerializer.Deserialize<Shape>(json).Should().Be(shape);
    }

    [Fact]
    public void Serialize_closes_a_generic_variant_over_its_type_argument()
    {
        const string json = """{"$case":"Full","Value":"x"}""";
        Box<string> box = new Box<string>.Full("x");

        JsonSerializer.Serialize(box).Should().Be(json);
        JsonSerializer.Deserialize<Box<string>>(json).Should().Be(box);
        JsonSerializer.Deserialize<Box<string>>("""{"$case":"Hollow"}""").Should().Be(new Box<string>.Hollow());
    }

    [Fact]
    public void Deserialize_rejects_a_payload_without_a_case()
    {
        var act = () => JsonSerializer.Deserialize<Box<long>>("""{"Value":1}""");

        act.Should().Throw<JsonException>().WithMessage("Box<Int64> is missing the \"$case\" discriminator.");
    }
}
