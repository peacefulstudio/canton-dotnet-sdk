// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for a value declared as <see cref="TreeEvent"/>:
/// the arm's own object with a <c>"$case"</c> discriminator naming the arm first, and an
/// <see cref="TreeEvent.Exercised"/> arm's child events written the same way, level by level.
/// </summary>
public class TreeEventJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions StrictPreset = new(JsonSerializerOptions.Strict);

    private static readonly JsonSerializerOptions Preserving = new() { ReferenceHandler = ReferenceHandler.Preserve };

    private static readonly JsonSerializerOptions IgnoringDefaults = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    private static readonly JsonSerializerOptions Unlimited = new() { MaxDepth = 1000 };

    private static readonly Identifier Iou = new("pkg", "Main", "Iou");

    private const string IouJson = """{"PackageId":"pkg","ModuleName":"Main","EntityName":"Iou","FullyQualifiedName":"Main:Iou"}""";

    private const string CreatedJson =
        """{"$case":"Created","EventId":"1:1","ContractId":"00c0ffee","TemplateId":""" + IouJson
        + ""","CreateArguments":{"RecordId":null,"Fields":[{"Label":"n","Value":{"$case":"DamlInt64","Value":1}}]},"WitnessParties":["Alice"],"Signatories":["Alice"],"Observers":[],"ContractKey":null,"CreatedAt":null,"InterfaceIds":[],"UndecodedCreateArguments":null,"PackageName":null}""";

    private const string ExercisedJson =
        """{"$case":"Exercised","EventId":"1:0","ContractId":"00c0ffee","TemplateId":""" + IouJson
        + ""","InterfaceId":null,"ChoiceName":"Archive","ChoiceArgument":{"$case":"DamlUnit"},"ExerciseResult":{"$case":"DamlText","Value":"done"},"Consuming":true,"ActingParties":["Alice"],"WitnessParties":["Alice"],"ChildEvents":["""
        + CreatedJson + """],"PackageName":"pkg-name"}""";

    private static TreeEvent.Created Created =>
        new("1:1", "00c0ffee", Iou, DamlRecord.Create(DamlField.Create("n", new DamlInt64(1))), Eq(new Party("Alice")), Eq(new Party("Alice")), Eq<Party>());

    private static TreeEvent.Exercised Exercised =>
        new(
            "1:0",
            "00c0ffee",
            Iou,
            null,
            new ChoiceName("Archive"),
            DamlUnit.Instance,
            new DamlText("done"),
            true,
            Eq(new Party("Alice")),
            Eq(new Party("Alice")),
            Eq<TreeEvent>(Created))
        {
            PackageName = "pkg-name",
        };

    private static TreeEvent.Exercised NestedExercised =>
        Exercised with
        {
            EventId = "1:9",
            ChildEvents = Eq<TreeEvent>(Exercised with { EventId = "1:8", ChildEvents = Eq<TreeEvent>(Exercised, Created) }, Created),
        };

    [Fact]
    public void TreeEvent_writes_a_created_arm_with_a_case_discriminator_first()
    {
        JsonSerializer.Serialize<TreeEvent>(Created).Should().Be(CreatedJson);
    }

    [Fact]
    public void TreeEvent_writes_an_exercised_arm_with_its_child_events_discriminated_in_place()
    {
        JsonSerializer.Serialize<TreeEvent>(Exercised).Should().Be(ExercisedJson);
    }

    [Fact]
    public void TreeEvent_reads_each_arm_back_equal_and_hash_equal_on_default_options()
    {
        var created = JsonSerializer.Deserialize<TreeEvent>(CreatedJson)!;
        var exercised = JsonSerializer.Deserialize<TreeEvent>(ExercisedJson)!;

        created.Should().BeOfType<TreeEvent.Created>().And.Be(Created);
        created.GetHashCode().Should().Be(Created.GetHashCode());
        exercised.Should().BeOfType<TreeEvent.Exercised>().And.Be(Exercised);
        exercised.GetHashCode().Should().Be(Exercised.GetHashCode());
    }

    [Fact]
    public void TreeEvent_nested_exercised_children_read_back_equal_on_default_and_AddDamlConverters_options()
    {
        TreeEvent value = NestedExercised;

        JsonSerializer.Deserialize<TreeEvent>(JsonSerializer.Serialize(value)).Should().Be(value);
        JsonSerializer.Deserialize<TreeEvent>(JsonSerializer.Serialize(value, Registered), Registered).Should().Be(value);
    }

    [Fact]
    public void TreeEvent_nested_children_keep_their_arm_and_order()
    {
        var read = (TreeEvent.Exercised)JsonSerializer.Deserialize<TreeEvent>(JsonSerializer.Serialize<TreeEvent>(NestedExercised))!;

        read.ChildEvents.Select(child => child.GetType()).Should().Equal(typeof(TreeEvent.Exercised), typeof(TreeEvent.Created));
        ((TreeEvent.Exercised)read.ChildEvents[0]).ChildEvents.Select(child => child.GetType())
            .Should().Equal(typeof(TreeEvent.Exercised), typeof(TreeEvent.Created));
        read.DescendantEvents().Select(child => child.GetType().Name).Should().Equal(
            "Exercised", "Exercised", "Created", "Created", "Created");
    }

    [Fact]
    public void TreeEvent_an_exercised_arm_declared_as_itself_keeps_its_own_shape_and_discriminates_its_children()
    {
        var json = JsonSerializer.Serialize(Exercised);

        json.Should().Be(ExercisedJson.Replace("""{"$case":"Exercised",""", "{", StringComparison.Ordinal));
        JsonSerializer.Deserialize<TreeEvent.Exercised>(json).Should().Be(Exercised);
    }

    [Fact]
    public void TreeEvent_a_transaction_tree_with_nested_exercises_reads_back_equal_and_hash_equal()
    {
        var tree = new TransactionTree("update-1", LedgerOffset.At(7), Eq<TreeEvent>(NestedExercised, Created));

        var read = JsonSerializer.Deserialize<TransactionTree>(JsonSerializer.Serialize(tree))!;

        read.Should().Be(tree);
        read.GetHashCode().Should().Be(tree.GetHashCode());
    }

    [Fact]
    public void TreeEvent_members_may_arrive_in_any_order()
    {
        var json = ExercisedJson.Replace("""{"$case":"Exercised",""", "{", StringComparison.Ordinal)
            .Replace(""","PackageName":"pkg-name"}""", ""","PackageName":"pkg-name","$case":"Exercised"}""", StringComparison.Ordinal);

        JsonSerializer.Deserialize<TreeEvent>(json).Should().Be(Exercised);
    }

    [Fact]
    public void TreeEvent_round_trips_under_a_camel_case_naming_policy_keeping_the_discriminator_spelling()
    {
        var json = JsonSerializer.Serialize<TreeEvent>(Exercised, CamelCase);

        json.Should().StartWith("""{"$case":"Exercised","eventId":"1:0",""").And.Contain(""","childEvents":[{"$case":"Created","eventId":"1:1",""");
        JsonSerializer.Deserialize<TreeEvent>(json, CamelCase).Should().Be(Exercised);
    }

    [Fact]
    public void TreeEvent_member_names_in_another_case_bind_only_when_names_are_case_insensitive()
    {
        var json = ExercisedJson.Replace("\"ChildEvents\"", "\"childevents\"", StringComparison.Ordinal);

        JsonSerializer.Deserialize<TreeEvent>(json, CaseInsensitive).Should().Be(Exercised);
        ((TreeEvent.Exercised)JsonSerializer.Deserialize<TreeEvent>(json)!).ChildEvents.Should().BeEmpty();
    }

    [Fact]
    public void TreeEvent_disallowing_unmapped_members_still_reads_every_arm()
    {
        JsonSerializer.Deserialize<TreeEvent>(JsonSerializer.Serialize<TreeEvent>(NestedExercised, Strict), Strict).Should().Be(NestedExercised);
    }

    [Fact]
    public void TreeEvent_an_unmapped_member_is_refused_when_unmapped_members_are_disallowed()
    {
        var json = CreatedJson.Replace("""{"$case":"Created",""", """{"$case":"Created","Extra":1,""", StringComparison.Ordinal);

        JsonSerializer.Deserialize<TreeEvent>(json).Should().Be(Created);
        var strict = () => JsonSerializer.Deserialize<TreeEvent>(json, Strict);
        strict.Should().Throw<JsonException>().Which.Message.Should().Contain("Extra");
    }

    [Fact]
    public void TreeEvent_round_trips_on_the_strict_preset()
    {
        JsonSerializer.Deserialize<TreeEvent>(JsonSerializer.Serialize<TreeEvent>(NestedExercised, StrictPreset), StrictPreset)
            .Should().Be(NestedExercised);
    }

    [Theory]
    [InlineData(""","ChildEvents":[]""", "\"ChildEvents\"")]
    [InlineData(",\"$case\":\"Exercised\"", "\"$case\"")]
    [InlineData(",\"EventId\":\"1:7\"", "EventId")]
    public void TreeEvent_the_strict_preset_refuses_a_duplicate_member(string duplicate, string namedInMessage)
    {
        var json = ExercisedJson[..^1] + duplicate + "}";

        JsonSerializer.Deserialize<TreeEvent>(json).Should().BeOfType<TreeEvent.Exercised>();
        var strict = () => JsonSerializer.Deserialize<TreeEvent>(json, StrictPreset);
        strict.Should().Throw<JsonException>().Which.Message.Should().Contain(namedInMessage);
    }

    [Fact]
    public void TreeEvent_ignoring_default_members_on_write_still_writes_the_child_events()
    {
        var leaf = Exercised with { ChildEvents = Eq<TreeEvent>() };
        TreeEvent value = Exercised with { ChildEvents = Eq<TreeEvent>(leaf) };

        var json = JsonSerializer.Serialize(value, IgnoringDefaults);

        JsonSerializer.Deserialize<TreeEvent>(json, IgnoringDefaults).Should().Be(value);
    }

    [Theory]
    [InlineData("""{"$case":"Nope"}""", "\"Nope\"")]
    [InlineData("""{"EventId":"1:0"}""", "\"$case\"")]
    [InlineData("""{"$case":7}""", "\"$case\"")]
    [InlineData("""[]""", "StartArray")]
    [InlineData("""{"$case":"Exercised","ChildEvents":[null]}""", "null element")]
    [InlineData("""{"$case":"Exercised","ChildEvents":{}}""", "\"ChildEvents\"")]
    [InlineData("""{"$case":"Exercised","ChildEvents":[{"$case":"Nope"}]}""", "\"Nope\"")]
    public void TreeEvent_malformed_payload_is_refused_with_a_JsonException_naming_the_problem(string json, string namedInMessage)
    {
        var read = () => JsonSerializer.Deserialize<TreeEvent>(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain(namedInMessage);
    }

    [Fact]
    public void TreeEvent_refuses_a_ReferenceHandler_on_write_and_on_read()
    {
        var write = () => JsonSerializer.Serialize<TreeEvent>(Exercised, Preserving);
        var read = () => JsonSerializer.Deserialize<TreeEvent>(ExercisedJson, Preserving);

        write.Should().Throw<JsonException>().Which.Message.Should().Contain("TreeEvent");
        read.Should().Throw<JsonException>().Which.Message.Should().Contain("TreeEvent");
    }

    [Fact]
    public void TreeEvent_nested_thirty_two_deep_is_the_deepest_default_options_read()
    {
        JsonSerializer.Deserialize<TreeEvent>(ChainJson(32, LeafExercisedJson(32))).Should().Be(Chain(32));
        var deeper = () => JsonSerializer.Deserialize<TreeEvent>(ChainJson(33, LeafExercisedJson(33)));
        deeper.Should().Throw<JsonException>();
    }

    [Fact]
    public void TreeEvent_nested_past_MaxDepth_is_refused_with_a_JsonException_on_write()
    {
        var write = () => JsonSerializer.Serialize<TreeEvent>(Chain(33));

        write.Should().Throw<JsonException>();
    }

    [Fact]
    public void TreeEvent_a_member_value_that_takes_the_tree_past_MaxDepth_is_refused_with_a_JsonException_on_write()
    {
        var fits = ChainOverArgumentNested(18);
        var tooDeep = ChainOverArgumentNested(19);

        JsonSerializer.Deserialize<TreeEvent>(JsonSerializer.Serialize(fits)).Should().Be(fits);
        var write = () => JsonSerializer.Serialize(tooDeep);
        write.Should().Throw<JsonException>().Which.Message.Should().Contain("maximum depth of 64");
        var read = () => JsonSerializer.Deserialize<TreeEvent>(JsonSerializer.Serialize(tooDeep, Unlimited));
        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void TreeEvent_at_the_deepest_default_nesting_reads_and_writes_on_a_256_KB_stack()
    {
        var json = ChainJson(32, LeafExercisedJson(32));
        TreeEvent? read = null;
        string? written = null;

        var thrown = StackProbe.OnStackOf(256, () =>
        {
            read = JsonSerializer.Deserialize<TreeEvent>(json);
            written = JsonSerializer.Serialize<TreeEvent>(Chain(32));
        });

        thrown.Should().BeNull();
        read.Should().Be(Chain(32));
        written.Should().Be(json);
    }

    [Fact]
    public void TreeEvent_with_a_bad_case_at_the_deepest_default_nesting_is_refused_with_a_JsonException_on_a_256_KB_stack()
    {
        var json = ChainJson(32, """{"$case":"Nope"}""");

        var thrown = StackProbe.OnStackOf(256, () => JsonSerializer.Deserialize<TreeEvent>(json));

        thrown.Should().BeOfType<JsonException>().Which.Message.Should().Contain("\"Nope\"");
    }

    [Fact]
    public void TreeEvent_three_hundred_deep_round_trips_on_a_one_megabyte_stack_when_MaxDepth_is_raised()
    {
        TreeEvent? read = null;

        var thrown = StackProbe.OnStackOf(1024, () =>
            read = JsonSerializer.Deserialize<TreeEvent>(JsonSerializer.Serialize<TreeEvent>(Chain(300), Unlimited), Unlimited));

        thrown.Should().BeNull();
        read.Should().Be(Chain(300));
    }

    [Fact]
    public void TreeEvent_with_a_bad_case_three_hundred_deep_is_refused_with_a_JsonException_on_a_one_megabyte_stack_when_MaxDepth_is_raised()
    {
        var json = ChainJson(300, """{"$case":"Nope"}""");

        var thrown = StackProbe.OnStackOf(1024, () => JsonSerializer.Deserialize<TreeEvent>(json, Unlimited));

        thrown.Should().BeOfType<JsonException>().Which.Message.Should().Contain("\"Nope\"");
    }

    private static TreeEvent ChainOverArgumentNested(int optionals)
    {
        DamlValue argument = DamlUnit.Instance;
        for (var level = 0; level < optionals; level++)
        {
            argument = DamlOptional.Some(argument);
        }

        TreeEvent value = ChainLink(22, Eq<TreeEvent>(Exercised with { ChoiceArgument = argument }));
        for (var level = 21; level >= 1; level--)
        {
            value = ChainLink(level, Eq(value));
        }

        return value;
    }

    private static EquatableArray<T> Eq<T>(params T[] items) => EquatableArray.Create(items);

    private static TreeEvent.Exercised ChainLink(int level, EquatableArray<TreeEvent> children) =>
        new(
            $"{level}",
            "00c0ffee",
            Iou,
            null,
            new ChoiceName("Archive"),
            DamlUnit.Instance,
            DamlUnit.Instance,
            false,
            Eq<Party>(),
            Eq<Party>(),
            children);

    private static TreeEvent Chain(int levels)
    {
        TreeEvent value = ChainLink(levels, Eq<TreeEvent>());
        for (var level = levels - 1; level >= 1; level--)
        {
            value = ChainLink(level, Eq(value));
        }

        return value;
    }

    private static string ChainLinkJson(int level, string children) =>
        $$"""{"$case":"Exercised","EventId":"{{level}}","ContractId":"00c0ffee","TemplateId":{{IouJson}},"InterfaceId":null,"ChoiceName":"Archive","ChoiceArgument":{"$case":"DamlUnit"},"ExerciseResult":{"$case":"DamlUnit"},"Consuming":false,"ActingParties":[],"WitnessParties":[],"ChildEvents":[{{children}}],"PackageName":null}""";

    private static string LeafExercisedJson(int level) => ChainLinkJson(level, string.Empty);

    private static string ChainJson(int levels, string innermost)
    {
        var json = innermost;
        for (var level = levels - 1; level >= 1; level--)
        {
            json = ChainLinkJson(level, json);
        }

        return json;
    }
}
