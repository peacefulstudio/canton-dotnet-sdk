// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class IntermediateModelApiSurfaceTests
{
    private static Assembly ModelAssembly => typeof(DamlType).Assembly;

    public static TheoryData<string> MovedOptionalRepresentationTypeNames() =>
        ["DamlWrappedOptional", "OptionalEncoding"];

    [Theory]
    [MemberData(nameof(MovedOptionalRepresentationTypeNames))]
    public void IntermediateModelApiSurface_no_optional_representation_type_is_exported(string movedTypeName)
    {
        ModelAssembly.GetExportedTypes()
            .Where(type => type.Name == movedTypeName)
            .Should().BeEmpty(
                "{0} is a C# optional-representation decision and now lives inside the emitter "
                + "(Daml.Codegen.CSharp); a consumer referencing the published Intermediate "
                + "package must no longer resolve it",
                movedTypeName);
    }

    [Fact]
    public void IntermediateModelApiSurface_daml_type_exposes_no_optional_query_member()
    {
        ModelAssembly.GetTypes()
            .Where(type => typeof(DamlType).IsAssignableFrom(type))
            .SelectMany(type => type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(member => member.Name.Contains("IsOptional", StringComparison.Ordinal))
            .Select(member => $"{member.DeclaringType!.Name}.{member.Name}")
            .Should().BeEmpty(
                "the neutral model must not answer 'is this optional' for its consumers — that "
                + "decision is the emitter's, through its own representation pre-pass. The scan covers "
                + "non-public types too (GetTypes, matching the mapper drift guard's subtype "
                + "enumeration), so an internal DamlType subtype carrying an IsOptional member cannot "
                + "evade the pin");
    }

    [Fact]
    public void IntermediateModelApiSurface_daml_type_subtypes_are_exactly_nine_public_nodes()
    {
        var publicNodes = ModelAssembly.GetExportedTypes()
            .Where(type => typeof(DamlType).IsAssignableFrom(type) && !type.IsAbstract)
            .ToList();

        publicNodes.Select(type => type.Name).Should().BeEquivalentTo(
        [
            "DamlPrimitiveType",
            "DamlTypeRef",
            "DamlTypeApp",
            "DamlTypeVar",
            "DamlListType",
            "DamlOptionalType",
            "DamlTextMapType",
            "DamlGenMapType",
            "DamlContractIdType",
        ],
            "the type algebra is exactly these nine public nodes — the four pre-existing shapes "
            + "plus the five applied-type nodes. A tenth public subtype means IDamlTypeVisitor, "
            + "every emitter switch, and this drift pin all need extending together");
        publicNodes.Should().OnlyContain(type => type.IsSealed,
            "every public DamlType node is a sealed record, so the algebra cannot be extended "
            + "outside the model assembly");
    }

    public static TheoryData<string, string[]> TypedNodeParameterNames() => new()
    {
        { "DamlListType", ["Element"] },
        { "DamlOptionalType", ["Value"] },
        { "DamlTextMapType", ["Value"] },
        { "DamlGenMapType", ["Key", "Value"] },
        { "DamlContractIdType", ["Payload"] },
    };

    [Theory]
    [MemberData(nameof(TypedNodeParameterNames))]
    public void IntermediateModelApiSurface_each_typed_node_declares_only_its_daml_type_parameters(
        string nodeTypeName,
        string[] expectedParameterNames)
    {
        var nodeType = ModelAssembly.GetType($"Daml.Codegen.Intermediate.Model.{nodeTypeName}")!;
        var constructor = nodeType.GetConstructors().Single();
        var parameters = constructor.GetParameters();

        parameters.Select(parameter => parameter.Name).Should().Equal(expectedParameterNames,
            "{0}'s positional parameters are the documented shape of the node", nodeTypeName);
        parameters.Should().OnlyContain(parameter => parameter.ParameterType == typeof(DamlType),
            "each typed node takes its arguments as DamlType values, so structural equality "
            + "compares type trees, not just the leaf constructor");
    }

    [Fact]
    public void IntermediateModelApiSurface_visitor_declares_exactly_one_method_per_public_node()
    {
        var visitorType = ModelAssembly.GetType("Daml.Codegen.Intermediate.Model.IDamlTypeVisitor`1")!;
        var closedVisitor = visitorType.MakeGenericType(typeof(object));
        var methods = closedVisitor.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        var publicNodes = ModelAssembly.GetExportedTypes()
            .Where(type => typeof(DamlType).IsAssignableFrom(type) && !type.IsAbstract)
            .ToList();

        methods.Should().HaveCount(publicNodes.Count,
            "the visitor carries exactly one arm per public node — no default arm, and no node "
            + "without an arm");
        foreach (var node in publicNodes)
        {
            methods.Should().ContainSingle(method =>
                    method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == node,
                "the arm for {0} takes exactly that node as its sole parameter", node.Name);
        }
    }

    [Fact]
    public void IntermediateModelApiSurface_typed_nodes_and_visitor_are_documented()
    {
        var docPath = Path.Combine(AppContext.BaseDirectory, "Daml.Codegen.Intermediate.xml");
        File.Exists(docPath).Should().BeTrue(
            "the published package carries XML documentation for its public members, and the test "
            + "output directory receives the generated file through the project reference");
        var doc = File.ReadAllText(docPath);

        string[] docEntries =
        [
            "T:Daml.Codegen.Intermediate.Model.DamlListType",
            "T:Daml.Codegen.Intermediate.Model.DamlOptionalType",
            "T:Daml.Codegen.Intermediate.Model.DamlTextMapType",
            "T:Daml.Codegen.Intermediate.Model.DamlGenMapType",
            "T:Daml.Codegen.Intermediate.Model.DamlContractIdType",
            "P:Daml.Codegen.Intermediate.Model.DamlListType.Element",
            "P:Daml.Codegen.Intermediate.Model.DamlOptionalType.Value",
            "P:Daml.Codegen.Intermediate.Model.DamlTextMapType.Value",
            "P:Daml.Codegen.Intermediate.Model.DamlGenMapType.Key",
            "P:Daml.Codegen.Intermediate.Model.DamlGenMapType.Value",
            "P:Daml.Codegen.Intermediate.Model.DamlContractIdType.Payload",
            "T:Daml.Codegen.Intermediate.Model.IDamlTypeVisitor`1",
            "M:Daml.Codegen.Intermediate.Model.DamlType.Accept``1(Daml.Codegen.Intermediate.Model.IDamlTypeVisitor{``0})",
        ];
        foreach (var entry in docEntries)
        {
            doc.Should().Contain($"\"{entry}\"",
                "the released XML documentation file must carry an entry for every new member");
        }
    }

    [Fact]
    public void IntermediateModelApiSurface_public_names_carry_no_csharp_or_encoding_concept()
    {
        var exportedTypes = ModelAssembly.GetExportedTypes().ToList();

        exportedTypes.Select(type => type.FullName!)
            .Concat(exportedTypes.SelectMany(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => $"{type.Name}.{member.Name}")))
            .Where(name => name.Contains("csharp", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("encoding", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty(
                "Daml.Codegen.Intermediate is a language-neutral exchange contract: a C# emission "
                + "or wire-encoding decision must not be reachable from its public surface");
    }
}
