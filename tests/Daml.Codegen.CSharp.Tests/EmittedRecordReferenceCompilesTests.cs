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

public class EmittedRecordReferenceCompilesTests
{
    [Fact]
    public void Emitted_sibling_record_referencing_nested_choice_arg_type_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MergeDelegation",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MergeDelegation_Merge",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "MergeDelegation_Merge"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "MergeDelegation",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "MergeDelegation_Merge",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("quantity", new DamlPrimitiveType(DamlPrimitive.Numeric))]),
                },
                new DamlDataType
                {
                    Name = "MergeDelegationCall",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("delegationCid", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Module", "MergeDelegation")])),
                        new DamlFieldDefinition("choiceArg", new DamlTypeRef("", "Test.Module", "MergeDelegation_Merge")),
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
            "a sibling record referencing a same-package nested choice-arg type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choice_argument_record_with_field_that_pascalcases_to_the_choice_name_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Quantity",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Quantity"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Quantity",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("quantity", new DamlPrimitiveType(DamlPrimitive.Numeric))]),
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

        var nestedSource = files
            .Single(f => f.RelativePath.EndsWith("Holding.Quantity.cs", StringComparison.Ordinal))
            .Content;
        nestedSource.Should().Contain("Quantity_",
            "the choice-arg-record field colliding with the nested choice type name must be disambiguated to Quantity_");
        nestedSource.Should().Contain("record.GetRequiredField(\"quantity\")",
            "the Daml wire field name \"quantity\" must be unchanged by the C# member rename");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a nested choice-arg record whose field PascalCases to the choice (and thus the nested record) name would be CS0542 unless disambiguated, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_a_variant_typed_field_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Choice",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Yes", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlVariantConstructor("No", null),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("pick", new DamlTypeRef("", "Test.Module", "Choice"))]),
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
            "a record field whose type is a variant must serialize via the variant's ToVariant/FromVariant, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void RequireForFieldType_recurses_into_arguments_when_base_is_a_nested_type_app()
    {
        var sb = new System.Text.StringBuilder();
        var indent = new IndentWriter(sb);

        var nestedBase = new DamlTypeApp(
            new DamlTypeRef("test-pkg", "Test.Module", "Wrapper"),
            [new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var nestedAppCarryingContractId = new DamlTypeApp(
            nestedBase,
            [ContractIdOf("Asset")]);

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };
        var resolver = new DarCrossPackageResolver(
            new DarModel { MainPackage = package, Dependencies = [] },
            new PackageNameTableCache(new CodeGenOptions(), package.PackageId));

        StdlibPackages.RequireForFieldType(resolver, package, indent, nestedAppCarryingContractId);

        indent.RequiredUsings.Should().Contain(
            "Daml.Runtime.Contracts",
            "any DamlTypeApp shape that transitively wraps a ContractId must add the runtime-contracts using, regardless of what the head's Base is (TypeRef, nested TypeApp, TypeVar) — otherwise a future MapDamlTypeToCSharp arm that learns to render the head type would emit `ContractId<...>` without the import.");
    }

    [Fact]
    public void Emitted_record_with_tuple_field_wrapping_contract_id_compiles()
    {
        var stdlibPackage = new DamlPackage
        {
            PackageId = "daml-prim-id",
            Name = "daml-prim",
            Version = new Version(0, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Tuple2",
                            TypeParams = ["a", "b"],
                            Definition = new DamlRecordDefinition([]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var contractIdTimesInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Tuple2"),
            [ContractIdOf("Asset"), new DamlPrimitiveType(DamlPrimitive.Int64)]);

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
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("pair", contractIdTimesInt)]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [stdlibPackage] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record field typed as a parametric stdlib type wrapping ContractId<T> must compile (the file needs `using Daml.Runtime.Contracts;`), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
