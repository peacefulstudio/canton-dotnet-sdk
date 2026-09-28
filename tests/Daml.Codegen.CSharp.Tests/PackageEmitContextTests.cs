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
        contexts[0].ModuleNamespaces.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Splice.Ans"] = "Splice.Ans",
            ["Splice.Ans.AmuletConversionRateFeed"] = "Splice.Ans.AmuletConversionRateFeed",
        });
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
    public void ForPackage_scopes_the_qualifier_to_the_module_namespace()
    {
        var context = PackageEmitContext.ForPackage(
            Package("canton-party-replication", Module("Canton.Party.Replication")), Options(), isMainPackage: true).Single();

        context.Qualifier.AllNamespaces.Should().BeEquivalentTo(
            "Canton", "Canton.Party", "Canton.Party.Replication");
    }

    [Fact]
    public void ForPackage_shadows_imported_names_only_with_types_declared_in_the_module_namespace_or_its_ancestors()
    {
        var contexts = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("A", dataTypes: [Record("Either")]),
                Module("A.B", dataTypes: [Record("Widget")]),
                Module("C", dataTypes: [Record("Party")])),
            Options(),
            isMainPackage: true);

        var child = contexts.Single(context => context.Module.Name == "A.B");
        child.Qualifier.DeclaredTypeNames.Should().BeEquivalentTo("Either", "Widget");
        child.Qualifier.Qualify("Either").Should().Be("global::Daml.Runtime.Stdlib.Either");
        child.Qualifier.Qualify("Party").Should().Be("Party");
    }

    [Fact]
    public void NamespaceOf_fails_for_a_module_the_package_does_not_declare()
    {
        var context = PackageEmitContext.ForPackage(Package("p", Module("M")), Options(), isMainPackage: true).Single();

        var act = () => context.NamespaceOf("Elsewhere");

        act.Should().Throw<CodegenException>().WithMessage("*Elsewhere*").WithMessage("*p*");
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
    public void ForPackage_records_local_enums_module_qualified()
    {
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("Splice.AmuletConfig", dataTypes: [Enum("Amulet", "Free", "Paid")])),
            Options(), isMainPackage: true).Single();

        context.LocalEnumQualifiedNames.Should().BeEquivalentTo("Splice.AmuletConfig:Amulet");
    }

    [Fact]
    public void ForPackage_records_local_variants_module_qualified()
    {
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("M", dataTypes:
            [
                Variant("Shape", new DamlVariantConstructor("Circle", null))
            ])),
            Options(), isMainPackage: true).Single();

        context.LocalVariantQualifiedNames.Should().BeEquivalentTo("M:Shape");
    }

    [Fact]
    public void ForPackage_flags_interface_shadowed_records_module_local()
    {
        var holdingRecord = Record("Holding");
        var unrelatedHolding = Record("Holding");
        var iface = new DamlInterface { Name = "Holding", Choices = [] };
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("Splice.Holding", dataTypes: [holdingRecord], interfaces: [iface]),
                Module("Other", dataTypes: [unrelatedHolding])),
            Options(), isMainPackage: true)[0];

        context.LocalInterfaceQualifiedNames.Should().BeEquivalentTo("Splice.Holding:Holding");
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

        context.LocalViewRecordMarkerNames.Should().Contain("M:AssetView", "IAsset");
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

        context.LocalViewRecordMarkerNames.Should().Contain("M:AssetView", "IAsset");
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

    [Fact]
    public void ForPackage_widens_reserved_names_to_a_record_colliding_with_an_interface_marker()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("IFactory")],
                    interfaces: [new DamlInterface { Name = "Factory", Choices = [] }])),
            Options(), isMainPackage: true).Single();

        context.LocalReservedTypeNames.Should().Contain("IFactory");
        context.LocalInterfaceMarkerNames["M:Factory"].Should().Be("IFactory_");
    }

    [Fact]
    public void ForPackage_widens_reserved_names_to_a_record_colliding_with_the_first_round_disambiguated_marker()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("IFactory_")],
                    templates:
                    [
                        new DamlTemplate { Name = "IFactory", Choices = [] },
                    ],
                    interfaces: [new DamlInterface { Name = "Factory", Choices = [] }])),
            Options(), isMainPackage: true).Single();

        context.LocalReservedTypeNames.Should().Contain("IFactory_");
        context.LocalInterfaceMarkerNames["M:Factory"].Should().Be("IFactory__");
    }

    [Fact]
    public void ForPackage_excludes_interface_placeholder_records_from_the_reserved_set()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("Factory")],
                    interfaces: [new DamlInterface { Name = "Factory", Choices = [] }])),
            Options(), isMainPackage: true).Single();

        context.LocalReservedTypeNames.Should().NotContain("Factory");
        context.LocalInterfaceMarkerNames["M:Factory"].Should().Be("IFactory");
    }

    [Fact]
    public void ForPackage_deterministically_assigns_the_same_marker_winner_across_modules_regardless_of_declaration_order()
    {
        DamlInterface Factory() => new() { Name = "Factory", Choices = [] };

        var declaredAlphaFirst = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("Alpha", interfaces: [Factory()]),
                Module("Beta", interfaces: [Factory()])),
            Options(), isMainPackage: true)[0];
        var declaredBetaFirst = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("Beta", interfaces: [Factory()]),
                Module("Alpha", interfaces: [Factory()])),
            Options(), isMainPackage: true)[0];

        declaredAlphaFirst.LocalInterfaceMarkerNames["Alpha:Factory"].Should().Be("IFactory");
        declaredAlphaFirst.LocalInterfaceMarkerNames["Beta:Factory"].Should().Be("IFactory_");
        declaredBetaFirst.LocalInterfaceMarkerNames["Alpha:Factory"].Should().Be("IFactory");
        declaredBetaFirst.LocalInterfaceMarkerNames["Beta:Factory"].Should().Be("IFactory_");
    }

    [Fact]
    public void ForPackage_excludes_nested_choice_argument_types_from_the_reserved_set()
    {
        var argType = Record("IFactory", new DamlFieldDefinition("to", new DamlPrimitiveType(DamlPrimitive.Party)));
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "M", "IFactory"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var template = new DamlTemplate { Name = "Account", Choices = [choice] };

        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [argType],
                    templates: [template],
                    interfaces: [new DamlInterface { Name = "Factory", Choices = [] }])),
            Options(), isMainPackage: true).Single();

        context.LocalReservedTypeNames.Should().NotContain("IFactory");
        context.LocalInterfaceMarkerNames["M:Factory"].Should().Be("IFactory");
    }

    [Fact]
    public void ForPackage_maps_nested_choice_argument_types_to_their_parent_template()
    {
        var argType = Record("TransferArg", new DamlFieldDefinition("to", new DamlPrimitiveType(DamlPrimitive.Party)));
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "M", "TransferArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var template = new DamlTemplate
        {
            Name = "Account",
            Choices = [choice]
        };
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("M", dataTypes: [argType], templates: [template])),
            Options(), isMainPackage: true).Single();

        context.LocalChoiceArgToTemplate.Should().ContainKey("M:TransferArg")
            .WhoseValue.Should().Be(new NestingTemplate("M", "Account", "Transfer"));
    }

    [Fact]
    public void ForPackage_records_the_module_of_the_template_a_choice_argument_declared_elsewhere_nests_inside()
    {
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "Args", "TransferArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("Args", dataTypes: [Record("TransferArg")]),
                Module("Banking", templates: [new DamlTemplate { Name = "Account", Choices = [choice] }])),
            Options(),
            isMainPackage: true)[0];

        context.LocalChoiceArgToTemplate["Args:TransferArg"].Should().Be(new NestingTemplate("Banking", "Account", "Transfer"));
    }

    [Fact]
    public void ForPackage_does_not_map_choice_args_that_are_not_local_data_types()
    {
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "M", "NotDeclaredHere"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var template = new DamlTemplate
        {
            Name = "Account",
            Choices = [choice]
        };
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("M", templates: [template])),
            Options(), isMainPackage: true).Single();

        context.LocalChoiceArgToTemplate.Should().NotContainKey("M:NotDeclaredHere");
    }

    [Fact]
    public void ForPackage_disambiguates_same_named_choice_arg_types_across_modules()
    {
        DamlModule ModuleWithTransferChoice(string moduleName, string templateName) => Module(
            moduleName,
            dataTypes: [Record("Transfer", new DamlFieldDefinition("to", new DamlPrimitiveType(DamlPrimitive.Party)))],
            templates:
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Do",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", moduleName, "Transfer"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ]);

        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                ModuleWithTransferChoice("Banking", "Account"),
                ModuleWithTransferChoice("Custody", "Vault")),
            Options(), isMainPackage: true)[0];

        context.LocalChoiceArgToTemplate["Banking:Transfer"].Should().Be(new NestingTemplate("Banking", "Account", "Do"));
        context.LocalChoiceArgToTemplate["Custody:Transfer"].Should().Be(new NestingTemplate("Custody", "Vault", "Do"));
    }

    [Fact]
    public void ForPackage_warns_and_keeps_first_on_same_module_choice_arg_name_clash()
    {
        DamlTemplate TemplateWithTransferChoice(string templateName) => new()
        {
            Name = templateName,
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
        var logger = new CapturingLogger();

        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("Transfer", new DamlFieldDefinition("to", new DamlPrimitiveType(DamlPrimitive.Party)))],
                    templates: [TemplateWithTransferChoice("Account"), TemplateWithTransferChoice("Vault")])),
            Options(),
            isMainPackage: true,
            logger).Single();

        context.LocalChoiceArgToTemplate["M:Transfer"].Should().Be(new NestingTemplate("M", "Account", "Do"));
        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("M:Transfer").And.Contain("Account").And.Contain("Vault").And.Contain("in the same package");
    }
}
