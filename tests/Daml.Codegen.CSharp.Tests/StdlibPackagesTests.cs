// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class StdlibPackagesTests
{
    private const string StdlibPackageId = "stdlib-pkg";
    private const string UserPackageId = "user-pkg";

    private static DamlPackage NamedPackage(string packageId, string name) =>
        new()
        {
            PackageId = packageId,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

    private static DarCrossPackageResolver Resolver(params DamlPackage[] packages) =>
        RealResolution.Of(NamedPackage("local-pkg", "local"), new CodeGenOptions(), packages).Resolver;

    private static DamlTypeRef Ref(string packageId, string module, string name) =>
        new(packageId, module, name);

    [Fact]
    public void IsInStdlibPackage_is_false_for_empty_package_id()
    {
        StdlibPackages.IsInStdlibPackage(Resolver(), Ref("", "DA.Types", "Tuple2")).Should().BeFalse();
    }

    [Fact]
    public void IsInStdlibPackage_is_false_for_unknown_package_id()
    {
        StdlibPackages.IsInStdlibPackage(Resolver(), Ref("missing", "DA.Types", "Tuple2")).Should().BeFalse();
    }

    [Fact]
    public void IsInStdlibPackage_is_true_for_resolved_stdlib_package()
    {
        var resolver = Resolver(NamedPackage(StdlibPackageId, "daml-stdlib"));
        StdlibPackages.IsInStdlibPackage(resolver, Ref(StdlibPackageId, "DA.Types", "Tuple2")).Should().BeTrue();
    }

    [Fact]
    public void IsInStdlibPackage_is_false_for_resolved_user_package()
    {
        var resolver = Resolver(NamedPackage(UserPackageId, "my-app"));
        StdlibPackages.IsInStdlibPackage(resolver, Ref(UserPackageId, "DA.Types", "Tuple2")).Should().BeFalse();
    }

    [Fact]
    public void IsStdlibTypeRef_parametric_is_false_for_user_package_shadowing_stdlib_type()
    {
        var resolver = Resolver(NamedPackage(UserPackageId, "my-app"));
        StdlibPackages.IsStdlibTypeRef(resolver, Ref(UserPackageId, "DA.Types", "Tuple2"), parametric: true).Should().BeFalse();
    }

    [Fact]
    public void IsStdlibTypeRef_parametric_is_true_for_stdlib_parametric_type()
    {
        var resolver = Resolver(NamedPackage(StdlibPackageId, "daml-stdlib"));
        StdlibPackages.IsStdlibTypeRef(resolver, Ref(StdlibPackageId, "DA.Types", "Tuple2"), parametric: true).Should().BeTrue();
    }

    [Fact]
    public void IsStdlibTypeRef_nonparametric_is_true_for_mapped_stdlib_type()
    {
        var resolver = Resolver(NamedPackage(StdlibPackageId, "daml-stdlib"));
        StdlibPackages.IsStdlibTypeRef(resolver, Ref(StdlibPackageId, "DA.Time.Types", "RelTime"), parametric: false).Should().BeTrue();
    }

    [Fact]
    public void IsStdlibTypeRef_is_false_for_unmapped_type_in_stdlib_package()
    {
        var resolver = Resolver(NamedPackage(StdlibPackageId, "daml-stdlib"));
        StdlibPackages.IsStdlibTypeRef(resolver, Ref(StdlibPackageId, "DA.Types", "NotAStdlibType"), parametric: false).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("-no-package-metadata")]
    [InlineData("UnknownPackage")]
    public void IsInStdlibPackage_is_true_for_placeholder_package_name(string placeholderName)
    {
        var resolver = Resolver(NamedPackage("placeholder-pkg", placeholderName));
        StdlibPackages.IsInStdlibPackage(resolver, Ref("placeholder-pkg", "DA.Types", "Tuple2")).Should().BeTrue();
    }
}
