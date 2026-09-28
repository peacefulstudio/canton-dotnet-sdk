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
    #region Cross-Package Type Reference Tests

    // Cross-DAR type refs are the headline of the spike: a generated record can
    // reference types defined in a different DAR shipped in the same archive (e.g.
    // splice-api-token-holding-v1's HoldingView references
    // splice-api-token-metadata-v1's Metadata). The codegen needs to (a) emit a
    // fully qualified C# name, (b) record the foreign package id so a
    // <PackageReference> ends up in the generated csproj. These tests pin both
    // halves down independent of any specific Splice version.

    [Fact]
    public void Generate_should_emit_fully_qualified_name_for_foreign_package_type_ref()
    {
        // Arrange — main package references a record defined in a foreign package.
        const string ForeignPackageId = "foreign-pkg-id";

        var foreignModule = new DamlModule
        {
            Name = "Foreign.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Meta",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("note", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };
        var foreignPkg = CreateTestPackage(ForeignPackageId, "foreign-pkg", foreignModule);

        var mainModule = new DamlModule
        {
            Name = "Main.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Wrapper",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("meta", new DamlTypeRef(ForeignPackageId, "Foreign.Module", "Meta"))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, foreignPkg);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();
        var wrapper = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wrapper.cs", StringComparison.Ordinal));

        wrapper.Should().NotBeNull();
        wrapper!.Content.Should().Contain("global::Foreign.Module.Meta Meta");
        wrapper.Content.Should().Contain("global::Foreign.Module.Meta.FromRecord(record.GetRequiredField(\"meta\").As<DamlRecord>())");
    }

    [Fact]
    public void Generate_should_emit_PackageReference_for_each_foreign_package_referenced()
    {
        // Arrange — two foreign packages, both referenced by fields of the main type.
        var foreignA = CreateTestPackage(
            "foreign-a-id",
            "foreign-a",
            new DamlModule
            {
                Name = "A.Module",
                Templates = [],
                DataTypes = [new DamlDataType { Name = "AType", Definition = new DamlRecordDefinition([]) }],
                Interfaces = []
            });
        var foreignB = CreateTestPackage(
            "foreign-b-id",
            "foreign-b",
            new DamlModule
            {
                Name = "B.Module",
                Templates = [],
                DataTypes = [new DamlDataType { Name = "BType", Definition = new DamlRecordDefinition([]) }],
                Interfaces = []
            });

        var mainModule = new DamlModule
        {
            Name = "Main.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Owner",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("a", new DamlTypeRef("foreign-a-id", "A.Module", "AType")),
                        new DamlFieldDefinition("b", new DamlTypeRef("foreign-b-id", "B.Module", "BType"))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, foreignA, foreignB);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = true
        };
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar).ToList();
        var csproj = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        // Assert
        csproj.Should().NotBeNull();
        csproj!.Content.Should().Contain("<PackageReference Include=\"Foreign.A\" Version=\"1.0.0.*-*\" />");
        csproj.Content.Should().Contain("<PackageReference Include=\"Foreign.B\" Version=\"1.0.0.*-*\" />");
    }

    [Fact]
    public void Generate_should_not_emit_foreign_namespace_or_PackageReference_for_unmapped_type_in_placeholder_package()
    {
        const string ForeignPackageId = "foreign-no-metadata-id";

        var foreignModule = new DamlModule
        {
            Name = "Foreign.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Meta",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("note", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };
        var foreignPkg = CreateTestPackage(ForeignPackageId, "-no-package-metadata", foreignModule);

        var mainModule = new DamlModule
        {
            Name = "Main.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Wrapper",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("meta", new DamlTypeRef(ForeignPackageId, "Foreign.Module", "Meta"))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, foreignPkg);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = true
        };
        var generator = CreateGenerator(options);

        var files = generator.Generate(dar).ToList();
        var wrapper = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wrapper.cs", StringComparison.Ordinal));
        var csproj = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        wrapper.Should().NotBeNull();
        wrapper!.Content.Should().Contain("[property: DamlFieldAttribute(\"meta\")] Meta Meta");
        wrapper.Content.Should().NotContain("No.Package.Metadata");

        csproj.Should().NotBeNull();
        csproj!.Content.Should().NotContain("No.Package.Metadata");
        csproj.Content.Should().NotContain("PackageReference Include=\".");
    }

    [Fact]
    public void Generate_should_map_Archive_choice_to_DamlUnit_when_template_package_is_placeholder_named()
    {
        const string PlaceholderPackageId = "lf1x-prim-id";

        var primModule = new DamlModule
        {
            Name = "DA.Internal.Template",
            Templates = [],
            DataTypes =
            [
                new DamlDataType { Name = "Archive", Definition = new DamlRecordDefinition([]) }
            ],
            Interfaces = []
        };
        var primPkg = CreateTestPackage(PlaceholderPackageId, "-no-package-metadata", primModule);

        var mainModule = new DamlModule
        {
            Name = "Main.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(PlaceholderPackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, primPkg);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = true
        };
        var generator = CreateGenerator(options);

        var files = generator.Generate(dar).ToList();
        var holding = files.FirstOrDefault(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal));
        var csproj = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        holding.Should().NotBeNull();
        holding!.Content.Should().Contain("Choice<Holding, DamlUnit,");
        holding.Content.Should().Contain("ArgumentEncoder = _ => DamlRecord.Create()");
        holding.Content.Should().NotContain("ArgumentEncoder = _ => DamlUnit.Instance");
        holding.Content.Should().NotContain("No.Package.Metadata");

        csproj.Should().NotBeNull();
        csproj!.Content.Should().NotContain("No.Package.Metadata");
        csproj.Content.Should().NotContain("PackageReference Include=\".");
    }

    [Fact]
    public void Generate_should_encode_Archive_interface_choice_argument_as_empty_record_when_argument_package_is_placeholder_named()
    {
        const string PlaceholderPackageId = "lf1x-prim-id";

        var primModule = new DamlModule
        {
            Name = "DA.Internal.Template",
            Templates = [],
            DataTypes =
            [
                new DamlDataType { Name = "Archive", Definition = new DamlRecordDefinition([]) }
            ],
            Interfaces = []
        };
        var primPkg = CreateTestPackage(PlaceholderPackageId, "-no-package-metadata", primModule);

        var mainModule = new DamlModule
        {
            Name = "Main.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(PlaceholderPackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ],
                    ViewType = null
                }
            ]
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, primPkg);

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = true
        };
        var generator = CreateGenerator(options);

        var files = generator.Generate(dar).ToList();
        var iface = files.FirstOrDefault(f => f.RelativePath.EndsWith("IAsset.cs", StringComparison.Ordinal));
        var csproj = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        iface.Should().NotBeNull();
        iface!.Content.Should().Contain("TryArchiveAsync(");
        iface.Content.Should().Contain("DamlRecord.Create()");
        iface.Content.Should().NotContain("ArgumentEncoder = _ => DamlUnit.Instance,");
        iface.Content.Should().NotContain("ExerciseCommand.For<IAsset>(contractId, new ChoiceName(\"Archive\"), DamlUnit.Instance)");
        iface.Content.Should().NotContain("No.Package.Metadata");

        csproj.Should().NotBeNull();
        csproj!.Content.Should().NotContain("No.Package.Metadata");
        csproj.Content.Should().NotContain("PackageReference Include=\".");
    }

    #endregion
}
