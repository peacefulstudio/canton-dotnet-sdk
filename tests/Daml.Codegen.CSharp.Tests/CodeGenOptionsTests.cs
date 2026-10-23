// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class CodeGenOptionsTests
{
    #region CodeGenOptions Defaults Tests
    [Fact]
    public void CodeGenOptions_should_have_correct_defaults()
    {
        var options = new CodeGenOptions();

        options.EnableNullableReferenceTypes.Should().BeTrue();
        options.GenerateXmlDocs.Should().BeTrue();
        options.UseFileScopedNamespaces.Should().BeTrue();
    }

    [Fact]
    public void CodeGenOptions_should_allow_customization()
    {
        var options = new CodeGenOptions
        {
            NamespacePrefix = "MyCompany.Contracts",
            RootFilter = ".*Iou.*",
            EnableNullableReferenceTypes = false,
        };

        options.NamespacePrefix.Should().Be("MyCompany.Contracts");
        options.RootFilter.Should().Be(".*Iou.*");
        options.EnableNullableReferenceTypes.Should().BeFalse();
    }

    [Fact]
    public void CodeGenOptions_should_have_correct_new_option_defaults()
    {
        var options = new CodeGenOptions();

        options.GenerateProjectFile.Should().BeFalse();
        options.IncludeDependencies.Should().BeFalse();
        options.TargetFramework.Should().Be("net10.0");
        options.RuntimePackageVersion.Should().BeNull();
    }

    #endregion

    #region CSharpCodeGenerator Integration Tests

    private static DamlModule CreateSimpleModule(string moduleName = "Test.Module", string templateName = "SimpleTemplate")
    {
        return new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = templateName,
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };
    }

    private static DamlModelBuilder MainPackageDar() =>
        new DamlModelBuilder().WithPackageId("main-package-id").WithPackageName("main-package");

    #endregion

    #region GenerateProjectFile Option Tests

    [Fact]
    public void Generate_should_include_project_file_when_GenerateProjectFile_is_true()
    {
        var options = new CodeGenOptions
        {
            GenerateProjectFile = true,
            TargetFramework = "net10.0"
        };
        var generator = CreateGenerator(options);
        var dar = MainPackageDar().WithModule(CreateSimpleModule()).Build();

        var files = generator.Generate(dar);

        var projectFile = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        projectFile.Should().NotBeNull();
        projectFile!.RelativePath.Should().Be("Main.Package.csproj");
        projectFile.Content.Should().Contain("<TargetFramework>net10.0</TargetFramework>");
    }

    [Fact]
    public void Generate_should_not_include_project_file_when_GenerateProjectFile_is_false()
    {
        var options = new CodeGenOptions
        {
            GenerateProjectFile = false
        };
        var generator = CreateGenerator(options);
        var dar = MainPackageDar().WithModule(CreateSimpleModule()).Build();

        var files = generator.Generate(dar);

        var projectFile = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        projectFile.Should().BeNull();
    }

    #endregion

    #region IncludeDependencies Option Tests

    [Fact]
    public void Generate_should_include_dependency_code_when_IncludeDependencies_is_true()
    {
        var depModule = CreateSimpleModule("Dep.Module", "DepTemplate");
        var depPackage = new DamlPackage
        {
            PackageId = "dep-package-id",
            Name = "dep-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [depModule],
            DependencyReferences = []
        };

        var options = new CodeGenOptions
        {
            IncludeDependencies = true
        };
        var generator = CreateGenerator(options);
        var dar = MainPackageDar().WithModule(CreateSimpleModule()).WithDependencies(depPackage).Build();

        var files = generator.Generate(dar);

        var mainFile = files.FirstOrDefault(f => f.RelativePath.Contains("SimpleTemplate.cs", StringComparison.Ordinal));
        var depFile = files.FirstOrDefault(f => f.RelativePath.Contains("DepTemplate.cs", StringComparison.Ordinal));

        mainFile.Should().NotBeNull();
        depFile.Should().NotBeNull();
    }

    [Fact]
    public void Generate_should_not_include_dependency_code_when_IncludeDependencies_is_false()
    {
        var depModule = CreateSimpleModule("Dep.Module", "DepTemplate");
        var depPackage = new DamlPackage
        {
            PackageId = "dep-package-id",
            Name = "dep-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [depModule],
            DependencyReferences = []
        };

        var options = new CodeGenOptions
        {
            IncludeDependencies = false
        };
        var generator = CreateGenerator(options);
        var dar = MainPackageDar().WithModule(CreateSimpleModule()).WithDependencies(depPackage).Build();

        var files = generator.Generate(dar);

        var mainFile = files.FirstOrDefault(f => f.RelativePath.Contains("SimpleTemplate.cs", StringComparison.Ordinal));
        var depFile = files.FirstOrDefault(f => f.RelativePath.Contains("DepTemplate.cs", StringComparison.Ordinal));

        mainFile.Should().NotBeNull();
        depFile.Should().BeNull("dependency code should not be generated when IncludeDependencies is false");
    }

    [Fact]
    public void Generate_should_include_multiple_dependencies_when_IncludeDependencies_is_true()
    {
        var dep1Module = CreateSimpleModule("Dep1.Module", "Dep1Template");
        var dep1Package = new DamlPackage
        {
            PackageId = "dep1-package-id",
            Name = "dep1-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [dep1Module],
            DependencyReferences = []
        };

        var dep2Module = CreateSimpleModule("Dep2.Module", "Dep2Template");
        var dep2Package = new DamlPackage
        {
            PackageId = "dep2-package-id",
            Name = "dep2-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [dep2Module],
            DependencyReferences = []
        };

        var options = new CodeGenOptions
        {
            IncludeDependencies = true
        };
        var generator = CreateGenerator(options);
        var dar = MainPackageDar().WithModule(CreateSimpleModule()).WithDependencies(dep1Package, dep2Package).Build();

        var files = generator.Generate(dar);

        var mainFile = files.FirstOrDefault(f => f.RelativePath.Contains("SimpleTemplate.cs", StringComparison.Ordinal));
        var dep1File = files.FirstOrDefault(f => f.RelativePath.Contains("Dep1Template.cs", StringComparison.Ordinal));
        var dep2File = files.FirstOrDefault(f => f.RelativePath.Contains("Dep2Template.cs", StringComparison.Ordinal));

        mainFile.Should().NotBeNull();
        dep1File.Should().NotBeNull();
        dep2File.Should().NotBeNull();
    }

    #endregion

    #region Combined Options Tests

    [Fact]
    public void Generate_should_not_reference_a_dependency_it_emits_inline_when_generating_a_project_file()
    {
        var dar = DarWhereMainRecordHoldsDependencyType();
        var options = new CodeGenOptions { GenerateProjectFile = true, IncludeDependencies = true, TargetFramework = "net10.0" };

        var files = CreateGenerator(options).Generate(dar);

        var projectFile = files.Single(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        files.Should().Contain(f => f.RelativePath.EndsWith("DepTemplate.cs", StringComparison.Ordinal));
        files.Should().Contain(f => f.RelativePath.EndsWith("Holder.cs", StringComparison.Ordinal));
        projectFile.Content.Should().NotContain("Dep.Package");
        projectFile.Content.Should().Contain("<PackageReference Include=\"Daml.Runtime\"");
    }

    [Fact]
    public void Generate_should_reference_a_dependency_it_does_not_emit_inline_when_generating_a_project_file()
    {
        var dar = DarWhereMainRecordHoldsDependencyType();
        var options = new CodeGenOptions { GenerateProjectFile = true, IncludeDependencies = false, TargetFramework = "net10.0" };

        var files = CreateGenerator(options).Generate(dar);

        var projectFile = files.Single(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        files.Should().NotContain(f => f.RelativePath.EndsWith("DepTemplate.cs", StringComparison.Ordinal));
        projectFile.Content.Should().Contain("<PackageReference Include=\"Dep.Package\" Version=\"2.0.0.*-*\" />");
    }

    [Fact]
    public void Generate_should_not_reference_the_main_package_from_its_own_project_file_when_a_dependency_uses_a_main_type()
    {
        var dependency = new DamlPackage
        {
            PackageId = "dep-package-id",
            Name = "dep-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Dep.Module",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "DepHolder",
                            Definition = new DamlRecordDefinition(
                                [new DamlFieldDefinition("held", new DamlTypeRef("main-package-id", "Main.Module", "MainTemplate"))])
                        }
                    ],
                    Interfaces = []
                }
            ],
            DependencyReferences = []
        };
        var dar = MainPackageDar().WithModule(CreateSimpleModule("Main.Module", "MainTemplate")).WithDependencies(dependency).Build();
        var options = new CodeGenOptions { GenerateProjectFile = true, IncludeDependencies = true, TargetFramework = "net10.0" };

        var files = CreateGenerator(options).Generate(dar);

        var projectFile = files.Single(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        files.Should().Contain(f => f.RelativePath.EndsWith("DepHolder.cs", StringComparison.Ordinal));
        projectFile.Content.Should().NotContain("Include=\"Main.Package\"");
        projectFile.Content.Should().NotContain("Include=\"Dep.Package\"");
    }

    private static DarModel DarWhereMainRecordHoldsDependencyType()
    {
        var depPackage = new DamlPackage
        {
            PackageId = "dep-package-id",
            Name = "dep-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [CreateSimpleModule("Dep.Module", "DepTemplate")],
            DependencyReferences = []
        };
        var mainModule = new DamlModule
        {
            Name = "Main.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("held", new DamlTypeRef("dep-package-id", "Dep.Module", "DepTemplate"))])
                }
            ],
            Interfaces = []
        };
        return MainPackageDar().WithModule(mainModule).WithDependencies(depPackage).Build();
    }

    [Fact]
    public void Generate_should_use_runtime_version_in_project_file()
    {
        var options = new CodeGenOptions
        {
            GenerateProjectFile = true,
            RuntimePackageVersion = "1.2.3"
        };
        var generator = CreateGenerator(options);
        var dar = MainPackageDar().WithModule(CreateSimpleModule()).Build();

        var files = generator.Generate(dar);

        var projectFile = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        projectFile.Should().NotBeNull();
        projectFile!.Content.Should().Contain("Version=\"1.2.3\"");
    }

    #endregion
}
