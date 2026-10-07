// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class PackageEmitContextTests
{
    private static DamlPackage Package(string name, params DamlModule[] modules) =>
        new()
        {
            PackageId = "pkg-id",
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DamlModule Module(
        string name,
        IReadOnlyList<DamlDataType>? dataTypes = null,
        IReadOnlyList<DamlTemplate>? templates = null,
        IReadOnlyList<DamlInterface>? interfaces = null) =>
        new()
        {
            Name = name,
            DataTypes = dataTypes ?? [],
            Templates = templates ?? [],
            Interfaces = interfaces ?? []
        };

    private static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    private static DamlDataType Enum(string name, params string[] ctors) =>
        new() { Name = name, Definition = new DamlEnumDefinition(ctors) };

    private static DamlDataType Variant(string name, params DamlVariantConstructor[] ctors) =>
        new() { Name = name, Definition = new DamlVariantDefinition(ctors) };

    private static CodeGenOptions Options(string? namespacePrefix = null) =>
        new() { NamespacePrefix = namespacePrefix };

    [Fact]
    public void ForPackage_maps_each_module_to_its_own_namespace_named_after_the_module()
    {
        var contexts = PackageEmitContext.ForPackage(
            Package("splice-amulet-name-service", Module("Splice.Ans"), Module("Splice.Ans.AmuletConversionRateFeed")),
            Options(),
            isMainPackage: true);

        contexts.Select(context => (context.Module.Name, context.Namespace)).Should().Equal(
            ("Splice.Ans", "Splice.Ans"),
            ("Splice.Ans.AmuletConversionRateFeed", "Splice.Ans.AmuletConversionRateFeed"));
    }

    [Fact]
    public void ForPackage_applies_the_namespace_prefix_override_to_the_main_package_only()
    {
        var main = PackageEmitContext.ForPackage(Package("p", Module("M")), Options("My.Override"), isMainPackage: true).Single();
        var dependency = PackageEmitContext.ForPackage(Package("p", Module("M")), Options("My.Override"), isMainPackage: false).Single();

        main.Namespace.Should().Be("My.Override.M");
        dependency.Namespace.Should().Be("M");
    }

    [Fact]
    public void ForPackage_collects_data_types_across_all_modules()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("M1", dataTypes: [Record("Alpha")]),
                Module("M2", dataTypes: [Record("Beta")])),
            Options(), isMainPackage: true)[0];

        context.DataTypes.Keys.Should().BeEquivalentTo("M1:Alpha", "M2:Beta");
    }

    [Fact]
    public void ForPackage_keeps_same_named_data_types_from_different_modules_distinct()
    {
        var first = Record("Amulet", new DamlFieldDefinition("a", new DamlPrimitiveType(DamlPrimitive.Text)));
        var second = Enum("Amulet", "X");
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("Splice.Amulet", dataTypes: [first]),
                Module("Splice.AmuletConfig", dataTypes: [second])),
            Options(), isMainPackage: true)[0];

        context.DataTypes["Splice.Amulet:Amulet"].Should().BeSameAs(first);
        context.DataTypes["Splice.AmuletConfig:Amulet"].Should().BeSameAs(second);
    }

    [Fact]
    public void ForPackage_maps_a_local_view_record_to_its_interface_marker()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("AssetView")],
                    interfaces:
                    [
                        new DamlInterface
                        {
                            Name = "Asset",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "AssetView"),
                        },
                    ])),
            Options(), isMainPackage: true).Single();

        context.LocalViewRecordMarkerNames.Should().Contain("M:AssetView", "global::M.IAsset");
    }

    [Fact]
    public void ForPackage_excludes_a_view_record_shared_by_two_interfaces_from_the_view_record_map()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("SharedView")],
                    interfaces:
                    [
                        new DamlInterface
                        {
                            Name = "Bond",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "SharedView"),
                        },
                        new DamlInterface
                        {
                            Name = "Asset",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "SharedView"),
                        },
                    ])),
            Options(), isMainPackage: true).Single();

        context.LocalViewRecordMarkerNames.Should().BeEmpty();
    }

    [Fact]
    public void ForPackage_excludes_foreign_missing_and_generic_view_types_from_the_view_record_map()
    {
        var genericView = new DamlDataType
        {
            Name = "GenericView",
            TypeParams = ["a"],
            Definition = new DamlRecordDefinition([]),
        };
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [genericView],
                    interfaces:
                    [
                        new DamlInterface
                        {
                            Name = "Foreign",
                            Choices = [],
                            ViewType = new DamlTypeRef("other-pkg", "M", "ForeignView"),
                        },
                        new DamlInterface
                        {
                            Name = "Dangling",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "NoSuchView"),
                        },
                        new DamlInterface
                        {
                            Name = "Generic",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "GenericView"),
                        },
                    ])),
            Options(), isMainPackage: true).Single();

        context.LocalViewRecordMarkerNames.Should().BeEmpty();
    }

    [Fact]
    public void ForPackage_excludes_a_view_record_that_is_an_interface_placeholder_from_the_view_record_map()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("Placeholder")],
                    interfaces:
                    [
                        new DamlInterface { Name = "Placeholder", Choices = [] },
                        new DamlInterface
                        {
                            Name = "Asset",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "Placeholder"),
                        },
                    ])),
            Options(), isMainPackage: true).Single();

        context.LocalViewRecordMarkerNames.Should().BeEmpty();
    }

    [Fact]
    public void ForPackage_excludes_a_non_record_view_type_from_the_view_record_map()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Enum("Colour", "Red"), Variant("Shape", new DamlVariantConstructor("Circle", null))],
                    interfaces:
                    [
                        new DamlInterface
                        {
                            Name = "Coloured",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "Colour"),
                        },
                        new DamlInterface
                        {
                            Name = "Shaped",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "Shape"),
                        },
                    ])),
            Options(), isMainPackage: true).Single();

        context.LocalViewRecordMarkerNames.Should().BeEmpty();
    }

    [Theory]
    [InlineData("view")]
    [InlineData("interfaceId")]
    [InlineData("assetView")]
    [InlineData("iAsset")]
    public void ForPackage_excludes_a_view_record_whose_field_does_not_mirror_cleanly(string fieldName)
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("AssetView", new DamlFieldDefinition(fieldName, new DamlPrimitiveType(DamlPrimitive.Party)))],
                    interfaces:
                    [
                        new DamlInterface
                        {
                            Name = "Asset",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "AssetView"),
                        },
                    ])),
            Options(), isMainPackage: true).Single();

        context.LocalViewRecordMarkerNames.Should().BeEmpty();
    }

    [Fact]
    public void ForPackage_keeps_a_view_record_whose_fields_mirror_cleanly()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("AssetView", new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)))],
                    interfaces:
                    [
                        new DamlInterface
                        {
                            Name = "Asset",
                            Choices = [],
                            ViewType = new DamlTypeRef("", "M", "AssetView"),
                        },
                    ])),
            Options(), isMainPackage: true).Single();

        context.LocalViewRecordMarkerNames.Should().Contain("M:AssetView", "global::M.IAsset");
    }

    [Fact]
    public void HasWitnessableViewRecord_admits_a_local_record_and_any_foreign_reference()
    {
        var localRecordView = new DamlInterface
        {
            Name = "Asset",
            Choices = [],
            ViewType = new DamlTypeRef("", "M", "AssetView"),
        };
        var foreignView = new DamlInterface
        {
            Name = "Foreign",
            Choices = [],
            ViewType = new DamlTypeRef("other-pkg", "Other", "ForeignView"),
        };
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("M", dataTypes: [Record("AssetView")], interfaces: [localRecordView, foreignView])),
            Options(), isMainPackage: true).Single();

        context.HasWitnessableViewRecord(localRecordView).Should().BeTrue();
        context.HasWitnessableViewRecord(foreignView).Should().BeTrue();
    }

    [Fact]
    public void HasWitnessableViewRecord_rejects_a_view_type_that_is_not_a_local_non_generic_record()
    {
        var genericView = new DamlDataType
        {
            Name = "GenericView",
            TypeParams = ["a"],
            Definition = new DamlRecordDefinition([]),
        };
        var viewless = new DamlInterface { Name = "Lockable", Choices = [] };
        var enumView = new DamlInterface
        {
            Name = "Coloured",
            Choices = [],
            ViewType = new DamlTypeRef("", "M", "Colour"),
        };
        var genericViewInterface = new DamlInterface
        {
            Name = "Generic",
            Choices = [],
            ViewType = new DamlTypeRef("", "M", "GenericView"),
        };
        var placeholderView = new DamlInterface
        {
            Name = "Placeheld",
            Choices = [],
            ViewType = new DamlTypeRef("", "M", "Placeholder"),
        };
        var danglingView = new DamlInterface
        {
            Name = "Dangling",
            Choices = [],
            ViewType = new DamlTypeRef("", "M", "NoSuchView"),
        };
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Enum("Colour", "Red"), genericView, Record("Placeholder")],
                    interfaces:
                    [
                        new DamlInterface { Name = "Placeholder", Choices = [] },
                        viewless,
                        enumView,
                        genericViewInterface,
                        placeholderView,
                        danglingView,
                    ])),
            Options(), isMainPackage: true).Single();

        context.HasWitnessableViewRecord(viewless).Should().BeFalse();
        context.HasWitnessableViewRecord(enumView).Should().BeFalse();
        context.HasWitnessableViewRecord(genericViewInterface).Should().BeFalse();
        context.HasWitnessableViewRecord(placeholderView).Should().BeFalse();
        context.HasWitnessableViewRecord(danglingView).Should().BeFalse();
    }
}
