// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedStructuralQualificationCompileTests
{
    private const string Module = "Test.Module";

    private static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    private static DamlFieldDefinition Field(string name, DamlType type) => new(name, type);

    private static DamlType Int64 => new DamlPrimitiveType(DamlPrimitive.Int64);

    private static DamlTypeRef Ref(string name) => new("", Module, name);

    private static DarModel DarOf(IReadOnlyList<DamlTemplate> templates, params DamlDataType[] dataTypes) =>
        new()
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-package-id",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules =
                [
                    new DamlModule { Name = Module, Templates = templates, DataTypes = dataTypes, Interfaces = [] },
                ],
                DependencyReferences = [],
            },
            Dependencies = [],
        };

    private static List<string> CompileErrors(DarModel dar) =>
        CompileEmittedFiles(CreateGenerator().Generate(dar))
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)
            .ToList();

    [Fact]
    public void Variant_referencing_a_type_named_tag_compiles()
    {
        var dar = DarOf(
            [],
            Record("Tag", Field("label", new DamlPrimitiveType(DamlPrimitive.Text))),
            new DamlDataType
            {
                Name = "Choice1",
                Definition = new DamlVariantDefinition(
                [
                    new DamlVariantConstructor("Pick", Ref("Tag")),
                    new DamlVariantConstructor("Other", Int64),
                ]),
            });

        CompileErrors(dar).Should().BeEmpty();
    }

    public static TheoryData<string, DamlType> StepResultShapes => new()
    {
        { "Step", Ref("Step") },
        { "[Step]", new DamlListType(Ref("Step")) },
    };

    [Theory]
    [MemberData(nameof(StepResultShapes))]
    public void Choice_result_named_after_a_template_field_compiles(string shape, DamlType resultType)
    {
        var template = new DamlTemplate
        {
            Name = "Sched",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Advance",
                    Consuming = false,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = resultType,
                },
            ],
        };
        var dar = DarOf(
            [template],
            Record("Step", Field("n", Int64)),
            Record("Sched", Field("owner", new DamlPrimitiveType(DamlPrimitive.Party)), Field("step", Int64)));

        CompileErrors(dar).Should().BeEmpty($"choice result `{shape}` beside field `step`");
    }

    [Fact]
    public void Unit_field_beside_a_field_named_damlunit_compiles()
    {
        var dar = DarOf(
            [],
            Record("Holder", Field("marker", new DamlPrimitiveType(DamlPrimitive.Unit)), Field("damlUnit", Int64)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Variant_constructor_named_like_its_variant_compiles()
    {
        var dar = DarOf(
            [],
            new DamlDataType
            {
                Name = "Step",
                Definition = new DamlVariantDefinition(
                [
                    new DamlVariantConstructor("Step", Int64),
                    new DamlVariantConstructor("Skip", null),
                ]),
            });

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Record_field_named_from_record_compiles()
    {
        var dar = DarOf(
            [],
            Record("FromRecord", Field("x", Int64)),
            Record("Holder", Field("fromRecord", Ref("FromRecord"))));

        CompileErrors(dar).Should().BeEmpty();
    }
}
