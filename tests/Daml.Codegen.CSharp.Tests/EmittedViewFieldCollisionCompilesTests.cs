// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins the field names that make a view record ineligible for the marker enrichment.
/// The record emitter and the interface emitter derive a field's C# member name
/// independently, each disambiguating only against its own enclosing type, and the marker
/// declares two members of its own — so four legal Daml view fields would otherwise emit a
/// marker the stamped record cannot satisfy: a field PascalCasing to the record's name is
/// renamed on the record but not on the marker (CS0535), a field PascalCasing to the
/// marker's name is renamed on the marker but not on the record (CS0535), and a field
/// PascalCasing to <c>View</c> or <c>InterfaceId</c> redeclares a member the marker already
/// owns (CS0102). Each must degrade to an un-stamped record beside an un-enriched marker
/// that still carries its <c>View</c> witness, so the interface stays subscribable.
/// </summary>
public class EmittedViewFieldCollisionCompilesTests
{
    private const string InterfaceName = "Asset";
    private const string MarkerFileName = "IAsset.cs";
    private const string ViewRecordName = "AssetView";
    private const string ViewRecordFileName = "AssetView.cs";

    private static IReadOnlyList<GeneratedFile> EmitViewedInterface(params string[] viewFieldNames)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                        [.. viewFieldNames.Select(name =>
                            new DamlFieldDefinition(name, new DamlPrimitiveType(DamlPrimitive.Party)))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices = [],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    private static string SourceOf(IReadOnlyList<GeneratedFile> files, string fileName) =>
        files.Single(f => f.RelativePath.EndsWith(fileName, StringComparison.Ordinal)).Content;

    private static void ShouldCompileCleanly(IReadOnlyList<GeneratedFile> files, string because)
    {
        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            because + "; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_view_record_with_no_colliding_field_is_stamped_and_mirrored()
    {
        var files = EmitViewedInterface("owner");

        SourceOf(files, ViewRecordFileName).Should().Contain(
            $"global::Daml.Runtime.Data.Party Owner\n) : global::Test.Module.IAsset, global::Daml.Runtime.Data.IDamlRecord<{ViewRecordName}>",
            "the collision cases below only prove something if the same fixture enriches a clean view record");
        SourceOf(files, MarkerFileName).Should().Contain("global::Daml.Runtime.Data.Party Owner { get; }");
        ShouldCompileCleanly(files, "the enriched shape is the baseline every degraded case falls back from");
    }

    [Theory]
    [InlineData("view", "the mirror would redeclare the marker's own View witness (CS0102)")]
    [InlineData("interfaceId", "the mirror would redeclare the marker's own InterfaceId (CS0102)")]
    [InlineData("assetView", "the record renames the property to AssetView_ but the marker declares AssetView (CS0535)")]
    [InlineData("iAsset", "the marker renames the property to IAsset_ but the record declares IAsset (CS0535)")]
    public void Emitted_view_record_with_a_colliding_field_is_left_un_stamped_and_compiles(
        string viewFieldName,
        string collision)
    {
        var files = EmitViewedInterface(viewFieldName);

        ShouldCompileCleanly(files, $"the emitter must degrade rather than emit uncompilable C#: {collision}");
        SourceOf(files, ViewRecordFileName).Should().NotContain(
            ": global::Test.Module.IAsset,",
            $"the view record must not be stamped with a marker it cannot satisfy: {collision}");
        SourceOf(files, MarkerFileName).Should().NotContain(
            "Party ",
            $"the marker must not mirror a field the stamped record would not implement: {collision}");
        SourceOf(files, MarkerFileName).Should().Contain(
            $"global::Daml.Runtime.Contracts.ViewDescriptor<I{InterfaceName}, global::Test.Module.{ViewRecordName}> View {{ get; }} = new();",
            "degrading the enrichment must still leave a subscribable marker paired with its view record");
    }

    private static IReadOnlyList<GeneratedFile> EmitViewedInterfaceWithChoice(string viewFieldName, string choiceName)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition(viewFieldName, new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = choiceName,
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    [Theory]
    [InlineData("choices", "Grant", "the mirror would redeclare the marker's explicit IHasChoices<TSelf>.Choices implementation (CS0108 under TreatWarningsAsErrors)")]
    [InlineData("choiceGrant", "Grant", "the mirror would redeclare the marker's own ChoiceGrant descriptor property (CS0102)")]
    public void Emitted_view_record_with_a_field_colliding_with_a_choice_member_is_left_un_stamped_and_compiles(
        string viewFieldName,
        string choiceName,
        string collision)
    {
        var files = EmitViewedInterfaceWithChoice(viewFieldName, choiceName);

        ShouldCompileCleanly(files, $"the emitter must degrade rather than emit uncompilable or warning-producing C#: {collision}");
        SourceOf(files, ViewRecordFileName).Should().NotContain(
            ": global::Test.Module.IAsset,",
            $"the view record must not be stamped with a marker it cannot satisfy: {collision}");
        SourceOf(files, MarkerFileName).Should().NotContain(
            "Party ",
            $"the marker must not mirror a field the stamped record would not implement: {collision}");
        SourceOf(files, MarkerFileName).Should().Contain(
            $"global::Daml.Runtime.Contracts.ViewDescriptor<I{InterfaceName}, global::Test.Module.{ViewRecordName}> View {{ get; }} = new();",
            "degrading the enrichment must still leave a subscribable marker paired with its view record");
    }

    private static IReadOnlyList<GeneratedFile> EmitViewedInterfaceWithTypedArgChoice(string viewFieldName)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "TransferArg",
                    Definition = new DamlRecordDefinition([]),
                },
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition(viewFieldName, new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "TransferArg"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    [Fact]
    public void Emitted_view_record_with_a_field_named_like_a_choice_arg_type_is_stamped_and_compiles()
    {
        var files = EmitViewedInterfaceWithTypedArgChoice("transferArg");

        ShouldCompileCleanly(files, "the choice argument decoder receiver is global::-rooted, so the mirrored TransferArg property cannot shadow it");
        SourceOf(files, ViewRecordFileName).Should().Contain(
            ": global::Test.Module.IAsset,",
            "a view field named like a same-module decoder receiver collides with nothing, because the marker emits every receiver global::-rooted");
        SourceOf(files, MarkerFileName).Should().Contain(
            "global::Daml.Runtime.Data.Party TransferArg { get; }",
            "the marker mirrors the field the stamped record implements");
        SourceOf(files, MarkerFileName).Should().Contain(
            $"global::Daml.Runtime.Contracts.ViewDescriptor<I{InterfaceName}, global::Test.Module.{ViewRecordName}> View {{ get; }} = new();",
            "the enriched marker still carries its View witness");
    }

    private static IReadOnlyList<GeneratedFile> EmitViewedInterfaceWithTypedReturnChoice(string viewFieldName)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Result",
                    Definition = new DamlRecordDefinition([]),
                },
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition(viewFieldName, new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Settle",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeRef("", "Test.Module", "Result"),
                        },
                    ],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    [Fact]
    public void Emitted_view_record_with_a_field_named_like_a_choice_return_type_is_stamped_and_compiles()
    {
        var files = EmitViewedInterfaceWithTypedReturnChoice("result");

        ShouldCompileCleanly(files, "the choice result decoder receiver is global::-rooted, so the mirrored Result property cannot shadow it");
        SourceOf(files, ViewRecordFileName).Should().Contain(
            ": global::Test.Module.IAsset,",
            "a view field named like a same-module decoder receiver collides with nothing, because the marker emits every receiver global::-rooted");
        SourceOf(files, MarkerFileName).Should().Contain(
            "global::Daml.Runtime.Data.Party Result { get; }",
            "the marker mirrors the field the stamped record implements");
        SourceOf(files, MarkerFileName).Should().Contain(
            $"global::Daml.Runtime.Contracts.ViewDescriptor<I{InterfaceName}, global::Test.Module.{ViewRecordName}> View {{ get; }} = new();",
            "the enriched marker still carries its View witness");
    }

    private static IReadOnlyList<GeneratedFile> EmitViewedInterfaceWithWrappedReturnChoice(string viewFieldName)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Result",
                    Definition = new DamlRecordDefinition([]),
                },
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition(viewFieldName, new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Settle",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlTypeRef("", "Test.Module", "Result")]),
                        },
                    ],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    [Fact]
    public void Emitted_view_record_with_a_field_named_like_a_wrapped_choice_return_type_is_stamped_and_compiles()
    {
        var files = EmitViewedInterfaceWithWrappedReturnChoice("result");

        ShouldCompileCleanly(files, "the decoder receiver inside List<Result> is global::-rooted, so the mirrored Result property cannot shadow it");
        SourceOf(files, ViewRecordFileName).Should().Contain(
            ": global::Test.Module.IAsset,",
            "a view field named like a same-module decoder receiver collides with nothing, because the marker emits every receiver global::-rooted");
        SourceOf(files, MarkerFileName).Should().Contain(
            "global::Daml.Runtime.Data.Party Result { get; }",
            "the marker mirrors the field the stamped record implements");
        SourceOf(files, MarkerFileName).Should().Contain(
            $"global::Daml.Runtime.Contracts.ViewDescriptor<I{InterfaceName}, global::Test.Module.{ViewRecordName}> View {{ get; }} = new();",
            "the enriched marker still carries its View witness");
    }

    private static IReadOnlyList<GeneratedFile> EmitViewedInterfaceWithWrappedOptionalReturnChoice(string viewFieldName)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Result",
                    Definition = new DamlRecordDefinition([]),
                },
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition(viewFieldName, new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Settle",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlWrappedOptional(
                                new DamlTypeRef("", "Test.Module", "Result"),
                                OptionalEncoding.Flat),
                        },
                    ],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };
        return CreateGenerator(new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true })
            .Generate(CreateTestDar(module));
    }

    [Fact]
    public void Emitted_view_record_with_a_field_named_like_a_wrapped_optional_choice_return_type_is_stamped_and_compiles()
    {
        var files = EmitViewedInterfaceWithWrappedOptionalReturnChoice("result");

        ShouldCompileCleanly(files, "the decoder receiver inside the wrapped optional is global::-rooted, so the mirrored Result property cannot shadow it");
        SourceOf(files, ViewRecordFileName).Should().Contain(
            ": global::Test.Module.IAsset,",
            "a view field named like a same-module decoder receiver collides with nothing, because the marker emits every receiver global::-rooted");
        SourceOf(files, MarkerFileName).Should().Contain(
            "global::Daml.Runtime.Data.Party Result { get; }",
            "the marker mirrors the field the stamped record implements");
        SourceOf(files, MarkerFileName).Should().Contain(
            $"global::Daml.Runtime.Contracts.ViewDescriptor<I{InterfaceName}, global::Test.Module.{ViewRecordName}> View {{ get; }} = new();",
            "the enriched marker still carries its View witness");
    }

    private static IReadOnlyList<GeneratedFile> EmitViewedInterfaceWithNestedTemplateReturnChoice(string viewFieldName)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "ExecuteArg",
                    Definition = new DamlRecordDefinition([]),
                },
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition(viewFieldName, new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Execute",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "ExecuteArg"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };
        var allDataTypes = new List<DamlDataType>(module.DataTypes)
        {
            new DamlDataType { Name = "Transfer", Definition = new DamlRecordDefinition([]) },
        };
        var templateModule = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Transfer",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Execute",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "ExecuteArg"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes = allDataTypes,
            Interfaces = module.Interfaces,
        };
        return CreateGenerator(new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true })
            .Generate(CreateTestDar(templateModule));
    }

    [Fact]
    public void Emitted_view_record_with_a_field_named_like_a_nesting_template_is_stamped_and_compiles()
    {
        var files = EmitViewedInterfaceWithNestedTemplateReturnChoice("transfer");

        ShouldCompileCleanly(files, "the nested decoder receiver is global::-rooted, so the mirrored Transfer property cannot shadow its first segment");
        SourceOf(files, ViewRecordFileName).Should().Contain(
            ": global::Test.Module.IAsset,",
            "a view field named like a same-module decoder receiver collides with nothing, because the marker emits every receiver global::-rooted");
        SourceOf(files, MarkerFileName).Should().Contain(
            "global::Daml.Runtime.Data.Party Transfer { get; }",
            "the marker mirrors the field the stamped record implements");
        SourceOf(files, MarkerFileName).Should().Contain(
            $"global::Daml.Runtime.Contracts.ViewDescriptor<I{InterfaceName}, global::Test.Module.{ViewRecordName}> View {{ get; }} = new();",
            "the enriched marker still carries its View witness");
    }

    private static IReadOnlyList<GeneratedFile> EmitInterfaceChoiceWithLocalArgTypeNamed(string argTypeName)
    {
        // The interface choice descriptor encoder/decoder emit bare type names inside the marker body.
        // If the arg type's C# name matches a member the marker already declares (e.g. InterfaceId,
        // View, or Choice{X}), the bare name resolves to the property instead of the type → CS0426.
        // The emitter must use global::-qualified names for all decoder expressions.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType { Name = argTypeName, Definition = new DamlRecordDefinition([]) },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", argTypeName),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    ViewType = null,
                },
            ],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    [Theory]
    [InlineData("InterfaceId", "the arg decoder emits InterfaceId.FromRecord(...) which collides with the marker's own static InterfaceId property (CS0426)")]
    [InlineData("ChoiceTransfer", "the arg decoder emits ChoiceTransfer.FromRecord(...) which collides with the marker's own static ChoiceTransfer descriptor property (CS0426)")]
    public void Emitted_interface_choice_descriptor_with_a_marker_reserved_arg_type_name_compiles(
        string argTypeName,
        string collision)
    {
        var files = EmitInterfaceChoiceWithLocalArgTypeNamed(argTypeName);

        ShouldCompileCleanly(files, $"the emitter must global::-qualify decoder receivers rather than emit uncompilable C#: {collision}");
    }

    [Fact]
    public void Emitted_marker_mirrors_a_collection_typed_and_a_keyword_named_view_field()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition(
                            "holders",
                            new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlPrimitiveType(DamlPrimitive.Party)])),
                        new DamlFieldDefinition(
                            "quantities",
                            new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.TextMap),
                                [new DamlPrimitiveType(DamlPrimitive.Numeric)])),
                        new DamlFieldDefinition("interface", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices = [],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };

        var files = CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));

        var marker = SourceOf(files, MarkerFileName);
        marker.Should().Contain(
            "using System.Collections.Generic;",
            "a mirrored collection field needs the same namespace the record emitter requires for it");
        marker.Should().Contain("Interface { get; }", "a keyword-named field PascalCases on the marker exactly as it does on the view record");
        marker.Should().NotContain("@interface", "the escape runs after PascalCasing, which already lifts the name clear of the keyword");
        ShouldCompileCleanly(files, "a mirrored field's type and name must resolve on the marker as on the record");
    }
}
