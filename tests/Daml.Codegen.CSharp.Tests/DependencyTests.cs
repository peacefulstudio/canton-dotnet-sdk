// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DependencyTests
{
    #region DamlPackageReference Tests

    [Fact]
    public void DamlPackageReference_should_store_package_id()
    {
        var reference = new DamlPackageReference
        {
            PackageId = "abc123def456"
        };

        reference.PackageId.Should().Be("abc123def456");
        reference.Name.Should().BeNull();
        reference.Version.Should().BeNull();
    }

    [Fact]
    public void DamlPackageReference_should_carry_name_and_version_set_at_construction()
    {
        var reference = new DamlPackageReference
        {
            PackageId = "abc123def456",
            Name = "my-dependency",
            Version = new Version(1, 2, 3)
        };

        reference.Name.Should().Be("my-dependency");
        reference.Version.Should().Be(new Version(1, 2, 3));
    }

    #endregion

    #region DamlPackage DependencyReferences Tests

    [Fact]
    public void DamlPackage_should_store_dependency_references()
    {
        var dependencies = new List<DamlPackageReference>
        {
            new() { PackageId = "dep1" },
            new() { PackageId = "dep2" }
        };

        var package = new DamlPackage
        {
            PackageId = "main-package",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = dependencies
        };

        package.DependencyReferences.Should().HaveCount(2);
        package.DependencyReferences[0].PackageId.Should().Be("dep1");
        package.DependencyReferences[1].PackageId.Should().Be("dep2");
    }

    #endregion

    #region DarModel GetPackageById Tests

    [Fact]
    public void DarModel_GetPackageById_should_find_main_package()
    {
        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dar = new DarModel
        {
            MainPackage = mainPackage,
            Dependencies = []
        };

        var found = dar.GetPackageById("main-pkg-id");

        found.Should().Be(mainPackage);
    }

    [Fact]
    public void DarModel_GetPackageById_should_find_dependency_package()
    {
        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var depPackage = new DamlPackage
        {
            PackageId = "dep-pkg-id",
            Name = "dependency",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dar = new DarModel
        {
            MainPackage = mainPackage,
            Dependencies = [depPackage]
        };

        var found = dar.GetPackageById("dep-pkg-id");

        found.Should().Be(depPackage);
    }

    [Fact]
    public void DarModel_GetPackageById_should_return_null_for_unknown_id()
    {
        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dar = new DarModel
        {
            MainPackage = mainPackage,
            Dependencies = []
        };

        var found = dar.GetPackageById("unknown-id");

        found.Should().BeNull();
    }

    [Fact]
    public void DarModel_GetPackageById_should_resolve_on_the_concrete_type_without_recursing()
    {
        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var depPackage = new DamlPackage
        {
            PackageId = "dep-pkg-id",
            Name = "dependency",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        DarModel dar = new()
        {
            MainPackage = mainPackage,
            Dependencies = [depPackage]
        };

        dar.GetPackageById("main-pkg-id").Should().Be(mainPackage);
        dar.GetPackageById("dep-pkg-id").Should().Be(depPackage);
        dar.GetPackageById("unknown-id").Should().BeNull();
    }

    #endregion

}
