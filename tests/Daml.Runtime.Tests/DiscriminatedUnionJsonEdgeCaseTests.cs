// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins <see cref="Daml.Runtime.Serialization.DiscriminatedUnionJson"/>'s failure-translation
/// paths, driven through the public <see cref="Optional{T}"/> union rather than the internal
/// type directly: a <see cref="JsonException"/> raised while decoding or encoding an arm's own
/// payload passes through unchanged, while any other exception is wrapped in one that names the
/// union and the failing case.
/// </summary>
public sealed class DiscriminatedUnionJsonEdgeCaseTests
{
    private sealed record Boom(int Value);

    private sealed class BoomConverter : JsonConverter<Boom>
    {
        public override Boom Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            throw new InvalidOperationException("boom on read");
        }

        public override void Write(Utf8JsonWriter writer, Boom value, JsonSerializerOptions options) =>
            throw new InvalidOperationException("boom on write");
    }

    private static readonly JsonSerializerOptions BoomOptions = new()
    {
        Converters = { new BoomConverter() },
    };

    private sealed class ScalarArmConverter : JsonConverter<Optional<int>.Some>
    {
        public override Optional<int>.Some Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Optional<int>.Some value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }

    private static readonly JsonSerializerOptions ScalarArmOptions = new()
    {
        Converters = { new ScalarArmConverter() },
    };

    [Fact]
    public void Optional_read_rethrows_a_JsonException_from_the_arms_own_payload_unchanged()
    {
        var act = () => JsonSerializer.Deserialize<Optional<int>>("""{"$case":"Some","Value":"not-a-number"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("The JSON value could not be converted to*Optional`1+Some*")
            .Which.Message.Should().NotContain("Cannot read");
    }

    [Fact]
    public void Optional_read_wraps_a_non_JsonException_from_the_arms_own_payload()
    {
        var act = () => JsonSerializer.Deserialize<Optional<Boom>>(
            """{"$case":"Some","Value":{"Value":1}}""", BoomOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("Cannot read Optional<DiscriminatedUnionJsonEdgeCaseTests.Boom> case \"Some\": boom on read")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("boom on read");
    }

    [Fact]
    public void Optional_write_rejects_an_arm_that_serializes_to_a_non_object_node()
    {
        Optional<int> value = new Optional<int>.Some(7);

        var act = () => JsonSerializer.Serialize(value, ScalarArmOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("Cannot write Optional<Int32> case \"Some\": expected an object, got Number.");
    }

    [Fact]
    public void Optional_write_wraps_a_non_JsonException_from_the_arms_own_payload()
    {
        Optional<Boom> value = new Optional<Boom>.Some(new Boom(1));

        var act = () => JsonSerializer.Serialize(value, BoomOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("Cannot write Optional<DiscriminatedUnionJsonEdgeCaseTests.Boom> case \"Some\": boom on write")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("boom on write");
    }
}
