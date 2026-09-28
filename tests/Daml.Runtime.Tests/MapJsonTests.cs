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
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="Map{TKey, TValue}"/>: the object
/// <see cref="System.Text.Json"/> derives from its members, read back as the map that wrote it. The
/// contract is a CLR round trip, not the Daml-LF record <c>{"map":[["a",1]]}</c> the ledger
/// encoding uses. What <see cref="DamlJsonConverters.AddDamlConverters"/> adds on top
/// is requiredness: a member the payload omits is refused rather than bound to a null the slot
/// forbids.
/// </summary>
public class MapJsonTests
{
    private static readonly JsonSerializerOptions Registered =
        new JsonSerializerOptions().AddDamlConverters();

    private static readonly JsonSerializerOptions HostBuiltWithoutDamlConverters = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions IgnoringReadOnlyProperties =
        new() { IgnoreReadOnlyProperties = true };

    private static readonly JsonSerializerOptions WritingNumbersAsStrings =
        new() { NumberHandling = JsonNumberHandling.WriteAsString };

    private static readonly JsonSerializerOptions IgnoringDefaultsOnWrite =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    private static readonly JsonSerializerOptions WithASentinelEntriesConverter =
        new() { Converters = { new SentinelEntriesConverter() } };

    private static Map<string, long> Balances =>
        new([new("alice", 10L), new("bob", 20L)]);

    private sealed record Book(string Name, Map<string, long> Balances);

    private sealed class SentinelEntriesConverter : JsonConverter<IReadOnlyList<KeyValuePair<string, long>>>
    {
        public override IReadOnlyList<KeyValuePair<string, long>> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return [new("sentinel", 999L)];
        }

        public override void Write(
            Utf8JsonWriter writer, IReadOnlyList<KeyValuePair<string, long>> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToList(), options);
    }

    private sealed record OptionalBook(string Name, Map<string, long>? Balances);

    private sealed record TextLedger(string Name, Map<string, string> Balances);

    /// <summary>
    /// <see cref="Map{TKey, TValue}"/> carries a custom
    /// <see cref="System.Text.Json.Serialization.JsonConverter{T}"/>, <see cref="MapJsonConverter{TKey, TValue}"/>,
    /// mirroring <see cref="SetJsonConverter{T}"/>: it reads the <c>Entries</c> member itself and
    /// translates the <see cref="ArgumentException"/> the shared
    /// <c>EventCollections.CopyRejectingNullKeysOrValues</c> path throws for a null key or value
    /// into a <see cref="JsonException"/> naming the offending index, on both bare options and
    /// under <see cref="DamlJsonConverters.AddDamlConverters"/>.
    /// </summary>
    [Fact]
    public void Map_refuses_a_null_entry_value_reached_through_deserialization_on_bare_options()
    {
        var act = () => JsonSerializer.Deserialize<TextLedger>(
            """{"Name":"ledger","Balances":{"Entries":[{"Key":"alice","Value":null}]}}""",
            HostBuiltWithoutDamlConverters);

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid entry in Map<String, String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Entry 0 has a null value*a Map<String, String> holds no null values*");
    }

    [Fact]
    public void Map_refuses_a_null_entry_value_reached_through_deserialization_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<TextLedger>(
            """{"Name":"ledger","Balances":{"Entries":[{"Key":"alice","Value":null}]}}""",
            Registered);

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid entry in Map<String, String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Entry 0 has a null value*a Map<String, String> holds no null values*");
    }

    [Fact]
    public void Map_refuses_a_null_entry_key_reached_through_deserialization_on_bare_options()
    {
        var act = () => JsonSerializer.Deserialize<TextLedger>(
            """{"Name":"ledger","Balances":{"Entries":[{"Key":null,"Value":"alice"}]}}""",
            HostBuiltWithoutDamlConverters);

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid entry in Map<String, String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Entry 0 has a null key*a Map<String, String> holds no null keys*");
    }

    [Fact]
    public void Map_refuses_a_null_entry_key_reached_through_deserialization_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<TextLedger>(
            """{"Name":"ledger","Balances":{"Entries":[{"Key":null,"Value":"alice"}]}}""",
            Registered);

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid entry in Map<String, String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Entry 0 has a null key*a Map<String, String> holds no null keys*");
    }

    [Fact]
    public void Map_writes_the_literal_JSON_object_of_its_entries()
    {
        var json = JsonSerializer.Serialize(Balances);

        json.Should().Be(
            """{"Entries":[{"Key":"alice","Value":10},{"Key":"bob","Value":20}],"Count":2}""");
    }

    [Fact]
    public void Map_round_trips_under_a_camelCase_naming_policy_without_case_insensitive_reads()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var written = JsonSerializer.Serialize(Balances, options);

        written.Should().Be(
            """{"entries":[{"key":"alice","value":10},{"key":"bob","value":20}],"count":2}""",
            "the previous reflection-based contract wrote and accepted whatever "
            + "PropertyNamingPolicy the caller configured, and the converter must not regress a "
            + "consumer who upgrades into a fixed PascalCase wire shape");
        JsonSerializer.Deserialize<Map<string, long>>(written, options).Should().Be(Balances);
    }

    [Fact]
    public void Map_rejects_an_unmapped_member_when_unmapped_members_are_disallowed()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

        var act = () => JsonSerializer.Deserialize<Map<string, long>>(
            """{"Entries":[{"Key":"alice","Value":10}],"Bogus":1}""", options);

        act.Should().Throw<JsonException>().WithMessage(
            "*Bogus*",
            "the previous reflection-based reader rejected an unmapped member under this setting, "
            + "and a payload with a misspelled or unexpected field must not silently succeed");
    }

    [Fact]
    public void Map_omits_the_read_only_Count_member_when_read_only_properties_are_ignored()
    {
        var json = JsonSerializer.Serialize(Balances, IgnoringReadOnlyProperties);

        json.Should().Be(
            """{"Entries":[{},{}]}""",
            "the previous reflection-based writer omitted the read-only Count, and propagated the "
            + "same option into each read-only KeyValuePair<TKey, TValue> entry of Entries, so a "
            + "consumer suppressing derived properties for a strict downstream schema must keep "
            + "getting that shape, KeyValuePair's own read-only Key and Value included");
    }

    [Fact]
    public void Map_omits_the_read_only_Count_member_when_it_equals_the_default_under_WhenWritingDefault()
    {
        var empty = new Map<string, long>([]);

        var json = JsonSerializer.Serialize(empty, IgnoringDefaultsOnWrite);

        json.Should().Be(
            """{"Entries":[]}""",
            "the previous reflection-based writer omitted the read-only Count under this setting "
            + "when it equalled its default of 0, and a direct write bypassing "
            + "DefaultIgnoreCondition changed that shape for a consumer suppressing default-valued "
            + "properties");
    }

    [Fact]
    public void Map_writes_a_nonzero_Count_under_WhenWritingDefault()
    {
        var json = JsonSerializer.Serialize(Balances, IgnoringDefaultsOnWrite);

        json.Should().Be(
            """{"Entries":[{"Key":"alice","Value":10},{"Key":"bob","Value":20}],"Count":2}""");
    }

    [Fact]
    public void Map_round_trips_its_own_default_output_when_unmapped_members_are_disallowed()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

        var written = JsonSerializer.Serialize(Balances, options);
        var act = () => JsonSerializer.Deserialize<Map<string, long>>(written, options);

        act.Should().NotThrow(
            "Write emits the read-only Count alongside Entries by default, and the previous "
            + "reflection reader recognized Count as a declared member rather than an unmapped one, "
            + "so a converter's own default output must still round-trip under Disallow");
    }

    [Fact]
    public void Map_writes_Count_through_the_configured_number_handling()
    {
        var json = JsonSerializer.Serialize(Balances, WritingNumbersAsStrings);

        json.Should().Be(
            """{"Entries":[{"Key":"alice","Value":"10"},{"Key":"bob","Value":"20"}],"Count":"2"}""",
            "the previous reflection-based writer emitted every numeric property, Count included, "
            + "through the configured NumberHandling, and a direct WriteNumber call bypasses that "
            + "contract for a consumer who writes numbers as strings");
    }

    [Fact]
    public void Map_reads_Entries_through_a_converter_registered_for_its_declared_interface_type()
    {
        var map = JsonSerializer.Deserialize<Map<string, long>>(
            """{"Entries":[{"Key":"alice","Value":10}]}""", WithASentinelEntriesConverter);

        map!.Entries.Should().Equal(
            [new("sentinel", 999L)],
            "Write already serializes Entries through its declared "
            + "IReadOnlyList<KeyValuePair<TKey, TValue>> type, so Read deserializing through the "
            + "concrete List<KeyValuePair<TKey, TValue>> instead bypassed a converter a caller "
            + "registered for the declared type");
    }

    [Fact]
    public void Map_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var book = new Book("ledger", Balances);

        var written = JsonSerializer.Serialize(book, Registered);

        JsonSerializer.Deserialize<Book>(written, Registered).Should().Be(book);
    }

    [Fact]
    public void Map_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var book = new Book("ledger", Balances);

        var written = JsonSerializer.Serialize(book, HostBuiltWithoutDamlConverters);

        JsonSerializer.Deserialize<Book>(written, HostBuiltWithoutDamlConverters).Should().Be(book);
    }

    [Fact]
    public void Map_refuses_a_payload_that_omits_a_member_the_slot_forbids_a_null_in_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<Book>("""{"Name":"ledger"}""", Registered);

        act.Should().Throw<JsonException>().WithMessage("*Balances*");
    }

    [Fact]
    public void Map_reads_an_omitted_member_as_the_absent_map_where_the_slot_permits_one()
    {
        var book = JsonSerializer.Deserialize<OptionalBook>("""{"Name":"ledger"}""", Registered);

        book!.Balances.Should().BeNull(
            "codegen renders Optional (Map k v) as a nullable Map<TKey, TValue> slot, so an omitted "
            + "member is the None the payload meant rather than a member it forgot");
    }

    [Fact]
    public void Map_reads_an_omitted_member_as_null_even_where_the_slot_forbids_one_on_options_that_register_nothing()
    {
        var book = JsonSerializer.Deserialize<Book>(
            """{"Name":"ledger"}""", HostBuiltWithoutDamlConverters);

        book!.Balances.Should().BeNull(
            "the modifier that marks the member required rides on AddDamlConverters, and options "
            + "that register nothing never install it");
    }
}
