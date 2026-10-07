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
    private static DamlPackage Package(params DamlModule[] modules) =>
        new()
        {
            PackageId = "pkg-id",
            Name = "p",
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

    private static DamlInterface Interface(string name) =>
        new() { Name = name, Choices = [] };

    private static PackageNameTable TableOf(
        DamlPackage package,
        string? rootFilter = null,
        string? namespacePrefix = null,
        bool isMainPackage = true) =>
        PackageNameTable.For(
            package,
            new CodeGenOptions { NamespacePrefix = namespacePrefix },
            isMainPackage,
            new TypeRootFilter(rootFilter));

    private static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    private static DamlDataType Enum(string name, params string[] constructors) =>
        new() { Name = name, Definition = new DamlEnumDefinition(constructors) };

    private static DamlDataType Variant(string name, params DamlVariantConstructor[] constructors) =>
        new() { Name = name, Definition = new DamlVariantDefinition(constructors) };

    private static DamlChoice Choice(string name, string argumentModule, string argumentType) =>
        new()
        {
            Name = name,
            Consuming = true,
            ArgumentType = new DamlTypeRef("", argumentModule, argumentType),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };

    private static DamlTemplate Template(string name, params DamlChoice[] choices) =>
        new() { Name = name, Choices = choices };

    private static DamlFieldDefinition PartyField(string name) =>
        new(name, new DamlPrimitiveType(DamlPrimitive.Party));

    [Fact]
    public void NestedChoiceArgumentHome_is_the_template_nesting_a_record_declared_in_another_module()
    {
        var table = TableOf(Package(
            Module("Args", dataTypes: [Record("TransferArg")]),
            Module("Banking", templates: [Template("Account", Choice("Transfer", "Args", "TransferArg"))])));

        table.NestedChoiceArgumentHome("Args", "TransferArg")
            .Should().Be(new NestingTemplate("Banking", "Account", "Transfer"));
    }

    [Fact]
    public void NestedChoiceArgumentHome_escapes_a_choice_name_that_is_a_csharp_keyword_in_the_nested_class_name()
    {
        var table = TableOf(Package(
            Module("Banking", dataTypes: [Record("EventArg")], templates: [Template("Account", Choice("event", "Banking", "EventArg"))])));

        table.NestedChoiceArgumentHome("Banking", "EventArg")!.NestedClassName.Should().Be("@event");
    }

    [Fact]
    public void NestedChoiceArgumentHome_keeps_the_first_template_when_two_templates_share_an_argument_type()
    {
        var table = TableOf(Package(
            Module(
                "M",
                dataTypes: [Record("Transfer", PartyField("to"))],
                templates:
                [
                    Template("Account", Choice("Do", "M", "Transfer")),
                    Template("Vault", Choice("Do", "M", "Transfer"))
                ])));

        table.NestedChoiceArgumentHome("M", "Transfer").Should().Be(new NestingTemplate("M", "Account", "Do"));
    }

    [Fact]
    public void LogWarnings_reports_one_1200_warning_naming_the_package_and_both_templates_when_two_templates_share_an_argument_type()
    {
        var table = TableOf(Package(
            Module(
                "M",
                dataTypes: [Record("Transfer", PartyField("to"))],
                templates:
                [
                    Template("Account", Choice("Do", "M", "Transfer")),
                    Template("Vault", Choice("Do", "M", "Transfer"))
                ])));
        var logger = new CapturingLogger();

        table.LogWarnings(logger);

        logger.WarningEventIds.Should().Equal(1200);
        logger.Warnings.Should().ContainSingle().Which.Should().Be(
            "Choice-argument type M:Transfer in package p is used by both templates Account and Vault in the same package; keeping Account and ignoring Vault. Rename one choice-argument type to disambiguate.");
    }

    [Fact]
    public void LogAmbiguities_reports_only_the_shared_argument_type_and_not_the_renamed_types()
    {
        var table = TableOf(Package(
            Module(
                "M",
                dataTypes: [Record("Transfer", PartyField("to"))],
                templates:
                [
                    Template("ToRecord"),
                    Template("Account", Choice("Do", "M", "Transfer")),
                    Template("Vault", Choice("Do", "M", "Transfer"))
                ])));
        var logger = new CapturingLogger();

        table.LogAmbiguities(logger);

        logger.WarningEventIds.Should().Equal(1200);
    }

    [Fact]
    public void LogWarnings_stays_silent_when_every_argument_type_has_one_template()
    {
        var table = TableOf(Package(
            Module(
                "M",
                dataTypes: [Record("TransferArg"), Record("CloseArg")],
                templates: [Template("Account", Choice("Transfer", "M", "TransferArg"), Choice("Close", "M", "CloseArg"))])));
        var logger = new CapturingLogger();

        table.LogWarnings(logger);

        logger.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("enum")]
    [InlineData("variant")]
    public void For_rejects_a_choice_argument_that_is_not_a_record_naming_the_template_the_choice_and_the_kind(string kind)
    {
        var argument = kind == "enum"
            ? Enum("Arg", "A", "B")
            : Variant("Arg", new DamlVariantConstructor("A", new DamlPrimitiveType(DamlPrimitive.Int64)));
        var package = Package(Module("M", dataTypes: [argument], templates: [Template("T", Choice("Go", "M", "Arg"))]));

        var build = () => TableOf(package);

        build.Should().Throw<CodegenException>().Which.Message.Should().Be(
            $"Choice 'Go' of Daml template 'M:T' in package 'p' takes the {kind} 'M:Arg' as its argument, but a choice argument must be a record. " +
            "damlc always synthesises a record for a choice, so the Daml model is malformed.");
    }

    [Fact]
    public void NestedChoiceArgumentHome_separates_the_same_simple_name_declared_in_two_modules()
    {
        DamlModule ModuleWithTransferChoice(string moduleName, string templateName) => Module(
            moduleName,
            dataTypes: [Record("Transfer", PartyField("to"))],
            templates: [Template(templateName, Choice("Do", moduleName, "Transfer"))]);

        var table = TableOf(Package(
            ModuleWithTransferChoice("Banking", "Account"),
            ModuleWithTransferChoice("Custody", "Vault")));

        table.NestedChoiceArgumentHome("Banking", "Transfer").Should().Be(new NestingTemplate("Banking", "Account", "Do"));
        table.NestedChoiceArgumentHome("Custody", "Transfer").Should().Be(new NestingTemplate("Custody", "Vault", "Do"));
    }

    [Fact]
    public void NestedChoiceArgumentHome_is_absent_for_a_record_no_choice_takes()
    {
        var table = TableOf(Package(
            Module("M", dataTypes: [Record("Plain"), Record("TransferArg")], templates: [Template("Account", Choice("Transfer", "M", "TransferArg"))])));

        table.NestedChoiceArgumentHome("M", "Plain").Should().BeNull();
    }

    [Fact]
    public void NestedChoiceArgumentHome_is_absent_for_a_type_the_package_does_not_declare()
    {
        var table = TableOf(Package(
            Module("M", templates: [Template("Account", Choice("Transfer", "M", "NotDeclaredHere"))])));

        table.NestedChoiceArgumentHome("M", "NotDeclaredHere").Should().BeNull();
    }

    [Fact]
    public void NestedChoiceArgumentRecord_is_the_record_a_choice_takes_as_its_argument()
    {
        var choice = Choice("Transfer", "M", "TransferArg");
        var table = TableOf(Package(
            Module("M", dataTypes: [Record("TransferArg", PartyField("to"))], templates: [Template("Account", choice)])));

        table.NestedChoiceArgumentRecord(choice)!.Fields.Select(field => field.Name).Should().Equal("to");
    }

    [Fact]
    public void NestedChoiceArgumentRecord_is_absent_for_an_argument_less_choice()
    {
        var choice = new DamlChoice
        {
            Name = "Archive",
            Consuming = true,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var table = TableOf(Package(Module("M", templates: [Template("Account", choice)])));

        table.NestedChoiceArgumentRecord(choice).Should().BeNull();
    }

    [Fact]
    public void IsInterface_is_true_for_a_declared_interface_and_false_for_a_record()
    {
        var table = TableOf(Package(
            Module("M", dataTypes: [Record("Holding"), Record("Plain")], interfaces: [Interface("Holding")])));

        table.IsInterface("M", "Holding").Should().BeTrue();
        table.IsInterface("M", "Plain").Should().BeFalse();
    }

    [Fact]
    public void IsInterface_separates_the_same_simple_name_declared_in_two_modules()
    {
        var table = TableOf(Package(
            Module("Alpha", dataTypes: [Record("Holding")], interfaces: [Interface("Holding")]),
            Module("Beta", dataTypes: [Record("Holding")])));

        table.IsInterface("Alpha", "Holding").Should().BeTrue();
        table.IsInterface("Beta", "Holding").Should().BeFalse();
    }

    [Fact]
    public void NamespaceOf_is_the_namespace_of_a_module_of_the_package()
    {
        var table = TableOf(Package(Module("Banking.Core")), namespacePrefix: "Acme");

        table.NamespaceOf("Banking.Core").Should().Be("Acme.Banking.Core");
    }

    [Fact]
    public void NamespaceOf_fails_for_a_module_the_package_does_not_declare()
    {
        var table = TableOf(Package(Module("M")));

        var act = () => table.NamespaceOf("Elsewhere");

        act.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Module 'Elsewhere' is not declared by package 'p'. The Daml model is malformed: a reference must name a module of the package it points into.");
    }
}
