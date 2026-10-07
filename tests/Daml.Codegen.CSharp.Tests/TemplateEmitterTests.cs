// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class TemplateEmitterTests
{
    private const string LocalPackageId = "test-package-id";
    private const string ModuleName = "Test.Module";

    private static DamlPackage Package(DamlModule module, Version? version = null, string? upgradedPackageId = null) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = version ?? new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
            UpgradedPackageId = upgradedPackageId,
        };

    private static CodeGenOptions Options(bool generateXmlDocs = true) =>
        new()
        {
            NamespacePrefix = "Test.Package",
            GenerateXmlDocs = generateXmlDocs,
        };

    private static string EmitTemplate(
        TemplateFixture fixture,
        DamlDataType[]? dataTypes = null,
        DamlInterface[]? interfaces = null,
        CodeGenOptions? options = null,
        Version? version = null,
        string? upgradedPackageId = null,
        ILogger? logger = null)
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [fixture.Template],
            DataTypes = [.. dataTypes ?? [], .. (interfaces ?? []).Select(iface => RecordDataType(iface.Name))],
            Interfaces = interfaces ?? [],
        };
        options ??= Options();
        var package = Package(module, version, upgradedPackageId);
        var resolution = RealResolution.Of(package, options);
        var context = resolution.Context;
        var resolver = resolution.Resolver;
        var mapper = new DamlTypeMapper(context, resolver);
        var party = new PartyAnalysis();
        var recordSerialization = new RecordSerializationEmitter(context, resolver, options, mapper);
        var choiceEmitter = new ChoiceEmitter(context, resolver, options, mapper, party);
        var submissionExtensions = new SubmissionExtensionsEmitter(options, party);
        var emitter = new TemplateEmitter(context, resolver, recordSerialization, choiceEmitter, submissionExtensions, options, logger);
        var sb = new StringBuilder();
        emitter.WriteTemplateType(new IndentWriter(sb), package, module, fixture.Template, fixture.Fields);
        return sb.ToString();
    }

    private static string EmitTemplate(
        DamlTemplate template,
        DamlDataType[]? dataTypes = null,
        DamlInterface[]? interfaces = null,
        CodeGenOptions? options = null,
        Version? version = null,
        string? upgradedPackageId = null,
        ILogger? logger = null) =>
        EmitTemplate(new TemplateFixture(template, []), dataTypes, interfaces, options, version, upgradedPackageId, logger);

    private sealed record TemplateFixture(DamlTemplate Template, IReadOnlyList<DamlFieldDefinition> Fields);

    private static TemplateFixture Template(
        string name,
        IReadOnlyList<DamlFieldDefinition>? fields = null,
        IReadOnlyList<DamlChoice>? choices = null,
        DamlType? key = null) =>
        new(
            new DamlTemplate
            {
                Name = name,
                Choices = choices ?? [],
                Key = key,
            },
            fields ?? []);

    private static DamlFieldDefinition Field(string name, DamlPrimitive primitive) =>
        new(name, new DamlPrimitiveType(primitive));

    private static DamlDataType RecordDataType(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    [Fact]
    public void TemplateEmitter_emits_the_template_record_with_the_ITemplate_facet()
    {
        var output = EmitTemplate(Template("SimpleTemplate", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain("public sealed partial record SimpleTemplate");
        output.Should().Contain(": global::Daml.Runtime.Contracts.ITemplate");
    }

    [Fact]
    public void TemplateEmitter_adds_the_IImplements_facet_when_the_template_implements_an_interface()
    {
        var output = EmitTemplate(
            new DamlTemplate
            {
                Name = "Vault",
                Choices = [],
                Implements = [new DamlTypeRef(LocalPackageId, ModuleName, "Asset")],
            },
            interfaces: [new DamlInterface { Name = "Asset", Choices = [] }]);

        output.Should().Contain("IImplements<global::Test.Package.Test.Module.IAsset>");
    }

    [Fact]
    public void TemplateEmitter_adds_the_IHasKey_facet_when_the_template_declares_a_key()
    {
        var output = EmitTemplate(
            new DamlTemplate
            {
                Name = "KeyedVault",
                Choices = [],
                Key = new DamlPrimitiveType(DamlPrimitive.Party),
                Implements = [new DamlTypeRef(LocalPackageId, ModuleName, "Asset")],
            },
            interfaces: [new DamlInterface { Name = "Asset", Choices = [] }]);

        output.Should().Contain(": global::Daml.Runtime.Contracts.ITemplate, global::Daml.Runtime.Contracts.IImplements<global::Test.Package.Test.Module.IAsset>, global::Daml.Runtime.Contracts.IHasKey<KeyedVault, global::Daml.Runtime.Data.Party>");
    }

    [Fact]
    public void TemplateEmitter_keeps_a_key_less_template_off_the_IHasKey_facet()
    {
        var output = EmitTemplate(Template("Keyless", [Field("owner", DamlPrimitive.Party)]));

        output.Should().NotContain("IHasKey");
        output.Should().NotContain("KeyDescriptor");
    }

    [Fact]
    public void TemplateEmitter_emits_the_key_witness_carrying_the_codec_for_a_record_key()
    {
        var output = EmitTemplate(
            Template(
                "Account",
                [Field("custodian", DamlPrimitive.Party)],
                key: new DamlTypeRef(LocalPackageId, ModuleName, "AccountKey")),
            dataTypes: [RecordDataType("AccountKey", Field("custodian", DamlPrimitive.Party))]);

        output.Should().Contain(
            ": global::Daml.Runtime.Contracts.ITemplate, global::Daml.Runtime.Contracts.IHasKey<Account, global::Test.Package.Test.Module.AccountKey>");
        output.Should().Contain(
            "public static global::Daml.Runtime.Contracts.KeyDescriptor<Account, global::Test.Package.Test.Module.AccountKey> Key { get; } =");
        output.Should().Contain(
            "KeyEncoder = key => key.ToRecord(),");
        output.Should().Contain(
            "KeyDecoder = value => global::Test.Package.Test.Module.AccountKey.FromRecord(value.As<global::Daml.Runtime.Data.DamlRecord>()),");
        output.Should().Contain(
            "KeyJsonReader = (json, context) => global::Test.Package.Test.Module.AccountKey.__ReadDamlLfJson(json, context),");
    }

    [Fact]
    public void TemplateEmitter_emits_the_key_witness_for_a_key_that_is_not_a_record()
    {
        var output = EmitTemplate(
            Template(
                "Steward",
                [Field("steward", DamlPrimitive.Party)],
                key: new DamlPrimitiveType(DamlPrimitive.Party)));

        output.Should().Contain(": global::Daml.Runtime.Contracts.ITemplate, global::Daml.Runtime.Contracts.IHasKey<Steward, global::Daml.Runtime.Data.Party>");
        output.Should().Contain("public static global::Daml.Runtime.Contracts.KeyDescriptor<Steward, global::Daml.Runtime.Data.Party> Key { get; } =");
        output.Should().Contain(
            "KeyEncoder = key => key.ToDamlValue(),");
        output.Should().Contain(
            "KeyDecoder = value => global::Daml.Runtime.Data.Party.FromDamlValue(value.As<global::Daml.Runtime.Data.DamlParty>()),");
        output.Should().Contain(
            "KeyJsonReader = (json, context) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadParty(json, context),");
    }

    [Fact]
    public void TemplateEmitter_hides_the_key_witness_behind_the_facet_when_a_field_takes_the_name()
    {
        var logger = new CapturingLogger();

        var output = EmitTemplate(
            Template(
                "Locker",
                [Field("key", DamlPrimitive.Text)],
                key: new DamlPrimitiveType(DamlPrimitive.Party)),
            logger: logger);

        output.Should().Contain(
            "static global::Daml.Runtime.Contracts.KeyDescriptor<Locker, global::Daml.Runtime.Data.Party> global::Daml.Runtime.Contracts.IHasKey<Locker, global::Daml.Runtime.Data.Party>.Key { get; } =");
        output.Should().Contain("KeyEncoder = key => key.ToDamlValue(),");
        output.Should().Contain("KeyDecoder = value => global::Daml.Runtime.Data.Party.FromDamlValue(value.As<global::Daml.Runtime.Data.DamlParty>()),");
        output.Should().NotContain("public static global::Daml.Runtime.Contracts.KeyDescriptor<Locker, global::Daml.Runtime.Data.Party> Key");
        output.Should().Contain("string Key");
    }

    [Fact]
    public void TemplateEmitter_hides_the_key_witness_behind_the_facet_when_the_template_is_named_Key()
    {
        var output = EmitTemplate(
            Template(
                "Key",
                [Field("owner", DamlPrimitive.Party)],
                key: new DamlPrimitiveType(DamlPrimitive.Party)));

        output.Should().Contain(
            "static global::Daml.Runtime.Contracts.KeyDescriptor<Key, global::Daml.Runtime.Data.Party> global::Daml.Runtime.Contracts.IHasKey<Key, global::Daml.Runtime.Data.Party>.Key { get; } =");
        output.Should().Contain("KeyEncoder = key => key.ToDamlValue(),");
        output.Should().Contain("KeyDecoder = value => global::Daml.Runtime.Data.Party.FromDamlValue(value.As<global::Daml.Runtime.Data.DamlParty>()),");
        output.Should().NotContain("public static global::Daml.Runtime.Contracts.KeyDescriptor<Key, global::Daml.Runtime.Data.Party> Key");
    }

    [Fact]
    public void TemplateEmitter_warns_when_the_template_name_takes_the_key_witness_name()
    {
        var logger = new CapturingLogger();

        EmitTemplate(
            Template(
                "Key",
                [Field("owner", DamlPrimitive.Party)],
                key: new DamlPrimitiveType(DamlPrimitive.Party)),
            logger: logger);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Test.Module:Key");
    }

    [Fact]
    public void TemplateEmitter_warns_when_a_field_takes_the_key_witness_name()
    {
        var logger = new CapturingLogger();

        EmitTemplate(
            Template(
                "Locker",
                [Field("key", DamlPrimitive.Text)],
                key: new DamlPrimitiveType(DamlPrimitive.Party)),
            logger: logger);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Test.Module:Locker")
            .And.Contain("key");
    }

    [Fact]
    public void TemplateEmitter_hides_the_Choices_witness_behind_the_facet_when_a_field_takes_the_name()
    {
        var logger = new CapturingLogger();

        var output = EmitTemplate(
            Template(
                "Vault",
                [Field("choices", DamlPrimitive.Text)],
                choices:
                [
                    new DamlChoice
                    {
                        Name = "Grant",
                        Consuming = true,
                        ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    },
                ]),
            logger: logger);

        output.Should().Contain(
            "static global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Commands.IChoice> global::Daml.Runtime.Contracts.IHasChoices<Vault>.Choices { get; } = [ChoiceGrant];");
        output.Should().NotContain("public static global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Commands.IChoice> Choices");
        output.Should().Contain("string Choices");
    }

    [Fact]
    public void TemplateEmitter_warns_when_a_field_takes_the_Choices_witness_name()
    {
        var logger = new CapturingLogger();

        EmitTemplate(
            Template(
                "Vault",
                [Field("choices", DamlPrimitive.Text)]),
            logger: logger);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Test.Module:Vault")
            .And.Contain("choices");
    }

    [Fact]
    public void TemplateEmitter_hides_the_Choices_witness_behind_the_facet_when_the_template_is_named_Choices()
    {
        var output = EmitTemplate(
            Template(
                "Choices",
                [Field("owner", DamlPrimitive.Party)],
                choices:
                [
                    new DamlChoice
                    {
                        Name = "Grant",
                        Consuming = true,
                        ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    },
                ]));

        output.Should().Contain(
            "static global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Commands.IChoice> global::Daml.Runtime.Contracts.IHasChoices<Choices>.Choices { get; } = [ChoiceGrant];");
        output.Should().NotContain("public static global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Commands.IChoice> Choices");
    }

    [Fact]
    public void TemplateEmitter_warns_when_the_template_name_takes_the_Choices_witness_name()
    {
        var logger = new CapturingLogger();

        EmitTemplate(
            Template(
                "Choices",
                [Field("owner", DamlPrimitive.Party)]),
            logger: logger);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Test.Module:Choices");
    }

    [Fact]
    public void TemplateEmitter_hides_the_Choices_witness_behind_the_facet_when_a_nested_choice_argument_is_named_Choices()
    {
        var output = EmitTemplate(
            Template(
                "Vault",
                [Field("owner", DamlPrimitive.Party)],
                choices:
                [
                    new DamlChoice
                    {
                        Name = "Choices",
                        Consuming = true,
                        ArgumentType = new DamlTypeRef(LocalPackageId, ModuleName, "ChoicesArgument"),
                        ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    },
                ]),
            dataTypes: [RecordDataType("ChoicesArgument", Field("amount", DamlPrimitive.Int64))]);

        output.Should().Contain(
            "static global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Commands.IChoice> global::Daml.Runtime.Contracts.IHasChoices<Vault>.Choices { get; } = [ChoiceChoices];");
        output.Should().NotContain("public static global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Commands.IChoice> Choices");
    }

    [Fact]
    public void TemplateEmitter_warns_when_a_nested_choice_argument_takes_the_Choices_witness_name()
    {
        var logger = new CapturingLogger();

        EmitTemplate(
            Template(
                "Vault",
                [Field("owner", DamlPrimitive.Party)],
                choices:
                [
                    new DamlChoice
                    {
                        Name = "Choices",
                        Consuming = true,
                        ArgumentType = new DamlTypeRef(LocalPackageId, ModuleName, "ChoicesArgument"),
                        ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    },
                ]),
            dataTypes: [RecordDataType("ChoicesArgument", Field("amount", DamlPrimitive.Int64))],
            logger: logger);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Test.Module:Vault")
            .And.Contain("Choices");
    }

    [Fact]
    public void TemplateEmitter_orders_IImplements_after_the_IUpgradeable_facet_in_the_base_list()
    {
        var output = EmitTemplate(
            new DamlTemplate
            {
                Name = "KeyedUpgradedVault",
                Choices = [],
                Key = new DamlPrimitiveType(DamlPrimitive.Party),
                Implements = [new DamlTypeRef(LocalPackageId, ModuleName, "Asset")],
            },
            interfaces: [new DamlInterface { Name = "Asset", Choices = [] }],
            upgradedPackageId: "old-package-id");

        output.Should().Contain(
            ": global::Daml.Runtime.Contracts.ITemplate, global::Daml.Runtime.Contracts.IUpgradeable, global::Daml.Runtime.Contracts.IImplements<global::Test.Package.Test.Module.IAsset>, global::Daml.Runtime.Contracts.IHasKey<KeyedUpgradedVault, global::Daml.Runtime.Data.Party>, global::Daml.Runtime.Contracts.IHasChoices<KeyedUpgradedVault>, global::Daml.Runtime.Data.IDamlRecord<KeyedUpgradedVault>");
    }

    [Fact]
    public void TemplateEmitter_adds_the_generic_IDamlRecord_facet_naming_the_template_itself()
    {
        var output = EmitTemplate(Template("SimpleTemplate", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain(": global::Daml.Runtime.Contracts.ITemplate, global::Daml.Runtime.Contracts.IHasChoices<SimpleTemplate>, global::Daml.Runtime.Data.IDamlRecord<SimpleTemplate>");
    }

    [Fact]
    public void TemplateEmitter_emits_static_template_metadata()
    {
        var output = EmitTemplate(Template("Asset", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain("public static global::Daml.Runtime.Data.Identifier TemplateId { get; }");
        output.Should().Contain("\"test-package-id\"");
        output.Should().Contain("\"Test.Module\"");
        output.Should().Contain("\"Asset\"");
        output.Should().Contain("public static string PackageId => \"test-package-id\";");
        output.Should().Contain("public static string PackageName => \"test-package\";");
        output.Should().Contain("public static global::System.Version PackageVersion { get; }");
    }

    [Fact]
    public void TemplateEmitter_emits_static_daml_type_descriptor()
    {
        var output = EmitTemplate(Template("Asset", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain(
            "public static global::Daml.Runtime.Contracts.DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, global::Daml.Runtime.Contracts.DamlTypeKind.Template, PackageName);");
    }

    [Fact]
    public void TemplateEmitter_nests_no_contract_id_or_contract_record_of_its_own()
    {
        var output = EmitTemplate(Template("Token", [Field("issuer", DamlPrimitive.Party)]));

        output.Should().NotContain("record ContractId(");
        output.Should().NotContain("record Contract(");
        output.Should().NotContain("IExercises");
    }

    [Fact]
    public void TemplateEmitter_maps_all_primitive_fields_to_their_csharp_types()
    {
        var output = EmitTemplate(Template("AllPrimitives",
        [
            Field("textField", DamlPrimitive.Text),
            Field("intField", DamlPrimitive.Int64),
            Field("boolField", DamlPrimitive.Bool),
            Field("numericField", DamlPrimitive.Numeric),
            Field("partyField", DamlPrimitive.Party),
            Field("dateField", DamlPrimitive.Date),
            Field("timestampField", DamlPrimitive.Timestamp),
        ]));

        output.Should().Contain("string TextField");
        output.Should().Contain("long IntField");
        output.Should().Contain("bool BoolField");
        output.Should().Contain("decimal NumericField");
        output.Should().Contain("Party PartyField");
        output.Should().Contain("DateOnly DateField");
        output.Should().Contain("DateTimeOffset TimestampField");
    }

    [Fact]
    public void TemplateEmitter_maps_complex_container_fields()
    {
        var output = EmitTemplate(Template("ComplexFields",
        [
            new DamlFieldDefinition("items", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
            new DamlFieldDefinition("maybeValue", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Int64)])),
            new DamlFieldDefinition("metadata", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.TextMap),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
        ]));

        output.Should().Contain("IReadOnlyList<string> Items");
        output.Should().Contain("long? MaybeValue");
        output.Should().Contain("IReadOnlyDictionary<string, string> Metadata");
    }

    [Fact]
    public void TemplateEmitter_emits_the_ToRecord_method()
    {
        var output = EmitTemplate(Template("Item",
        [
            Field("name", DamlPrimitive.Text),
            Field("count", DamlPrimitive.Int64),
        ]));

        output.Should().Contain("public global::Daml.Runtime.Data.DamlRecord ToRecord()");
        output.Should().Contain("global::Daml.Runtime.Data.DamlField.Create(\"name\", new global::Daml.Runtime.Data.DamlText(Name))");
        output.Should().Contain("global::Daml.Runtime.Data.DamlField.Create(\"count\", new global::Daml.Runtime.Data.DamlInt64(Count))");
    }

    [Fact]
    public void TemplateEmitter_emits_the_FromRecord_method()
    {
        var output = EmitTemplate(Template("Status",
        [
            Field("isActive", DamlPrimitive.Bool),
            Field("amount", DamlPrimitive.Numeric),
        ]));

        output.Should().Contain("public static Status FromRecord(global::Daml.Runtime.Data.DamlRecord record)");
        output.Should().Contain("IsActive: record.GetRequiredField(\"isActive\").As<global::Daml.Runtime.Data.DamlBool>().Value");
        output.Should().Contain("Amount: record.GetRequiredField(\"amount\").As<global::Daml.Runtime.Data.DamlNumeric>().Value");
    }

    [Fact]
    public void TemplateEmitter_serializes_list_fields_through_the_shared_serializer()
    {
        var output = EmitTemplate(Template("Tagged",
        [
            new DamlFieldDefinition("tags", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
        ]));

        output.Should().Contain("new global::Daml.Runtime.Data.DamlList(Tags.Select(x => (global::Daml.Runtime.Data.DamlValue)new global::Daml.Runtime.Data.DamlText(x)).ToList())");
    }

    [Fact]
    public void TemplateEmitter_serializes_optional_fields_through_the_shared_serializer()
    {
        var output = EmitTemplate(Template("OptionalTemplate",
        [
            new DamlFieldDefinition("maybeText", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
        ]));

        output.Should().Contain("MaybeText is { } __MaybeText ? new global::Daml.Runtime.Data.DamlOptional(new global::Daml.Runtime.Data.DamlText(__MaybeText)) : global::Daml.Runtime.Data.DamlOptional.None");
    }

    [Fact]
    public void TemplateEmitter_handles_a_template_with_no_fields()
    {
        var output = EmitTemplate(Template("EmptyTemplate"));

        output.Should().Contain("public sealed partial record EmptyTemplate : global::Daml.Runtime.Contracts.ITemplate");
        output.Should().Contain("public global::Daml.Runtime.Data.DamlRecord ToRecord()");
        output.Should().Contain("DamlRecord.Create(");
    }

    [Fact]
    public void TemplateEmitter_uses_the_package_version_in_metadata()
    {
        var output = EmitTemplate(
            Template("Versioned", [Field("value", DamlPrimitive.Text)]),
            version: new Version(2, 3, 4));

        output.Should().Contain("new(2, 3, 4)");
    }

    [Fact]
    public void TemplateEmitter_decodes_the_key_through_the_key_witness()
    {
        var output = EmitTemplate(
            Template("Keyed", [Field("owner", DamlPrimitive.Party)], key: new DamlPrimitiveType(DamlPrimitive.Party)));

        output.Should().Contain("global::Daml.Runtime.Contracts.IHasKey<Keyed, global::Daml.Runtime.Data.Party>");
        output.Should().Contain("KeyDecoder = value => global::Daml.Runtime.Data.Party.FromDamlValue(value.As<global::Daml.Runtime.Data.DamlParty>()),");
    }

    [Fact]
    public void TemplateEmitter_leaves_a_key_less_template_without_a_key_witness()
    {
        var output = EmitTemplate(Template("Keyless", [Field("owner", DamlPrimitive.Party)]));

        output.Should().NotContain("IHasKey");
        output.Should().NotContain("ContractKey");
    }

    [Fact]
    public void TemplateEmitter_adds_the_IUpgradeable_facet_when_the_package_is_an_upgrade()
    {
        var output = EmitTemplate(
            Template("Upgraded", [Field("owner", DamlPrimitive.Party)]),
            upgradedPackageId: "old-package-id");

        output.Should().Contain("IUpgradeable");
        output.Should().Contain("public static string? UpgradedPackageId => \"old-package-id\";");
    }

    [Fact]
    public void TemplateEmitter_delegates_choice_descriptor_emission_to_the_choice_emitter()
    {
        var output = EmitTemplate(Template(
            "WithChoice",
            [Field("owner", DamlPrimitive.Party)],
            choices:
            [
                new DamlChoice
                {
                    Name = "DoIt",
                    Consuming = true,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                },
            ]));

        output.Should().Contain("ChoiceDoIt");
    }

    [Fact]
    public void TemplateEmitter_delegates_submission_extension_emission_to_the_submission_emitter()
    {
        var output = EmitTemplate(Template("Submittable", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain("public static class SubmittableSubmissionExtensions");
        output.Should().Contain("public static global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<Submittable>>> TryCreateAsync(");
    }

    [Fact]
    public void TemplateEmitter_emits_every_xml_doc_when_enabled()
    {
        var output = EmitTemplate(
            Template("Documented", [Field("owner", DamlPrimitive.Party)], key: new DamlPrimitiveType(DamlPrimitive.Party)));

        output.Should().Contain("/// Generated from Daml template Test.Module:Documented");
        output.Should().Contain("/// <summary>Gets the template identifier.</summary>");
        output.Should().Contain("/// <summary>Gets the package ID.</summary>");
        output.Should().Contain("/// <summary>Gets the package name.</summary>");
        output.Should().Contain("/// <summary>Gets the package version.</summary>");
    }

    [Fact]
    public void TemplateEmitter_omits_every_xml_doc_when_disabled()
    {
        var output = EmitTemplate(
            Template("Documented", [Field("owner", DamlPrimitive.Party)], key: new DamlPrimitiveType(DamlPrimitive.Party)),
            options: Options(generateXmlDocs: false));

        output.Should().NotContain("/// Generated from Daml template Test.Module:Documented");
        output.Should().NotContain("Gets the template identifier");
        output.Should().NotContain("Gets the package ID");
        output.Should().NotContain("Gets the package name");
        output.Should().NotContain("Gets the package version");

        output.Should().Contain("public sealed partial record Documented");
        output.Should().Contain("public static global::Daml.Runtime.Data.Identifier TemplateId { get; }");
        output.Should().Contain("global::Daml.Runtime.Contracts.IHasKey<Documented, global::Daml.Runtime.Data.Party>");
    }

    [Fact]
    public void TemplateEmitter_emits_the_nested_choice_argument_partial_record()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes = [],
            Interfaces = [],
        };
        var options = Options();
        var package = Package(module);
        var resolution = RealResolution.Of(package, options);
        var context = resolution.Context;
        var resolver = resolution.Resolver;
        var mapper = new DamlTypeMapper(context, resolver);
        var party = new PartyAnalysis();
        var recordSerialization = new RecordSerializationEmitter(context, resolver, options, mapper);
        var choiceEmitter = new ChoiceEmitter(context, resolver, options, mapper, party);
        var submissionExtensions = new SubmissionExtensionsEmitter(options, party);
        var emitter = new TemplateEmitter(context, resolver, recordSerialization, choiceEmitter, submissionExtensions, options);

        var template = Template("Account", [Field("owner", DamlPrimitive.Party)]).Template;
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", ModuleName, "TransferArgs"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
        };
        var argumentRecord = new DamlRecordDefinition([Field("newOwner", DamlPrimitive.Party)]);

        var sb = new StringBuilder();
        emitter.WriteNestedChoiceArgumentType(new IndentWriter(sb), template, choice, argumentRecord);
        var output = sb.ToString();

        output.Should().Contain("public sealed partial record Account");
        output.Should().Contain("public sealed record Transfer(");
        output.Should().Contain("public global::Daml.Runtime.Data.DamlRecord ToRecord()");
        output.Should().Contain("public static Transfer FromRecord(global::Daml.Runtime.Data.DamlRecord record)");
    }

    [Fact]
    public void TemplateEmitter_filters_templates_with_the_root_filter()
    {
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            RootFilter = "Test\\.Module:Include.*",
        };

        var module = new DamlModule
        {
            Name = ModuleName,
            Templates =
            [
                Template("IncludeMe").Template,
                Template("ExcludeMe").Template,
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "IncludeMe",
                    Definition = new DamlRecordDefinition([Field("owner", DamlPrimitive.Party)])
                },
                new DamlDataType
                {
                    Name = "ExcludeMe",
                    Definition = new DamlRecordDefinition([Field("owner", DamlPrimitive.Party)])
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator(options).Generate(CreateTestDar(module));

        var templateFiles = files
            .Where(f => f.RelativePath.EndsWith("IncludeMe.cs", global::System.StringComparison.Ordinal)
                     || f.RelativePath.EndsWith("ExcludeMe.cs", global::System.StringComparison.Ordinal))
            .ToList();
        templateFiles.Should().HaveCount(1);
        templateFiles[0].RelativePath.Should().EndWith("IncludeMe.cs");
    }
}
