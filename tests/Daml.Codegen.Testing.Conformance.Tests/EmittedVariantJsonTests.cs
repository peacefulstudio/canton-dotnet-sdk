// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using System.Text.Json.Serialization;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// System.Text.Json writes against the declared type and ignores a <c>[JsonConverter]</c> on an
/// interface, so a generated variant declared as its abstract base neither wrote its arm's payload
/// nor read back at all. These run against the corpus's own generated variants, so they
/// pin what the emitter writes, on bare options with no <c>AddDamlConverters()</c>.
/// </summary>
public class EmittedVariantJsonTests
{
    private const string WinJson = """{"$case":"Win","Value":{"Prize":1.5,"Tier":"gold"},"Tag":"Win"}""";
    private const string PendingJson = """{"$case":"Pending","Tag":"Pending"}""";

    private sealed record OutcomeHolder(Outcome Outcome);

    [Fact]
    public void Emitted_variant_writes_a_payload_case_as_its_discriminated_arm()
    {
        Outcome outcome = new Outcome.Win(new Outcome_Win(Prize: 1.5m, Tier: "gold"));

        JsonSerializer.Serialize(outcome).Should().Be(WinJson);
    }

    [Fact]
    public void Emitted_variant_reads_a_payload_case_back_as_its_arm()
    {
        var outcome = JsonSerializer.Deserialize<Outcome>(WinJson);

        outcome.Should().Be(new Outcome.Win(new Outcome_Win(Prize: 1.5m, Tier: "gold")));
    }

    [Fact]
    public void Emitted_variant_writes_a_nullary_case_as_its_discriminated_arm()
    {
        Outcome outcome = new Outcome.Pending();

        JsonSerializer.Serialize(outcome).Should().Be(PendingJson);
    }

    [Fact]
    public void Emitted_variant_reads_a_nullary_case_back_as_its_arm()
    {
        var outcome = JsonSerializer.Deserialize<Outcome>(PendingJson);

        outcome.Should().Be(new Outcome.Pending());
    }

    [Fact]
    public void Emitted_generic_variant_round_trips_a_payload_case()
    {
        const string json = """{"$case":"Filled","Value":7,"Tag":"Filled"}""";
        Slot<long> slot = new Slot<long>.Filled(7);

        JsonSerializer.Serialize(slot).Should().Be(json);
        JsonSerializer.Deserialize<Slot<long>>(json).Should().Be(slot);
    }

    [Fact]
    public void Emitted_generic_variant_round_trips_a_nullary_case()
    {
        const string json = """{"$case":"Vacant","Tag":"Vacant"}""";
        Slot<long> slot = new Slot<long>.Vacant();

        JsonSerializer.Serialize(slot).Should().Be(json);
        JsonSerializer.Deserialize<Slot<long>>(json).Should().Be(slot);
    }

    [Fact]
    public void Emitted_generic_variant_round_trips_a_DamlUnit_payload_on_bare_options()
    {
        const string json = """{"$case":"Filled","Value":{},"Tag":"Filled"}""";
        Slot<DamlUnit> slot = new Slot<DamlUnit>.Filled(DamlUnit.Instance);

        JsonSerializer.Serialize(slot).Should().Be(json);
        JsonSerializer.Deserialize<Slot<DamlUnit>>(json).Should().Be(slot);
    }

    [Fact]
    public void Emitted_generic_variant_round_trips_a_DamlUnit_payload_under_AddDamlConverters()
    {
        const string json = """{"$case":"Filled","Value":{},"Tag":"Filled"}""";
        var options = new JsonSerializerOptions().AddDamlConverters();
        Slot<DamlUnit> slot = new Slot<DamlUnit>.Filled(DamlUnit.Instance);

        JsonSerializer.Serialize(slot, options).Should().Be(json);
        JsonSerializer.Deserialize<Slot<DamlUnit>>(json, options).Should().Be(slot);
    }

    [Fact]
    public void Emitted_variant_arm_carrying_DamlUnit_writes_its_discriminated_arm_with_AddDamlConverters()
    {
        const string json = """{"$case":"Filled","Value":{},"Tag":"Filled"}""";
        var options = new JsonSerializerOptions().AddDamlConverters();
        var filled = new Slot<DamlUnit>.Filled(DamlUnit.Instance);

        JsonSerializer.Serialize(filled, options).Should().Be(json);
        JsonSerializer.Deserialize<Slot<DamlUnit>.Filled>(json, options).Should().Be(filled);
    }

    [Fact]
    public void Emitted_variant_round_trips_as_a_field_of_a_dto()
    {
        const string json = """{"Outcome":{"$case":"Pending","Tag":"Pending"}}""";
        var holder = new OutcomeHolder(new Outcome.Pending());

        JsonSerializer.Serialize(holder).Should().Be(json);
        JsonSerializer.Deserialize<OutcomeHolder>(json).Should().Be(holder);
    }

    [Fact]
    public void Emitted_variant_arm_typed_variable_writes_its_discriminated_arm_with_AddDamlConverters()
    {
        var options = new JsonSerializerOptions().AddDamlConverters();

        JsonSerializer.Serialize(new Outcome.Pending(), options).Should().Be(PendingJson);
    }

    [Fact]
    public void Emitted_variant_read_rejects_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<Outcome>("""{"$case":"Lose","Tag":"Lose"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("*Outcome names an unknown case \"Lose\"; expected one of Win, Pending*");
    }

    [Fact]
    public void Emitted_variant_write_refuses_a_reference_handler()
    {
        var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };

        var act = () => JsonSerializer.Serialize<Outcome>(new Outcome.Pending(), options);

        act.Should().Throw<JsonException>().WithMessage("Reference handling is not supported for Outcome*");
    }

    [Fact]
    public void Emitted_variant_read_refuses_a_reference_handler()
    {
        var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles };

        var act = () => JsonSerializer.Deserialize<Outcome>(PendingJson, options);

        act.Should().Throw<JsonException>().WithMessage("Reference handling is not supported for Outcome*");
    }
}
