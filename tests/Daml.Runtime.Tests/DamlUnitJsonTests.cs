// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="DamlUnit"/>: an empty JSON
/// object that reads back as <see cref="DamlUnit.Instance"/>, bare or as the result of a
/// unit-returning choice's <see cref="ExerciseOutcome{T}"/>. Without its converter the private
/// constructor leaves the reflection-based deserializer nothing to call.
/// </summary>
public class DamlUnitJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private sealed record Wrapper(string Name, DamlUnit Outcome);

    private sealed record TwoUnits(DamlUnit A, DamlUnit B);

    public static IEnumerable<object[]> ReferenceHandlers =>
    [
        [ReferenceHandler.Preserve],
        [ReferenceHandler.IgnoreCycles],
    ];

    [Fact]
    public void ExerciseOutcome_One_of_DamlUnit_writes_its_result_as_the_empty_object()
    {
        var json = JsonSerializer.Serialize(new ExerciseOutcome<DamlUnit>.One(DamlUnit.Instance));

        json.Should().Be("""{"Result":{}}""");
    }

    [Fact]
    public void ExerciseOutcome_One_of_DamlUnit_round_trips_on_options_that_register_nothing()
    {
        var outcome = new ExerciseOutcome<DamlUnit>.One(DamlUnit.Instance);

        var written = JsonSerializer.Serialize(outcome);

        JsonSerializer.Deserialize<ExerciseOutcome<DamlUnit>.One>(written).Should().Be(outcome);
    }

    [Fact]
    public void ExerciseOutcome_One_of_DamlUnit_round_trips_under_AddDamlConverters()
    {
        var outcome = new ExerciseOutcome<DamlUnit>.One(DamlUnit.Instance);

        var written = JsonSerializer.Serialize(outcome, Registered);

        JsonSerializer.Deserialize<ExerciseOutcome<DamlUnit>.One>(written, Registered).Should().Be(outcome);
    }

    [Fact]
    public void DamlUnit_writes_the_empty_object()
    {
        JsonSerializer.Serialize(DamlUnit.Instance).Should().Be("{}");
    }

    [Fact]
    public void DamlUnit_reads_the_empty_object_literal()
    {
        JsonSerializer.Deserialize<DamlUnit>("{}").Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void DamlUnit_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var wrapper = new Wrapper("alice", DamlUnit.Instance);

        var written = JsonSerializer.Serialize(wrapper);

        JsonSerializer.Deserialize<Wrapper>(written).Should().Be(wrapper);
    }

    [Fact]
    public void DamlUnit_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var wrapper = new Wrapper("alice", DamlUnit.Instance);

        var written = JsonSerializer.Serialize(wrapper, Registered);

        JsonSerializer.Deserialize<Wrapper>(written, Registered).Should().Be(wrapper);
    }

    [Fact]
    public void DamlUnit_refuses_a_payload_that_is_not_an_object()
    {
        var act = () => JsonSerializer.Deserialize<DamlUnit>("\"c\"");

        act.Should().Throw<JsonException>().WithMessage("*Expected a JSON object for DamlUnit*");
    }

    [Fact]
    public void DamlUnit_ignores_members_a_forward_compatible_producer_might_add()
    {
        JsonSerializer.Deserialize<DamlUnit>("""{"future":"field"}""").Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void DamlUnit_writes_the_empty_object_even_under_a_property_naming_policy()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var json = JsonSerializer.Serialize(DamlUnit.Instance, options);

        json.Should().Be("{}", "DamlUnit writes no property for a naming policy to rename");
        JsonSerializer.Deserialize<DamlUnit>(json, options).Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void DamlUnit_ignores_JsonUnmappedMemberHandling_Disallow_because_its_converter_bypasses_the_reflection_contract()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

        var written = JsonSerializer.Serialize(DamlUnit.Instance, options);

        written.Should().Be("{}", "DamlUnit's own write declares no member, so Disallow has nothing to reject");
        JsonSerializer.Deserialize<DamlUnit>("""{"future":"field"}""", options).Should().Be(
            DamlUnit.Instance,
            "the converter's Read walks and skips every member itself rather than deserializing "
            + "through System.Text.Json's reflection contract, so UnmappedMemberHandling never reaches it");
    }

    [Theory]
    [MemberData(nameof(ReferenceHandlers))]
    public void DamlUnit_round_trips_under_every_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };

        var written = JsonSerializer.Serialize(DamlUnit.Instance, options);

        written.Should().Be(
            "{}", "the converter's Write never makes a nested JsonSerializer call, so there is no "
            + "independent reference resolver for a ReferenceHandler to disrupt");
        JsonSerializer.Deserialize<DamlUnit>(written, options).Should().Be(DamlUnit.Instance);
    }

    [Theory]
    [MemberData(nameof(ReferenceHandlers))]
    public void DamlUnit_round_trips_two_aliased_references_under_every_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };
        var wrapper = new TwoUnits(DamlUnit.Instance, DamlUnit.Instance);

        var written = JsonSerializer.Serialize(wrapper, options);

        JsonSerializer.Deserialize<TwoUnits>(written, options).Should().Be(
            wrapper,
            "two references to the same DamlUnit.Instance singleton are exactly the aliasing shape "
            + "ReferenceHandler.Preserve exists to track");
    }
}
