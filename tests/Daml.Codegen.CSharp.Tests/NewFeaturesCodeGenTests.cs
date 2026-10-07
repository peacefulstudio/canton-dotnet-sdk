// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Tests for new code generation features: Contract Keys, Interfaces, Generic Types, and Package Upgrades.
/// </summary>
public class NewFeaturesCodeGenTests
{
    #region Contract Keys Tests

    private static DamlModule KeyedModule(string templateName, DamlType key, IReadOnlyList<DamlDataType>? extraDataTypes = null) =>
        new()
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Choices = [],
                    Key = key
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = templateName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("assetId", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                },
                .. extraDataTypes ?? []
            ],
            Interfaces = []
        };

    private static string EmitKeyed(string templateName, DamlType key, IReadOnlyList<DamlDataType>? extraDataTypes = null)
    {
        var files = CreateGenerator().Generate(CreateTestDar(KeyedModule(templateName, key, extraDataTypes)));
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith($"{templateName}.cs", StringComparison.Ordinal));
        templateFile.Should().NotBeNull();
        return templateFile!.Content;
    }

    [Fact]
    public void Generate_should_decode_a_primitive_key_through_the_key_witness()
    {
        var code = EmitKeyed("AssetWithKey", new DamlPrimitiveType(DamlPrimitive.Text));

        code.Should().Contain("IHasKey<AssetWithKey, string>");
        code.Should().Contain("KeyDecoder = value => value.As<global::Daml.Runtime.Data.DamlText>().Value,");
    }

    [Fact]
    public void Generate_should_decode_a_party_key_through_the_key_witness()
    {
        var code = EmitKeyed("UserProfile", new DamlPrimitiveType(DamlPrimitive.Party));

        code.Should().Contain("global::Daml.Runtime.Contracts.IHasKey<UserProfile, global::Daml.Runtime.Data.Party>");
        code.Should().Contain("KeyDecoder = value => global::Daml.Runtime.Data.Party.FromDamlValue(value.As<global::Daml.Runtime.Data.DamlParty>()),");
    }

    [Fact]
    public void Generate_should_decode_a_record_key_through_the_key_witness()
    {
        var code = EmitKeyed(
            "CompositeKeyTemplate",
            new DamlTypeRef("", "Test.Module", "AssetKey"),
            [
                new DamlDataType
                {
                    Name = "AssetKey",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("assetId", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ]);

        code.Should().Contain("IHasKey<CompositeKeyTemplate, global::Test.Module.AssetKey>");
        code.Should().Contain("KeyDecoder = value => global::Test.Module.AssetKey.FromRecord(value.As<global::Daml.Runtime.Data.DamlRecord>()),");
    }

    [Fact]
    public void Generate_should_emit_no_unescaped_cref_when_the_key_type_contains_angle_brackets()
    {
        var code = EmitKeyed(
            "ListKeyTemplate",
            new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Text)]));

        code.Should().Contain("global::Daml.Runtime.Contracts.IHasKey<ListKeyTemplate, global::System.Collections.Generic.IReadOnlyList<string>>");
        code.Should().NotMatchRegex(@"cref=""[^""]*<[^""]*""",
            "a cref must render angle brackets as {{ }}, or a consumer building with GenerateDocumentationFile and TreatWarningsAsErrors fails on malformed XML");
    }

    [Fact]
    public void Generate_should_emit_no_instance_key_member_on_the_template_payload()
    {
        var code = EmitKeyed("AssetWithKey", new DamlPrimitiveType(DamlPrimitive.Text));

        code.Should().NotMatchRegex(@"\bpublic\s+(?!static\b)[^;{=]+?\s+Key\b",
            "the payload is what a caller constructs locally, so it cannot know the key of a contract it has not created yet");
    }

    [Fact]
    public void Generate_should_leave_a_key_less_template_without_a_key_slot()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "NoKeyTemplate",
                    Choices = [],
                    Key = null
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NoKeyTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))
                    ])
                }
            ],
            Interfaces = []
        };

        var files = CreateGenerator().Generate(CreateTestDar(module));
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("NoKeyTemplate.cs", StringComparison.Ordinal));

        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().NotContain("IHasKey");
        code.Should().NotContain("ContractKey");
    }

    #endregion

    #region Generic Types Tests

    [Fact]
    public void Generate_should_create_generic_record_with_type_parameters()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Container",
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlTypeVar("a"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var containerFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Container.cs", StringComparison.Ordinal));

        // Assert
        containerFile.Should().NotBeNull();
        var code = containerFile!.Content;

        code.Should().Contain("public sealed record Container<TA>");
        code.Should().Contain("TA Value");
    }

    [Fact]
    public void Generate_should_create_generic_record_with_multiple_type_parameters()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Pair",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("first", new DamlTypeVar("a")),
                        new DamlFieldDefinition("second", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var pairFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Pair.cs", StringComparison.Ordinal));

        // Assert
        pairFile.Should().NotBeNull();
        var code = pairFile!.Content;

        code.Should().Contain("public sealed record Pair<TA, TB>");
        code.Should().Contain("TA First");
        code.Should().Contain("TB Second");
    }

    [Fact]
    public void Generate_should_create_generic_variant_with_type_parameters()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Either",
                    TypeParams = ["a", "b"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Left", new DamlTypeVar("a")),
                        new DamlVariantConstructor("Right", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var eitherFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Either.cs", StringComparison.Ordinal));

        // Assert
        eitherFile.Should().NotBeNull();
        var code = eitherFile!.Content;

        code.Should().Contain("public abstract record Either<TA, TB>");
        code.Should().Contain("public sealed record Left(TA Value) : Either<TA, TB>");
        code.Should().Contain("public sealed record Right(TB Value) : Either<TA, TB>");
    }

    [Fact]
    public void Generate_should_include_typeparam_documentation_for_generic_types()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Box",
                    TypeParams = ["t"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("contents", new DamlTypeVar("t"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var boxFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Box.cs", StringComparison.Ordinal));

        // Assert
        boxFile.Should().NotBeNull();
        var code = boxFile!.Content;

        code.Should().Contain("/// <typeparam name=\"TT\">Type parameter t</typeparam>");
    }

    [Fact]
    public void Generate_should_handle_record_without_type_parameters()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NonGeneric",
                    TypeParams = [], // Empty type params
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var nonGenericFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("NonGeneric.cs", StringComparison.Ordinal));

        // Assert
        nonGenericFile.Should().NotBeNull();
        var code = nonGenericFile!.Content;

        code.Should().Contain("public sealed record NonGeneric(");
        code.Should().NotContain("NonGeneric<");
    }

    [Fact]
    public void Generate_should_sanitize_type_parameter_names()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Wrapper",
                    TypeParams = ["some_type", "another-type"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("val1", new DamlTypeVar("some_type")),
                        new DamlFieldDefinition("val2", new DamlTypeVar("another-type"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var wrapperFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wrapper.cs", StringComparison.Ordinal));

        // Assert
        wrapperFile.Should().NotBeNull();
        var code = wrapperFile!.Content;

        code.Should().Contain("TSomeType");
        code.Should().Contain("TAnotherU002dtype");
    }

    #endregion

    #region Package Upgrades Tests

    [Fact]
    public void Generate_should_implement_IUpgradeable_when_package_has_upgraded_package_id()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "UpgradedTemplate",
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "UpgradedTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module, upgradedPackageId: "previous-package-id-12345");
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("UpgradedTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("IUpgradeable");
        code.Should().Contain("public static string? UpgradedPackageId => \"previous-package-id-12345\";");
    }

    [Fact]
    public void Generate_should_not_implement_IUpgradeable_when_no_upgraded_package()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "NormalTemplate",
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NormalTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module, upgradedPackageId: null);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("NormalTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().NotContain("IUpgradeable");
        code.Should().NotContain("UpgradedPackageId");
    }

    [Fact]
    public void Generate_should_include_upgraded_package_id_documentation()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "DocumentedUpgrade",
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DocumentedUpgrade",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module, upgradedPackageId: "old-package-id");
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("DocumentedUpgrade.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("/// <summary>Gets the package ID that this package upgrades.</summary>");
    }

    [Fact]
    public void Generate_should_carry_both_the_contract_key_and_IUpgradeable_when_applicable()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "FullFeaturedTemplate",
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text)
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "FullFeaturedTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("id", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module, upgradedPackageId: "previous-version-id");
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("FullFeaturedTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("ITemplate");
        code.Should().Contain("IUpgradeable");
        code.Should().Contain("IHasKey<FullFeaturedTemplate, string>");
    }

    #endregion

    #region Combined Features Tests

    [Fact]
    public void Generate_should_handle_module_with_templates_interfaces_and_data_types()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Party)
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                },
                new DamlDataType
                {
                    Name = "Wrapper",
                    TypeParams = ["t"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlTypeVar("t"))
                    ])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Transferable",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module, upgradedPackageId: "old-pkg-id");
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);

        // Assert
        files.Should().HaveCountGreaterThan(2);

        // Template should exist with key and upgrade support
        var assetFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        assetFile.Should().NotBeNull();
        assetFile!.Content.Should().Contain("global::Daml.Runtime.Contracts.IHasKey<Asset, global::Daml.Runtime.Data.Party>");
        assetFile.Content.Should().Contain("IUpgradeable");

        // Generic data type should exist
        var wrapperFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wrapper.cs", StringComparison.Ordinal));
        wrapperFile.Should().NotBeNull();
        wrapperFile!.Content.Should().Contain("Wrapper<TT>");

        // Interface should exist
        var interfaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ITransferable.cs", StringComparison.Ordinal));
        interfaceFile.Should().NotBeNull();
        interfaceFile!.Content.Should().Contain("public interface ITransferable");
    }

    #endregion
}
