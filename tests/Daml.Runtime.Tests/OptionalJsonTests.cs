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
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="Optional{T}"/>: a
/// <c>"$case"</c>-discriminated object that reads back as the arm that wrote it. The contract
/// is a CLR round trip, not the Daml-LF null-or-value encoding <see cref="Optional{T}.ToValue"/>
/// uses, so what the tests assert is that a value survives the trip, on the bare
/// options a host builds for itself as much as on
/// <see cref="DamlJsonConverters.AddDamlConverters"/>.
/// </summary>
public class OptionalJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private sealed record Wrapper(string Name, Optional<string> Nickname);

    [Fact]
    public void Optional_reads_its_own_write_back_for_Some()
    {
        Optional<string> value = new Optional<string>.Some("c");

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<Optional<string>>(written).Should().Be(value);
    }

    [Fact]
    public void Optional_reads_its_own_write_back_for_None()
    {
        Optional<string> value = new Optional<string>.None();

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<Optional<string>>(written).Should().Be(value);
    }

    [Fact]
    public void Optional_writes_the_case_discriminator_as_the_first_member()
    {
        Optional<string> value = new Optional<string>.Some("c");

        var json = JsonSerializer.Serialize(value);

        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().First().Name.Should().Be("$case");
        document.RootElement.GetProperty("$case").GetString().Should().Be("Some");
        document.RootElement.GetProperty("Value").GetString().Should().Be("c");
        document.RootElement.GetProperty("HasValue").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Optional_writes_None_with_the_discriminator_and_its_own_HasValue_only()
    {
        Optional<string> value = new Optional<string>.None();

        var json = JsonSerializer.Serialize(value);

        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("$case", "HasValue");
        document.RootElement.GetProperty("$case").GetString().Should().Be("None");
        document.RootElement.GetProperty("HasValue").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Optional_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var wrapper = new Wrapper("alice", new Optional<string>.Some("c"));

        var written = JsonSerializer.Serialize(wrapper);

        JsonSerializer.Deserialize<Wrapper>(written).Should().Be(wrapper);
    }

    [Fact]
    public void Optional_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var wrapper = new Wrapper("alice", new Optional<string>.None());

        var written = JsonSerializer.Serialize(wrapper, Registered);

        JsonSerializer.Deserialize<Wrapper>(written, Registered).Should().Be(wrapper);
    }

    [Fact]
    public void Optional_distinguishes_an_outer_None_from_a_Some_wrapping_an_inner_None()
    {
        Optional<Optional<int>> outerNone = new Optional<Optional<int>>.None();
        Optional<Optional<int>> someOfNone = new Optional<Optional<int>>.Some(new Optional<int>.None());

        var outerNoneJson = JsonSerializer.Serialize(outerNone);
        var someOfNoneJson = JsonSerializer.Serialize(someOfNone);

        outerNoneJson.Should().NotBe(
            someOfNoneJson,
            "a bare null-for-None encoding cannot tell these two shapes apart, and the per-level "
            + "discriminator is what fixes that");
        JsonSerializer.Deserialize<Optional<Optional<int>>>(outerNoneJson).Should().Be(outerNone);
        JsonSerializer.Deserialize<Optional<Optional<int>>>(someOfNoneJson).Should().Be(someOfNone);
    }

    [Fact]
    public void Optional_round_trips_a_Some_wrapping_an_inner_Some_when_nested()
    {
        Optional<Optional<int>> value = new Optional<Optional<int>>.Some(new Optional<int>.Some(9));

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<Optional<Optional<int>>>(written).Should().Be(value);
    }

    [Fact]
    public void Optional_refuses_a_payload_that_is_not_an_object()
    {
        var act = () => JsonSerializer.Deserialize<Optional<string>>("\"c\"");

        act.Should().Throw<JsonException>().WithMessage("*Expected object token for Optional<String>*");
    }

    [Fact]
    public void Optional_refuses_a_payload_missing_the_discriminator()
    {
        var act = () => JsonSerializer.Deserialize<Optional<string>>("""{"Value":"c"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("*Optional<String> is missing the \"$case\" discriminator*");
    }

    [Fact]
    public void Optional_refuses_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<Optional<string>>("""{"$case":"Maybe","Value":"c"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("*Optional<String> names an unknown case \"Maybe\"*Some, None*");
    }

    [Fact]
    public void Optional_round_trips_under_a_property_naming_policy()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper };
        Optional<string> value = new Optional<string>.Some("c");

        var json = JsonSerializer.Serialize(value, options);

        using (var document = JsonDocument.Parse(json))
        {
            document.RootElement.GetProperty("$case").GetString().Should().Be(
                "Some", "the discriminator is inserted directly, not through a serialized property, "
                + "so a naming policy does not touch it — SNAKE_CASE_UPPER, unlike CamelCase, actually "
                + "rewrites \"$case\" (to \"$CASE\") if it were applied, so this pins a policy that can "
                + "catch that regression");
            document.RootElement.GetProperty("VALUE").GetString().Should().Be(
                "c", "the arm's own Value property is written through the policy like any other member");
            document.RootElement.TryGetProperty("Value", out _).Should().BeFalse(
                "the PascalCase name must not also appear once the policy has renamed it");
        }
        JsonSerializer.Deserialize<Optional<string>>(json, options).Should().Be(value);
    }

    [Fact]
    public void Optional_reads_back_under_options_that_disallow_unmapped_members()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        Optional<string> value = new Optional<string>.Some("c");

        var written = JsonSerializer.Serialize(value, options);

        JsonSerializer.Deserialize<Optional<string>>(written, options).Should().Be(
            value,
            "\"$case\" is not a member either arm declares, so a caller hardening against unmapped "
            + "members must not see it as one when the union's own converter reads the arm back");
    }

    [Fact]
    public void Optional_round_trips_inside_a_List()
    {
        List<Optional<string>> value = [new Optional<string>.Some("a"), new Optional<string>.None()];

        var json = JsonSerializer.Serialize(value);

        using (var document = JsonDocument.Parse(json))
        {
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            document.RootElement.GetArrayLength().Should().Be(2);
            document.RootElement[0].GetProperty("$case").GetString().Should().Be("Some");
            document.RootElement[1].GetProperty("$case").GetString().Should().Be("None");
        }
        JsonSerializer.Deserialize<List<Optional<string>>>(json).Should().Equal(value);
    }

    public static IEnumerable<object[]> UnsupportedReferenceHandlers =>
    [
        [ReferenceHandler.Preserve],
        [ReferenceHandler.IgnoreCycles],
    ];

    [Theory]
    [MemberData(nameof(UnsupportedReferenceHandlers))]
    public void Optional_write_refuses_a_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };
        Optional<string> value = new Optional<string>.Some("c");

        var act = () => JsonSerializer.Serialize(value, options);

        act.Should().Throw<JsonException>().WithMessage(
            "Reference handling is not supported for Optional<String>: this Daml union's converter "
            + "reaches its arm's payload through a nested JsonSerializer call that starts its own "
            + "independent reference resolver, so JsonSerializerOptions.ReferenceHandler.Preserve "
            + "would silently duplicate an aliased object under a colliding \"$id\" and "
            + "ReferenceHandler.IgnoreCycles would not recognize a cycle through this type at all. "
            + "Serialize or deserialize a value containing Optional<String> with "
            + "JsonSerializerOptions.ReferenceHandler left null.");
    }

    [Theory]
    [MemberData(nameof(UnsupportedReferenceHandlers))]
    public void Optional_read_refuses_a_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };

        var act = () => JsonSerializer.Deserialize<Optional<string>>("""{"$case":"Some","Value":"c"}""", options);

        act.Should().Throw<JsonException>().WithMessage(
            "Reference handling is not supported for Optional<String>: this Daml union's converter "
            + "reaches its arm's payload through a nested JsonSerializer call that starts its own "
            + "independent reference resolver, so JsonSerializerOptions.ReferenceHandler.Preserve "
            + "would silently duplicate an aliased object under a colliding \"$id\" and "
            + "ReferenceHandler.IgnoreCycles would not recognize a cycle through this type at all. "
            + "Serialize or deserialize a value containing Optional<String> with "
            + "JsonSerializerOptions.ReferenceHandler left null.");
    }

    [Fact]
    public void Optional_writes_the_case_discriminator_when_the_static_type_is_the_concrete_Some_arm()
    {
        Optional<string>.Some value = new("c");

        var json = JsonSerializer.Serialize(value, Registered);

        json.Should().Be(
            """{"$case":"Some","Value":"c","HasValue":true}""",
            "a caller holding the concrete arm type — not the declared-abstract Optional<string> — "
            + "must still get the discriminated shape once OptionalJsonConverterFactory is "
            + "registered: its [JsonConverter] attribute lives on Optional<T> and, unlike its "
            + "CanConvert, is not inherited by JsonSerializer's converter resolution when the arm "
            + "type itself is what gets looked up, so only AddDamlConverters's explicit "
            + "registration — not the attribute — reaches this arm-typed lookup");
    }

    [Fact]
    public void Optional_writes_the_case_discriminator_when_the_static_type_is_the_concrete_None_arm()
    {
        Optional<string>.None value = new();

        var json = JsonSerializer.Serialize(value, Registered);

        json.Should().Be("""{"$case":"None","HasValue":false}""");
    }

    [Fact]
    public void Optional_arm_typed_write_reads_back_through_the_base_type()
    {
        Optional<string>.Some value = new("c");

        var written = JsonSerializer.Serialize(value, Registered);

        JsonSerializer.Deserialize<Optional<string>>(written, Registered).Should().Be(value);
    }

    [Fact]
    public void Optional_arm_typed_write_on_bare_options_falls_back_to_the_plain_reflection_shape()
    {
        Optional<string>.Some value = new("c");

        var json = JsonSerializer.Serialize(value);

        json.Should().Be(
            """{"Value":"c","HasValue":true}""",
            "OptionalJsonConverterFactory is reached for an arm-typed lookup only through "
            + "JsonSerializerOptions.Converters, e.g. via AddDamlConverters, because "
            + "[JsonConverter] on Optional<T> is not inherited by Optional<string>.Some; bare "
            + "options carry neither the attribute on the arm nor the registration, so the arm "
            + "keeps writing its own plain members with no \"$case\", exactly as it did before "
            + "this factory started matching arm types at all");
    }

    [Fact]
    public void Optional_round_trips_inside_a_Dictionary_keyed_by_string()
    {
        Dictionary<string, Optional<int>> value = new()
        {
            ["a"] = new Optional<int>.Some(1),
            ["b"] = new Optional<int>.None(),
        };

        var json = JsonSerializer.Serialize(value);

        using (var document = JsonDocument.Parse(json))
        {
            document.RootElement.GetProperty("a").GetProperty("$case").GetString().Should().Be("Some");
            document.RootElement.GetProperty("a").GetProperty("Value").GetInt32().Should().Be(1);
            document.RootElement.GetProperty("b").GetProperty("$case").GetString().Should().Be("None");
        }
        JsonSerializer.Deserialize<Dictionary<string, Optional<int>>>(json).Should().BeEquivalentTo(value);
    }
}
