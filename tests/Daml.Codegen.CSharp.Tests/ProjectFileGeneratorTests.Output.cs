// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public partial class ProjectFileGeneratorTests
{
    [Fact]
    public void GenerateProjectFile_should_include_target_framework()
    {
        // Arrange
        var options = CreateOptions(targetFramework: "net9.0");
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<TargetFramework>net9.0</TargetFramework>");
    }

    [Fact]
    public void GenerateProjectFile_should_include_nullable_when_enabled()
    {
        // Arrange
        var options = CreateOptions(enableNullable: true);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<Nullable>enable</Nullable>");
    }

    [Fact]
    public void GenerateProjectFile_should_not_include_nullable_when_disabled()
    {
        // Arrange
        var options = CreateOptions(enableNullable: false);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().NotContain("<Nullable>");
    }

    [Fact]
    public void GenerateProjectFile_should_include_implicit_usings()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<ImplicitUsings>enable</ImplicitUsings>");
    }

    [Fact]
    public void GenerateProjectFile_should_not_pin_LangVersion_for_a_key_bearing_package()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "keyed-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.3",
            Modules =
            [
                new DamlModule
                {
                    Name = "Test.Module",
                    Templates =
                    [
                        new DamlTemplate
                        {
                            Name = "KeyedTemplate",
                            Choices = [],
                            Key = new DamlPrimitiveType(DamlPrimitive.Text),
                        },
                    ],
                    DataTypes = [],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().NotContain(
            "<LangVersion>",
            "a key-bearing package emits no C#-13-only syntax, so it must not raise the SDK floor of every consumer");
    }

    [Fact]
    public void GenerateProjectFile_should_xml_escape_target_framework_in_element_text()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0 & <evil>",
            GenerateProjectFile = true,
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain(
            "<TargetFramework>net10.0 &amp; &lt;evil&gt;</TargetFramework>",
            "user-supplied target framework flows into csproj element text and must be XML-escaped");
    }

    [Fact]
    public void GenerateReadme_should_be_emitted_at_the_project_root()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.RelativePath.Should().Be("README.md");
    }

    [Fact]
    public void GenerateIcon_should_return_the_png_icon_bytes_at_the_project_root()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);

        var file = generator.GenerateIcon();

        file.RelativePath.Should().Be("icon.png");
        file.BinaryContent.Should().NotBeNullOrEmpty();
        file.BinaryContent!.Take(4).Should().Equal(
            new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            "the emitted icon must be a real PNG (magic bytes 0x89 'P' 'N' 'G')");
    }

    [Fact]
    public void GenerateReadme_should_contain_the_package_id_daml_name_and_license()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            RuntimePackageVersion = "1.2.3",
            PackageLicenseExpression = "MIT",
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.Content.Should().Contain("# Splice.Amulet");
        file.Content.Should().Contain("`splice-amulet`");
        file.Content.Should().Contain("1.2.3");
        file.Content.Should().Contain("MIT");
        file.Content.Should().Contain("https://github.com/peacefulstudio/canton-dotnet-sdk");
    }

    [Fact]
    public void GenerateReadme_should_add_prerelease_flag_to_install_hint_for_a_prerelease_package()
    {
        var options = CreateOptions(versionSuffix: "preview.2");
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.Content.Should().Contain("dotnet add package Splice.Amulet --prerelease");
    }

    [Fact]
    public void GenerateReadme_should_not_add_prerelease_flag_to_install_hint_for_a_stable_package()
    {
        var options = CreateOptions(versionSuffix: null);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.Content.Should().Contain("dotnet add package Splice.Amulet");
        file.Content.Should().NotContain("--prerelease");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://github.com/acme/widgets")]
    public void GenerateReadme_should_keep_the_generator_attribution_link_regardless_of_repository_url(string? repositoryUrl)
    {
        var options = CreateOptions(repositoryUrl: repositoryUrl);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.Content.Should().Contain(
            "[Daml.Codegen.CSharp]: https://github.com/peacefulstudio/canton-dotnet-sdk",
            "the attribution link identifies the generator tool, not the consumer's repository");
    }
}
