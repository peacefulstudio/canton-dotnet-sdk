// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="Set{T}"/>: a bare JSON array
/// that reads back as the set that wrote it. The contract is a CLR round trip, not the Daml-LF
/// record <c>{"map":[["alice",{}]]}</c> the ledger encoding uses, so what the
/// tests assert is that a value survives the trip, on the bare options a host builds for itself as
/// much as on <see cref="DamlJsonConverters.AddDamlConverters"/>.
/// </summary>
public class SetJsonTests
{
    private static readonly Party Alice = new("Alice::122012ab");

    private static readonly Party Bob = new("Bob::122034cd");

    private static readonly JsonSerializerOptions Registered =
        new JsonSerializerOptions().AddDamlConverters();

    private static readonly JsonSerializerOptions HostBuiltWithoutDamlConverters = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Vault(string Name, Set<Party> Keyholders);

    private sealed record OptionalVault(string Name, Set<Party>? Keyholders);

    private sealed record Unconvertible(bool Fails);

    private sealed class ThrowingElementConverter : JsonConverter<Unconvertible>
    {
        public override Unconvertible Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new InvalidOperationException("the element converter refused to read");

        public override void Write(Utf8JsonWriter writer, Unconvertible value, JsonSerializerOptions options) =>
            throw new InvalidOperationException("the element converter refused to write");
    }

    [Fact]
    public void Set_reads_its_own_write_back()
    {
        var written = JsonSerializer.Serialize(new Set<Party>([Alice, Bob]));

        JsonSerializer.Deserialize<Set<Party>>(written).Should().Be(new Set<Party>([Alice, Bob]));
    }

    [Fact]
    public void Set_writes_the_bare_array_of_its_elements()
    {
        var json = JsonSerializer.Serialize(new Set<Party>([Alice, Bob]));

        using var document = JsonDocument.Parse(json);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.EnumerateArray().Select(element => element.GetString())
            .Should().BeEquivalentTo([Alice.Value, Bob.Value]);
    }

    [Fact]
    public void Set_round_trips_an_empty_set_through_the_empty_array()
    {
        var written = JsonSerializer.Serialize(new Set<Party>([]));

        written.Should().Be("[]");
        JsonSerializer.Deserialize<Set<Party>>(written)!.Count.Should().Be(0);
    }

    [Fact]
    public void Set_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var vault = new Vault("cold", new Set<Party>([Alice, Bob]));

        var written = JsonSerializer.Serialize(vault, HostBuiltWithoutDamlConverters);

        JsonSerializer.Deserialize<Vault>(written, HostBuiltWithoutDamlConverters).Should().Be(vault);
    }

    [Fact]
    public void Set_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var vault = new Vault("cold", new Set<Party>([Alice, Bob]));

        var written = JsonSerializer.Serialize(vault, Registered);

        JsonSerializer.Deserialize<Vault>(written, Registered).Should().Be(vault);
    }

    [Fact]
    public void Set_writes_its_elements_in_order_of_first_occurrence()
    {
        var json = JsonSerializer.Serialize(new Set<string>(["charlie", "alice", "bob", "alice"]));

        json.Should().Be("""["charlie","alice","bob"]""",
            "the payload has to be reproducible for a golden file or a content hash to be stable, "
            + "and enumeration order of the backing set is the order elements were first added; "
            + "swapping that backing store for one that reorders — a frozen set, say — would break "
            + "this and nothing else would notice");
    }

    [Fact]
    public void Set_reads_its_elements_through_the_callers_own_options()
    {
        var numbers = JsonSerializer.Deserialize<Set<int>>("""["1","2"]""", HostBuiltWithoutDamlConverters);

        numbers!.Elements.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void Set_collapses_a_duplicate_element_on_read()
    {
        var set = JsonSerializer.Deserialize<Set<string>>("""["a","a"]""");

        set!.Count.Should().Be(1);
    }

    [Fact]
    public void Set_names_a_nested_generic_element_type_with_its_own_argument()
    {
        var act = () => JsonSerializer.Deserialize<Set<EquatableArray<Party>>>("\"alice\"");

        act.Should().Throw<JsonException>()
            .WithMessage("*Expected array token for Set<EquatableArray<Party>>*");
    }

    [Fact]
    public void Set_refuses_a_null_element()
    {
        var act = () => JsonSerializer.Deserialize<Set<string>>("""["alice",null]""");

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid element in Set<String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Element 1 is null*a Set<String> holds no null elements*");
    }

    [Fact]
    public void Constructor_refuses_a_null_element_so_Set_never_holds_one_to_write()
    {
        var act = () => new Set<string>([null!]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Element 0 is null*a Set<String> holds no null elements*")
            .WithParameterName("elements");
    }

    [Fact]
    public void Set_refuses_a_payload_that_omits_a_member_the_slot_forbids_a_null_in_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<Vault>("""{"Name":"cold"}""", Registered);

        act.Should().Throw<JsonException>().WithMessage("*Keyholders*");
    }

    [Fact]
    public void Set_reads_an_omitted_member_as_the_absent_set_where_the_slot_permits_one()
    {
        var vault = JsonSerializer.Deserialize<OptionalVault>("""{"Name":"cold"}""", Registered);

        vault!.Keyholders.Should().BeNull(
            "codegen renders Optional (Set a) as a nullable Set<T> slot, so an omitted member is the "
            + "None the payload meant rather than a member it forgot");
    }

    [Fact]
    public void Set_reads_an_omitted_member_as_null_even_where_the_slot_forbids_one_on_options_that_register_nothing()
    {
        var vault = JsonSerializer.Deserialize<Vault>(
            """{"Name":"cold"}""", HostBuiltWithoutDamlConverters);

        vault!.Keyholders.Should().BeNull();
    }

    [Fact]
    public void Set_refuses_a_payload_that_is_not_an_array()
    {
        var act = () => JsonSerializer.Deserialize<Set<Party>>("""{"Elements":["alice"],"Count":1}""");

        act.Should().Throw<JsonException>().WithMessage("*Expected array token for Set<Party>*");
    }

    [Fact]
    public void Set_reads_a_null_as_the_absent_set_where_the_slot_permits_one()
    {
        var vault = JsonSerializer.Deserialize<OptionalVault>("""{"Name":"cold","Keyholders":null}""", Registered);

        vault!.Keyholders.Should().BeNull();
    }

    [Fact]
    public void Set_refuses_a_null_where_the_slot_forbids_one()
    {
        var act = () => JsonSerializer.Deserialize<Vault>("""{"Name":"cold","Keyholders":null}""", Registered);

        act.Should().Throw<JsonException>()
            .WithMessage("*constructor parameter 'Keyholders'*doesn't allow null values*");
    }

    [Fact]
    public void Set_reports_the_element_that_could_not_be_read()
    {
        var options = new JsonSerializerOptions { Converters = { new ThrowingElementConverter() } };

        var act = () => JsonSerializer.Deserialize<Set<Unconvertible>>("""[{"Fails":true}]""", options);

        act.Should().Throw<JsonException>()
            .WithMessage("*element 0 of Set<SetJsonTests.Unconvertible>*")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*refused to read*");
    }

    [Fact]
    public void Set_reports_the_element_that_could_not_be_written()
    {
        var options = new JsonSerializerOptions { Converters = { new ThrowingElementConverter() } };

        var act = () => JsonSerializer.Serialize(new Set<Unconvertible>([new(true)]), options);

        act.Should().Throw<JsonException>()
            .WithMessage("*element 0 of Set<SetJsonTests.Unconvertible>*")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*refused to write*");
    }
}
