// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="Either{TL, TR}"/>: a
/// <c>"$case"</c>-discriminated object that reads back as the arm that wrote it. The contract is
/// a CLR round trip, not the Daml-LF <see cref="Data.DamlVariant"/> encoding
/// <see cref="Either{TL, TR}.ToValue"/> uses, so what the tests assert is that a
/// value survives the trip, on the bare options a host builds for itself as much as on
/// <see cref="DamlJsonConverters.AddDamlConverters"/>.
/// </summary>
public class EitherJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private sealed record Wrapper(string Name, Either<string, int> Outcome);

    [Fact]
    public void Either_reads_its_own_write_back_for_Left()
    {
        Either<string, int> value = new Either<string, int>.Left("e");

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<Either<string, int>>(written).Should().Be(value);
    }

    [Fact]
    public void Either_reads_its_own_write_back_for_Right()
    {
        Either<string, int> value = new Either<string, int>.Right(1);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<Either<string, int>>(written).Should().Be(value);
    }

    [Fact]
    public void Either_writes_the_case_discriminator_as_the_first_member()
    {
        Either<string, int> value = new Either<string, int>.Left("e");

        var json = JsonSerializer.Serialize(value);

        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("$case", "Value");
        document.RootElement.GetProperty("$case").GetString().Should().Be("Left");
        document.RootElement.GetProperty("Value").GetString().Should().Be("e");
    }

    [Fact]
    public void Either_writes_Right_under_its_own_case_and_type()
    {
        Either<string, int> value = new Either<string, int>.Right(1);

        var json = JsonSerializer.Serialize(value);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("$case").GetString().Should().Be("Right");
        document.RootElement.GetProperty("Value").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Either_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var wrapper = new Wrapper("alice", new Either<string, int>.Right(3));

        var written = JsonSerializer.Serialize(wrapper);

        JsonSerializer.Deserialize<Wrapper>(written).Should().Be(wrapper);
    }

    [Fact]
    public void Either_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var wrapper = new Wrapper("alice", new Either<string, int>.Left("nope"));

        var written = JsonSerializer.Serialize(wrapper, Registered);

        JsonSerializer.Deserialize<Wrapper>(written, Registered).Should().Be(wrapper);
    }

    [Fact]
    public void Either_names_both_type_arguments_when_left_and_right_share_no_shape()
    {
        var act = () => JsonSerializer.Deserialize<Either<string, int>>("\"e\"");

        act.Should().Throw<JsonException>().WithMessage("*Expected object token for Either<String, Int32>*");
    }

    [Fact]
    public void Either_refuses_a_payload_missing_the_discriminator()
    {
        var act = () => JsonSerializer.Deserialize<Either<string, int>>("""{"Value":"e"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("*Either<String, Int32> is missing the \"$case\" discriminator*");
    }

    [Fact]
    public void Either_refuses_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<Either<string, int>>("""{"$case":"Middle","Value":"e"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("*Either<String, Int32> names an unknown case \"Middle\"*Left, Right*");
    }

    [Fact]
    public void Either_round_trips_under_a_property_naming_policy()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper };
        Either<string, int> value = new Either<string, int>.Left("e");

        var json = JsonSerializer.Serialize(value, options);

        using (var document = JsonDocument.Parse(json))
        {
            document.RootElement.GetProperty("$case").GetString().Should().Be(
                "Left", "the discriminator is inserted directly, not through a serialized property, "
                + "so a naming policy does not touch it — SNAKE_CASE_UPPER, unlike CamelCase, actually "
                + "rewrites \"$case\" (to \"$CASE\") if it were applied, so this pins a policy that can "
                + "catch that regression");
            document.RootElement.GetProperty("VALUE").GetString().Should().Be(
                "e", "the arm's own Value property is written through the policy like any other member");
            document.RootElement.TryGetProperty("Value", out _).Should().BeFalse(
                "the PascalCase name must not also appear once the policy has renamed it");
        }
        JsonSerializer.Deserialize<Either<string, int>>(json, options).Should().Be(value);
    }

    [Fact]
    public void Either_reads_back_under_options_that_disallow_unmapped_members()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        Either<string, int> value = new Either<string, int>.Right(1);

        var written = JsonSerializer.Serialize(value, options);

        JsonSerializer.Deserialize<Either<string, int>>(written, options).Should().Be(
            value,
            "\"$case\" is not a member either arm declares, so a caller hardening against unmapped "
            + "members must not see it as one when the union's own converter reads the arm back");
    }

    [Fact]
    public void Either_round_trips_wrapping_a_record_like_payload()
    {
        Either<string, Payload> value = new Either<string, Payload>.Right(new Payload("alice", 3));

        var json = JsonSerializer.Serialize(value);

        using (var document = JsonDocument.Parse(json))
        {
            document.RootElement.GetProperty("$case").GetString().Should().Be("Right");
            document.RootElement.GetProperty("Value").GetProperty("Name").GetString().Should().Be("alice");
            document.RootElement.GetProperty("Value").GetProperty("Amount").GetInt32().Should().Be(3);
        }
        JsonSerializer.Deserialize<Either<string, Payload>>(json).Should().Be(value);
    }

    public static IEnumerable<object[]> UnsupportedReferenceHandlers =>
    [
        [ReferenceHandler.Preserve],
        [ReferenceHandler.IgnoreCycles],
    ];

    [Theory]
    [MemberData(nameof(UnsupportedReferenceHandlers))]
    public void Either_write_refuses_a_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };
        Either<string, int> value = new Either<string, int>.Left("e");

        var act = () => JsonSerializer.Serialize(value, options);

        act.Should().Throw<JsonException>().WithMessage(
            "Reference handling is not supported for Either<String, Int32>: this Daml union's "
            + "converter reaches its arm's payload through a nested JsonSerializer call that starts "
            + "its own independent reference resolver, so JsonSerializerOptions.ReferenceHandler.Preserve "
            + "would silently duplicate an aliased object under a colliding \"$id\" and "
            + "ReferenceHandler.IgnoreCycles would not recognize a cycle through this type at all. "
            + "Serialize or deserialize a value containing Either<String, Int32> with "
            + "JsonSerializerOptions.ReferenceHandler left null.");
    }

    [Theory]
    [MemberData(nameof(UnsupportedReferenceHandlers))]
    public void Either_read_refuses_a_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };

        var act = () => JsonSerializer.Deserialize<Either<string, int>>("""{"$case":"Left","Value":"e"}""", options);

        act.Should().Throw<JsonException>().WithMessage(
            "Reference handling is not supported for Either<String, Int32>: this Daml union's "
            + "converter reaches its arm's payload through a nested JsonSerializer call that starts "
            + "its own independent reference resolver, so JsonSerializerOptions.ReferenceHandler.Preserve "
            + "would silently duplicate an aliased object under a colliding \"$id\" and "
            + "ReferenceHandler.IgnoreCycles would not recognize a cycle through this type at all. "
            + "Serialize or deserialize a value containing Either<String, Int32> with "
            + "JsonSerializerOptions.ReferenceHandler left null.");
    }

    [Fact]
    public void Either_writes_the_case_discriminator_when_the_static_type_is_the_concrete_Left_arm()
    {
        Either<string, int>.Left value = new("e");

        var json = JsonSerializer.Serialize(value, Registered);

        json.Should().Be(
            """{"$case":"Left","Value":"e"}""",
            "a caller holding the concrete arm type — not the declared-abstract Either<string, int> "
            + "— must still get the discriminated shape once EitherJsonConverterFactory is "
            + "registered: its [JsonConverter] attribute lives on Either<TL, TR> and, unlike its "
            + "CanConvert, is not inherited by JsonSerializer's converter resolution when the arm "
            + "type itself is what gets looked up, so only AddDamlConverters's explicit "
            + "registration — not the attribute — reaches this arm-typed lookup");
    }

    [Fact]
    public void Either_writes_the_case_discriminator_when_the_static_type_is_the_concrete_Right_arm()
    {
        Either<string, int>.Right value = new(1);

        var json = JsonSerializer.Serialize(value, Registered);

        json.Should().Be("""{"$case":"Right","Value":1}""");
    }

    [Fact]
    public void Either_arm_typed_write_reads_back_through_the_base_type()
    {
        Either<string, int>.Left value = new("e");

        var written = JsonSerializer.Serialize(value, Registered);

        JsonSerializer.Deserialize<Either<string, int>>(written, Registered).Should().Be(value);
    }

    [Fact]
    public void Either_arm_typed_write_on_bare_options_falls_back_to_the_plain_reflection_shape()
    {
        Either<string, int>.Left value = new("e");

        var json = JsonSerializer.Serialize(value);

        json.Should().Be(
            """{"Value":"e"}""",
            "EitherJsonConverterFactory is reached for an arm-typed lookup only through "
            + "JsonSerializerOptions.Converters, e.g. via AddDamlConverters, because "
            + "[JsonConverter] on Either<TL, TR> is not inherited by Either<string, int>.Left; bare "
            + "options carry neither the attribute on the arm nor the registration, so the arm "
            + "keeps writing its own plain members with no \"$case\", exactly as it did before "
            + "this factory started matching arm types at all");
    }

    private sealed record Payload(string Name, int Amount);
}
