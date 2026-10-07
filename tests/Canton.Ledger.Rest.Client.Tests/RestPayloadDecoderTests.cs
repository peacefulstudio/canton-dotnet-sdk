// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Pins how the REST payload decoder maps each outcome of the generated type registry: a registered type
/// decodes, an unregistered one and an ambiguous one are refused with the exception texts callers read.
/// Every group of fixtures owns a distinct module and entity so the process-wide registry never lets one
/// test's registrations change another's lookup.
/// </summary>
public sealed class RestPayloadDecoderTests
{
    private const string LoadAdvice =
        "load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.";

    private const string MissingTypeAdvice =
        LoadAdvice + " A host without a deps.json registers each generated assembly itself, once, with RuntimeHelpers.RunModuleConstructor(typeof(AnyGeneratedType).Module.ModuleHandle).";

    private const string Fixtures = "Canton.Ledger.Rest.Client.Tests.RestPayloadDecoderTests";

    [ModuleInitializer]
    internal static void RegisterFixtures()
    {
        GeneratedTypeReaders.ForRecord<AlphaTemplate>();
        GeneratedTypeReaders.ForRecord<BetaTemplate>();
        GeneratedTypeReaders.ForRecord<FallbackTemplate>();
        GeneratedTypeReaders.ForKey<FallbackTemplate, Party>();
        GeneratedTypeReaders.ForChoices<FallbackTemplate>();
        GeneratedTypeReaders.ForRecord<DivergentOne>();
        GeneratedTypeReaders.ForChoices<DivergentOne>();
        GeneratedTypeReaders.ForRecord<DivergentTwo>();
        GeneratedTypeReaders.ForChoices<DivergentTwo>();
        GeneratedTypeReaders.ForRecord<SameIdOne>();
        GeneratedTypeReaders.ForChoices<SameIdOne>();
        GeneratedTypeReaders.ForRecord<SameIdTwo>();
        GeneratedTypeReaders.ForChoices<SameIdTwo>();
        GeneratedTypeReaders.ForRecord<KeylessTemplate>();
        GeneratedTypeReaders.ForChoices<IfaceAlpha>();
        GeneratedTypeReaders.ForChoices<IfaceDivergentOne>();
        GeneratedTypeReaders.ForChoices<IfaceDivergentTwo>();
    }

    [Fact]
    public void CreatePayloadOf_prefers_the_template_declaring_the_exact_package_id_over_a_same_named_one()
    {
        var beta = RestPayloadDecoder.CreatePayloadOf(
            CreatedEvent("exact-b:Exact.Mod:Thing", """{"beta": "b"}"""), new RuntimeIdentifier("exact-b", "Exact.Mod", "Thing"));
        var alpha = RestPayloadDecoder.CreatePayloadOf(
            CreatedEvent("exact-a:Exact.Mod:Thing", """{"alpha": "a"}"""), new RuntimeIdentifier("exact-a", "Exact.Mod", "Thing"));

        beta.Arguments.GetRequiredField("beta").Should().Be(new DamlText("b"));
        beta.Undecoded.Should().BeNull();
        alpha.Arguments.GetRequiredField("alpha").Should().Be(new DamlText("a"));
    }

    [Fact]
    public void CreatePayloadOf_resolves_the_one_template_declaring_the_module_and_entity_under_another_package_id()
    {
        var created = CreatedEvent("upgraded-pkg:Fallback.Mod:Thing", """{"owner": "party::alice"}""");

        var payload = RestPayloadDecoder.CreatePayloadOf(created, new RuntimeIdentifier("upgraded-pkg", "Fallback.Mod", "Thing"));

        payload.Arguments.GetRequiredField("owner").Should().Be(new DamlParty("party::alice"));
    }

    [Fact]
    public void CreatePayloadOf_carries_the_create_argument_when_templates_of_two_other_packages_declare_the_module_and_entity()
    {
        var created = CreatedEvent("upgraded-pkg:Divergent.Mod:Thing", """{"owner":"alice::ns"}""");
        var templateId = new RuntimeIdentifier("upgraded-pkg", "Divergent.Mod", "Thing");

        var payload = RestPayloadDecoder.CreatePayloadOf(created, templateId);

        payload.Arguments.Should().Be(new DamlRecord(templateId, []));
        payload.Undecoded.Should().Be(new DamlUndecodedJson("""{"owner":"alice::ns"}"""));
    }

    [Fact]
    public void CreatePayloadOf_carries_the_create_argument_when_two_generated_types_claim_the_template_id()
    {
        var created = CreatedEvent("same-pkg:Same.Mod:Thing", """{"owner":"alice::ns"}""");
        var templateId = new RuntimeIdentifier("same-pkg", "Same.Mod", "Thing");

        var payload = RestPayloadDecoder.CreatePayloadOf(created, templateId);

        payload.Arguments.Should().Be(new DamlRecord(templateId, []));
        payload.Undecoded.Should().Be(new DamlUndecodedJson("""{"owner":"alice::ns"}"""));
    }

    [Fact]
    public void CreatePayloadOf_carries_the_create_argument_when_no_generated_type_is_registered()
    {
        var created = CreatedEvent("absent-pkg:Absent.Mod:Thing", """{"amount":"12.5","owner":"alice::ns"}""");
        var templateId = new RuntimeIdentifier("absent-pkg", "Absent.Mod", "Thing");

        var payload = RestPayloadDecoder.CreatePayloadOf(created, templateId);

        payload.Arguments.Should().Be(new DamlRecord(templateId, []));
        payload.Undecoded.Should().Be(new DamlUndecodedJson("""{"amount":"12.5","owner":"alice::ns"}"""));
    }

    [Fact]
    public void ContractKeyOf_decodes_the_key_a_template_registers()
    {
        var created = CreatedEvent("fallback-pkg:Fallback.Mod:Thing", """{"owner": "party::alice"}""", contractKeyJson: "\"party::alice\"");

        var key = RestPayloadDecoder.ContractKeyOf(created, new RuntimeIdentifier("fallback-pkg", "Fallback.Mod", "Thing"));

        key!.Value.Should().Be(new DamlParty("party::alice"));
    }

    [Fact]
    public void ContractKeyOf_refuses_a_key_for_a_template_that_declares_none()
    {
        var created = CreatedEvent("keyless-pkg:Keyless.Mod:Thing", "{}", contractKeyJson: "\"party::alice\"");

        var refusal = FluentActions.Invoking(() => RestPayloadDecoder.ContractKeyOf(
                created, new RuntimeIdentifier("keyless-pkg", "Keyless.Mod", "Thing")))
            .Should().Throw<TemplateTypeRequiredException>().Which;

        refusal.TypeId.Should().Be("keyless-pkg:Keyless.Mod:Thing");
        refusal.Message.Should().Be(
            $"A contract key arrived for 'keyless-pkg:Keyless.Mod:Thing', but the loaded generated template {Fixtures}+KeylessTemplate declares no contract key, so it was likely generated for a different version of the package; {LoadAdvice}");
    }

    [Fact]
    public void ContractKeyOf_carries_the_key_when_two_generated_types_claim_the_template_id()
    {
        var created = CreatedEvent("same-pkg:Same.Mod:Thing", "{}", contractKeyJson: "\"party::alice\"");

        var key = RestPayloadDecoder.ContractKeyOf(created, new RuntimeIdentifier("same-pkg", "Same.Mod", "Thing"));

        key!.Value.Should().Be(new DamlUndecodedJson("\"party::alice\""));
        key.TemplateId.Should().Be(new RuntimeIdentifier("same-pkg", "Same.Mod", "Thing"));
    }

    [Fact]
    public void ContractKeyOf_carries_the_key_when_no_generated_type_is_registered()
    {
        var created = CreatedEvent("absent-pkg:Absent.Mod:Thing", "{}", contractKeyJson: """{"_1":"alice::ns","_2":"7"}""");

        var key = RestPayloadDecoder.ContractKeyOf(created, new RuntimeIdentifier("absent-pkg", "Absent.Mod", "Thing"));

        key!.Value.Should().Be(new DamlUndecodedJson("""{"_1":"alice::ns","_2":"7"}"""));
    }

    [Fact]
    public void ContractKeyOf_returns_no_key_when_the_event_carries_none_for_an_unregistered_template()
    {
        var created = CreatedEvent("absent-pkg:Absent.Mod:Thing", "{}");

        RestPayloadDecoder.ContractKeyOf(created, new RuntimeIdentifier("absent-pkg", "Absent.Mod", "Thing")).Should().BeNull();
    }

    [Fact]
    public void ExercisePayloadsOf_decodes_a_choice_a_template_declares()
    {
        var exercised = ExercisedEvent("fallback-pkg:Fallback.Mod:Thing", "Split");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("fallback-pkg", "Fallback.Mod", "Thing"));

        payloads.Argument.Should().Be(DamlUnit.Instance);
        payloads.Result.Should().Be(new DamlInt64(7));
    }

    [Fact]
    public void ExercisePayloadsOf_resolves_the_one_template_declaring_the_module_and_entity_under_another_package_id()
    {
        var exercised = ExercisedEvent("upgraded-pkg:Fallback.Mod:Thing", "Split");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("upgraded-pkg", "Fallback.Mod", "Thing"));

        payloads.Result.Should().Be(new DamlInt64(7));
    }

    [Fact]
    public void ExercisePayloadsOf_carries_the_payloads_when_the_template_declares_no_such_choice()
    {
        var exercised = ExercisedEvent("fallback-pkg:Fallback.Mod:Thing", "Merge");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("fallback-pkg", "Fallback.Mod", "Thing"));

        payloads.Argument.Should().Be(new DamlUndecodedJson("{}"));
        payloads.Result.Should().Be(new DamlUndecodedJson("\"7\""));
    }

    [Fact]
    public void ExercisePayloadsOf_carries_the_payloads_when_the_template_id_is_claimed_by_two_generated_types()
    {
        var exercised = ExercisedEvent("same-pkg:Same.Mod:Thing", "Split");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("same-pkg", "Same.Mod", "Thing"));

        payloads.Argument.Should().Be(new DamlUndecodedJson("{}"));
        payloads.Result.Should().Be(new DamlUndecodedJson("\"7\""));
    }

    [Fact]
    public void ExercisePayloadsOf_carries_the_payloads_when_templates_of_two_other_packages_declare_the_module_and_entity()
    {
        var exercised = ExercisedEvent("upgraded-pkg:Divergent.Mod:Thing", "Split");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("upgraded-pkg", "Divergent.Mod", "Thing"));

        payloads.Argument.Should().Be(new DamlUndecodedJson("{}"));
        payloads.Result.Should().Be(new DamlUndecodedJson("\"7\""));
    }

    [Fact]
    public void ExercisePayloadsOf_carries_the_payloads_when_no_template_is_registered()
    {
        var exercised = ExercisedEvent("absent-pkg:Absent.Mod:Thing", "Honor", exerciseResultJson: """{"status":"done"}""");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("absent-pkg", "Absent.Mod", "Thing"));

        payloads.Argument.Should().Be(new DamlUndecodedJson("{}"));
        payloads.Result.Should().Be(new DamlUndecodedJson("""{"status":"done"}"""));
    }

    [Fact]
    public void ExercisePayloadsOf_names_the_template_when_the_event_carries_an_empty_choice()
    {
        var exercised = ExercisedEvent("fallback-pkg:Fallback.Mod:Thing", "");

        FluentActions.Invoking(() => RestPayloadDecoder.ExercisePayloadsOf(
                exercised, new RuntimeIdentifier("fallback-pkg", "Fallback.Mod", "Thing")))
            .Should().Throw<TemplateTypeRequiredException>()
            .Which.Message.Should().Be(
                $"No generated type is loaded for choice '' of 'fallback-pkg:Fallback.Mod:Thing'; {MissingTypeAdvice}");
    }

    [Fact]
    public void ExercisePayloadsOf_decodes_a_choice_an_interface_declares()
    {
        var exercised = ExercisedEvent("impl-pkg:Impl.Mod:Thing", "Split", interfaceId: "iface-a:Iface.Mod:IThing");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("impl-pkg", "Impl.Mod", "Thing"));

        payloads.Result.Should().Be(new DamlInt64(7));
    }

    [Fact]
    public void ExercisePayloadsOf_resolves_the_one_interface_declaring_the_module_and_entity_under_another_package_id()
    {
        var exercised = ExercisedEvent("impl-pkg:Impl.Mod:Thing", "Split", interfaceId: "upgraded-pkg:Iface.Mod:IThing");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("impl-pkg", "Impl.Mod", "Thing"));

        payloads.Result.Should().Be(new DamlInt64(7));
    }

    [Fact]
    public void ExercisePayloadsOf_carries_the_payloads_when_the_interface_declares_no_such_choice()
    {
        var exercised = ExercisedEvent("impl-pkg:Impl.Mod:Thing", "Merge", interfaceId: "iface-a:Iface.Mod:IThing");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("impl-pkg", "Impl.Mod", "Thing"));

        payloads.Argument.Should().Be(new DamlUndecodedJson("{}"));
        payloads.Result.Should().Be(new DamlUndecodedJson("\"7\""));
    }

    [Fact]
    public void ExercisePayloadsOf_carries_the_payloads_when_no_interface_is_registered()
    {
        var exercised = ExercisedEvent("impl-pkg:Impl.Mod:Thing", "Split", interfaceId: "iface-absent:Absent.Mod:IThing");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("impl-pkg", "Impl.Mod", "Thing"));

        payloads.Argument.Should().Be(new DamlUndecodedJson("{}"));
        payloads.Result.Should().Be(new DamlUndecodedJson("\"7\""));
    }

    [Fact]
    public void ExercisePayloadsOf_carries_the_payloads_when_interfaces_of_two_other_packages_declare_the_module_and_entity()
    {
        var exercised = ExercisedEvent("impl-pkg:Impl.Mod:Thing", "Split", interfaceId: "upgraded-pkg:IfaceDivergent.Mod:IThing");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier("impl-pkg", "Impl.Mod", "Thing"));

        payloads.Argument.Should().Be(new DamlUndecodedJson("{}"));
        payloads.Result.Should().Be(new DamlUndecodedJson("\"7\""));
    }

    [Fact]
    public void The_generated_conformance_template_registers_its_key()
    {
        var created = CreatedEvent(
            "e72ec259be271bedbcb0d33f19a47008de832963a569e5a04f1ed7b8efb434e9:ContractKeys:Steward", "{}", contractKeyJson: "\"party::alice\"");

        var key = RestPayloadDecoder.ContractKeyOf(created, Steward.TemplateId);

        key!.Value.Should().Be(new DamlParty("party::alice"));
    }

    [Fact]
    public void The_generated_conformance_template_registers_its_choices()
    {
        var exercised = ExercisedEvent(
            "e72ec259be271bedbcb0d33f19a47008de832963a569e5a04f1ed7b8efb434e9:ContractKeys:Account", "Archive", exerciseResultJson: "{}");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, Account.TemplateId);

        payloads.Result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void The_generated_conformance_interface_registers_its_choices()
    {
        var exercised = ExercisedEvent(
            "e72ec259be271bedbcb0d33f19a47008de832963a569e5a04f1ed7b8efb434e9:RichTypes:Asset",
            "Archive",
            interfaceId: "e72ec259be271bedbcb0d33f19a47008de832963a569e5a04f1ed7b8efb434e9:RichTypes:Holding",
            exerciseResultJson: "{}");

        var payloads = RestPayloadDecoder.ExercisePayloadsOf(exercised, new RuntimeIdentifier(
            "e72ec259be271bedbcb0d33f19a47008de832963a569e5a04f1ed7b8efb434e9", "RichTypes", "Asset"));

        payloads.Result.Should().Be(DamlUnit.Instance);
    }

    private static WireCreatedEvent CreatedEvent(string templateId, string createArgumentJson, string? contractKeyJson = null) =>
        JsonSerializer.Deserialize<WireCreatedEvent>(
            $$"""
            {
              "contractId": "00created",
              "templateId": "{{templateId}}",
              "createArgument": {{createArgumentJson}}
              {{(contractKeyJson is null ? string.Empty : $", \"contractKey\": {contractKeyJson}")}}
            }
            """,
            RestRefitSettings.SerializerOptions)!;

    private static WireExercisedEvent ExercisedEvent(string templateId, string choice, string? interfaceId = null, string exerciseResultJson = "\"7\"") =>
        JsonSerializer.Deserialize<WireExercisedEvent>(
            $$"""
            {
              "contractId": "00exercised",
              "templateId": "{{templateId}}",
              {{(interfaceId is null ? string.Empty : $"\"interfaceId\": \"{interfaceId}\",")}}
              "choice": "{{choice}}",
              "choiceArgument": {},
              "exerciseResult": {{exerciseResultJson}}
            }
            """,
            RestRefitSettings.SerializerOptions)!;

    private static Choice<TOwner, DamlUnit, long> SplitChoice<TOwner>() where TOwner : IDamlType => new()
    {
        Name = new ChoiceName("Split"),
        Consuming = false,
        ArgumentEncoder = unit => unit,
        ArgumentDecoder = value => value.As<DamlUnit>(),
        ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
        ResultDecoder = result => result.As<DamlInt64>().Value,
        ResultJsonReader = DamlLfJsonDecoders.ReadInt64,
    };

    private sealed record AlphaTemplate : ITemplate, IDamlRecord<AlphaTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("exact-a", "Exact.Mod", "Thing");
        public static string PackageId => "exact-a";
        public static string PackageName => "exact";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("alpha", DamlLfJsonDecoders.ReadText));

        public static AlphaTemplate FromRecord(DamlRecord record) => new();
    }

    private sealed record BetaTemplate : ITemplate, IDamlRecord<BetaTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("exact-b", "Exact.Mod", "Thing");
        public static string PackageId => "exact-b";
        public static string PackageName => "exact";
        public static Version PackageVersion { get; } = new(2, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("beta", DamlLfJsonDecoders.ReadText));

        public static BetaTemplate FromRecord(DamlRecord record) => new();
    }

    private sealed record FallbackTemplate
        : ITemplate, IDamlRecord<FallbackTemplate>, IHasKey<FallbackTemplate, Party>, IHasChoices<FallbackTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("fallback-pkg", "Fallback.Mod", "Thing");
        public static string PackageId => "fallback-pkg";
        public static string PackageName => "fallback";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<FallbackTemplate, Party> Key { get; } = new()
        {
            KeyEncoder = owner => owner.ToDamlValue(),
            KeyDecoder = value => Party.FromDamlValue(value.As<DamlParty>()),
            KeyJsonReader = DamlLfJsonDecoders.ReadParty,
        };

        public static Choice<FallbackTemplate, DamlUnit, long> ChoiceSplit { get; } = SplitChoice<FallbackTemplate>();
        public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceSplit];
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", DamlLfJsonDecoders.ReadParty));

        public static FallbackTemplate FromRecord(DamlRecord record) => new();
    }

    private sealed record DivergentOne : ITemplate, IDamlRecord<DivergentOne>, IHasChoices<DivergentOne>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("divergent-a", "Divergent.Mod", "Thing");
        public static string PackageId => "divergent-a";
        public static string PackageName => "divergent";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public static Choice<DivergentOne, DamlUnit, long> ChoiceSplit { get; } = SplitChoice<DivergentOne>();
        public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceSplit];
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);

        public static DivergentOne FromRecord(DamlRecord record) => new();
    }

    private sealed record DivergentTwo : ITemplate, IDamlRecord<DivergentTwo>, IHasChoices<DivergentTwo>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("divergent-b", "Divergent.Mod", "Thing");
        public static string PackageId => "divergent-b";
        public static string PackageName => "divergent";
        public static Version PackageVersion { get; } = new(2, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public static Choice<DivergentTwo, DamlUnit, long> ChoiceSplit { get; } = SplitChoice<DivergentTwo>();
        public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceSplit];
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);

        public static DivergentTwo FromRecord(DamlRecord record) => new();
    }

    private sealed record SameIdOne : ITemplate, IDamlRecord<SameIdOne>, IHasChoices<SameIdOne>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("same-pkg", "Same.Mod", "Thing");
        public static string PackageId => "same-pkg";
        public static string PackageName => "same";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public static Choice<SameIdOne, DamlUnit, long> ChoiceSplit { get; } = SplitChoice<SameIdOne>();
        public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceSplit];
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);

        public static SameIdOne FromRecord(DamlRecord record) => new();
    }

    private sealed record SameIdTwo : ITemplate, IDamlRecord<SameIdTwo>, IHasChoices<SameIdTwo>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("same-pkg", "Same.Mod", "Thing");
        public static string PackageId => "same-pkg";
        public static string PackageName => "same";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public static Choice<SameIdTwo, DamlUnit, long> ChoiceSplit { get; } = SplitChoice<SameIdTwo>();
        public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceSplit];
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);

        public static SameIdTwo FromRecord(DamlRecord record) => new();
    }

    private sealed record KeylessTemplate : ITemplate, IDamlRecord<KeylessTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("keyless-pkg", "Keyless.Mod", "Thing");
        public static string PackageId => "keyless-pkg";
        public static string PackageName => "keyless";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);

        public static KeylessTemplate FromRecord(DamlRecord record) => new();
    }

    private sealed record IfaceAlpha : IDamlInterface, IHasChoices<IfaceAlpha>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-a", "Iface.Mod", "IThing");
        public static string PackageId => "iface-a";
        public static string PackageName => "iface";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public static Choice<IfaceAlpha, DamlUnit, long> ChoiceSplit { get; } = SplitChoice<IfaceAlpha>();
        public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceSplit];
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private sealed record IfaceDivergentOne : IDamlInterface, IHasChoices<IfaceDivergentOne>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-div-a", "IfaceDivergent.Mod", "IThing");
        public static string PackageId => "iface-div-a";
        public static string PackageName => "iface-divergent";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public static Choice<IfaceDivergentOne, DamlUnit, long> ChoiceSplit { get; } = SplitChoice<IfaceDivergentOne>();
        public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceSplit];
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private sealed record IfaceDivergentTwo : IDamlInterface, IHasChoices<IfaceDivergentTwo>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-div-b", "IfaceDivergent.Mod", "IThing");
        public static string PackageId => "iface-div-b";
        public static string PackageName => "iface-divergent";
        public static Version PackageVersion { get; } = new(2, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public static Choice<IfaceDivergentTwo, DamlUnit, long> ChoiceSplit { get; } = SplitChoice<IfaceDivergentTwo>();
        public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceSplit];
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }
}
