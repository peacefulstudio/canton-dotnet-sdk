// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public partial class PackageNameTableTests
{
    private static DamlPackage PackageWithId(string packageId, params DamlModule[] modules) =>
        new()
        {
            PackageId = packageId,
            Name = "p",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DamlInterface InterfaceWithChoice(string name) =>
        new() { Name = name, Choices = [Choice("Pass", "M", "Unit")] };

    [Fact]
    public void Registration_is_absent_when_the_package_declares_no_template_and_no_interface_with_choices()
    {
        var table = TableOf(Package(Module("M", dataTypes: [Record("Person")], interfaces: [Interface("IPlain")])));

        table.Registration.Should().BeNull();
    }

    [Fact]
    public void Registration_names_its_class_after_the_package_id()
    {
        var table = TableOf(PackageWithId("ab12cd", Module("M", templates: [Template("Asset")])));

        table.Registration!.ClassName.Should().Be("PackageRegistration_ab12cd");
    }

    [Fact]
    public void Registration_class_names_of_two_package_ids_with_the_same_package_name_differ()
    {
        var first = TableOf(PackageWithId("0011", Module("M", templates: [Template("Asset")])));
        var second = TableOf(PackageWithId("0022", Module("M", templates: [Template("Asset")])));

        first.Registration!.ClassName.Should().Be("PackageRegistration_0011");
        second.Registration!.ClassName.Should().Be("PackageRegistration_0022");
    }

    [Fact]
    public void Registration_spells_a_package_id_outside_identifier_characters_injectively()
    {
        var hyphenated = TableOf(PackageWithId("a-b", Module("M", templates: [Template("Asset")])));
        var spelledEscape = TableOf(PackageWithId("a_u002db", Module("M", templates: [Template("Asset")])));

        hyphenated.Registration!.ClassName.Should().Be("PackageRegistration_a_u002db");
        spelledEscape.Registration!.ClassName.Should().Be("PackageRegistration_a__u002db");
    }

    [Fact]
    public void Registration_lists_every_template_and_every_interface_with_choices()
    {
        var table = TableOf(Package(Module(
            "M",
            templates: [Template("Asset"), Template("Loan")],
            interfaces: [InterfaceWithChoice("Holding"), Interface("Plain")])));

        table.Registration!.Templates.Select(template => (template.Module.Name, template.Template.Name))
            .Should().Equal(("M", "Asset"), ("M", "Loan"));
        table.Registration.Interfaces.Select(iface => (iface.Module.Name, iface.Interface.Name))
            .Should().Equal(("M", "Holding"));
    }

    [Fact]
    public void Registration_omits_a_template_and_an_interface_the_root_filter_excludes()
    {
        var table = TableOf(
            Package(Module(
                "M",
                templates: [Template("Asset"), Template("Loan")],
                interfaces: [InterfaceWithChoice("Holding"), InterfaceWithChoice("Pledge")])),
            rootFilter: "^M:(Asset|Holding)$");

        table.Registration!.Templates.Select(template => template.Template.Name).Should().Equal("Asset");
        table.Registration.Interfaces.Select(iface => iface.Interface.Name).Should().Equal("Holding");
    }

    [Fact]
    public void Registration_is_absent_when_the_root_filter_excludes_every_registrable_type()
    {
        var table = TableOf(Package(Module("M", templates: [Template("Asset")])), rootFilter: "M:Other");

        table.Registration.Should().BeNull();
    }

    [Fact]
    public void Registration_homes_its_class_in_the_namespace_of_the_first_module_by_ordinal_name_that_registers()
    {
        var table = TableOf(Package(
            Module("Zeta", templates: [Template("Asset")]),
            Module("Alpha", dataTypes: [Record("Person")]),
            Module("Beta", templates: [Template("Loan")])));

        table.Registration!.Namespace.Should().Be("Beta");
    }

    [Fact]
    public void Registration_homes_its_class_under_the_namespace_prefix_of_the_main_package_only()
    {
        var module = Module("M", templates: [Template("Asset")]);

        TableOf(Package(module), namespacePrefix: "Acme").Registration!.Namespace.Should().Be("Acme.M");
        TableOf(Package(module), namespacePrefix: "Acme", isMainPackage: false).Registration!.Namespace.Should().Be("M");
    }

    [Fact]
    public void Registration_class_name_gains_trailing_underscores_until_it_clashes_with_no_top_level_type_of_the_package()
    {
        var table = TableOf(PackageWithId(
            "ab12",
            Module("M", templates: [Template("Asset")], dataTypes: [Record("PackageRegistration_ab12")]),
            Module("N", dataTypes: [Record("PackageRegistration_ab12_")])));

        table.Registration!.ClassName.Should().Be("PackageRegistration_ab12__");
    }

    [Fact]
    public void TopLevelTypeNames_includes_the_registration_class_for_its_home_module_only()
    {
        var table = TableOf(PackageWithId(
            "ab12",
            Module("Alpha", templates: [Template("Asset")]),
            Module("Beta", templates: [Template("Loan")])));

        table.TopLevelTypeNames("Alpha").Should().BeEquivalentTo(["Asset", "PackageRegistration_ab12"]);
        table.TopLevelTypeNames("Beta").Should().BeEquivalentTo(["Loan"]);
    }
}
