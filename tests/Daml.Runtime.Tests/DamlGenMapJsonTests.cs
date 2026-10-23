// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for a <see cref="DamlGenMap"/> declared as itself:
/// its entries as an array of <c>Key</c>/<c>Value</c> objects, each side a <see cref="DamlValue"/>
/// carrying its own <c>"$case"</c>, so every entry reads back as the pair it was.
/// </summary>
public class DamlGenMapJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions StrictPreset = new(JsonSerializerOptions.Strict);

    private static readonly JsonSerializerOptions Preserving = new() { ReferenceHandler = ReferenceHandler.Preserve };

    private static readonly JsonSerializerOptions Unlimited = new() { MaxDepth = 1000 };

    private const string TwoEntriesJson =
        """{"Entries":[{"Key":{"$case":"DamlParty","Value":"Alice"},"Value":{"$case":"DamlInt64","Value":1}},{"Key":{"$case":"DamlText","Value":"Alice"},"Value":{"$case":"DamlInt64","Value":2}}]}""";

    private static DamlGenMap TwoEntries =>
        DamlGenMap.Create((new DamlParty("Alice"), new DamlInt64(1)), (new DamlText("Alice"), new DamlInt64(2)));

    private sealed record Holder(DamlGenMap Map);

    [Fact]
    public void DamlGenMap_declared_as_itself_is_written_as_key_and_value_entries()
    {
        JsonSerializer.Serialize(TwoEntries).Should().Be(TwoEntriesJson);
    }

    [Fact]
    public void DamlGenMap_with_no_entries_is_written_as_an_empty_entry_array()
    {
        JsonSerializer.Serialize(DamlGenMap.Create()).Should().Be("""{"Entries":[]}""");
    }

    [Fact]
    public void DamlGenMap_reads_back_equal_and_hash_equal_with_every_entry_in_order_on_default_options()
    {
        var read = JsonSerializer.Deserialize<DamlGenMap>(TwoEntriesJson)!;

        read.Should().Be(TwoEntries);
        read.GetHashCode().Should().Be(TwoEntries.GetHashCode());
        read.Entries.Should().Equal(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlText("Alice"), new DamlInt64(2)));
    }

    [Fact]
    public void DamlGenMap_reads_back_equal_on_AddDamlConverters_options()
    {
        var json = JsonSerializer.Serialize(TwoEntries, Registered);

        json.Should().Be(TwoEntriesJson);
        JsonSerializer.Deserialize<DamlGenMap>(json, Registered).Should().Be(TwoEntries);
    }

    [Fact]
    public void DamlGenMap_member_reads_back_through_its_holder()
    {
        var holder = new Holder(TwoEntries);

        JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(holder)).Should().Be(holder);
    }

    [Fact]
    public void DamlGenMap_round_trips_under_a_camel_case_naming_policy()
    {
        var json = JsonSerializer.Serialize(DamlGenMap.Create((new DamlInt64(1), new DamlText("one"))), CamelCase);

        json.Should().Be("""{"entries":[{"key":{"$case":"DamlInt64","value":1},"value":{"$case":"DamlText","value":"one"}}]}""");
        JsonSerializer.Deserialize<DamlGenMap>(json, CamelCase).Should().Be(DamlGenMap.Create((new DamlInt64(1), new DamlText("one"))));
    }

    [Fact]
    public void DamlGenMap_with_a_duplicate_key_is_refused_with_a_JsonException()
    {
        const string json =
            """{"Entries":[{"Key":{"$case":"DamlInt64","Value":1},"Value":{"$case":"DamlText","Value":"a"}},{"Key":{"$case":"DamlInt64","Value":1},"Value":{"$case":"DamlText","Value":"b"}}]}""";

        var read = () => JsonSerializer.Deserialize<DamlGenMap>(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain("duplicate key");
    }

    [Fact]
    public void DamlGenMap_keys_that_differ_only_in_their_arm_are_distinct()
    {
        var read = JsonSerializer.Deserialize<DamlGenMap>(TwoEntriesJson)!;

        read.Count.Should().Be(2);
        read.Entries[0].Key.Should().BeOfType<DamlParty>();
        read.Entries[1].Key.Should().BeOfType<DamlText>();
    }

    [Theory]
    [InlineData("""{}""", "\"Entries\"")]
    [InlineData("""{"Entries":null}""", "\"Entries\"")]
    [InlineData("""{"Entries":[{"Key":{"$case":"DamlInt64","Value":1}}]}""", "\"Value\"")]
    [InlineData("""{"Entries":[null]}""", "null element")]
    [InlineData("""{"Entries":[{"Key":{"$case":"Nope"},"Value":{"$case":"DamlUnit"}}]}""", "\"Nope\"")]
    [InlineData("""[]""", "StartArray")]
    public void DamlGenMap_malformed_payload_is_refused_with_a_JsonException_naming_the_problem(string json, string namedInMessage)
    {
        var read = () => JsonSerializer.Deserialize<DamlGenMap>(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain(namedInMessage);
    }

    [Fact]
    public void DamlGenMap_case_member_is_unmapped_when_declared_as_itself()
    {
        const string json = """{"$case":"DamlGenMap","Entries":[]}""";

        JsonSerializer.Deserialize<DamlGenMap>(json).Should().Be(DamlGenMap.Create());
        var strict = () => JsonSerializer.Deserialize<DamlGenMap>(json, Strict);
        strict.Should().Throw<JsonException>().Which.Message.Should().Contain("\"$case\"");
    }

    [Fact]
    public void DamlGenMap_round_trips_on_the_strict_preset()
    {
        JsonSerializer.Deserialize<DamlGenMap>(JsonSerializer.Serialize(TwoEntries, StrictPreset), StrictPreset).Should().Be(TwoEntries);
    }

    [Fact]
    public void DamlGenMap_refuses_a_ReferenceHandler()
    {
        var write = () => JsonSerializer.Serialize(TwoEntries, Preserving);
        var read = () => JsonSerializer.Deserialize<DamlGenMap>(TwoEntriesJson, Preserving);

        write.Should().Throw<JsonException>();
        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void DamlGenMap_nested_twenty_one_deep_is_the_deepest_default_options_read()
    {
        JsonSerializer.Deserialize<DamlGenMap>(NestedJson(21, """{"$case":"DamlUnit"}""")).Should().Be(Nested(21));
        var deeper = () => JsonSerializer.Deserialize<DamlGenMap>(NestedJson(22, """{"$case":"DamlUnit"}"""));
        deeper.Should().Throw<JsonException>();
    }

    [Fact]
    public void DamlGenMap_nested_past_MaxDepth_is_refused_with_a_JsonException_on_write()
    {
        var write = () => JsonSerializer.Serialize(Nested(22));

        write.Should().Throw<JsonException>();
    }

    [Fact]
    public void DamlGenMap_at_the_deepest_default_nesting_reads_and_writes_on_a_256_KB_stack()
    {
        var json = NestedJson(21, """{"$case":"DamlUnit"}""");
        DamlGenMap? read = null;
        string? written = null;

        var thrown = StackProbe.OnStackOf(256, () =>
        {
            read = JsonSerializer.Deserialize<DamlGenMap>(json);
            written = JsonSerializer.Serialize(Nested(21));
        });

        thrown.Should().BeNull();
        read.Should().Be(Nested(21));
        written.Should().Be(json);
    }

    [Fact]
    public void DamlGenMap_with_a_bad_case_at_the_deepest_default_nesting_is_refused_with_a_JsonException_on_a_256_KB_stack()
    {
        var json = NestedJson(21, """{"$case":"Nope"}""");

        var thrown = StackProbe.OnStackOf(256, () => JsonSerializer.Deserialize<DamlGenMap>(json));

        thrown.Should().BeOfType<JsonException>().Which.Message.Should().Contain("\"Nope\"");
    }

    [Fact]
    public void DamlGenMap_three_hundred_deep_round_trips_on_a_one_megabyte_stack_when_MaxDepth_is_raised()
    {
        DamlGenMap? read = null;

        var thrown = StackProbe.OnStackOf(1024, () =>
            read = JsonSerializer.Deserialize<DamlGenMap>(JsonSerializer.Serialize(Nested(300), Unlimited), Unlimited));

        thrown.Should().BeNull();
        read.Should().Be(Nested(300));
    }

    private static DamlGenMap Nested(int levels)
    {
        DamlValue value = DamlUnit.Instance;
        for (var level = 1; level < levels; level++)
        {
            value = DamlGenMap.Create((new DamlInt64(level), value));
        }

        return DamlGenMap.Create((new DamlInt64(levels), value));
    }

    private static string NestedJson(int levels, string innermost)
    {
        var json = innermost;
        for (var level = 1; level < levels; level++)
        {
            json = $$"""{"$case":"DamlGenMap","Entries":[{"Key":{"$case":"DamlInt64","Value":{{level}}},"Value":{{json}}}]}""";
        }

        return $$"""{"Entries":[{"Key":{"$case":"DamlInt64","Value":{{levels}}},"Value":{{json}}}]}""";
    }
}
