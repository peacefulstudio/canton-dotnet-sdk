// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class InterfaceEmitterTests
{
    private const string LocalPackageId = "test-package-id";
    private const string ModuleName = "Test.Module";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(DamlModule module) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

    private static CodeGenOptions Options(bool generateXmlDocs) =>
        new() { NamespacePrefix = "Test.Package", GenerateXmlDocs = generateXmlDocs };

    private static string EmitInterface(
        DamlInterface iface,
        DamlDataType[]? dataTypes = null,
        bool generateXmlDocs = true,
        DamlInterface[]? siblingInterfaces = null)
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes = dataTypes ?? [],
            Interfaces = [iface, .. siblingInterfaces ?? []],
        };
        var options = Options(generateXmlDocs);
        var context = PackageEmitContext.ForPackage(Package(module), options, isMainPackage: true).Single();
        var resolver = new StubResolver();
        var mapper = new DamlTypeMapper(context, resolver);
        var choiceEmitter = new ChoiceEmitter(context, resolver, options, mapper, new PartyAnalysis());
        var emitter = new InterfaceEmitter(context, mapper, resolver, choiceEmitter, options);
        var sb = new StringBuilder();
        emitter.WriteInterfaceType(new IndentWriter(sb), Package(module), module, iface);
        return sb.ToString();
    }

    private static DamlInterface Interface(string name, DamlType? viewType = null, params DamlChoice[] choices) =>
        new() { Name = name, Choices = choices, ViewType = viewType };

    [Fact]
    public void InterfaceEmitter_emits_the_interface_declaration_with_an_I_prefix()
    {
        var output = EmitInterface(Interface("Transferable"));

        output.Should().Contain("public interface ITransferable");
    }

    [Fact]
    public void InterfaceEmitter_declares_IDamlInterface_as_the_base_facet()
    {
        var output = EmitInterface(Interface("Lockable"));

        output.Should().Contain(": IDamlInterface");
    }

    [Fact]
    public void InterfaceEmitter_emits_static_interface_metadata()
    {
        var output = EmitInterface(Interface("Holdable"));

        output.Should().Contain("static Identifier IDamlInterface.InterfaceId =>");
        output.Should().Contain("\"test-package-id\"");
        output.Should().Contain("\"Test.Module\"");
        output.Should().Contain("\"Holdable\"");
        output.Should().Contain("static string IDamlInterface.PackageId =>");
        output.Should().Contain("static string IDamlInterface.PackageName =>");
        output.Should().Contain("static Version IDamlInterface.PackageVersion =>");
    }

    [Fact]
    public void InterfaceEmitter_emits_explicit_daml_type_descriptor()
    {
        var output = EmitInterface(Interface("Holdable"));

        output.Should().Contain(
            "static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId => new(new Identifier(\"test-package-id\", \"Test.Module\", \"Holdable\"), DamlTypeKind.Interface, \"test-package\");");
    }

    private static DamlDataType AssetViewRecord() =>
        new()
        {
            Name = "AssetView",
            Definition = new DamlRecordDefinition(
            [
                new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric)),
            ]),
        };

    private static string EmitAssetWithLocalView(bool generateXmlDocs = true) =>
        EmitInterface(
            Interface("Asset", viewType: new DamlTypeRef("", ModuleName, "AssetView")),
            dataTypes: [AssetViewRecord()],
            generateXmlDocs: generateXmlDocs);

    [Fact]
    public void InterfaceEmitter_adds_the_IHasView_facet_when_the_interface_has_a_view_type()
    {
        var output = EmitAssetWithLocalView();

        output.Should().Contain("IHasView<AssetView>");
    }

    [Fact]
    public void InterfaceEmitter_omits_the_IHasView_facet_when_the_interface_has_no_view_type()
    {
        var output = EmitInterface(Interface("Lockable"));

        output.Should().NotContain("IHasView");
    }

    [Fact]
    public void InterfaceEmitter_emits_the_View_witness_static_for_a_local_view_record()
    {
        var output = EmitAssetWithLocalView();

        output.Should().Contain("public static ViewDescriptor<IAsset, AssetView> View { get; } = new();");
    }

    [Fact]
    public void InterfaceEmitter_mirrors_the_view_fields_as_marker_instance_properties()
    {
        var output = EmitAssetWithLocalView();

        output.Should().Contain("Party Owner { get; }");
        output.Should().Contain("decimal Amount { get; }");
    }

    [Fact]
    public void InterfaceEmitter_documents_marker_equality_as_reference_equality_when_the_view_is_local()
    {
        var output = EmitAssetWithLocalView();

        output.Should().Contain("reference equality");
    }

    [Fact]
    public void InterfaceEmitter_omits_the_view_enrichment_when_the_interface_has_no_view_type()
    {
        var output = EmitInterface(Interface("Lockable"));

        output.Should().NotContain("ViewDescriptor");
        output.Should().NotContain("View { get; }");
        output.Should().NotContain("reference equality");
    }

    [Fact]
    public void InterfaceEmitter_emits_the_witness_without_view_properties_for_a_foreign_view_record()
    {
        var output = EmitInterface(
            Interface("Asset", viewType: new DamlTypeRef("foreign-package-id", "Foreign.Module", "AssetView")));

        output.Should().Contain("public static ViewDescriptor<IAsset, AssetView> View { get; } = new();");
        output.Should().NotContain("Owner { get; }");
        output.Should().NotContain("reference equality");
    }

    [Fact]
    public void InterfaceEmitter_does_not_enrich_a_foreign_view_ref_that_name_collides_with_a_local_view_record()
    {
        var output = EmitInterface(
            Interface("Foreign", viewType: new DamlTypeRef("other-package-id", ModuleName, "AssetView")),
            dataTypes: [AssetViewRecord()],
            siblingInterfaces: [Interface("Asset", viewType: new DamlTypeRef("", ModuleName, "AssetView"))]);

        output.Should().Contain("public static ViewDescriptor<IForeign, AssetView> View { get; } = new();");
        output.Should().NotContain("Owner { get; }");
        output.Should().NotContain("reference equality");
    }

    [Fact]
    public void InterfaceEmitter_omits_the_witness_for_a_local_view_type_that_is_not_a_record()
    {
        var output = EmitInterface(
            Interface("Coloured", viewType: new DamlTypeRef("", ModuleName, "Colour")),
            dataTypes: [new DamlDataType { Name = "Colour", Definition = new DamlEnumDefinition(["Red"]) }]);

        output.Should().Contain("IHasView<Colour>");
        output.Should().NotContain("ViewDescriptor");
    }

    [Fact]
    public void InterfaceEmitter_omits_the_enrichment_for_a_view_record_whose_field_collides_with_the_witness()
    {
        var output = EmitInterface(
            Interface("Asset", viewType: new DamlTypeRef("", ModuleName, "AssetView")),
            dataTypes:
            [
                new DamlDataType
                {
                    Name = "AssetView",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("view", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ]);

        output.Should().Contain("public static ViewDescriptor<IAsset, AssetView> View { get; } = new();");
        output.Should().NotContain("Party View { get; }");
        output.Should().NotContain("reference equality");
    }

    [Fact]
    public void InterfaceEmitter_writes_no_signature_comment_into_the_marker_body_for_a_choice()
    {
        var output = EmitInterface(Interface(
            "Transferable",
            viewType: null,
            new DamlChoice
            {
                Name = "Transfer",
                Consuming = true,
                ArgumentType = new DamlPrimitiveType(DamlPrimitive.Party),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
            }));

        output.Should().NotContain("// Choice Transfer");
        output.Should().NotContain("// Interface method Transfer.");
    }

    [Fact]
    public void InterfaceEmitter_emits_the_sibling_choice_exerciser_class_when_the_interface_has_choices()
    {
        var output = EmitInterface(Interface(
            "Transferable",
            viewType: null,
            new DamlChoice
            {
                Name = "Transfer",
                Consuming = true,
                ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                Controllers = DamlPartyAnalysis.Dynamic,
                Observers = DamlPartyAnalysis.Dynamic,
            }));

        output.Should().Contain("public static class ITransferableExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<DamlUnit>> TryTransferAsync(");
    }

    [Fact]
    public void InterfaceEmitter_emits_no_choice_exerciser_class_when_the_interface_has_no_choices()
    {
        var output = EmitInterface(Interface("Lockable"));

        output.Should().NotContain("public static class");
    }

    [Fact]
    public void InterfaceEmitter_adds_the_IHasChoices_facet_when_the_interface_has_a_choice()
    {
        var output = EmitInterface(Interface(
            "Transferable",
            viewType: null,
            new DamlChoice
            {
                Name = "Transfer",
                Consuming = true,
                ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                Controllers = DamlPartyAnalysis.Dynamic,
                Observers = DamlPartyAnalysis.Dynamic,
            }));

        output.Should().Contain(": IDamlInterface, IHasChoices<ITransferable>");
    }

    [Fact]
    public void InterfaceEmitter_omits_the_IHasChoices_facet_when_the_interface_has_no_choices()
    {
        var output = EmitInterface(Interface("Lockable"));

        output.Should().NotContain("IHasChoices");
    }

    [Fact]
    public void InterfaceEmitter_emits_the_Choices_aggregate_as_an_explicit_IHasChoices_implementation()
    {
        var output = EmitInterface(Interface(
            "Transferable",
            viewType: null,
            new DamlChoice
            {
                Name = "Transfer",
                Consuming = true,
                ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                Controllers = DamlPartyAnalysis.Dynamic,
                Observers = DamlPartyAnalysis.Dynamic,
            }));

        output.Should().Contain(
            "static IReadOnlyList<IChoice> IHasChoices<ITransferable>.Choices { get; } = [ChoiceTransfer];");
        output.Should().NotContain("public static IReadOnlyList<IChoice> Choices");
    }

    [Fact]
    public void InterfaceEmitter_emits_every_xml_doc_when_enabled()
    {
        var output = EmitInterface(Interface("Documented"), generateXmlDocs: true);

        output.Should().Contain("/// <summary>");
        output.Should().Contain("/// Generated from Daml interface Test.Module:Documented");
        output.Should().Contain("/// <summary>Gets the interface identifier.</summary>");
        output.Should().Contain("/// <summary>Gets the package ID.</summary>");
        output.Should().Contain("/// <summary>Gets the package name.</summary>");
        output.Should().Contain("/// <summary>Gets the package version.</summary>");
    }

    [Fact]
    public void InterfaceEmitter_omits_every_xml_doc_when_disabled()
    {
        var output = EmitInterface(Interface("Documented"), generateXmlDocs: false);

        output.Should().NotContain("/// Generated from Daml interface Test.Module:Documented");
        output.Should().NotContain("Gets the interface identifier");
        output.Should().NotContain("Gets the package ID");
        output.Should().NotContain("Gets the package name");
        output.Should().NotContain("Gets the package version");

        output.Should().Contain("public interface IDocumented");
        output.Should().Contain("static Identifier IDamlInterface.InterfaceId =>");
        output.Should().Contain("static string IDamlInterface.PackageId =>");
    }

    [Fact]
    public void InterfaceEmitter_filters_interfaces_with_the_root_filter()
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
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                Interface("IncludeMe"),
                Interface("ExcludeMe"),
            ],
        };

        var files = CreateGenerator(options).Generate(CreateTestDar(module));

        var interfaceFiles = files
            .Where(f => f.RelativePath.Contains("IIncludeMe") || f.RelativePath.Contains("IExcludeMe"))
            .ToList();
        interfaceFiles.Should().HaveCount(1);
        interfaceFiles[0].RelativePath.Should().Contain("IIncludeMe");
    }
}
