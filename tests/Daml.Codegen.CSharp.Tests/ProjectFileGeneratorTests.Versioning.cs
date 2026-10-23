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
    public void GenerateProjectFile_should_emit_4_part_version_with_r0_by_default()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(2, 3, 4),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<Version>2.3.4.0</Version>");
    }

    [Fact]
    public void GenerateProjectFile_should_derive_the_nuget_version_from_the_three_part_version_when_the_dar_version_is_a_snapshot()
    {
        var generator = new ProjectFileGenerator(CreateOptions());
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "daml-stdlib",
            Version = new Version(3, 3, 0),
            RawVersion = "3.3.0.20250502.13767.0",
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<Version>3.3.0.0</Version>");
    }

    [Fact]
    public void GenerateProjectFile_should_throw_on_negative_EmitterCounter()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            EmitterCounter = -1,
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

        var act = () => generator.GenerateProjectFile(package);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EmitterCounter*M.m.p.g*");
    }

    [Fact]
    public void GenerateProjectFile_should_throw_on_two_part_dar_version()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var act = () => generator.GenerateProjectFile(package);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*3-part*M.m.p.g*");
    }

    [Fact]
    public void GenerateProjectFile_should_use_EmitterCounter_as_4th_version_segment()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            EmitterCounter = 7,
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(0, 1, 17),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<Version>0.1.17.7</Version>");
    }

    [Fact]
    public void GenerateProjectFile_should_append_version_suffix_to_package_version_only()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            RuntimePackageVersion = "1.2.3",
            EmitterCounter = 5,
            VersionSuffix = "preview.2",
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(0, 1, 6),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<Version>0.1.6.5-preview.2</Version>");
        file.Content.Should().Contain("<PackageReference Include=\"Daml.Runtime\" Version=\"1.2.3\" />");
        file.Content.Should().NotContain("Version=\"1.2.3-preview.2\"");
    }
}
