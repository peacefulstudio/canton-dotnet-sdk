// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Daml.Codegen.Intermediate.Model;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class PackageNameTableCacheTests
{
    private const string MainPackageId = "main-id";

    private static DamlPackage Package(string id, string name, IReadOnlyList<DamlModule> modules) =>
        new()
        {
            PackageId = id,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DamlModule Module(string name, params DamlTemplate[] templates) =>
        new() { Name = name, DataTypes = [], Templates = templates, Interfaces = [] };

    private static DamlTemplate Template(string name) => new() { Name = name, Choices = [] };

    private static DamlTemplate TemplateTakingTransfer(string name) =>
        new()
        {
            Name = name,
            Choices =
            [
                new DamlChoice
                {
                    Name = "Do",
                    Consuming = true,
                    ArgumentType = new DamlTypeRef("", "M", "Transfer"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                }
            ]
        };

    private static DamlPackage PackageRenamingATemplateAndSharingAnArgument(string id, string name) =>
        Package(
            id,
            name,
            [
                new DamlModule
                {
                    Name = "M",
                    DataTypes = [new DamlDataType { Name = "Transfer", Definition = new DamlRecordDefinition([]) }],
                    Templates = [Template("ToRecord"), TemplateTakingTransfer("Account"), TemplateTakingTransfer("Vault")],
                    Interfaces = []
                }
            ]);

    private static PackageNameTableCache CacheOf(CodeGenOptions? options = null, CapturingLogger? logger = null) =>
        new(options ?? new CodeGenOptions(), MainPackageId, logger);

    [Fact]
    public void For_builds_a_package_once_per_package_id_and_root_filter()
    {
        var modules = new CountingModules([Module("M", Template("Account"))]);
        var package = Package("dep-id", "dep", modules);
        var cache = CacheOf();

        var first = cache.For(package);
        var enumerationsAfterFirst = modules.EnumerationCount;
        var second = cache.For(package);

        second.Should().BeSameAs(first);
        enumerationsAfterFirst.Should().BeGreaterThan(0);
        modules.EnumerationCount.Should().Be(enumerationsAfterFirst);
    }

    [Fact]
    public void For_builds_the_same_package_again_under_a_different_root_filter()
    {
        var package = Package("dep-id", "dep", [Module("M", Template("ToRecord"))]);
        var cache = CacheOf();

        var unfiltered = cache.For(package, new TypeRootFilter(null));
        var filteredOut = cache.For(package, new TypeRootFilter("^M:Nothing$"));
        var filteredIn = cache.For(package, new TypeRootFilter("^M:ToRecord$"));

        unfiltered.EmittedName("M", "ToRecord").Should().Be("ToRecord_");
        filteredOut.EmittedName("M", "ToRecord").Should().Be("ToRecord");
        filteredIn.Should().NotBeSameAs(filteredOut);
        filteredIn.EmittedName("M", "ToRecord").Should().Be("ToRecord_");
    }

    [Fact]
    public void For_serves_two_equal_root_filters_from_one_build()
    {
        var package = Package("dep-id", "dep", [Module("M", Template("ToRecord"))]);
        var cache = CacheOf();

        var first = cache.For(package, new TypeRootFilter("^M:ToRecord$"));
        var second = cache.For(package, new TypeRootFilter("^M:ToRecord$"));

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void For_filters_the_main_package_by_the_root_filter()
    {
        var main = Package(MainPackageId, "main", [Module("M", Template("ToRecord"))]);

        var table = CacheOf(new CodeGenOptions { RootFilter = "^M:Nothing$" }).For(main);

        table.EmittedName("M", "ToRecord").Should().Be("ToRecord");
    }

    [Fact]
    public void For_leaves_a_dependency_unfiltered_when_dependencies_are_not_included()
    {
        var dependency = Package("dep-id", "dep", [Module("M", Template("ToRecord"))]);

        var table = CacheOf(new CodeGenOptions { RootFilter = "^M:Nothing$", IncludeDependencies = false }).For(dependency);

        table.EmittedName("M", "ToRecord").Should().Be("ToRecord_");
    }

    [Fact]
    public void For_filters_a_dependency_by_the_root_filter_when_dependencies_are_included()
    {
        var dependency = Package("dep-id", "dep", [Module("M", Template("ToRecord"))]);

        var table = CacheOf(new CodeGenOptions { RootFilter = "^M:Nothing$", IncludeDependencies = true }).For(dependency);

        table.EmittedName("M", "ToRecord").Should().Be("ToRecord");
    }

    [Fact]
    public void For_prefixes_the_main_package_namespaces_and_leaves_a_dependency_unprefixed()
    {
        var cache = CacheOf(new CodeGenOptions { NamespacePrefix = "Acme" });

        var main = cache.For(Package(MainPackageId, "main", [Module("M")]));
        var dependency = cache.For(Package("dep-id", "dep", [Module("M")]));

        main.ModuleNamespaces["M"].Should().Be("Acme.M");
        dependency.ModuleNamespaces["M"].Should().Be("M");
    }

    [Fact]
    public void For_warns_about_a_renamed_type_and_a_shared_argument_once_for_the_main_package()
    {
        var logger = new CapturingLogger();
        var cache = CacheOf(logger: logger);
        var main = PackageRenamingATemplateAndSharingAnArgument(MainPackageId, "main-pkg");

        cache.For(main);
        cache.For(main);

        logger.WarningEventIds.Should().Equal(1201, 1200);
    }

    [Fact]
    public void For_names_the_package_in_the_shared_argument_warning_of_a_dependency_and_raises_it_once()
    {
        var logger = new CapturingLogger();
        var cache = CacheOf(logger: logger);
        var dependency = PackageRenamingATemplateAndSharingAnArgument("dep-id", "dep-pkg");

        cache.For(dependency);
        cache.For(dependency);

        logger.WarningEventIds.Should().Equal(1200);
        logger.Warnings.Should().ContainSingle().Which.Should().Be(
            "Choice-argument type M:Transfer in package dep-pkg is used by both templates Account and Vault in the same package; keeping Account and ignoring Vault. Rename one choice-argument type to disambiguate.");
    }

    [Fact]
    public void For_reports_every_warning_of_a_dependency_that_is_emitted()
    {
        var logger = new CapturingLogger();
        var cache = CacheOf(new CodeGenOptions { IncludeDependencies = true }, logger);

        cache.For(PackageRenamingATemplateAndSharingAnArgument("dep-id", "dep-pkg"));

        logger.WarningEventIds.Should().Equal(1201, 1200);
    }

    [Fact]
    public void For_leaves_the_rename_of_a_dependency_that_is_not_emitted_unreported()
    {
        var logger = new CapturingLogger();
        var cache = CacheOf(new CodeGenOptions { IncludeDependencies = false }, logger);

        cache.For(Package("dep-id", "dep-pkg", [Module("M", Template("ToRecord"))]));

        logger.Records.Should().BeEmpty();
    }
}
