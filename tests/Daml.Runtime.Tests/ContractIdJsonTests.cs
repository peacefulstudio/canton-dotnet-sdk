// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ContractIdJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private sealed record TemplatePayload(ContractId<Marker> Marker, Party Owner);

    private sealed record OptionalContractIdPayload(ContractId<Marker>? Marker, string Note);

    [Fact]
    public void ContractId_should_serialize_as_json_string()
    {
        var json = JsonSerializer.Serialize(new ContractId<Marker>("00abc"));

        json.Should().Be("\"00abc\"");
    }

    [Fact]
    public void ContractId_should_deserialize_from_json_string()
    {
        var contractId = JsonSerializer.Deserialize<ContractId<Marker>>("\"00abc\"");

        contractId!.Value.Should().Be("00abc");
    }

    [Fact]
    public void ContractId_should_round_trip_inside_a_record_payload()
    {
        var json = "{\"Marker\":\"00abc\",\"Owner\":\"Alice::1220ab\"}";

        var payload = JsonSerializer.Deserialize<TemplatePayload>(json);

        payload!.Marker.Value.Should().Be("00abc");
        payload.Owner.Value.Should().Be("Alice::1220ab");
        JsonSerializer.Serialize(payload).Should().Be(json);
    }

    [Fact]
    public void ContractId_should_reject_a_non_string_token()
    {
        var deserialize = () => JsonSerializer.Deserialize<ContractId<Marker>>("{\"value\":\"00abc\"}");

        deserialize.Should().Throw<JsonException>();
    }

    [Fact]
    public void ContractId_should_reject_an_empty_string()
    {
        var deserialize = () => JsonSerializer.Deserialize<ContractId<Marker>>("\"\"");

        deserialize.Should().Throw<JsonException>();
    }

    [Fact]
    public void Optional_ContractId_should_round_trip_as_json_null()
    {
        var json = "{\"Marker\":null,\"Note\":\"none held\"}";

        var payload = JsonSerializer.Deserialize<OptionalContractIdPayload>(json);

        payload!.Marker.Should().BeNull();
        JsonSerializer.Serialize(payload).Should().Be(json);
    }

    [Fact]
    public void ContractId_should_throw_JsonException_when_non_nullable_field_receives_null()
    {
        var json = "{\"Marker\":null,\"Owner\":\"Alice::1220ab\"}";

        var deserialize = () => JsonSerializer.Deserialize<TemplatePayload>(json, Registered);

        deserialize.Should().Throw<JsonException>(
            "a null on a non-nullable ContractId field must fail as loudly as it already does on the "
            + "Party field beside it, rather than silently yielding a null the caller only discovers "
            + "when it dereferences the id");
    }

    [Fact]
    public void Optional_ContractId_should_round_trip_as_json_null_under_the_registered_converters()
    {
        var json = "{\"Marker\":null,\"Note\":\"none held\"}";

        var payload = JsonSerializer.Deserialize<OptionalContractIdPayload>(json, Registered);

        payload!.Marker.Should().BeNull(
            "Optional (ContractId T) is a supported Daml shape, so rejecting a null has to key off the "
            + "declared nullability — a converter that refuses every null token cannot tell the two "
            + "declarations apart, because both are the same reference type at runtime");
        JsonSerializer.Serialize(payload, Registered).Should().Be(json);
    }

    [Fact]
    public void AddDamlConverters_should_respect_nullable_annotations()
    {
        var options = new JsonSerializerOptions().AddDamlConverters();

        options.RespectNullableAnnotations.Should().BeTrue(
            "ContractId<T> is a reference type, so nothing but the annotation distinguishes a required "
            + "field from an Optional one; the two struct converters reject a null token themselves, and "
            + "this is what gives the third member of the family the same posture");
    }

    [Fact]
    public void AddDamlConverters_should_register_every_converter_in_All()
    {
        var options = new JsonSerializerOptions().AddDamlConverters();

        options.Converters.Should().Contain(c => c is ContractIdJsonConverterFactory);
        options.Converters.Select(c => c.GetType().Name).Should().BeEquivalentTo(
            [
                "PartyJsonConverter",
                "ContractIdJsonConverterFactory",
                "SynchronizerIdJsonConverter",
                "CommandIdJsonConverter",
                "ChoiceNameJsonConverter",
                "WorkflowIdJsonConverter",
                "LedgerOffsetJsonConverter",
                "SubmitterInfoJsonConverter",
                "EquatableArrayJsonConverterFactory",
                "SetJsonConverterFactory",
                "MapJsonConverterFactory",
                "NonEmptyJsonConverterFactory",
                "OptionalJsonConverterFactory",
                "EitherJsonConverterFactory",
                "ContractStreamEventJsonConverterFactory",
                "InterfaceStreamEventJsonConverterFactory",
                "TransportStatusJsonConverterFactory",
                "DamlUnitJsonConverter",
                "DamlVariantJsonConverterFactory",
            ],
            "naming the converters is what catches one silently dropping out of DamlJsonConverters.All, "
            + "which a count compared against All itself cannot");
    }

    [Fact]
    public void AddDamlConverters_should_keep_the_bare_string_wire_shape_for_a_host_composed_options()
    {
        var options = new JsonSerializerOptions().AddDamlConverters();

        JsonSerializer.Serialize(new ContractId<Marker>("00abc"), options).Should().Be("\"00abc\"");
        JsonSerializer.Deserialize<ContractId<Marker>>("\"00abc\"", options)!.Value.Should().Be("00abc");
    }

    [Fact]
    public void ContractIdJsonConverterFactory_should_reject_an_unrelated_type()
    {
        var factory = new ContractIdJsonConverterFactory();

        factory.CanConvert(typeof(Party)).Should().BeFalse();
        var create = () => factory.CreateConverter(typeof(Party), new JsonSerializerOptions());
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ContractIdJsonConverterFactory_should_reject_the_abstract_erased_base()
    {
        var factory = new ContractIdJsonConverterFactory();

        factory.CanConvert(typeof(Daml.Runtime.Contracts.ContractId)).Should().BeFalse(
            "the erased base is abstract, so there is nothing for Read to construct");
    }

    private sealed record Marker : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "Marker");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }
}
