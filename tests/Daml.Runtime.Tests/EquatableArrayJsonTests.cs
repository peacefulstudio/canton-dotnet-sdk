// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the wire shape of <see cref="EquatableArray{T}"/> — a JSON array, with the elements
/// converted through the same <see cref="JsonSerializerOptions"/> as everything else — and the
/// three postures that make a record carrying one safe to persist and read back: a populated
/// array round-trips, an explicit <c>null</c> is refused, and a constructor parameter the
/// payload never mentions is refused rather than silently read as empty.
/// </summary>
public class EquatableArrayJsonTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddDamlConverters();

    private static readonly Party Alice = new("Alice::122012ab");

    private static readonly Party Bob = new("Bob::122034cd");

    private static readonly Identifier AssetTemplate = new("cafe", "Asset", "Token");

    private static readonly CreatedContract AssetContract = new(
        "event-1",
        "contract-1",
        AssetTemplate,
        new DamlRecord(null, [new DamlField("amount", new DamlInt64(42))]),
        [Alice],
        [Alice, Bob],
        [],
        null,
        null);

    private sealed record OptionalListPayload(string Note)
    {
        public EquatableArray<string> Tags { get; init; }
    }

    private sealed record NullableListPayload(EquatableArray<string>? Tags);

    private sealed record HostDefaultedListPayload(string Note, EquatableArray<string> Tags = default);

    private sealed class NestedInGeneric<T>
    {
        public sealed record Element(int Value);
    }

    private abstract record AbstractElement;

    private sealed class NestedInPlain
    {
        public sealed record Element(int Value);
    }

    private sealed record OptionalFieldPayload(
        Party Owner,
        long Count,
        string? Note,
        EquatableArray<Party> Witnesses);

    private sealed record Unconvertible(bool Fails);

    private sealed class ThrowingElementConverter : JsonConverter<Unconvertible>
    {
        public override Unconvertible Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new InvalidOperationException("the element converter refused to read");

        public override void Write(Utf8JsonWriter writer, Unconvertible value, JsonSerializerOptions options)
        {
            if (value.Fails)
            {
                throw new InvalidOperationException("the element converter refused to write");
            }

            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }

    private sealed class StubArrayConverter : JsonConverter<EquatableArray<Party>>
    {
        internal static readonly Party Stub = new("Stub::1220ef");

        public override EquatableArray<Party> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return [Stub];
        }

        public override void Write(Utf8JsonWriter writer, EquatableArray<Party> value, JsonSerializerOptions options) =>
            writer.WriteStringValue("stub");
    }

    private static readonly JsonSerializerOptions BareOptions = new();

    private static readonly JsonSerializerOptions StubConverterOptions =
        new() { Converters = { new StubArrayConverter() } };

    private static readonly JsonSerializerOptions ThrowingElementOptions =
        new JsonSerializerOptions { Converters = { new ThrowingElementConverter() } }.AddDamlConverters();

    [Fact]
    public void EquatableArray_serializes_as_a_json_array()
    {
        EquatableArray<string> tags = ["a", "b"];

        var json = JsonSerializer.Serialize(tags, Options);

        json.Should().Be("""["a","b"]""");
    }

    [Fact]
    public void EquatableArray_serializes_default_as_an_empty_json_array()
    {
        var json = JsonSerializer.Serialize(default(EquatableArray<string>), Options);

        json.Should().Be("[]");
    }

    [Fact]
    public void EquatableArray_deserializes_from_a_json_array()
    {
        var tags = JsonSerializer.Deserialize<EquatableArray<string>>("""["a","b"]""", Options);

        tags.Should().Equal("a", "b");
    }

    [Fact]
    public void EquatableArray_deserializes_an_empty_json_array_as_Empty()
    {
        var tags = JsonSerializer.Deserialize<EquatableArray<string>>("[]", Options);

        tags.Should().BeEmpty();
    }

    [Fact]
    public void EquatableArray_reads_its_elements_through_the_callers_own_options()
    {
        var options = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        }.AddDamlConverters();

        var numbers = JsonSerializer.Deserialize<EquatableArray<int>>("""["1","2"]""", options);

        numbers.Should().Equal(1, 2);
    }

    [Fact]
    public void EquatableArray_writes_Party_elements_as_bare_strings()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var json = JsonSerializer.Serialize(parties, Options);

        json.Should().Be("""["Alice::122012ab","Bob::122034cd"]""");
    }

    [Fact]
    public void EquatableArray_of_a_record_element_round_trips()
    {
        EquatableArray<Identifier> templates = [AssetTemplate];

        var restored = JsonSerializer.Deserialize<EquatableArray<Identifier>>(
            JsonSerializer.Serialize(templates, Options),
            Options);

        restored.Should().Equal(AssetTemplate);
    }

    [Fact]
    public void EquatableArray_throws_JsonException_when_the_token_is_null()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<string>>("null", Options);

        act.Should().Throw<JsonException>().WithMessage("*EquatableArray<String>*");
    }

    [Fact]
    public void EquatableArray_throws_JsonException_when_the_token_is_not_an_array()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<string>>("\"a\"", Options);

        act.Should().Throw<JsonException>()
            .WithMessage("*Expected array token for EquatableArray<String>, got String*");
    }

    [Fact]
    public void EquatableArray_throws_JsonException_when_an_element_is_null()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<string>>("""["a",null]""", Options);

        act.Should().Throw<JsonException>().WithMessage("*Element 1 is null*");
    }

    [Fact]
    public void EquatableArray_reads_null_as_absent_when_the_slot_is_declared_nullable()
    {
        var payload = JsonSerializer.Deserialize<NullableListPayload>("""{"Tags":null}""", Options);

        payload!.Tags.Should().BeNull();
    }

    [Fact]
    public void ArchivedEvent_round_trips_through_json()
    {
        var archived = new ArchivedEvent("event-1", "contract-1", AssetTemplate, [Alice, Bob]);

        var restored = JsonSerializer.Deserialize<ArchivedEvent>(
            JsonSerializer.Serialize(archived, Options),
            Options);

        restored.Should().Be(archived);
    }

    [Fact]
    public void ArchivedEvent_throws_JsonException_when_WitnessParties_is_absent()
    {
        var json = """
            {"EventId":"event-1","ContractId":"contract-1",
             "TemplateId":{"PackageId":"cafe","ModuleName":"Asset","EntityName":"Token"}}
            """;

        var act = () => JsonSerializer.Deserialize<ArchivedEvent>(json, Options);

        act.Should().Throw<JsonException>().WithMessage("*WitnessParties*");
    }

    [Fact]
    public void ArchivedEvent_reads_an_empty_WitnessParties_array_as_Empty()
    {
        var json = """
            {"EventId":"event-1","ContractId":"contract-1",
             "TemplateId":{"PackageId":"cafe","ModuleName":"Asset","EntityName":"Token"},
             "WitnessParties":[]}
            """;

        var archived = JsonSerializer.Deserialize<ArchivedEvent>(json, Options);

        archived!.WitnessParties.Should().BeEmpty();
    }

    [Fact]
    public void OptionalListPayload_reads_an_absent_init_only_slot_as_Empty()
    {
        var payload = JsonSerializer.Deserialize<OptionalListPayload>("""{"Note":"n"}""", Options);

        payload!.Tags.Should().BeEmpty();
    }

    [Fact]
    public void EquatableArray_translates_a_failing_element_read_to_JsonException()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<Unconvertible>>("[{}]", ThrowingElementOptions);

        act.Should().Throw<JsonException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*refused to read*");
    }

    [Fact]
    public void EquatableArray_translates_a_failing_element_write_to_JsonException()
    {
        EquatableArray<Unconvertible> values = [new Unconvertible(false), new Unconvertible(true)];

        var act = () => JsonSerializer.Serialize(values, ThrowingElementOptions);

        act.Should().Throw<JsonException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*refused to write*");
    }

    [Fact]
    public void EquatableArray_names_the_index_of_the_element_that_failed_to_write()
    {
        EquatableArray<Unconvertible> values = [new Unconvertible(false), new Unconvertible(true)];

        var act = () => JsonSerializer.Serialize(values, ThrowingElementOptions);

        act.Should().Throw<JsonException>().WithMessage("*element 1*");
    }

    [Fact]
    public void EquatableArray_names_the_index_of_the_element_that_failed()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<int>>("""[1,2,"three"]""", Options);

        act.Should().Throw<JsonException>().WithMessage("*element 2*");
    }

    [Fact]
    public void EquatableArray_renders_a_generic_element_type_without_its_arity()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<EquatableArray<string>>>("null", Options);

        act.Should().Throw<JsonException>().WithMessage("*EquatableArray<EquatableArray<String>>*");
    }

    [Fact]
    public void EquatableArrayJsonConverterFactory_does_not_convert_the_open_generic()
    {
        new EquatableArrayJsonConverterFactory().CanConvert(typeof(EquatableArray<>)).Should().BeFalse();
    }

    [Fact]
    public void EquatableArrayJsonConverterFactory_throws_ArgumentException_for_the_open_generic()
    {
        var act = () => new EquatableArrayJsonConverterFactory().CreateConverter(typeof(EquatableArray<>), Options);

        act.Should().Throw<ArgumentException>().WithMessage("*is not a closed EquatableArray<T>*");
    }

    [Fact]
    public void AddDamlConverters_does_not_make_every_constructor_parameter_required()
    {
        var options = new JsonSerializerOptions().AddDamlConverters();

        options.RespectRequiredConstructorParameters.Should().BeFalse(
            "requiredness is scoped to EquatableArray<T> parameters by a JsonTypeInfo modifier; the "
            + "serializer-wide flag also captures nullable parameters that carry no default, which "
            + "makes a payload omitting TransactionResult.CommandId unreadable");
    }

    [Fact]
    public void TransactionResult_reads_an_absent_CommandId_as_null()
    {
        var json = """
            {"UpdateId":"u1","CompletionOffset":1,
             "CreatedContracts":[],"ArchivedContractIds":[]}
            """;

        var result = JsonSerializer.Deserialize<TransactionResult>(json, Options);

        result!.CommandId.Should().BeNull();
    }

    [Fact]
    public void TransactionResult_reads_an_absent_ExercisedEvents_as_Empty()
    {
        var json = """
            {"UpdateId":"u1","CompletionOffset":1,
             "CreatedContracts":[],"ArchivedContractIds":[]}
            """;

        var result = JsonSerializer.Deserialize<TransactionResult>(json, Options);

        result!.ExercisedEvents.Should().BeEmpty();
    }

    [Fact]
    public void TransactionResult_throws_JsonException_when_CreatedContracts_is_absent()
    {
        var json = """
            {"UpdateId":"u1","CompletionOffset":1,"ArchivedContractIds":[]}
            """;

        var act = () => JsonSerializer.Deserialize<TransactionResult>(json, Options);

        act.Should().Throw<JsonException>().WithMessage("*CreatedContracts*");
    }

    [Fact]
    public void OptionalFieldPayload_reads_an_absent_optional_field_as_null()
    {
        var json = """{"Owner":"Alice::122012ab","Count":1,"Witnesses":[]}""";

        var payload = JsonSerializer.Deserialize<OptionalFieldPayload>(json, Options);

        payload!.Note.Should().BeNull(
            "codegen emits a Daml Optional as a positional T? with no default, so a serializer-wide "
            + "required-constructor-parameter rule would refuse every payload that omits a None");
    }

    [Fact]
    public void OptionalFieldPayload_throws_JsonException_when_the_list_member_is_absent()
    {
        var json = """{"Owner":"Alice::122012ab","Count":1,"Note":"n"}""";

        var act = () => JsonSerializer.Deserialize<OptionalFieldPayload>(json, Options);

        act.Should().Throw<JsonException>().WithMessage("*Witnesses*");
    }

    [Fact]
    public void CreatedContract_reads_an_absent_InterfaceIds_as_Empty()
    {
        var json = """
            {"EventId":"event-1","ContractId":"contract-1",
             "TemplateId":{"PackageId":"cafe","ModuleName":"Asset","EntityName":"Token"},
             "Payload":{"RecordId":null,"Fields":[]},
             "WitnessParties":[],"Signatories":[],"Observers":[]}
            """;

        var created = JsonSerializer.Deserialize<CreatedContract>(json, Options);

        created!.InterfaceIds.Should().BeEmpty();
    }

    [Fact]
    public void AddDamlConverters_composes_onto_a_TypeInfoResolver_the_caller_installed()
    {
        var callerModifierRan = false;
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
                .WithAddedModifier(_ => callerModifierRan = true),
        }.AddDamlConverters();

        JsonSerializer.Deserialize<OptionalListPayload>("""{"Note":"n"}""", options);

        callerModifierRan.Should().BeTrue();
    }

    [Fact]
    public void AddDamlConverters_still_requires_the_list_when_the_caller_installed_a_TypeInfoResolver()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        }.AddDamlConverters();
        var json = """
            {"EventId":"event-1","ContractId":"contract-1",
             "TemplateId":{"PackageId":"cafe","ModuleName":"Asset","EntityName":"Token"}}
            """;

        var act = () => JsonSerializer.Deserialize<ArchivedEvent>(json, options);

        act.Should().Throw<JsonException>().WithMessage("*WitnessParties*");
    }

    [Fact]
    public void AddDamlConverters_leaves_a_nullable_EquatableArray_parameter_optional()
    {
        var payload = JsonSerializer.Deserialize<NullableListPayload>("{}", Options);

        payload!.Tags.Should().BeNull();
    }

    [Fact]
    public void CreatedContract_round_trips_without_DamlValueJsonConverter_registered()
    {
        var json = JsonSerializer.Serialize(AssetContract, Options);

        var restored = JsonSerializer.Deserialize<CreatedContract>(json, Options);

        restored.Should().Be(AssetContract);
    }

    [Fact]
    public void CreatedContract_round_trips_when_DamlValueJsonConverter_is_added()
    {
        var options = new JsonSerializerOptions { Converters = { new DamlValueJsonConverter() } }
            .AddDamlConverters();

        var restored = JsonSerializer.Deserialize<CreatedContract>(
            JsonSerializer.Serialize(AssetContract, options),
            options);

        restored.Should().Be(AssetContract);
    }

    [Fact]
    public void EquatableArray_translates_an_unsupported_element_type_to_JsonException()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<AbstractElement>>("[{}]", Options);

        act.Should().Throw<JsonException>()
            .WithMessage("*element 0 of EquatableArray<EquatableArrayJsonTests.AbstractElement>*")
            .WithInnerException<NotSupportedException>();
    }

    [Fact]
    public void EquatableArray_reports_a_nested_generic_element_type_as_JsonException()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<NestedInGeneric<int>.Element>>("null", Options);

        act.Should().Throw<JsonException>().WithMessage("*EquatableArray<Element<Int32>>*");
    }

    [Fact]
    public void EquatableArray_renders_a_type_nested_in_a_non_generic_type_with_its_declaring_name()
    {
        var act = () => JsonSerializer.Deserialize<EquatableArray<NestedInPlain.Element>>("null", Options);

        act.Should().Throw<JsonException>().WithMessage("*EquatableArray<NestedInPlain.Element>*");
    }

    [Fact]
    public void AddDamlConverters_leaves_a_list_parameter_that_carries_a_default_optional()
    {
        var payload = JsonSerializer.Deserialize<HostDefaultedListPayload>("""{"Note":"n"}""", Options);

        payload!.Tags.Should().BeEmpty();
    }

    [Fact]
    public void EquatableArray_round_trips_on_bare_options_through_the_attribute_on_the_struct()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var json = JsonSerializer.Serialize(parties, BareOptions);
        var restored = JsonSerializer.Deserialize<EquatableArray<Party>>(json, BareOptions);

        json.Should().Be("""["Alice::122012ab","Bob::122034cd"]""");
        restored.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void ArchivedEvent_round_trips_on_bare_options_through_the_attribute_on_the_struct()
    {
        var archived = new ArchivedEvent("event-1", "contract-1", AssetTemplate, [Alice, Bob]);

        var json = JsonSerializer.Serialize(archived, BareOptions);
        var restored = JsonSerializer.Deserialize<ArchivedEvent>(json, BareOptions);

        json.Should().Be(
            """{"EventId":"event-1","ContractId":"contract-1","TemplateId":{"PackageId":"cafe","ModuleName":"Asset","EntityName":"Token","FullyQualifiedName":"Asset:Token"},"WitnessParties":["Alice::122012ab","Bob::122034cd"]}""");
        restored.Should().Be(archived);
    }

    [Fact]
    public void ArchivedEvent_writes_the_same_bytes_on_bare_options_as_on_the_registered_ones()
    {
        var archived = new ArchivedEvent("event-1", "contract-1", AssetTemplate, [Alice, Bob]);

        JsonSerializer.Serialize(archived, BareOptions)
            .Should().Be(JsonSerializer.Serialize(archived, Options));
    }

    [Fact]
    public void ArchivedEvent_refuses_an_explicit_null_WitnessParties_on_bare_options()
    {
        var json = """
            {"EventId":"event-1","ContractId":"contract-1",
             "TemplateId":{"PackageId":"cafe","ModuleName":"Asset","EntityName":"Token"},
             "WitnessParties":null}
            """;

        var act = () => JsonSerializer.Deserialize<ArchivedEvent>(json, BareOptions);

        act.Should().Throw<JsonException>().WithMessage("*EquatableArray<Party> cannot be null*");
    }

    [Fact]
    public void A_converter_on_the_options_wins_over_the_attribute_on_the_struct()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var json = JsonSerializer.Serialize(parties, StubConverterOptions);
        var restoredFromTheStubShape =
            JsonSerializer.Deserialize<EquatableArray<Party>>(json, StubConverterOptions);
        var restoredFromAnArray = JsonSerializer.Deserialize<EquatableArray<Party>>(
            """["Alice::122012ab","Bob::122034cd"]""",
            StubConverterOptions);

        json.Should().Be("\"stub\"");
        restoredFromTheStubShape.Should().Equal(StubArrayConverter.Stub);
        restoredFromAnArray.Should().Equal(StubArrayConverter.Stub);
    }

    [Fact]
    public void The_attribute_alone_reads_an_omitted_list_parameter_as_empty()
    {
        var json = """
            {"EventId":"event-1","ContractId":"contract-1",
             "TemplateId":{"PackageId":"cafe","ModuleName":"Asset","EntityName":"Token"}}
            """;

        var archived = JsonSerializer.Deserialize<ArchivedEvent>(json, BareOptions);

        archived.Should().Be(new ArchivedEvent("event-1", "contract-1", AssetTemplate, []));
    }

    [Fact]
    public void AddDamlConverters_writes_a_required_list_that_DefaultIgnoreCondition_would_drop()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        }.AddDamlConverters();
        var archived = new ArchivedEvent("event-1", "contract-1", AssetTemplate, []);

        var restored = JsonSerializer.Deserialize<ArchivedEvent>(
            JsonSerializer.Serialize(archived, options),
            options);

        restored.Should().Be(archived);
    }
}
