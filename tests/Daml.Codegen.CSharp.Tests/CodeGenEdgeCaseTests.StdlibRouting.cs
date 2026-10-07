// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public partial class CodeGenEdgeCaseTests
{
    private sealed record StdlibRoutingCase(
        string StdlibPackageId,
        string StdlibPackageName,
        string StdlibModuleName,
        DamlDataType StdlibDataType,
        string ConsumerTypeName,
        string ConsumerFieldName,
        DamlType ConsumerFieldType,
        bool GenerateProjectFile,
        IReadOnlyList<string> ExpectedInConsumer,
        IReadOnlyList<string> ExpectedAbsentFromConsumer,
        IReadOnlyList<string> ExpectedAbsentFromProjectFile);

    private static DamlDataType Tuple2DataType() =>
        new()
        {
            Name = "Tuple2",
            TypeParams = ["a", "b"],
            Definition = new DamlRecordDefinition(
            [
                new DamlFieldDefinition("_1", new DamlTypeVar("a")),
                new DamlFieldDefinition("_2", new DamlTypeVar("b"))
            ])
        };

    private static DamlType Tuple2OfIntAndText(string packageId) =>
        new DamlTypeApp(
            new DamlTypeRef(packageId, "DA.Types", "Tuple2"),
            [
                new DamlPrimitiveType(DamlPrimitive.Int64),
                new DamlPrimitiveType(DamlPrimitive.Text)
            ]);

    private static readonly Dictionary<string, StdlibRoutingCase> StdlibRoutingCasesByName = new()
    {
        ["RelTime from daml-stdlib-DA-Time-Types"] = new StdlibRoutingCase(
            StdlibPackageId: "stdlib-time-id",
            StdlibPackageName: "daml-stdlib-DA-Time-Types",
            StdlibModuleName: "DA.Time.Types",
            StdlibDataType: new DamlDataType
            {
                Name = "RelTime",
                Definition = new DamlRecordDefinition(
                [
                    new DamlFieldDefinition("microseconds", new DamlPrimitiveType(DamlPrimitive.Int64))
                ])
            },
            ConsumerTypeName: "Timer",
            ConsumerFieldName: "d",
            ConsumerFieldType: new DamlTypeRef("stdlib-time-id", "DA.Time.Types", "RelTime"),
            GenerateProjectFile: true,
            ExpectedInConsumer: ["using Daml.Runtime.Stdlib;", "RelTime D", "RelTime.FromRecord"],
            ExpectedAbsentFromConsumer: [],
            ExpectedAbsentFromProjectFile: ["Daml.Stdlib", "daml-stdlib"]),

        ["Tuple2 from daml-prim-DA-Types"] = new StdlibRoutingCase(
            StdlibPackageId: "daml-prim-id",
            StdlibPackageName: "daml-prim-DA-Types",
            StdlibModuleName: "DA.Types",
            StdlibDataType: Tuple2DataType(),
            ConsumerTypeName: "Pair",
            ConsumerFieldName: "p",
            ConsumerFieldType: Tuple2OfIntAndText("daml-prim-id"),
            GenerateProjectFile: false,
            ExpectedInConsumer:
            [
                "using Daml.Runtime.Stdlib;",
                "Tuple2<long, string>",
                "Tuple2<long, string>.FromRecord("
            ],
            ExpectedAbsentFromConsumer: [],
            ExpectedAbsentFromProjectFile: []),

        ["Set from daml-stdlib-DA-Set-Types"] = new StdlibRoutingCase(
            StdlibPackageId: "stdlib-set-types",
            StdlibPackageName: "daml-stdlib-DA-Set-Types",
            StdlibModuleName: "DA.Set.Types",
            StdlibDataType: new DamlDataType
            {
                Name = "Set",
                TypeParams = ["k"],
                Definition = new DamlRecordDefinition(
                [
                    new DamlFieldDefinition("map", new DamlTypeApp(
                        new DamlPrimitiveType(DamlPrimitive.GenMap),
                        [new DamlTypeVar("k"), new DamlPrimitiveType(DamlPrimitive.Unit)]))
                ])
            },
            ConsumerTypeName: "Roster",
            ConsumerFieldName: "members",
            ConsumerFieldType: new DamlTypeApp(
                new DamlTypeRef("stdlib-set-types", "DA.Set.Types", "Set"),
                [new DamlPrimitiveType(DamlPrimitive.Party)]),
            GenerateProjectFile: false,
            ExpectedInConsumer:
            [
                "using Daml.Runtime.Stdlib;",
                "global::Daml.Runtime.Stdlib.Set<global::Daml.Runtime.Data.Party>",
                "global::Daml.Runtime.Stdlib.Set<global::Daml.Runtime.Data.Party>.FromRecord("
            ],
            ExpectedAbsentFromConsumer: [],
            ExpectedAbsentFromProjectFile: []),

        ["Tuple2 from a user package that squats on DA.Types"] = new StdlibRoutingCase(
            StdlibPackageId: "user-pkg-id",
            StdlibPackageName: "my-cheeky-package",
            StdlibModuleName: "DA.Types",
            StdlibDataType: Tuple2DataType(),
            ConsumerTypeName: "Pair",
            ConsumerFieldName: "p",
            ConsumerFieldType: Tuple2OfIntAndText("user-pkg-id"),
            GenerateProjectFile: false,
            ExpectedInConsumer: [],
            ExpectedAbsentFromConsumer: ["Daml.Runtime.Stdlib.Tuple2", "using Daml.Runtime.Stdlib;"],
            ExpectedAbsentFromProjectFile: []),

        ["Tuple2 from a placeholder-named prim package"] = new StdlibRoutingCase(
            StdlibPackageId: "lf1x-prim-types-id",
            StdlibPackageName: "-no-package-metadata",
            StdlibModuleName: "DA.Types",
            StdlibDataType: Tuple2DataType(),
            ConsumerTypeName: "Pair",
            ConsumerFieldName: "p",
            ConsumerFieldType: Tuple2OfIntAndText("lf1x-prim-types-id"),
            GenerateProjectFile: true,
            ExpectedInConsumer:
            [
                "using Daml.Runtime.Stdlib;",
                "Tuple2<long, string>",
                "Tuple2<long, string>.FromRecord("
            ],
            ExpectedAbsentFromConsumer: ["No.Package.Metadata"],
            ExpectedAbsentFromProjectFile: ["No.Package.Metadata", "PackageReference Include=\"."]),
    };

    public static TheoryData<string> StdlibRoutingCaseNames => new(StdlibRoutingCasesByName.Keys);

    [Theory]
    [MemberData(nameof(StdlibRoutingCaseNames))]
    public void Generate_routes_a_referenced_type_through_Daml_Runtime_Stdlib_only_when_its_package_is_stdlib(
        string caseName)
    {
        var routing = StdlibRoutingCasesByName[caseName];

        var stdlibPackage = CreateTestPackage(
            routing.StdlibPackageId,
            routing.StdlibPackageName,
            new DamlModule
            {
                Name = routing.StdlibModuleName,
                Templates = [],
                DataTypes = [routing.StdlibDataType],
                Interfaces = []
            });

        var consumerPackage = CreateTestPackage(
            "main-pkg-id",
            "main-pkg",
            new DamlModule
            {
                Name = "App.Module",
                Templates = [],
                DataTypes =
                [
                    new DamlDataType
                    {
                        Name = routing.ConsumerTypeName,
                        Definition = new DamlRecordDefinition(
                        [
                            new DamlFieldDefinition(routing.ConsumerFieldName, routing.ConsumerFieldType)
                        ])
                    }
                ],
                Interfaces = []
            });

        var generator = CreateGenerator(new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = routing.GenerateProjectFile
        });

        var files = generator.Generate(CreateMultiPackageDar(consumerPackage, stdlibPackage)).ToList();
        var consumer = files.FirstOrDefault(f =>
            f.RelativePath.EndsWith($"{routing.ConsumerTypeName}.cs", global::System.StringComparison.Ordinal));

        consumer.Should().NotBeNull();
        foreach (var expected in routing.ExpectedInConsumer)
        {
            consumer!.Content.Should().Contain(expected);
        }
        foreach (var absent in routing.ExpectedAbsentFromConsumer)
        {
            consumer!.Content.Should().NotContain(absent);
        }

        if (!routing.GenerateProjectFile)
        {
            return;
        }

        var projectFile = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", global::System.StringComparison.Ordinal));
        projectFile.Should().NotBeNull();
        foreach (var absent in routing.ExpectedAbsentFromProjectFile)
        {
            projectFile!.Content.Should().NotContain(absent);
        }
    }
}
