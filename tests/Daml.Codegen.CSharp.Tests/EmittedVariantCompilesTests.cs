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

public class EmittedVariantCompilesTests
{
    [Fact]
    public void Emitted_variant_with_constructor_that_matches_the_type_name_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Wrapper",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Wrapper", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlVariantConstructor("Empty", null),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar).ToList();

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant whose constructor name equals its own type name would be CS0542 unless disambiguated, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_constructor_referencing_nested_choice_arg_type_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "DsoRules",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "DsoRules_AddSv",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "DsoRules_AddSv"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DsoRules",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("dso", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "DsoRules_AddSv",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("svParty", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "DsoRules_ActionRequiringConfirmation",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("SRARC_AddSv",
                            new DamlTypeRef("", "Test.Module", "DsoRules_AddSv")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant constructor referencing a same-package nested choice-arg type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_constructor_with_contract_id_argument_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "AnyValue",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("AnyContractId", ContractIdOf("Asset")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant constructor whose argument type is ContractId<T> must compile (the file needs `using Daml.Runtime.Contracts;`), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_constructor_whose_payload_is_another_variant_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Inner",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Lit", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlVariantConstructor("None", null),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Outer",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Wrap", new DamlTypeRef("", "Test.Module", "Inner")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant constructor whose payload is another variant must round-trip through that variant's ToVariant/FromVariant, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_recursive_variant_with_direct_nested_variant_payload_compiles_without_ToRecord_CS1061_regression()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Formula",
                    TypeParams = ["a"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Proposition", new DamlTypeVar("a")),
                        new DamlVariantConstructor("Negation", new DamlTypeApp(
                            new DamlTypeRef("", "Test.Module", "Formula"),
                            [new DamlTypeVar("a")])),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar).ToList();

        var formulaSource = files.Single(f => f.RelativePath.EndsWith("Formula.cs", StringComparison.Ordinal)).Content;
        formulaSource.Should().Contain("Value.ToVariant(",
            "a direct nested-variant payload must serialize via ToVariant(), not ToRecord()");
        formulaSource.Should().NotContain("Value.ToRecord(",
            "a nested-variant payload has no ToRecord() — serialization must use ToVariant()");
        formulaSource.Should().Contain(".FromVariant(",
            "a direct nested-variant payload must deserialize via FromVariant(), not FromRecord()");
        formulaSource.Should().NotContain(".FromRecord(",
            "a nested-variant payload has no FromRecord() — deserialization must use FromVariant()");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a recursive variant whose payload is the same variant must round-trip via ToVariant/FromVariant, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_with_list_of_nested_variant_payload_serializes_elements_via_ToVariant_and_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Formula",
                    TypeParams = ["a"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Proposition", new DamlTypeVar("a")),
                        new DamlVariantConstructor("Conjunction", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlTypeApp(
                                new DamlTypeRef("", "Test.Module", "Formula"),
                                [new DamlTypeVar("a")])])),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar).ToList();

        var formulaSource = files.Single(f => f.RelativePath.EndsWith("Formula.cs", StringComparison.Ordinal)).Content;
        formulaSource.Should().Contain("x.ToVariant(",
            "the list element serializer must convert nested-variant payloads via ToVariant(), not ToRecord()");
        formulaSource.Should().NotContain("x.ToRecord(",
            "a nested-variant list payload has no ToRecord() — serialization must use ToVariant()");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant carrying a list of nested-variant payloads must round-trip via ToVariant/FromVariant, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
