// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class ExercisedResultDecoderTests
{
    private static readonly ChoiceName Pick = new("Pick");

    private static readonly Identifier VehicleId = new("pkg-vehicle", "Fleet", "Vehicle");

    private static readonly Identifier ParkableId = new("pkg-parkable", "Fleet", "Parkable");

    public sealed record Picked(string Text) : IDamlRecord<Picked>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("text", new DamlText(Text)));

        public static Picked FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("text").As<DamlText>().Value);

        public static DamlRecord __ReadDamlLfJson(System.Text.Json.JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    public sealed class VehicleTemplate : IDamlType, IHasChoices<VehicleTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(VehicleId, DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<VehicleTemplate, Picked>(Pick, value => new Picked("template:" + Picked.FromRecord(value.As<DamlRecord>()).Text))];
    }

    public sealed class ParkableInterface : IDamlType, IHasChoices<ParkableInterface>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(ParkableId, DamlTypeKind.Interface, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<ParkableInterface, Picked>(Pick, value => new Picked("interface:" + Picked.FromRecord(value.As<DamlRecord>()).Text))];
    }

    public sealed class OpaqueTemplate : IDamlType, IHasChoices<OpaqueTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-opaque", "Fleet", "Opaque"), DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<OpaqueTemplate, DamlValue>(Pick, _ => new DamlText("from-descriptor"))];
    }

    public sealed class TwinOneTemplate : IDamlType, IHasChoices<TwinOneTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-twin", "Fleet", "Twin"), DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<TwinOneTemplate, Picked>(Pick, value => new Picked("twin-one:" + Picked.FromRecord(value.As<DamlRecord>()).Text))];
    }

    public sealed class TwinTwoTemplate : IDamlType, IHasChoices<TwinTwoTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-twin", "Fleet", "Twin"), DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<TwinTwoTemplate, Picked>(Pick, value => new Picked("twin-two:" + Picked.FromRecord(value.As<DamlRecord>()).Text))];
    }

    public sealed class VersionOneTemplate : IDamlType, IHasChoices<VersionOneTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-v1", "Fleet", "Versioned"), DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<VersionOneTemplate, Picked>(Pick, value => new Picked("v1:" + Picked.FromRecord(value.As<DamlRecord>()).Text))];
    }

    public sealed class VersionTwoTemplate : IDamlType, IHasChoices<VersionTwoTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-v2", "Fleet", "Versioned"), DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<VersionTwoTemplate, Picked>(Pick, value => new Picked("v2:" + Picked.FromRecord(value.As<DamlRecord>()).Text))];
    }

    public sealed class RefusingTemplate : IDamlType, IHasChoices<RefusingTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-refusing", "Fleet", "Refusing"), DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<RefusingTemplate, Picked>(Pick, _ => throw new FormatException("descriptor refused the value"))];
    }

    public sealed class OptionalTemplate : IDamlType, IHasChoices<OptionalTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-optional", "Fleet", "Optional"), DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
            [ChoiceReturning<OptionalTemplate, string?>(Pick, _ => null)];
    }

    private static Choice<TOwner, DamlUnit, TResult> ChoiceReturning<TOwner, TResult>(
        ChoiceName name, Func<DamlValue, TResult> resultDecoder)
        where TOwner : IDamlType => new()
        {
            Name = name,
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = resultDecoder,
            ArgumentJsonReader = (_, _) => throw new NotSupportedException(),
            ResultJsonReader = (_, _) => throw new NotSupportedException(),
        };

    private static ExercisedEvent ExercisedWith(
        Identifier templateId, Identifier? interfaceId, DamlValue result, ChoiceName? choice = null) =>
        new(
            "00vehicle",
            templateId,
            interfaceId,
            choice ?? Pick,
            DamlUnit.Instance,
            result,
            false,
            [(Party)"alice::ns1"],
            [(Party)"alice::ns1"]);

    [Fact]
    public void Decode_decodes_through_the_template_choice_descriptor_when_the_template_resolves()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<VehicleTemplate>();

        var decoded = ExercisedResultDecoder.Decode<Picked>(
            ExercisedWith(VehicleId, null, new Picked("x").ToRecord()), registry);

        decoded.Should().Be(new Picked("template:x"));
    }

    public sealed class CountingTemplate : IDamlType, IHasChoices<CountingTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-counting", "Fleet", "Counting"), DamlTypeKind.Template, "fleet");

        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            new Choice<CountingTemplate, DamlUnit, long>
            {
                Name = Pick,
                Consuming = false,
                ArgumentEncoder = _ => DamlUnit.Instance,
                ArgumentDecoder = _ => DamlUnit.Instance,
                ResultDecoder = value => value.As<DamlInt64>().Value,
                ArgumentJsonReader = (_, _) => throw new NotSupportedException(),
                ResultJsonReader = DamlLfJsonDecoders.ReadInt64,
            },
        ];
    }

    [Fact]
    public void Decode_decodes_a_carried_result_through_the_resolved_choice_json_reader()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<CountingTemplate>();
        var counting = new Identifier("pkg-counting", "Fleet", "Counting");

        var decoded = ExercisedResultDecoder.Decode<long>(
            ExercisedWith(counting, null, new DamlUndecodedJson("\"42\"")), registry);

        decoded.Should().Be(42L);
    }

    [Fact]
    public void Decode_decodes_a_carried_result_through_FromDamlValue_when_no_choice_resolves()
    {
        var decoded = ExercisedResultDecoder.Decode<long>(
            ExercisedWith(VehicleId, null, new DamlUndecodedJson("\"42\"")), new GeneratedTypeRegistry());

        decoded.Should().Be(42L);
    }

    [Fact]
    public void Decode_decodes_through_the_interface_choice_descriptor_named_by_the_event_interface_id()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<ParkableInterface>();

        var decoded = ExercisedResultDecoder.Decode<Picked>(
            ExercisedWith(VehicleId, ParkableId, new Picked("x").ToRecord()), registry);

        decoded.Should().Be(new Picked("interface:x"));
    }

    [Fact]
    public void Decode_prefers_the_interface_choice_over_a_template_choice_of_the_same_name()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<VehicleTemplate>();
        registry.ForChoices<ParkableInterface>();

        var decoded = ExercisedResultDecoder.Decode<Picked>(
            ExercisedWith(VehicleId, ParkableId, new Picked("x").ToRecord()), registry);

        decoded.Should().Be(new Picked("interface:x"));
    }

    [Fact]
    public void Decode_returns_the_raw_value_for_a_DamlValue_result_without_consulting_the_registry()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<OpaqueTemplate>();

        var decoded = ExercisedResultDecoder.Decode<DamlValue>(
            ExercisedWith(new Identifier("pkg-opaque", "Fleet", "Opaque"), null, new DamlText("raw")), registry);

        decoded.Should().Be(new DamlText("raw"));
    }

    [Fact]
    public void Decode_returns_the_raw_value_for_a_DamlValue_subtype_result_without_consulting_the_registry()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<OpaqueTemplate>();

        var decoded = ExercisedResultDecoder.Decode<DamlText>(
            ExercisedWith(new Identifier("pkg-opaque", "Fleet", "Opaque"), null, new DamlText("raw")), registry);

        decoded.Should().Be(new DamlText("raw"));
    }

    [Fact]
    public void Decode_falls_back_to_FromDamlValue_when_the_descriptor_result_type_is_not_assignable_to_TResult()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<RefusingTemplate>();

        var decoded = ExercisedResultDecoder.Decode<string>(
            ExercisedWith(new Identifier("pkg-refusing", "Fleet", "Refusing"), null, new DamlText("plain")), registry);

        decoded.Should().Be("plain");
    }

    [Fact]
    public void Decode_decodes_through_FromDamlValue_when_no_choice_is_registered()
    {
        var decoded = ExercisedResultDecoder.Decode<Picked>(
            ExercisedWith(VehicleId, null, new Picked("x").ToRecord()), new GeneratedTypeRegistry());

        decoded.Should().Be(new Picked("x"));
    }

    [Fact]
    public void Decode_decodes_through_FromDamlValue_when_the_lookup_is_ambiguous()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<TwinOneTemplate>();
        registry.ForChoices<TwinTwoTemplate>();

        var decoded = ExercisedResultDecoder.Decode<Picked>(
            ExercisedWith(new Identifier("pkg-twin", "Fleet", "Twin"), null, new Picked("x").ToRecord()), registry);

        decoded.Should().Be(new Picked("x"));
    }

    [Fact]
    public void Decode_decodes_through_the_version_whose_package_id_the_event_names_when_two_versions_are_registered()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<VersionOneTemplate>();
        registry.ForChoices<VersionTwoTemplate>();

        var decoded = ExercisedResultDecoder.Decode<Picked>(
            ExercisedWith(new Identifier("pkg-v2", "Fleet", "Versioned"), null, new Picked("x").ToRecord()), registry);

        decoded.Should().Be(new Picked("v2:x"));
    }

    [Fact]
    public void Decode_decodes_through_FromDamlValue_when_two_versions_are_registered_and_the_event_names_neither()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<VersionOneTemplate>();
        registry.ForChoices<VersionTwoTemplate>();

        var decoded = ExercisedResultDecoder.Decode<Picked>(
            ExercisedWith(new Identifier("pkg-v3", "Fleet", "Versioned"), null, new Picked("x").ToRecord()), registry);

        decoded.Should().Be(new Picked("x"));
    }

    [Fact]
    public void Decode_lets_a_descriptor_failure_surface_as_itself()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<RefusingTemplate>();

        var act = () => ExercisedResultDecoder.Decode<Picked>(
            ExercisedWith(new Identifier("pkg-refusing", "Fleet", "Refusing"), null, new Picked("x").ToRecord()), registry);

        act.Should().Throw<FormatException>().WithMessage("descriptor refused the value");
    }

    [Fact]
    public void Decode_returns_null_when_the_descriptor_decodes_the_result_to_null()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<OptionalTemplate>();

        var decoded = ExercisedResultDecoder.Decode<string?>(
            ExercisedWith(new Identifier("pkg-optional", "Fleet", "Optional"), null, DamlOptional.None), registry);

        decoded.Should().BeNull();
    }

    [Fact]
    public void Decode_names_the_choice_when_no_binding_is_registered_and_FromDamlValue_cannot_decode()
    {
        var act = () => ExercisedResultDecoder.Decode<long>(
            ExercisedWith(VehicleId, null, new DamlText("x")), new GeneratedTypeRegistry());

        act.Should().Throw<NotSupportedException>().WithMessage(
            "Choice 'Pick' of 'pkg-vehicle:Fleet:Vehicle' did not resolve to a generated result decoder: no generated binding for it is registered. "
            + "Decoding its result as System.Int64 through FromDamlValue failed: "
            + "Cannot convert Daml.Runtime.Data.DamlText to System.Int64. Use a DamlValue-derived type as TResult for direct access.");
    }

    [Fact]
    public void Decode_names_the_candidates_when_the_binding_is_ambiguous_and_FromDamlValue_cannot_decode()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<TwinOneTemplate>();
        registry.ForChoices<TwinTwoTemplate>();

        var act = () => ExercisedResultDecoder.Decode<long>(
            ExercisedWith(new Identifier("pkg-twin", "Fleet", "Twin"), null, new DamlText("x")), registry);

        act.Should().Throw<NotSupportedException>().WithMessage(
            "Choice 'Pick' of 'pkg-twin:Fleet:Twin' did not resolve to a generated result decoder: its generated binding is ambiguous between "
            + "Daml.Runtime.Tests.ExercisedResultDecoderTests+TwinOneTemplate, Daml.Runtime.Tests.ExercisedResultDecoderTests+TwinTwoTemplate. "
            + "Decoding its result as System.Int64 through FromDamlValue failed: "
            + "Cannot convert Daml.Runtime.Data.DamlText to System.Int64. Use a DamlValue-derived type as TResult for direct access.");
    }

    [Fact]
    public void Decode_names_both_result_types_when_the_descriptor_result_type_is_not_assignable_and_FromDamlValue_cannot_decode()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<VehicleTemplate>();

        var act = () => ExercisedResultDecoder.Decode<long>(
            ExercisedWith(VehicleId, null, new DamlText("x")), registry);

        act.Should().Throw<NotSupportedException>().WithMessage(
            "Choice 'Pick' of 'pkg-vehicle:Fleet:Vehicle' did not resolve to a generated result decoder: "
            + "its generated result type Daml.Runtime.Tests.ExercisedResultDecoderTests+Picked is not assignable to System.Int64. "
            + "Decoding its result as System.Int64 through FromDamlValue failed: "
            + "Cannot convert Daml.Runtime.Data.DamlText to System.Int64. Use a DamlValue-derived type as TResult for direct access.");
    }
}
