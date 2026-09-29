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

public class RecordEmitterTests
{
    private const string LocalPackageId = "pkg-id";
    private const string ModuleName = "Test.Module";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => typeRef.Name;

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(params DamlDataType[] dataTypes) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = ModuleName,
                    Templates = [],
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static CodeGenOptions Options(bool generateXmlDocs) =>
        new() { NamespacePrefix = "Test.Package", GenerateXmlDocs = generateXmlDocs };

    private static string Emit(string targetName, DamlDataType[] packageTypes, bool generateXmlDocs = true)
    {
        var options = Options(generateXmlDocs);
        var context = PackageEmitContext.ForPackage(Package(packageTypes), options, isMainPackage: true).Single();
        var resolver = new StubResolver();
        var mapper = new DamlTypeMapper(context, resolver);
        var serialization = new RecordSerializationEmitter(context, resolver, options, mapper);
        var emitter = new RecordEmitter(context, options, serialization);
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes = packageTypes,
            Interfaces = [],
        };
        var target = packageTypes.First(d => d.Name == targetName);
        var sb = new StringBuilder();
        emitter.WriteRecordType(new IndentWriter(sb), module, target, (DamlRecordDefinition)target.Definition!);
        return sb.ToString();
    }

    private static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    private static string EmitRecord(DamlDataType target, bool generateXmlDocs = true) =>
        Emit(target.Name, [target], generateXmlDocs);

    private static DamlFieldDefinition Field(string name, DamlPrimitive primitive) =>
        new(name, new DamlPrimitiveType(primitive));

    private static DamlVariantConstructor Ctor(string name, DamlType? argument = null) =>
        new(name, argument);

    private static DamlDataType Variant(string name, params DamlVariantConstructor[] constructors) =>
        new() { Name = name, Definition = new DamlVariantDefinition(constructors) };

    [Fact]
    public void RecordEmitter_emits_the_sealed_record_declaration_with_primitive_fields()
    {
        var output = EmitRecord(Record(
            "PersonInfo",
            Field("name", DamlPrimitive.Text),
            Field("age", DamlPrimitive.Int64),
            Field("active", DamlPrimitive.Bool)));

        output.Should().Contain("public sealed record PersonInfo(");
        output.Should().Contain("string Name");
        output.Should().Contain("long Age");
        output.Should().Contain("bool Active");
        output.Should().Contain(": IDamlRecord<PersonInfo>");
    }

    [Fact]
    public void RecordEmitter_stamps_the_generic_IDamlRecord_facet_naming_the_record_itself()
    {
        var output = EmitRecord(Record("Profile", Field("nickname", DamlPrimitive.Text)));

        output.Should().Contain(": IDamlRecord<Profile>");
    }

    private static DamlInterface ViewingInterface(string name, string viewRecordName) =>
        new()
        {
            Name = name,
            Choices = [],
            ViewType = new DamlTypeRef("", ModuleName, viewRecordName),
        };

    private static string EmitWithInterfaces(string targetName, DamlDataType[] packageTypes, DamlInterface[] interfaces)
    {
        var options = Options(generateXmlDocs: true);
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes = packageTypes,
            Interfaces = interfaces,
        };
        var package = new DamlPackage
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };
        var context = PackageEmitContext.ForPackage(package, options, isMainPackage: true).Single();
        var resolver = new StubResolver();
        var mapper = new DamlTypeMapper(context, resolver);
        var serialization = new RecordSerializationEmitter(context, resolver, options, mapper);
        var emitter = new RecordEmitter(context, options, serialization);
        var target = packageTypes.First(d => d.Name == targetName);
        var sb = new StringBuilder();
        emitter.WriteRecordType(new IndentWriter(sb), module, target, (DamlRecordDefinition)target.Definition!);
        return sb.ToString();
    }

    [Fact]
    public void RecordEmitter_stamps_a_view_record_with_its_interface_marker_before_the_record_facet()
    {
        var output = EmitWithInterfaces(
            "AssetView",
            [Record("AssetView", Field("owner", DamlPrimitive.Party))],
            [ViewingInterface("Asset", "AssetView")]);

        output.Should().Contain(": IAsset, IDamlRecord<AssetView>");
    }

    [Fact]
    public void RecordEmitter_leaves_a_view_record_shared_by_two_interfaces_unstamped()
    {
        var output = EmitWithInterfaces(
            "SharedView",
            [Record("SharedView", Field("owner", DamlPrimitive.Party))],
            [ViewingInterface("Bond", "SharedView"), ViewingInterface("Asset", "SharedView")]);

        output.Should().Contain(": IDamlRecord<SharedView>");
        output.Should().NotContain("IAsset");
        output.Should().NotContain("IBond");
    }

    [Fact]
    public void RecordEmitter_maps_numeric_fields_to_decimal()
    {
        var output = EmitRecord(Record(
            "Amount",
            Field("value", DamlPrimitive.Numeric),
            Field("currency", DamlPrimitive.Text)));

        output.Should().Contain("decimal Value");
        output.Should().Contain("string Currency");
    }

    [Fact]
    public void RecordEmitter_maps_date_and_timestamp_fields()
    {
        var output = EmitRecord(Record(
            "Event",
            Field("eventDate", DamlPrimitive.Date),
            Field("createdAt", DamlPrimitive.Timestamp)));

        output.Should().Contain("DateOnly EventDate");
        output.Should().Contain("DateTimeOffset CreatedAt");
    }

    [Fact]
    public void RecordEmitter_maps_party_fields()
    {
        var output = EmitRecord(Record(
            "Ownership",
            Field("owner", DamlPrimitive.Party),
            Field("receiver", DamlPrimitive.Party)));

        output.Should().Contain("Party Owner");
        output.Should().Contain("Party Receiver");
    }

    [Fact]
    public void RecordEmitter_maps_optional_fields_to_nullable()
    {
        var output = EmitRecord(Record(
            "OptionalData",
            new DamlFieldDefinition("maybeText", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
            new DamlFieldDefinition("maybeNumber", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Int64)]))));

        output.Should().Contain("string? MaybeText");
        output.Should().Contain("long? MaybeNumber");
    }

    [Fact]
    public void RecordEmitter_decodes_optional_fields_through_AsOptional_in_FromRecord()
    {
        var output = EmitRecord(Record(
            "OptionalData",
            new DamlFieldDefinition("maybeText", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Text)]))));

        output.Should().Contain(
            "MaybeText: record.GetOptionalField(\"maybeText\").AsOptional().HasValue"
            + " ? record.GetOptionalField(\"maybeText\").AsOptional().Value!.As<DamlText>().Value : null",
            "FromRecord must normalize through AsOptional so JSON-decoded records, which flatten Some to the bare value, still decode");
        output.Should().NotContain(".As<DamlOptional>()");
    }

    [Fact]
    public void RecordEmitter_maps_list_fields_to_readonly_list()
    {
        var output = EmitRecord(Record(
            "Collection",
            new DamlFieldDefinition("items", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
            new DamlFieldDefinition("counts", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Int64)]))));

        output.Should().Contain("IReadOnlyList<string> Items");
        output.Should().Contain("IReadOnlyList<long> Counts");
    }

    [Fact]
    public void RecordEmitter_maps_textmap_field_to_readonly_dictionary()
    {
        var output = EmitRecord(Record(
            "Metadata",
            new DamlFieldDefinition("attributes", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.TextMap),
                [new DamlPrimitiveType(DamlPrimitive.Text)]))));

        output.Should().Contain("IReadOnlyDictionary<string, string> Attributes");
    }

    [Fact]
    public void RecordEmitter_maps_contract_id_field_to_typed_contract_id()
    {
        var output = EmitRecord(Record(
            "Reference",
            new DamlFieldDefinition("assetRef", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.ContractId),
                [new DamlTypeRef("", ModuleName, "Asset")]))));

        output.Should().Contain("ContractId<Asset> AssetRef");
    }

    [Fact]
    public void RecordEmitter_emits_a_ToRecord_method()
    {
        var output = EmitRecord(Record("Simple", Field("value", DamlPrimitive.Text)));

        output.Should().Contain("public DamlRecord ToRecord()");
        output.Should().Contain("DamlRecord.Create(");
        output.Should().Contain("DamlField.Create(\"value\", new DamlText(Value))");
    }

    [Fact]
    public void RecordEmitter_emits_a_FromRecord_method()
    {
        var output = EmitRecord(Record("Simple", Field("value", DamlPrimitive.Text)));

        output.Should().Contain("public static Simple FromRecord(DamlRecord record)");
        output.Should().Contain("record.GetRequiredField(\"value\")");
    }

    [Fact]
    public void RecordEmitter_references_other_record_types_through_FromRecord()
    {
        var address = Record(
            "Address",
            Field("street", DamlPrimitive.Text),
            Field("city", DamlPrimitive.Text));
        var person = Record(
            "Person",
            Field("name", DamlPrimitive.Text),
            new DamlFieldDefinition("homeAddress", new DamlTypeRef("", ModuleName, "Address")));

        var output = Emit("Person", [address, person]);

        output.Should().Contain("Address HomeAddress");
        output.Should().Contain("Address.FromRecord(");
    }

    [Fact]
    public void RecordEmitter_routes_record_field_through_ToVariant_and_FromVariant_when_field_is_a_variant()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes =
            [
                Variant("Choice", Ctor("Yes", new DamlPrimitiveType(DamlPrimitive.Int64))),
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("pick", new DamlTypeRef(string.Empty, ModuleName, "Choice"))
                    ])
                }
            ],
            Interfaces = []
        };

        var files = CreateGenerator().Generate(CreateTestDar(module));
        var code = files.First(f => f.RelativePath.EndsWith("Holder.cs", StringComparison.Ordinal)).Content;

        code.Should().Contain("DamlField.Create(\"pick\", Pick.ToVariant())");
        code.Should().Contain("Pick: Choice.FromVariant(record.GetRequiredField(\"pick\").As<DamlVariant>())");
    }

    [Fact]
    public void RecordEmitter_declares_type_parameters_with_docs_when_enabled()
    {
        var box = new DamlDataType
        {
            Name = "Box",
            TypeParams = ["a"],
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]),
        };

        var output = EmitRecord(box, generateXmlDocs: true);

        output.Should().Contain("public sealed record Box<TA>(");
        output.Should().Contain("TA Value");
        output.Should().Contain("/// Type parameters: a");
        output.Should().Contain("/// <typeparam name=\"TA\">Type parameter a</typeparam>");
    }

    [Fact]
    public void RecordEmitter_declares_type_parameters_without_docs_when_disabled()
    {
        var box = new DamlDataType
        {
            Name = "Box",
            TypeParams = ["a"],
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]),
        };

        var output = EmitRecord(box, generateXmlDocs: false);

        output.Should().Contain("public sealed record Box<TA>(");
        output.Should().NotContain("/// Type parameters: a");
        output.Should().NotContain("/// <typeparam");
    }

    [Fact]
    public void RecordEmitter_constrains_every_type_parameter_to_notnull()
    {
        var pair = new DamlDataType
        {
            Name = "Pair",
            TypeParams = ["a", "b"],
            Definition = new DamlRecordDefinition(
            [
                new DamlFieldDefinition("first", new DamlTypeVar("a")),
                new DamlFieldDefinition("second", new DamlTypeVar("b")),
            ]),
        };

        var output = EmitRecord(pair, generateXmlDocs: false);

        output.Should().Contain(") where TA : notnull where TB : notnull",
            "a Daml type variable ranges only over serialisable Daml types, none of which is nullable, "
            + "and without the constraint a reflection-driven reader decodes the slot as an Optional");
    }

    [Fact]
    public void RecordEmitter_leaves_the_return_type_of_FromRecord_unconstrained()
    {
        var box = new DamlDataType
        {
            Name = "Box",
            TypeParams = ["a"],
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]),
        };

        var output = EmitRecord(box, generateXmlDocs: false);

        output.Should().Contain("public static Box<TA> FromRecord(",
            "a constraint clause belongs on the declaration only; repeating it on a member's return type is not C#");
    }

    [Fact]
    public void RecordEmitter_leaves_a_non_generic_record_unconstrained()
    {
        var output = EmitRecord(Record("Simple", Field("value", DamlPrimitive.Text)), generateXmlDocs: false);

        output.Should().NotContain("notnull");
    }

    [Fact]
    public void RecordEmitter_emits_every_xml_doc_when_enabled()
    {
        var output = EmitRecord(Record("Simple", Field("value", DamlPrimitive.Text)), generateXmlDocs: true);

        output.Should().Contain("/// Generated from Daml record Simple");
        output.Should().Contain("/// <summary>Converts this value to a DamlRecord.</summary>");
        output.Should().Contain("/// <summary>Creates an instance from a DamlRecord.</summary>");
    }

    [Fact]
    public void RecordEmitter_omits_every_xml_doc_when_disabled()
    {
        var output = EmitRecord(Record("Simple", Field("value", DamlPrimitive.Text)), generateXmlDocs: false);

        output.Should().NotContain("/// Generated from Daml record Simple");
        output.Should().NotContain("Converts this value to a DamlRecord");
        output.Should().NotContain("Creates an instance from a DamlRecord");

        output.Should().Contain("public sealed record Simple(");
        output.Should().Contain("public DamlRecord ToRecord()");
        output.Should().Contain("public static Simple FromRecord(DamlRecord record)");
    }
}
