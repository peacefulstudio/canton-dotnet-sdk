// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="NonEmpty{T}"/>: the object
/// <see cref="System.Text.Json"/> derives from its <c>Hd</c> and <c>Tl</c> members, read back as the
/// list that wrote it. The contract is a CLR round trip, not the Daml-LF record the ledger encoding
/// uses. What <see cref="DamlJsonConverters.AddDamlConverters"/> adds on top is
/// requiredness: a member the payload omits is refused rather than bound to a null the slot forbids.
/// </summary>
public class NonEmptyJsonTests
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

    private static readonly JsonSerializerOptions IgnoringReadOnlyProperties =
        new() { IgnoreReadOnlyProperties = true };

    private static readonly JsonSerializerOptions WithASentinelTlConverter =
        new() { Converters = { new SentinelTlConverter() } };

    private static readonly JsonSerializerOptions IgnoringDefaultsOnWrite =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    private static readonly JsonSerializerOptions IgnoringDefaultsOnWriteWithRequiredConstructorParameters =
        new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            RespectRequiredConstructorParameters = true,
        };

    private static readonly JsonSerializerOptions IgnoringNullsOnWrite =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static readonly JsonSerializerOptions IgnoringNullsOnWriteWithRequiredConstructorParameters =
        new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            RespectRequiredConstructorParameters = true,
        };

    private sealed record Committee(string Name, NonEmpty<Party> Members);

    private sealed record OptionalCommittee(string Name, NonEmpty<Party>? Members);

    private sealed record TextGroup(string Name, NonEmpty<string> Members);

    private sealed record VoteTally(string Name, NonEmpty<long> Members);

    private sealed record Boom(int Value);

    private sealed class ThrowingBoomConverter : JsonConverter<Boom>
    {
        public override Boom Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            throw new InvalidOperationException("boom on Hd");
        }

        public override void Write(Utf8JsonWriter writer, Boom value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingBoomListConverter : JsonConverter<IReadOnlyList<Boom>>
    {
        public override IReadOnlyList<Boom> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            throw new InvalidOperationException("boom on Tl");
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<Boom> value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    private sealed class SentinelTlConverter : JsonConverter<IReadOnlyList<string>>
    {
        public override IReadOnlyList<string> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return ["sentinel"];
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToList(), options);
    }

    /// <summary>
    /// <see cref="NonEmpty{T}"/> carries a custom
    /// <see cref="System.Text.Json.Serialization.JsonConverter{T}"/>, <see cref="NonEmptyJsonConverter{T}"/>,
    /// mirroring <see cref="SetJsonConverter{T}"/> and <see cref="MapJsonConverter{TKey, TValue}"/>:
    /// it reads <c>Hd</c> and <c>Tl</c> itself and translates the <see cref="ArgumentException"/>
    /// the shared <c>EventCollections.RejectNull</c> and <c>CopyRejectingNullElements</c> paths
    /// throw for a null head or tail element into a <see cref="JsonException"/> naming the
    /// offending slot, on both bare options and under
    /// <see cref="DamlJsonConverters.AddDamlConverters"/>.
    /// </summary>
    [Fact]
    public void NonEmpty_refuses_a_null_tail_element_reached_through_deserialization_on_bare_options()
    {
        var act = () => JsonSerializer.Deserialize<TextGroup>(
            """{"Name":"board","Members":{"Hd":"alice","Tl":[null]}}""",
            HostBuiltWithoutDamlConverters);

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid element in NonEmpty<String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Element 0 is null*a NonEmpty<String> holds no null elements*");
    }

    [Fact]
    public void NonEmpty_refuses_a_null_tail_element_reached_through_deserialization_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<TextGroup>(
            """{"Name":"board","Members":{"Hd":"alice","Tl":[null]}}""",
            Registered);

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid element in NonEmpty<String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Element 0 is null*a NonEmpty<String> holds no null elements*");
    }

    [Fact]
    public void NonEmpty_refuses_a_null_Hd_reached_through_deserialization_on_bare_options()
    {
        var act = () => JsonSerializer.Deserialize<TextGroup>(
            """{"Name":"board","Members":{"Hd":null,"Tl":[]}}""",
            HostBuiltWithoutDamlConverters);

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid element in NonEmpty<String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Hd is null*a NonEmpty<String> holds no null elements*");
    }

    /// <remarks>
    /// Unlike <see cref="Set{T}"/>'s constructor parameter, <c>Hd</c> is read by
    /// <see cref="NonEmptyJsonConverter{T}"/> itself rather than by
    /// <see cref="System.Text.Json"/>'s own constructor binding, so
    /// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> never sees it — the
    /// converter's own <c>Hd</c> guard rejects the null instead, the same posture it holds on
    /// bare options.
    /// </remarks>
    [Fact]
    public void NonEmpty_refuses_a_null_Hd_reached_through_deserialization_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<TextGroup>(
            """{"Name":"board","Members":{"Hd":null,"Tl":[]}}""",
            Registered);

        act.Should().Throw<JsonException>()
            .WithMessage("*Invalid element in NonEmpty<String>*")
            .WithInnerException<ArgumentException>()
            .WithMessage("*Hd is null*a NonEmpty<String> holds no null elements*");
    }

    [Fact]
    public void NonEmpty_round_trips_under_a_camelCase_naming_policy_without_case_insensitive_reads()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var group = new NonEmpty<string>("alice", ["bob"]);

        var written = JsonSerializer.Serialize(group, options);

        written.Should().Be(
            """{"hd":"alice","tl":["bob"],"all":["alice","bob"]}""",
            "the previous reflection-based contract wrote and accepted whatever "
            + "PropertyNamingPolicy the caller configured, and the converter must not regress a "
            + "consumer who upgrades into a fixed PascalCase wire shape");
        JsonSerializer.Deserialize<NonEmpty<string>>(written, options).Should().Be(group);
    }

    [Fact]
    public void NonEmpty_rejects_an_omitted_value_typed_Hd_when_required_constructor_parameters_are_respected()
    {
        var options = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };

        var act = () => JsonSerializer.Deserialize<VoteTally>(
            """{"Name":"board","Members":{"Tl":[2,3]}}""", options);

        act.Should().Throw<JsonException>().WithMessage(
            "*NonEmpty<Int64>*requires*Hd*",
            "System.Text.Json's own constructor binding rejected a missing non-optional Hd under "
            + "this setting before the converter existed; a value-typed Hd silently defaulting to 0 "
            + "would turn malformed persisted data into a valid but incorrect Daml value");
    }

    [Fact]
    public void NonEmpty_reads_an_omitted_value_typed_Hd_as_the_default_when_required_constructor_parameters_are_not_respected()
    {
        var tally = JsonSerializer.Deserialize<VoteTally>("""{"Name":"board","Members":{"Tl":[2,3]}}""");

        tally!.Members.Hd.Should().Be(
            0L,
            "RespectRequiredConstructorParameters defaults to false, matching System.Text.Json's own "
            + "pre-converter behavior for a missing non-optional value-typed constructor parameter");
    }

    [Fact]
    public void NonEmpty_rejects_an_unmapped_member_when_unmapped_members_are_disallowed()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

        var act = () => JsonSerializer.Deserialize<NonEmpty<string>>(
            """{"Hd":"alice","Tl":[],"Bogus":1}""", options);

        act.Should().Throw<JsonException>().WithMessage(
            "*Bogus*",
            "the previous reflection-based reader rejected an unmapped member under this setting, "
            + "and a payload with a misspelled or unexpected field must not silently succeed");
    }

    [Fact]
    public void NonEmpty_omits_the_read_only_All_member_when_read_only_properties_are_ignored()
    {
        var json = JsonSerializer.Serialize(new NonEmpty<Party>(Alice, [Bob]), IgnoringReadOnlyProperties);

        json.Should().Be(
            """{"Hd":"Alice::122012ab","Tl":["Bob::122034cd"]}""",
            "the previous reflection-based writer omitted the read-only All under this setting, "
            + "and a consumer suppressing derived properties for a strict downstream schema must "
            + "keep getting that shape");
    }

    [Fact]
    public void NonEmpty_omits_a_default_valued_Hd_under_WhenWritingDefault()
    {
        var group = new NonEmpty<long>(0, []);

        var json = JsonSerializer.Serialize(group, IgnoringDefaultsOnWrite);

        json.Should().Be(
            """{"Tl":[],"All":[0]}""",
            "the previous reflection-based writer omitted Hd under this setting when it equalled "
            + "its default of 0, the same posture it held for every other property, and a direct "
            + "write bypassing DefaultIgnoreCondition changed that shape for a consumer suppressing "
            + "default-valued properties");
    }

    [Fact]
    public void NonEmpty_writes_a_nonzero_Hd_under_WhenWritingDefault()
    {
        var group = new NonEmpty<long>(5, []);

        var json = JsonSerializer.Serialize(group, IgnoringDefaultsOnWrite);

        json.Should().Be("""{"Hd":5,"Tl":[],"All":[5]}""");
    }

    [Fact]
    public void NonEmpty_still_rejects_its_own_output_when_WhenWritingDefault_drops_Hd_and_required_constructor_parameters_are_respected()
    {
        var written = JsonSerializer.Serialize(
            new NonEmpty<long>(0, [2, 3]), IgnoringDefaultsOnWriteWithRequiredConstructorParameters);

        var act = () => JsonSerializer.Deserialize<NonEmpty<long>>(
            written, IgnoringDefaultsOnWriteWithRequiredConstructorParameters);

        act.Should().Throw<JsonException>().WithMessage(
            "*NonEmpty<Int64>*requires*Hd*",
            "System.Text.Json's own reflection contract carried this same footgun before the "
            + "converter existed: combining WhenWritingDefault with "
            + "RespectRequiredConstructorParameters drops a default-valued required parameter on "
            + "write and then refuses to read it back, so the converter must reproduce that "
            + "behavior rather than paper over it");
    }

    [Fact]
    public void NonEmpty_omits_a_null_Hd_under_WhenWritingNull()
    {
#pragma warning disable CS8714
        var group = new NonEmpty<long?>(null, []);
#pragma warning restore CS8714

        var json = JsonSerializer.Serialize(group, IgnoringNullsOnWrite);

        json.Should().Be(
            """{"Tl":[],"All":[null]}""",
            "the previous reflection-based writer omitted Hd under this setting when it was null "
            + "for a nullable value type element, the same posture it held for every other "
            + "reference- or nullable-valued property, and a direct write bypassing "
            + "DefaultIgnoreCondition changed that shape for a consumer suppressing null-valued "
            + "properties");
    }

    [Fact]
    public void NonEmpty_writes_a_present_Hd_under_WhenWritingNull()
    {
#pragma warning disable CS8714
        var group = new NonEmpty<long?>(7, []);
#pragma warning restore CS8714

        var json = JsonSerializer.Serialize(group, IgnoringNullsOnWrite);

        json.Should().Be("""{"Hd":7,"Tl":[],"All":[7]}""");
    }

    [Fact]
    public void NonEmpty_still_rejects_its_own_output_when_WhenWritingNull_drops_Hd_and_required_constructor_parameters_are_respected()
    {
#pragma warning disable CS8714
        var group = new NonEmpty<long?>(null, [2, 3]);
#pragma warning restore CS8714
        var written = JsonSerializer.Serialize(group, IgnoringNullsOnWriteWithRequiredConstructorParameters);

#pragma warning disable CS8714
        var act = () => JsonSerializer.Deserialize<NonEmpty<long?>>(
            written, IgnoringNullsOnWriteWithRequiredConstructorParameters);
#pragma warning restore CS8714

        act.Should().Throw<JsonException>().WithMessage(
            "*NonEmpty*requires*Hd*",
            "System.Text.Json's own reflection contract carried this same footgun before the "
            + "converter existed: combining WhenWritingNull with RespectRequiredConstructorParameters "
            + "drops a null required parameter on write and then refuses to read it back, so the "
            + "converter must reproduce that behavior rather than paper over it");
    }

    [Fact]
    public void NonEmpty_round_trips_its_own_default_output_when_unmapped_members_are_disallowed()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var group = new NonEmpty<string>("alice", ["bob"]);

        var written = JsonSerializer.Serialize(group, options);
        var act = () => JsonSerializer.Deserialize<NonEmpty<string>>(written, options);

        act.Should().NotThrow(
            "Write emits the read-only All alongside Hd and Tl by default, and the previous "
            + "reflection reader recognized All as a declared member rather than an unmapped one, "
            + "so a converter's own default output must still round-trip under Disallow");
    }

    [Fact]
    public void NonEmpty_reads_Tl_through_a_converter_registered_for_its_declared_interface_type()
    {
        var group = JsonSerializer.Deserialize<NonEmpty<string>>(
            """{"Hd":"alice","Tl":["bob"]}""", WithASentinelTlConverter);

        group!.Tl.Should().Equal(
            ["sentinel"],
            "Write already serializes Tl through its declared IReadOnlyList<T> type, so Read "
            + "deserializing through the concrete List<T> instead bypassed a converter a caller "
            + "registered for the declared type");
    }

    [Fact]
    public void NonEmpty_writes_the_literal_JSON_object_of_its_head_and_tail()
    {
        var json = JsonSerializer.Serialize(new NonEmpty<Party>(Alice, [Bob]));

        json.Should().Be(
            """{"Hd":"Alice::122012ab","Tl":["Bob::122034cd"],"All":["Alice::122012ab","Bob::122034cd"]}""");
    }

    [Fact]
    public void NonEmpty_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var committee = new Committee("board", new NonEmpty<Party>(Alice, [Bob]));

        var written = JsonSerializer.Serialize(committee, Registered);

        JsonSerializer.Deserialize<Committee>(written, Registered).Should().Be(committee);
    }

    [Fact]
    public void NonEmpty_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var committee = new Committee("board", new NonEmpty<Party>(Alice, [Bob]));

        var written = JsonSerializer.Serialize(committee, HostBuiltWithoutDamlConverters);

        JsonSerializer.Deserialize<Committee>(written, HostBuiltWithoutDamlConverters)
            .Should().Be(committee);
    }

    [Fact]
    public void NonEmpty_refuses_a_payload_that_omits_a_member_the_slot_forbids_a_null_in_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<Committee>("""{"Name":"board"}""", Registered);

        act.Should().Throw<JsonException>().WithMessage("*Members*");
    }

    [Fact]
    public void NonEmpty_reads_an_omitted_member_as_the_absent_list_where_the_slot_permits_one()
    {
        var committee = JsonSerializer.Deserialize<OptionalCommittee>(
            """{"Name":"board"}""", Registered);

        committee!.Members.Should().BeNull(
            "codegen renders Optional (NonEmpty a) as a nullable NonEmpty<T> slot, so an omitted "
            + "member is the None the payload meant rather than a member it forgot");
    }

    [Fact]
    public void NonEmpty_reads_an_omitted_member_as_null_even_where_the_slot_forbids_one_on_options_that_register_nothing()
    {
        var committee = JsonSerializer.Deserialize<Committee>(
            """{"Name":"board"}""", HostBuiltWithoutDamlConverters);

        committee!.Members.Should().BeNull(
            "the modifier that marks the member required rides on AddDamlConverters, and options "
            + "that register nothing never install it");
    }

    [Fact]
    public void NonEmpty_rejects_a_non_object_token()
    {
        var act = () => JsonSerializer.Deserialize<NonEmpty<Boom>>("[1]");

        act.Should().Throw<JsonException>().WithMessage(
            "Expected object token for NonEmpty<NonEmptyJsonTests.Boom>, got StartArray.");
    }

    [Fact]
    public void NonEmpty_skips_an_unmapped_member_when_unmapped_members_are_not_disallowed()
    {
        var group = JsonSerializer.Deserialize<NonEmpty<string>>(
            """{"Hd":"alice","Tl":[],"Bogus":1}""");

        group.Should().Be(new NonEmpty<string>("alice", []));
    }

    [Fact]
    public void NonEmpty_wraps_a_non_JsonException_thrown_while_reading_Hd()
    {
        var options = new JsonSerializerOptions { Converters = { new ThrowingBoomConverter() } };

        var act = () => JsonSerializer.Deserialize<NonEmpty<Boom>>(
            """{"Hd":{"Value":1},"Tl":[]}""", options);

        act.Should().Throw<JsonException>()
            .WithMessage("Cannot read NonEmpty<NonEmptyJsonTests.Boom>.Hd: boom on Hd")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("boom on Hd");
    }

    [Fact]
    public void NonEmpty_wraps_a_non_JsonException_thrown_while_reading_Tl()
    {
        var options = new JsonSerializerOptions { Converters = { new ThrowingBoomListConverter() } };

        var act = () => JsonSerializer.Deserialize<NonEmpty<Boom>>(
            """{"Hd":{"Value":1},"Tl":[]}""", options);

        act.Should().Throw<JsonException>()
            .WithMessage("Cannot read NonEmpty<NonEmptyJsonTests.Boom>.Tl: boom on Tl")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("boom on Tl");
    }
}
