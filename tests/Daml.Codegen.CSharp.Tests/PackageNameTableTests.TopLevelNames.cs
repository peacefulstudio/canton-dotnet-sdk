// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Daml.Codegen.Intermediate.Model;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public partial class PackageNameTableTests
{
    private static DamlDataType VariantOf(string name, params string[] constructors) =>
        Variant(name, constructors.Select(constructor => new DamlVariantConstructor(constructor, null)).ToArray());

    [Fact]
    public void EmittedName_is_the_sanitised_daml_name_for_a_plain_type()
    {
        var table = TableOf(Package(Module("M", dataTypes: [Record("Person")])));

        table.EmittedName("M", "Person").Should().Be("Person");
        table.Renames.Should().BeEmpty();
    }

    [Fact]
    public void EmittedName_gains_one_trailing_underscore_for_a_record_a_variant_and_a_template_named_like_a_generated_member()
    {
        var table = TableOf(Package(Module(
            "M",
            dataTypes: [Record("ToRecord"), VariantOf("Tag", "A")],
            templates: [Template("TemplateId")])));

        table.EmittedName("M", "ToRecord").Should().Be("ToRecord_");
        table.EmittedName("M", "Tag").Should().Be("Tag_");
        table.EmittedName("M", "TemplateId").Should().Be("TemplateId_");
        table.Renames.Select(rename => (rename.Module, rename.DamlName, rename.Kind, rename.ClashingMember, rename.EmittedName)).Should().Equal(
            ("M", "TemplateId", "template", "TemplateId", "TemplateId_"),
            ("M", "ToRecord", "record", "ToRecord", "ToRecord_"),
            ("M", "Tag", "variant", "Tag", "Tag_"));
    }

    [Fact]
    public void EmittedName_leaves_an_enum_named_like_a_generated_member_unrenamed()
    {
        var table = TableOf(Package(Module("M", dataTypes: [Enum("Equals", "A")])));

        table.EmittedName("M", "Equals").Should().Be("Equals");
        table.Renames.Should().BeEmpty();
    }

    [Fact]
    public void EmittedName_renames_only_the_declaration_kind_that_carries_the_member()
    {
        var table = TableOf(Package(
            Module("Alpha", dataTypes: [VariantOf("Tag", "A")]),
            Module("Beta", dataTypes: [Record("Tag")])));

        table.EmittedName("Alpha", "Tag").Should().Be("Tag_");
        table.EmittedName("Beta", "Tag").Should().Be("Tag");
    }

    [Fact]
    public void For_rejects_a_renamed_type_whose_suffixed_name_another_type_already_emits()
    {
        var package = Package(Module("M", dataTypes: [Record("ToRecord"), Record("ToRecord_")]));

        var build = () => TableOf(package);

        build.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Daml record 'M:ToRecord' in package 'p' is named like the member 'ToRecord' the generated record type carries, so it would be emitted as 'ToRecord_' - but Daml type 'M:ToRecord_' already emits that C# name. Rename one of 'ToRecord' and 'ToRecord_' in Daml.");
    }

    [Fact]
    public void For_rejects_two_variant_constructors_emitted_as_the_same_nested_type()
    {
        var package = Package(Module("M", dataTypes: [VariantOf("V", "V", "V_")]));

        var build = () => TableOf(package);

        build.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Constructors 'V' and 'V_' of Daml variant 'M:V' in package 'p' would both be emitted as the nested C# type 'V_'. Rename one of them in Daml.");
    }

    [Fact]
    public void For_rejects_a_variant_constructor_named_like_a_type_parameter_of_its_variant()
    {
        var variant = new DamlDataType
        {
            Name = "V",
            TypeParams = ["a"],
            Definition = new DamlVariantDefinition([new DamlVariantConstructor("TA", null)])
        };

        var build = () => TableOf(Package(Module("M", dataTypes: [variant])));

        build.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Constructor 'TA' of Daml variant 'M:V' in package 'p' would be emitted as a nested C# type named 'TA', which the generated variant type already declares as a type parameter. Rename the constructor 'TA' or the type variable in Daml.");
    }

    [Fact]
    public void For_rejects_a_variant_constructor_named_like_a_member_of_its_variant()
    {
        var build = () => TableOf(Package(Module("M", dataTypes: [VariantOf("V", "Tag")])));

        build.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Constructor 'Tag' of Daml variant 'M:V' in package 'p' would be emitted as a nested C# type named 'Tag', which the generated variant type already declares as a member. Rename the constructor 'Tag' in Daml.");
    }

    [Fact]
    public void For_rejects_a_choice_with_a_record_argument_named_like_a_member_of_its_template()
    {
        var package = Package(Module("M", dataTypes: [Record("Arg")], templates: [Template("Account", Choice("TemplateId", "M", "Arg"))]));

        var build = () => TableOf(package);

        build.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Choice 'TemplateId' of Daml template 'M:Account' in package 'p' takes a record argument that would be emitted as a nested C# type named 'TemplateId', which the generated template type already declares as a member. Rename the choice 'TemplateId' in Daml.");
    }

    [Fact]
    public void For_rejects_a_choice_with_a_record_argument_named_like_its_own_template()
    {
        var package = Package(Module("M", dataTypes: [Record("Arg")], templates: [Template("Account", Choice("Account", "M", "Arg"))]));

        var build = () => TableOf(package);

        build.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Choice 'Account' of Daml template 'M:Account' in package 'p' takes a record argument that would be emitted as a nested C# type named 'Account', which the generated template type already declares as a member. Rename the choice 'Account' in Daml.");
    }

    [Fact]
    public void EmittedName_leaves_a_template_the_root_filter_excludes_unrenamed()
    {
        var package = Package(Module("M", dataTypes: [Record("Other"), Record("ToRecord_")], templates: [Template("ToRecord")]));

        var table = TableOf(package, rootFilter: "^M:Other$");

        table.EmittedName("M", "ToRecord").Should().Be("ToRecord");
        table.Renames.Should().BeEmpty();
    }

    [Fact]
    public void For_rejects_the_same_package_when_the_root_filter_admits_the_template()
    {
        var package = Package(Module("M", dataTypes: [Record("Other"), Record("ToRecord_")], templates: [Template("ToRecord")]));

        var build = () => TableOf(package, rootFilter: "^M:ToRecord$");

        build.Should().Throw<CodegenException>().Which.Message.Should().StartWith("Daml template 'M:ToRecord' in package 'p' is named like the member 'ToRecord'");
    }

    [Fact]
    public void For_ignores_a_choice_clash_of_a_template_the_root_filter_excludes()
    {
        var package = Package(Module("M", dataTypes: [Record("Arg")], templates: [Template("Account", Choice("TemplateId", "M", "Arg"))]));

        var table = TableOf(package, rootFilter: "^M:Nothing$");

        table.EmittedName("M", "Account").Should().Be("Account");
    }

    [Fact]
    public void ReservedTopLevelTypeNames_keeps_the_name_of_a_template_the_root_filter_excludes()
    {
        var package = Package(Module("M", dataTypes: [Record("Other"), Record("ToRecord_")], templates: [Template("ToRecord")]));

        var table = TableOf(package, rootFilter: "^M:Other$");

        table.ReservedTopLevelTypeNames.Should().BeEquivalentTo(["ToRecord", "ToRecord_", "Other"]);
    }

    [Fact]
    public void ReservedTopLevelTypeNames_holds_the_emitted_name_of_every_top_level_type_of_the_package()
    {
        var table = TableOf(Package(
            Module("Alpha", dataTypes: [Record("ToRecord"), Enum("Colour", "Red")], templates: [Template("Account")]),
            Module("Beta", dataTypes: [Record("Person")])));

        table.ReservedTopLevelTypeNames.Should().BeEquivalentTo(["ToRecord_", "Colour", "Account", "Person"]);
    }

    [Fact]
    public void ReservedTopLevelTypeNames_widens_to_a_record_colliding_with_an_interface_marker()
    {
        var table = TableOf(Package(Module("M", dataTypes: [Record("IFactory")], interfaces: [Interface("Factory")])));

        table.ReservedTopLevelTypeNames.Should().Contain("IFactory");
        table.InterfaceMarkerName("M", "Factory").Should().Be("IFactory_");
    }

    [Fact]
    public void ReservedTopLevelTypeNames_widens_to_a_record_colliding_with_the_first_round_disambiguated_marker()
    {
        var table = TableOf(Package(Module(
            "M",
            dataTypes: [Record("IFactory_")],
            templates: [Template("IFactory")],
            interfaces: [Interface("Factory")])));

        table.ReservedTopLevelTypeNames.Should().Contain("IFactory_");
        table.InterfaceMarkerName("M", "Factory").Should().Be("IFactory__");
    }

    [Fact]
    public void ReservedTopLevelTypeNames_excludes_the_record_lf_declares_beside_an_interface_of_the_same_name()
    {
        var table = TableOf(Package(Module("M", dataTypes: [Record("Factory")], interfaces: [Interface("Factory")])));

        table.ReservedTopLevelTypeNames.Should().NotContain("Factory");
        table.InterfaceMarkerName("M", "Factory").Should().Be("IFactory");
    }

    [Fact]
    public void ReservedTopLevelTypeNames_excludes_a_record_nested_as_a_choice_argument()
    {
        var table = TableOf(Package(Module(
            "M",
            dataTypes: [Record("IFactory", PartyField("to"))],
            templates: [Template("Account", Choice("Transfer", "M", "IFactory"))],
            interfaces: [Interface("Factory")])));

        table.ReservedTopLevelTypeNames.Should().NotContain("IFactory");
        table.InterfaceMarkerName("M", "Factory").Should().Be("IFactory");
    }

    [Fact]
    public void InterfaceMarkerName_gives_the_unsuffixed_name_to_the_same_interface_whatever_the_module_declaration_order()
    {
        var declaredAlphaFirst = TableOf(Package(
            Module("Alpha", interfaces: [Interface("Factory")]),
            Module("Beta", interfaces: [Interface("Factory")])));
        var declaredBetaFirst = TableOf(Package(
            Module("Beta", interfaces: [Interface("Factory")]),
            Module("Alpha", interfaces: [Interface("Factory")])));

        declaredAlphaFirst.InterfaceMarkerName("Alpha", "Factory").Should().Be("IFactory");
        declaredAlphaFirst.InterfaceMarkerName("Beta", "Factory").Should().Be("IFactory_");
        declaredBetaFirst.InterfaceMarkerName("Alpha", "Factory").Should().Be("IFactory");
        declaredBetaFirst.InterfaceMarkerName("Beta", "Factory").Should().Be("IFactory_");
    }

    [Fact]
    public void TopLevelTypeNames_lists_the_emitted_types_and_markers_of_one_module()
    {
        var table = TableOf(Package(
            Module(
                "Alpha",
                dataTypes: [Record("ToRecord"), Record("Arg"), Record("Factory")],
                templates: [Template("Account", Choice("Do", "Alpha", "Arg"))],
                interfaces: [Interface("Factory")]),
            Module("Beta", dataTypes: [Record("Person")])));

        table.TopLevelTypeNames("Alpha").Should().BeEquivalentTo(["ToRecord_", "Account", "IFactory", "PackageRegistration_pkg_u002did"]);
        table.TopLevelTypeNames("Beta").Should().BeEquivalentTo(["Person"]);
    }

    [Fact]
    public void ModuleNamespaces_prefix_the_main_package_modules_and_leave_a_dependency_unprefixed()
    {
        var package = Package(Module("Banking.Core"), Module("Acme.Ledger"));

        var main = TableOf(package, namespacePrefix: "Acme.Ledger", isMainPackage: true);
        var dependency = TableOf(package, namespacePrefix: "Acme.Ledger", isMainPackage: false);

        main.ModuleNamespaces.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Banking.Core"] = "Acme.Ledger.Banking.Core",
            ["Acme.Ledger"] = "Acme.Ledger"
        });
        dependency.ModuleNamespaces.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Banking.Core"] = "Banking.Core",
            ["Acme.Ledger"] = "Acme.Ledger"
        });
    }

    [Fact]
    public void For_rejects_a_package_declaring_the_same_module_twice()
    {
        var build = () => TableOf(Package(Module("M"), Module("M")));

        build.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Package 'p' declares module 'M' more than once. The Daml model is malformed: module names are unique within a package.");
    }

    [Fact]
    public void LogWarnings_reports_a_1201_warning_for_each_type_emitted_under_a_suffixed_name()
    {
        var table = TableOf(Package(Module(
            "M",
            dataTypes: [Record("ToRecord"), VariantOf("Tag", "A")],
            templates: [Template("TemplateId")])));
        var logger = new CapturingLogger();

        table.LogWarnings(logger);

        logger.WarningEventIds.Should().Equal(1201, 1201, 1201);
        logger.Warnings.Should().Equal(
            "Daml template M:TemplateId in package p is named like the member 'TemplateId' the generated template type carries; it is emitted as 'TemplateId_'. Its Daml name, the wire name, is unchanged.",
            "Daml record M:ToRecord in package p is named like the member 'ToRecord' the generated record type carries; it is emitted as 'ToRecord_'. Its Daml name, the wire name, is unchanged.",
            "Daml variant M:Tag in package p is named like the member 'Tag' the generated variant type carries; it is emitted as 'Tag_'. Its Daml name, the wire name, is unchanged.");
    }

    [Fact]
    public void LogWarnings_reports_a_1202_warning_for_a_record_field_whose_property_would_clash()
    {
        var table = TableOf(Package(Module("M", dataTypes: [Record("Holder", PartyField("toRecord"))])));
        var logger = new CapturingLogger();

        table.LogWarnings(logger);

        logger.WarningEventIds.Should().Equal(1202);
        logger.Warnings.Should().Equal(
            "Field toRecord of Daml record M:Holder in package p would be the property 'ToRecord', which the generated record type already carries; it is emitted as 'ToRecord_'. Its Daml name, the wire name, is unchanged.");
    }

    [Fact]
    public void LogWarnings_reports_a_1202_warning_for_a_template_payload_field_whose_property_would_clash()
    {
        var table = TableOf(Package(Module(
            "M",
            dataTypes: [Record("Account", PartyField("templateId"))],
            templates: [Template("Account")])));
        var logger = new CapturingLogger();

        table.LogWarnings(logger);

        logger.WarningEventIds.Should().Equal(1202);
        logger.Warnings.Should().Equal(
            "Field templateId of Daml template M:Account in package p would be the property 'TemplateId', which the generated template type already carries; it is emitted as 'TemplateId_'. Its Daml name, the wire name, is unchanged.");
    }

    [Fact]
    public void LogWarnings_reports_the_type_renames_before_the_shared_choice_argument_ambiguity()
    {
        var table = TableOf(Package(Module(
            "M",
            dataTypes: [Record("ToRecord"), Record("Transfer", PartyField("to"))],
            templates:
            [
                Template("Account", Choice("Do", "M", "Transfer")),
                Template("Vault", Choice("Do", "M", "Transfer"))
            ])));
        var logger = new CapturingLogger();

        table.LogWarnings(logger);

        logger.WarningEventIds.Should().Equal(1201, 1200);
    }

    [Fact]
    public void LogWarnings_stays_silent_for_a_package_whose_names_all_compile_unchanged()
    {
        var table = TableOf(Package(Module("M", dataTypes: [Record("Person", PartyField("owner"))], templates: [Template("Account")])));
        var logger = new CapturingLogger();

        table.LogWarnings(logger);

        logger.Records.Should().BeEmpty();
    }
}
