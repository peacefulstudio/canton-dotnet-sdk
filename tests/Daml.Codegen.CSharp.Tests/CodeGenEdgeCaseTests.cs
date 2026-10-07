// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Tests for edge cases and special scenarios in code generation.
/// </summary>
public partial class CodeGenEdgeCaseTests
{
    private static DamlPackage CreateTestPackage(string packageId, string packageName, params DamlModule[] modules) =>
        new()
        {
            PackageId = packageId,
            Name = packageName,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DarModel CreateMultiPackageDar(DamlPackage main, params DamlPackage[] dependencies) =>
        new()
        {
            MainPackage = main,
            Dependencies = dependencies
        };

    #region Numeric Type Scale Tests

    [Fact]
    public void Generate_should_handle_numeric_with_scale_argument()
    {
        // Arrange - Numeric 10 is represented as DamlTypeApp(Numeric, [10])
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Amount",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.Numeric),
                            [new DamlTypeVar("10")])) // Numeric 10 scale
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var amountFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Amount.cs", global::System.StringComparison.Ordinal));

        // Assert
        amountFile.Should().NotBeNull();
        var code = amountFile!.Content;

        // Numeric with scale still maps to decimal
        code.Should().Contain("decimal Value");
        code.Should().Contain("new global::Daml.Runtime.Data.DamlNumeric(Value)");
    }

    [Fact]
    public void Generate_should_not_import_stdlib_namespace_for_numeric_scale_type_variable()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Amount",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.Numeric),
                            [new DamlTypeVar("10")]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var amountFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Amount.cs", global::System.StringComparison.Ordinal));

        amountFile.Should().NotBeNull();
        var code = amountFile!.Content;

        code.Should().Contain("decimal Value");
        code.Should().NotContain("using Daml.Runtime.Stdlib;");
    }

    #endregion

    #region Type Variable Tests

    [Fact]
    public void Generate_should_handle_type_variable()
    {
        // Arrange - Generic types have type variables
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Container",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("item", new DamlTypeVar("a"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var containerFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Container.cs", global::System.StringComparison.Ordinal));

        // Assert
        containerFile.Should().NotBeNull();
        var code = containerFile!.Content;

        // Type variables are mapped to generic type parameters (a -> TA)
        code.Should().Contain("TA Item");
    }

    [Fact]
    public void Generate_should_import_stdlib_namespace_for_generic_stub_in_record()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Container",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("item", new DamlTypeVar("a"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var containerFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Container.cs", global::System.StringComparison.Ordinal));

        containerFile.Should().NotBeNull();
        var code = containerFile!.Content;

        code.Should().Contain("GenericStub.NotImplemented");
        code.Should().Contain("using Daml.Runtime.Stdlib;");
    }

    [Fact]
    public void Generate_should_import_stdlib_namespace_for_generic_stub_in_variant()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Box",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Holds", new DamlTypeVar("a"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var boxFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Box.cs", global::System.StringComparison.Ordinal));

        boxFile.Should().NotBeNull();
        var code = boxFile!.Content;

        code.Should().Contain("GenericStub.NotImplemented");
        code.Should().Contain("using Daml.Runtime.Stdlib;");
    }

    [Fact]
    public void Generate_should_import_stdlib_namespace_for_generic_stub_in_list_element_type_variable()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Container",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("items", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlTypeVar("a")]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var containerFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Container.cs", global::System.StringComparison.Ordinal));

        containerFile.Should().NotBeNull();
        var code = containerFile!.Content;

        code.Should().Contain("GenericStub.NotImplemented");
        code.Should().Contain("using Daml.Runtime.Stdlib;");
    }

    #endregion
}
