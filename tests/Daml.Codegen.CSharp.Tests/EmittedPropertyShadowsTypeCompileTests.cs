// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedPropertyShadowsTypeCompileTests
{
    private const string Module = "Test.Module";

    private static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    private static DamlFieldDefinition Field(string name, DamlType type) => new(name, type);

    private static DamlType Int64 => new DamlPrimitiveType(DamlPrimitive.Int64);

    private static DarModel DarWithStepAndHolder(DamlType stepFieldType) =>
        DarOf(
            [],
            Record("Step", Field("amount", Int64)),
            Record("Schedule", Field("step", stepFieldType)));

    private static DarModel DarOf(IReadOnlyList<DamlTemplate> templates, params DamlDataType[] dataTypes)
    {
        var module = new DamlModule
        {
            Name = Module,
            Templates = templates,
            DataTypes = dataTypes,
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

    private static DamlTypeRef StepRef => new("", Module, "Step");

    public static TheoryData<string, DamlType> StepFieldShapes => new()
    {
        { "Step", StepRef },
        { "[Step]", new DamlListType(StepRef) },
        { "Optional Step", new DamlOptionalType(StepRef) },
        { "TextMap Step", new DamlTextMapType(StepRef) },
    };

    [Theory]
    [MemberData(nameof(StepFieldShapes))]
    public void Property_named_after_a_sibling_type_does_not_break_the_emitted_code(string shape, DamlType fieldType)
    {
        var files = CreateGenerator().Generate(DarWithStepAndHolder(fieldType));

        var errors = CompileErrors(files);

        errors.Should().BeEmpty($"a field `step : {shape}` beside a type `Step` must compile, got: {string.Join("\n", errors)}");
    }

    private static DamlTemplate SchedWithAdvanceChoice() => new()
    {
        Name = "Sched",
        Choices =
        [
            new DamlChoice
            {
                Name = "Advance",
                Consuming = false,
                ArgumentType = new DamlTypeRef("", Module, "Advance"),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
            },
        ],
    };

    public static TheoryData<string, DamlType> ChoiceArgumentFieldShapes => new()
    {
        { "Advance", new DamlTypeRef("", Module, "Advance") },
        { "[Advance]", new DamlListType(new DamlTypeRef("", Module, "Advance")) },
    };

    [Theory]
    [MemberData(nameof(ChoiceArgumentFieldShapes))]
    public void Property_named_after_the_template_a_choice_argument_is_nested_in_does_not_break_the_emitted_code(
        string shape,
        DamlType fieldType)
    {
        var dar = DarOf(
            [SchedWithAdvanceChoice()],
            Record("Sched", Field("owner", Int64)),
            Record("Advance", Field("days", Int64)),
            Record("Holder", Field("sched", fieldType)));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"a field `sched : {shape}` whose emitted receiver is `Sched.Advance` must compile, got: {string.Join("\n", errors)}");
    }

    public static TheoryData<string, DamlType> GenericApplicationShapes => new()
    {
        { "Slot Int", new DamlTypeApp(new DamlTypeRef("", Module, "Slot"), [Int64]) },
        { "[Slot Int]", new DamlListType(new DamlTypeApp(new DamlTypeRef("", Module, "Slot"), [Int64])) },
        { "Slot Step", new DamlTypeApp(new DamlTypeRef("", Module, "Slot"), [new DamlTypeRef("", Module, "Step")]) },
    };

    [Theory]
    [MemberData(nameof(GenericApplicationShapes))]
    public void Property_named_after_a_generic_type_does_not_break_the_emitted_code(string shape, DamlType fieldType)
    {
        var dar = DarOf(
            [],
            Record("Step", Field("amount", Int64)),
            new DamlDataType
            {
                Name = "Slot",
                TypeParams = ["a"],
                Definition = new DamlRecordDefinition([Field("value", new DamlTypeVar("a"))]),
            },
            Record("Holder", Field("slot", fieldType)));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"a field `slot : {shape}` beside a generic type `Slot` must compile, got: {string.Join("\n", errors)}");
    }

    [Fact]
    public void Property_named_after_a_primitive_runtime_type_does_not_break_the_emitted_code()
    {
        var dar = DarOf([], Record("Holder", Field("party", new DamlPrimitiveType(DamlPrimitive.Party))));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"a field `party : Party` must compile, got: {string.Join("\n", errors)}");
    }

    [Fact]
    public void Template_payload_field_typed_after_a_type_named_like_the_choices_property_compiles()
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
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                },
            ],
        };
        var dar = DarOf(
            [template],
            Record("Choices", Field("amount", Int64)),
            Record("Sched", Field("history", new DamlListType(new DamlTypeRef("", Module, "Choices")))));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"a template field `history : [Choices]` must compile, got: {string.Join("\n", errors)}");
    }

    [Fact]
    public void Property_named_after_an_enum_extensions_receiver_compiles()
    {
        var stepEnum = new DamlDataType { Name = "Step", Definition = new DamlEnumDefinition(["A", "B"]) };
        var dar = DarOf(
            [],
            stepEnum,
            Record("Holder", Field("step", StepRef), Field("stepExtensions", Int64)));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"got: {string.Join("\n", errors)}");
    }

    [Fact]
    public void Optional_enum_field_named_after_its_type_compiles()
    {
        var stepEnum = new DamlDataType { Name = "Step", Definition = new DamlEnumDefinition(["A", "B"]) };
        var dar = DarOf([], stepEnum, Record("Holder", Field("step", new DamlOptionalType(StepRef))));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"got: {string.Join("\n", errors)}");
    }

    [Fact]
    public void Party_field_beside_a_field_named_party_compiles()
    {
        var dar = DarOf([], Record("Holder", Field("owner", new DamlPrimitiveType(DamlPrimitive.Party)), Field("party", Int64)));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"got: {string.Join("\n", errors)}");
    }

    [Fact]
    public void Template_choice_descriptor_property_named_like_a_field_type_compiles()
    {
        var template = new DamlTemplate
        {
            Name = "Sched",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Transfer",
                    Consuming = false,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                },
            ],
        };
        var dar = DarOf(
            [template],
            Record("ChoiceTransfer", Field("amount", Int64)),
            Record("Sched", Field("history", new DamlListType(new DamlTypeRef("", Module, "ChoiceTransfer")))));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"got: {string.Join("\n", errors)}");
    }

    [Fact]
    public void Choice_argument_field_typed_after_an_enclosing_template_payload_member_compiles()
    {
        var dar = DarOf(
            [SchedWithAdvanceChoice()],
            Record("Step", Field("amount", Int64)),
            Record("Sched", Field("step", Int64)),
            Record("Advance", Field("history", new DamlListType(StepRef))));

        var errors = CompileErrors(CreateGenerator().Generate(dar));

        errors.Should().BeEmpty($"got: {string.Join("\n", errors)}");
    }

    private static List<string> CompileErrors(IReadOnlyList<GeneratedFile> files) =>
        CompileEmittedFiles(files)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)
            .ToList();
}
