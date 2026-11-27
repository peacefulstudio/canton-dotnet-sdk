// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that a List, TextMap or GenMap whose element is an <c>Optional</c> spelled with C#
/// nullable syntax emits C# that compiles and round-trips. The element conversion of such a
/// collection is a conditional expression, so a cast written in front of it binds to its
/// condition (CS0030), a pattern local derived from the element expression <c>kv.Value</c> is
/// not an identifier (CS1026), and an untyped <c>null</c> branch leaves the conditional without a
/// type inside the collection's lambda (CS0411).
/// </summary>
public class EmittedCollectionOfOptionalCompilesTests
{
    private static readonly DamlType Int = new DamlPrimitiveType(DamlPrimitive.Int64);
    private static readonly DamlType Text = new DamlPrimitiveType(DamlPrimitive.Text);

    private static DamlType Opt(DamlType inner) => new DamlOptionalType(inner);

    private static readonly IReadOnlyDictionary<string, DamlType> Shapes = new Dictionary<string, DamlType>(StringComparer.Ordinal)
    {
        ["list_of_optional_int"] = new DamlListType(Opt(Int)),
        ["list_of_optional_text"] = new DamlListType(Opt(Text)),
        ["list_of_optional_party"] = new DamlListType(Opt(new DamlPrimitiveType(DamlPrimitive.Party))),
        ["list_of_optional_bool"] = new DamlListType(Opt(new DamlPrimitiveType(DamlPrimitive.Bool))),
        ["list_of_optional_date"] = new DamlListType(Opt(new DamlPrimitiveType(DamlPrimitive.Date))),
        ["list_of_optional_record"] = new DamlListType(Opt(new DamlTypeRef("", "Test.Module", "Tag"))),
        ["list_of_optional_enum"] = new DamlListType(Opt(new DamlTypeRef("", "Test.Module", "Shade"))),
        ["textmap_of_optional_int"] = new DamlTextMapType(Opt(Int)),
        ["textmap_of_optional_text"] = new DamlTextMapType(Opt(Text)),
        ["genmap_of_text_to_optional_int"] = new DamlGenMapType(Text, Opt(Int)),
        ["genmap_of_int_to_optional_text"] = new DamlGenMapType(Int, Opt(Text)),
        ["genmap_of_optional_int_to_optional_text"] = new DamlGenMapType(Opt(Int), Opt(Text)),
        ["optional_of_list_of_optional_int"] = Opt(new DamlListType(Opt(Int))),
        ["list_of_textmap_of_optional_int"] = new DamlListType(new DamlTextMapType(Opt(Int))),
        ["list_of_optional_list_of_optional_int"] = new DamlListType(Opt(new DamlListType(Opt(Int)))),
        ["list_of_list_of_optional_text"] = new DamlListType(new DamlListType(Opt(Text))),
        ["textmap_of_optional_textmap_of_optional_int"] = new DamlTextMapType(Opt(new DamlTextMapType(Opt(Int)))),
    };

    public static TheoryData<string, bool> ShapesUnderBothNullableSettings()
    {
        var data = new TheoryData<string, bool>();
        foreach (var shape in Shapes.Keys)
        {
            data.Add(shape, true);
            data.Add(shape, false);
        }
        return data;
    }

    private static DarModel Package(DamlType fieldType)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Book",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Tally",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Tally"),
                            ReturnType = fieldType,
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Bag",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("items", fieldType)]),
                },
                new DamlDataType
                {
                    Name = "Book",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("entries", fieldType),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Tally",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("counts", fieldType)]),
                },
                new DamlDataType
                {
                    Name = "Carrier",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Carried", fieldType),
                        new DamlVariantConstructor("Empty", new DamlPrimitiveType(DamlPrimitive.Unit)),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Tag",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("label", Text)]),
                },
                new DamlDataType { Name = "Shade", Definition = new DamlEnumDefinition(["Light", "Dark"]) },
            ],
            Interfaces = [],
        };

        return new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-package-id",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [],
        };
    }

    private static IReadOnlyList<GeneratedFile> Generate(DamlType fieldType, bool nullable = true) =>
        CreateGenerator(new CodeGenOptions
        {
            EnableNullableReferenceTypes = nullable,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
        }).Generate(Package(fieldType));

    [Theory]
    [MemberData(nameof(ShapesUnderBothNullableSettings))]
    public void Collection_of_optional_in_a_record_variant_template_and_choice_compiles(string shape, bool nullable)
    {
        var files = Generate(Shapes[shape], nullable);

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "a {0} field, variant argument, choice argument and choice result must compile, but got: {1}",
            shape,
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    public static TheoryData<string, string> RoundTripCases() => new()
    {
        { "list_of_optional_int", """{"items":[null,"7"]}""" },
        { "textmap_of_optional_text", """{"items":{"absent":null,"present":"hi"}}""" },
        { "genmap_of_text_to_optional_int", """{"items":[["absent",null],["present","7"]]}""" },
    };

    private static object Sample(string shape) => shape switch
    {
        "list_of_optional_int" => new List<long?> { null, 7L },
        "textmap_of_optional_text" => new Dictionary<string, string?> { ["absent"] = null, ["present"] = "hi" },
        "genmap_of_text_to_optional_int" => new Dictionary<string, long?> { ["absent"] = null, ["present"] = 7L },
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    private static (Type Bag, object Original) EmitBag(string shape)
    {
        var assembly = EmitToAssembly(Generate(Shapes[shape]));
        var bag = assembly.GetTypes().Single(t => t.Name == "Bag");
        return (bag, Activator.CreateInstance(bag, Sample(shape))!);
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Collection_of_optional_round_trips_through_DamlRecord(string shape, string _)
    {
        var (bag, original) = EmitBag(shape);

        var record = (DamlRecord)bag.GetMethod("ToRecord", Type.EmptyTypes)!.Invoke(original, null)!;
        var decoded = bag.GetMethod("FromRecord", BindingFlags.Public | BindingFlags.Static, [typeof(DamlRecord)])!
            .Invoke(null, [record]);

        decoded.Should().Be(original);
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Collection_of_optional_round_trips_through_LF_JSON(string shape, string wire)
    {
        var (bag, original) = EmitBag(shape);

        var record = (DamlRecord)bag.GetMethod("ToRecord", Type.EmptyTypes)!.Invoke(original, null)!;
        var json = DamlJsonSerializer.Serialize(record);
        var decoded = typeof(DamlLfJsonReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "Read" && m.GetParameters()[0].ParameterType == typeof(string))
            .MakeGenericMethod(bag)
            .Invoke(null, [json, null]);

        json.Should().Be(wire);
        decoded.Should().Be(original);
    }
}
