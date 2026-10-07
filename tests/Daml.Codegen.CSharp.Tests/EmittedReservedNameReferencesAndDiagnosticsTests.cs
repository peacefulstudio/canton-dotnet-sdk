// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.ReservedTypeNameFixtures;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedReservedNameReferencesAndDiagnosticsTests
{
    private const string OtherModule = "Other.Module";

    private static IReadOnlyList<GeneratedFile> GenerateLogging(DarModel dar, CapturingLogger logger) =>
        new CSharpCodeGenerator(new CodeGenOptions { IncludeDependencies = true }, new ForwardingLogger(logger)).Generate(dar);

    [Fact]
    public void Renamed_record_emits_one_warning_naming_package_module_entity_member_and_emitted_name()
    {
        var logger = new CapturingLogger();

        GenerateLogging(DarOf([], Record("ToRecord", Field("x", IntType))), logger);

        var warning = logger.Warnings.Should().ContainSingle().Subject;
        warning.Should().Contain("test-package").And.Contain("Test.Module:ToRecord").And.Contain("'ToRecord'").And.Contain("'ToRecord_'");
    }

    [Fact]
    public void Renamed_field_emits_one_warning_naming_the_field_and_the_emitted_property()
    {
        var logger = new CapturingLogger();

        GenerateLogging(DarOf([], Record("Box", Field("fromRecord", IntType))), logger);

        var warning = logger.Warnings.Should().ContainSingle().Subject;
        warning.Should().Contain("fromRecord").And.Contain("Test.Module:Box").And.Contain("'FromRecord'").And.Contain("'FromRecord_'");
    }

    [Fact]
    public void Names_that_compile_unchanged_emit_no_warning()
    {
        var logger = new CapturingLogger();

        GenerateLogging(DarOf([], Record("Box", Field("x", IntType)), Variant("Choice", new DamlVariantConstructor("A", null))), logger);

        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Choice_argument_shared_by_two_templates_of_a_referenced_dependency_emits_one_1200_warning_naming_the_package()
    {
        var sharedArgument = RefIn(OtherModule, "Transfer", "other-pkg-id");
        var foreign = PackageOf(
            "other-pkg-id",
            "other-pkg",
            ModuleOf(
                OtherModule,
                [
                    Template("Account", null, Choice("Do", new DamlPrimitiveType(DamlPrimitive.Unit), sharedArgument)),
                    Template("Vault", null, Choice("Do", new DamlPrimitiveType(DamlPrimitive.Unit), sharedArgument))
                ],
                Record("Account", Field("owner", PartyType)),
                Record("Vault", Field("owner", PartyType)),
                Record("Transfer", Field("to", PartyType))));
        var main = PackageOf(
            "test-package-id",
            "test-package",
            ModuleOf(Module, [], Record("Holder", Field("inner", sharedArgument))));
        var logger = new CapturingLogger();

        GenerateLogging(new DarModel { MainPackage = main, Dependencies = [foreign] }, logger);

        logger.WarningEventIds.Should().Equal(1200);
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("Choice-argument type Other.Module:Transfer in package other-pkg is used by both templates Account and Vault");
    }

    [Fact]
    public void Second_collision_is_a_codegen_error_naming_package_module_and_both_daml_names()
    {
        var dar = DarOf([], Record("Equals", Field("x", IntType)), Record("Equals_", Field("x", IntType)));

        var failure = () => CreateGenerator().Generate(dar);

        var message = failure.Should().Throw<CodegenException>().Which.Message;
        message.Should().Contain("test-package").And.Contain("Test.Module:Equals").And.Contain("Test.Module:Equals_").And.Contain("Rename").And.Contain("in Daml");
    }

    [Fact]
    public void Renamed_template_keeps_its_daml_name_as_the_wire_name()
    {
        var dar = DarOf(
            [Template("TemplateId", new DamlPrimitiveType(DamlPrimitive.Text), Choice("Reissue", IntType))],
            Record("TemplateId", Field("owner", PartyType)));

        var emitted = CreateGenerator().Generate(dar).Single(file => file.RelativePath.EndsWith("TemplateId_.cs", StringComparison.Ordinal)).Content;

        emitted.Should().Contain("new(\"test-package-id\", \"Test.Module\", \"TemplateId\")");
    }

    [Fact]
    public void Renamed_record_referenced_from_another_module_compiles()
    {
        var dar = DarOfModules(
            ModuleOf(Module, [], Record("ToRecord", Field("x", IntType))),
            ModuleOf(OtherModule, [], Record("Holder", Field("inner", RefIn(Module, "ToRecord")))));

        CompileErrors(dar).Should().BeEmpty();
        DeclaresType(dar, "ToRecord_").Should().BeTrue();
    }

    [Fact]
    public void Renamed_record_referenced_from_another_package_compiles()
    {
        var foreign = PackageOf("other-pkg-id", "other-pkg", ModuleOf(OtherModule, [], Record("ToRecord", Field("x", IntType))));
        var main = PackageOf(
            "test-package-id",
            "test-package",
            ModuleOf(Module, [], Record("Holder", Field("inner", RefIn(OtherModule, "ToRecord", "other-pkg-id")))));
        var dar = new DarModel { MainPackage = main, Dependencies = [foreign] };

        var errors = CompileEmittedFiles(GenerateLogging(dar, new CapturingLogger()))
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
            .ToList();

        errors.Should().BeEmpty();
        CompileEmittedFilesToCompilation(GenerateLogging(dar, new CapturingLogger()), DocumentationMode.Parse)
            .GetTypeByMetadataName("Other.Module.ToRecord_").Should().NotBeNull();
    }

    [Fact]
    public void Template_payload_field_named_like_a_template_member_compiles()
    {
        var dar = DarOf(
            [Template("Asset", null, Choice("Reissue", IntType))],
            Record("Asset", Field("templateId", PartyType), Field("toRecord", IntType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Theory]
    [InlineData("GetType")]
    [InlineData("Finalize")]
    [InlineData("MemberwiseClone")]
    [InlineData("ReferenceEquals")]
    public void Keyed_template_named_like_an_inherited_object_method_compiles_unrenamed(string name)
    {
        var dar = DarOf(
            [Template(name, new DamlPrimitiveType(DamlPrimitive.Party), Choice("Reissue", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [Ref(name)])))],
            Record(name, Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
        DeclaresType(dar, name).Should().BeTrue();
    }

    [Theory]
    [InlineData("BumpCommand")]
    [InlineData("TryBumpAsync")]
    [InlineData("ProjectBumpResult")]
    [InlineData("DecodeBumpResult")]
    public void Template_named_like_a_choice_extension_method_compiles_unrenamed(string name)
    {
        var dar = DarOf(
            [Template(name, null, Choice("Bump", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [Ref(name)]), Ref("Bump")), Choice("Peek", IntType))],
            Record(name, Field("owner", PartyType)),
            Record("Bump", Field("by", IntType)));

        CompileErrors(dar).Should().BeEmpty();
        DeclaresType(dar, name).Should().BeTrue();
    }

    [Fact]
    public void Template_excluded_by_the_root_filter_does_not_collide_with_a_same_named_suffixed_record()
    {
        var dar = DarOf(
            [Template("Equals", PartyType)],
            Record("Equals", Field("owner", PartyType)),
            Record("Equals_", Field("x", IntType)));

        var files = new CSharpCodeGenerator(new CodeGenOptions { RootFilter = "Equals_$" }, new ForwardingLogger(new CapturingLogger())).Generate(dar);

        files.Select(file => Path.GetFileName(file.RelativePath)).Should().Contain("Equals_.cs").And.NotContain("Equals.cs");
    }

    [Fact]
    public void Template_field_named_like_a_choice_with_a_nested_argument_record_compiles()
    {
        var dar = DarOf(
            [Template("Holder", null, Choice("Reissue", IntType, Ref("Reissue")))],
            Record("Holder", Field("reissue", IntType)),
            Record("Reissue", Field("by", IntType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Template_named_like_a_choice_extension_with_a_same_named_controller_field_compiles()
    {
        var bump = new DamlChoice
        {
            Name = "Bump",
            Consuming = false,
            ArgumentType = Ref("Bump"),
            ReturnType = new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [Ref("TryBumpAsync")]),
            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("tryBumpAsync")]),
        };
        var dar = DarOf(
            [Template("TryBumpAsync", null, bump)],
            Record("TryBumpAsync", Field("tryBumpAsync", PartyType)),
            Record("Bump", Field("by", IntType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Choice_result_slot_for_a_template_named_like_the_result_record_compiles()
    {
        var dar = DarOfModules(
            ModuleOf(OtherModule, [Template("BumpResult", null)], Record("BumpResult", Field("owner", PartyType))),
            ModuleOf(
                Module,
                [Template("Holder", null, Choice("Bump", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [RefIn(OtherModule, "BumpResult")])))],
                Record("Holder", Field("owner", PartyType))));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Equals")]
    [InlineData("GetType")]
    [InlineData("ToString")]
    [InlineData("ToDamlEnum")]
    [InlineData("FromDamlEnum")]
    [InlineData("ExpectedConstructors")]
    public void Enum_named_like_a_member_of_its_extensions_class_compiles_unrenamed(string name)
    {
        var dar = DarOf([], Enum(name, "Red", "Green"), Record("Holder", Field("inner", Ref(name))));

        CompileErrors(dar).Should().BeEmpty();
        DeclaresType(dar, name).Should().BeTrue();
    }

    [Theory]
    [InlineData("GetType")]
    [InlineData("Deconstruct")]
    [InlineData("FromCreatedContracts")]
    [InlineData("PrintMembers")]
    [InlineData("EqualityContract")]
    [InlineData("ReferenceEquals")]
    public void Template_named_like_a_record_member_compiles_beside_a_choice_returning_its_contract_id(string name)
    {
        var dar = DarOf(
            [
                Template(name, null),
                Template("Maker", null, Choice("Make", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [Ref(name)]))),
            ],
            Record(name, Field("owner", PartyType)),
            Record("Maker", Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Record_and_field_both_named_to_record_compile()
    {
        var dar = DarOf([], Record("ToRecord", Field("toRecord", IntType), Field("other", IntType)));

        CompileErrors(dar).Should().BeEmpty();
        DeclaresType(dar, "ToRecord_").Should().BeTrue();
    }

    [Fact]
    public void Template_payload_field_named_like_a_choice_descriptor_compiles()
    {
        var dar = DarOf(
            [Template("Asset", null, Choice("Reissue", IntType))],
            Record("Asset", Field("choiceReissue", IntType), Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Keyed_template_payload_field_named_like_a_by_key_builder_compiles()
    {
        var dar = DarOf(
            [Template("Asset", new DamlPrimitiveType(DamlPrimitive.Party), Choice("Reissue", IntType))],
            Record("Asset", Field("reissueByKeyCommand", IntType), Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Theory]
    [InlineData("UpgradedPackageId")]
    [InlineData("Plain")]
    public void Template_in_an_upgraded_package_named_like_its_upgraded_package_member_compiles(string name)
    {
        var dar = DarOf([Template(name, null, Choice("Reissue", IntType))], Record(name, Field("upgradedPackageId", IntType)));
        var main = dar.MainPackage;
        var upgraded = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = main.PackageId,
                Name = main.Name,
                Version = main.Version,
                LfVersion = main.LfVersion,
                Modules = main.Modules,
                DependencyReferences = main.DependencyReferences,
                UpgradedPackageId = "older-package-id",
            },
            Dependencies = [],
        };

        CompileErrors(upgraded).Should().BeEmpty();
    }

    [Fact]
    public void Reference_to_a_dependency_template_is_renamed_whatever_the_root_filter_admits_locally()
    {
        var foreign = PackageOf(
            "other-pkg-id",
            "other-pkg",
            ModuleOf(OtherModule, [Template("Equals", PartyType)], Record("Equals", Field("owner", PartyType))));
        var main = PackageOf(
            "test-package-id",
            "test-package",
            ModuleOf(Module, [], Record("Holder", Field("inner", RefIn(OtherModule, "Equals", "other-pkg-id")))));

        var files = new CSharpCodeGenerator(new CodeGenOptions { RootFilter = "^Test\\.Module:" }, new ForwardingLogger(new CapturingLogger()))
            .Generate(new DarModel { MainPackage = main, Dependencies = [foreign] });

        files.Single(file => Path.GetFileName(file.RelativePath) == "Holder.cs").Content.Should().Contain("global::Other.Module.Equals_");
    }

    [Fact]
    public void Dependency_template_excluded_by_the_root_filter_does_not_collide_when_dependencies_are_emitted()
    {
        var foreign = PackageOf(
            "other-pkg-id",
            "other-pkg",
            ModuleOf(OtherModule, [Template("Equals", PartyType)], Record("Equals", Field("owner", PartyType)), Record("Equals_", Field("x", IntType))));
        var main = PackageOf(
            "test-package-id",
            "test-package",
            ModuleOf(Module, [], Record("Holder", Field("inner", RefIn(OtherModule, "Equals_", "other-pkg-id")))));

        var files = new CSharpCodeGenerator(
                new CodeGenOptions { RootFilter = "Test\\.Module:|Equals_$", IncludeDependencies = true },
                new ForwardingLogger(new CapturingLogger()))
            .Generate(new DarModel { MainPackage = main, Dependencies = [foreign] });

        files.Select(file => Path.GetFileName(file.RelativePath)).Should().Contain("Equals_.cs").And.NotContain("Equals.cs");
    }

    [Fact]
    public void Second_collision_inside_a_dependency_is_reported_as_a_dependency_problem()
    {
        var foreign = PackageOf(
            "other-pkg-id",
            "other-pkg",
            ModuleOf(OtherModule, [], Record("Equals", Field("x", IntType)), Record("Equals_", Field("x", IntType))));
        var main = PackageOf(
            "test-package-id",
            "test-package",
            ModuleOf(Module, [], Record("Holder", Field("inner", RefIn(OtherModule, "Equals", "other-pkg-id")))));

        var failure = () => CreateGenerator().Generate(new DarModel { MainPackage = main, Dependencies = [foreign] });

        failure.Should().Throw<CodegenException>().Which.Message.Should().Contain("Dependency package 'other-pkg'").And.Contain("Other.Module:Equals_");
    }

    private sealed class ForwardingLogger(ILogger inner) : ILogger<CSharpCodeGenerator>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
