// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedTemplateKeyAndChoiceWrapperCompilesTests
{
    [Fact]
    public void Emitted_template_with_key_compiles_standalone()
    {
        var files = GenerateKeyBearingTemplate();

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "a key-bearing package must compile with no consumer contribution, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_template_with_key_has_no_doc_diagnostics()
    {
        var files = GenerateKeyBearingTemplate();

        var docDiagnostics = CompileEmittedFilesWithDocDiagnostics(files)
            .Where(d => d.Id is "CS1570" or "CS1572" or "CS1573" or "CS1574" or "CS1580" or "CS1584" or "CS1658")
            .ToList();

        docDiagnostics.Should().BeEmpty(
            "the by-key command builders carry crefs and param docs that a consumer building with GenerateDocumentationFile compiles, but got: {0}",
            string.Join("\n", docDiagnostics.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_non_contract_wrapper_with_nested_unit_return_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Sink",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MaybeNothing",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.Optional),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        },
                        new DamlChoice
                        {
                            Name = "ListOfUnits",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        },
                        new DamlChoice
                        {
                            Name = "MapOfUnits",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.TextMap),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Sink",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "non-CID wrappers with nested Unit returns must compile end-to-end, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_non_contract_choice_wrapper_for_optional_unit_return_decodes_via_DamlUnit_cast_and_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Probe",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MaybeAck",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.Optional),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Probe",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted Optional () non-CID wrapper should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));

        var probe = files.First(f => f.RelativePath.EndsWith("Probe.cs", StringComparison.Ordinal));
        probe.Content.Should().Contain(".As<global::Daml.Runtime.Data.DamlUnit>()");
        probe.Content.Should().NotContain("default! /* TODO: Implement deserialization for unit");
    }

    [Fact]
    public void Emitted_non_contract_choice_wrappers_compile_for_decimal_record_and_unit_returns()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Oracle",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetTrailingTwap",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                        },
                        new DamlChoice
                        {
                            Name = "ComputeReport",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeRef("", "Test.Module", "Report"),
                        },
                        new DamlChoice
                        {
                            Name = "Tick",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Oracle",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Report",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("twap", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                        new DamlFieldDefinition("samples", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted non-CID wrapper code (Decimal / record / Unit returns) should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choice_exerciser_stops_compiling_without_the_ledger_extensions_using()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateTestDar(module));

        var asEmitted = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        asEmitted.Should().BeEmpty(
            "the emitted exerciser surface carries the ledger-extensions using itself, but got: {0}",
            string.Join("\n", asEmitted.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));

        var withoutLedgerExtensions = files
            .Select(f => f.IsBinary
                ? f
                : GeneratedFile.Text(
                    f.RelativePath,
                    f.Content.Replace("using Daml.Ledger.Abstractions.Extensions;", string.Empty, StringComparison.Ordinal)))
            .ToList();

        var errors = CompileEmittedFiles(withoutLedgerExtensions)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().Contain(
            d => d.Id == "CS1061"
                 && d.GetMessage(CultureInfo.InvariantCulture).Contains("TrySubmitSingleAsync", StringComparison.Ordinal),
            "the emitted exercisers reach TrySubmitSingleAsync as an extension method on ILedgerWriter, so dropping its namespace must break the build on that member specifically — any other missing-member error would satisfy a bare CS1061 gate while proving nothing, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture))));
    }

    [Fact]
    public void Emitted_submission_extensions_compile_without_the_ledger_extensions_using()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "ChoicelessAssetWithKey",
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "ChoicelessAssetWithKey",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateTestDar(module));

        var template = files.Should().ContainSingle(f => f.RelativePath.EndsWith("ChoicelessAssetWithKey.cs", StringComparison.Ordinal)).Subject;

        template.Content.Should()
            .Contain("public static class ChoicelessAssetWithKeySubmissionExtensions",
                "a choice-free template emits the typed-submitter surface and nothing that exercises a choice")
            .And.NotContain("using Daml.Ledger.Abstractions.Extensions;",
                "the emitted TryCreateAsync reaches SingleCommandExtensions.TryCreateAsync by its global-qualified name, so the typed-submitter surface needs no extension-method namespace");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "the typed-submitter surface must compile without the namespace the exercisers require, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_by_key_exercisers_compile_for_named_controllers_arguments_and_tuple_keys()
    {
        var errors = CompileEmittedFiles(GenerateTupleKeyedTemplateWithStaticControllers())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "the Try<Choice>ByKeyAsync exercisers of a tuple-keyed template with static controllers must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_by_key_exercisers_have_no_doc_diagnostics()
    {
        var docDiagnostics = CompileEmittedFilesWithDocDiagnostics(GenerateTupleKeyedTemplateWithStaticControllers())
            .Where(d => d.Id is "CS1570" or "CS1572" or "CS1573" or "CS1574" or "CS1580" or "CS1584" or "CS1658")
            .ToList();

        docDiagnostics.Should().BeEmpty(
            "the by-key exercisers carry crefs, a remarks block and param docs that a consumer building with GenerateDocumentationFile compiles, but got: {0}",
            string.Join("\n", docDiagnostics.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_by_key_exercisers_emit_one_named_overload_per_static_controller_choice_beside_the_submitter_overload()
    {
        var files = GenerateTupleKeyedTemplateWithStaticControllers();

        var vault = files.Should().ContainSingle(f => f.RelativePath.EndsWith("Vault.cs", StringComparison.Ordinal)).Subject;

        vault.Content.Split("> TryDepositByKeyAsync(").Length.Should().Be(3, "Deposit has a static controller, so it gets a named-Party and a SubmitterInfo overload");
        vault.Content.Split("> TryAuditByKeyAsync(").Length.Should().Be(2, "Audit has a dynamic controller, so it gets only the SubmitterInfo overload");
    }

    private static IReadOnlyList<GeneratedFile> GenerateTupleKeyedTemplateWithStaticControllers()
    {
        var partyType = new DamlPrimitiveType(DamlPrimitive.Party);
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Vault",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Deposit",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "DepositArg"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner"), new DamlPartyPayloadField("agent")]),
                        },
                        new DamlChoice
                        {
                            Name = "Audit",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Dynamic,
                        },
                        new DamlChoice
                        {
                            Name = "Rotate",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Vault"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                        },
                    ],
                    Key = TupleType(partyType, new DamlPrimitiveType(DamlPrimitive.Text)),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DepositArg",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64))]),
                },
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", partyType),
                        new DamlFieldDefinition("agent", partyType),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var options = new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true };
        return CreateGenerator(options).Generate(new DarModel { MainPackage = package, Dependencies = [Daml.Codegen.CSharp.Tests.TestHelpers.TestPackages.DamlPrim()] });
    }
}
