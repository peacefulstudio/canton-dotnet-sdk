// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins the double-representation edge at the emitter's boundary: the readers deliver
/// applied builtins as typed nodes and the emitter consumes them natively through its
/// visitor arms, while a hand-built legacy <c>DamlTypeApp(builtin, args)</c> that never
/// passed a reader still maps — the mapper folds a complete application of one of the
/// five folding builtins into its typed node at the visitor, so both representations
/// generate identical C#; a malformed application (wrong arity, or a signature-only
/// builtin in a data position) fails loudly by name instead of reaching a silent
/// fallback.
/// </summary>
public class TypedNodeEmissionTests
{
    [Theory]
    [MemberData(nameof(FoldingBuiltinPairs))]
    public void CSharpCodeGenerator_emits_identical_csharp_for_a_legacy_application_and_its_typed_node(
        string _,
        DamlType legacy,
        DamlType node)
    {
        var legacyFiles = new CSharpCodeGenerator(new CodeGenOptions()).Generate(SingleRecordModel(legacy));
        var nodeFiles = new CSharpCodeGenerator(new CodeGenOptions()).Generate(SingleRecordModel(node));

        nodeFiles.Should().HaveCount(legacyFiles.Count);
        foreach (var (legacyFile, nodeFile) in legacyFiles.Zip(nodeFiles))
        {
            nodeFile.RelativePath.Should().Be(legacyFile.RelativePath);
            nodeFile.Content.Should().Be(legacyFile.Content,
                "the emitter folds a complete legacy application of a folding builtin into its typed "
                + "node at the visitor, so a hand-built model that never passed a reader emits exactly "
                + "what the reader-fed typed-node model emits");
        }
    }

    [Fact]
    public void CSharpCodeGenerator_emits_identical_csharp_for_nested_and_composed_shapes_in_both_representations()
    {
        var text = new DamlPrimitiveType(DamlPrimitive.Text);
        var party = new DamlPrimitiveType(DamlPrimitive.Party);
        var legacy = new DamlTypeApp(
            new DamlTypeRef("main-pkg", "M", "Box"),
            [
                new DamlTypeApp(
                    new DamlPrimitiveType(DamlPrimitive.List),
                    [
                        new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [party, new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [text])]),
                    ]),
            ]);
        var node = new DamlTypeApp(
            new DamlTypeRef("main-pkg", "M", "Box"),
            [new DamlListType(new DamlGenMapType(party, new DamlOptionalType(text)))]);

        var legacyFiles = new CSharpCodeGenerator(new CodeGenOptions()).Generate(SingleRecordModel(legacy));
        var nodeFiles = new CSharpCodeGenerator(new CodeGenOptions()).Generate(SingleRecordModel(node));

        nodeFiles.Should().HaveCount(legacyFiles.Count);
        foreach (var (legacyFile, nodeFile) in legacyFiles.Zip(nodeFiles))
        {
            nodeFile.RelativePath.Should().Be(legacyFile.RelativePath);
            nodeFile.Content.Should().Be(legacyFile.Content,
                "the fold recurses through generic arguments and node children alike, so composed "
                + "shapes agree across the two representations in every type position the record emits");
        }
    }

    /// <summary>
    /// The five folding builtins, each as its legacy application spelling and its typed node.
    /// </summary>
    public static TheoryData<string, DamlType, DamlType> FoldingBuiltinPairs()
    {
        var text = new DamlPrimitiveType(DamlPrimitive.Text);
        var int64 = new DamlPrimitiveType(DamlPrimitive.Int64);
        var party = new DamlPrimitiveType(DamlPrimitive.Party);
        return new TheoryData<string, DamlType, DamlType>
        {
            { "List", App(DamlPrimitive.List, text), new DamlListType(text) },
            { "Optional", App(DamlPrimitive.Optional, text), new DamlOptionalType(text) },
            { "TextMap", App(DamlPrimitive.TextMap, int64), new DamlTextMapType(int64) },
            { "ContractId", App(DamlPrimitive.ContractId, text), new DamlContractIdType(text) },
            { "GenMap", App(DamlPrimitive.GenMap, party, int64), new DamlGenMapType(party, int64) },
        };
    }

    private static DamlType App(DamlPrimitive primitive, params DamlType[] arguments) =>
        new DamlTypeApp(new DamlPrimitiveType(primitive), [.. arguments]);

    private static DarModel SingleRecordModel(DamlType fieldType) => new()
    {
        MainPackage = Package("main-pkg", module: new DamlModule
        {
            Name = "M",
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "R",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("field", fieldType)]),
                },
                new DamlDataType
                {
                    Name = "Box",
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("item", new DamlTypeVar("a"))]),
                },
            ],
            Templates = [],
            Interfaces = [],
        }),
        Dependencies = [],
    };

    private static DamlPackage Package(string packageId, DamlModule module) => new()
    {
        PackageId = packageId,
        Name = "pkg",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [module],
        DependencyReferences = [],
    };
}
