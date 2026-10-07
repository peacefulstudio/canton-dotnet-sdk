// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Compile-level regression coverage for a reported CS0117 build failure:
/// the two Daml.Finance interface families (<c>daml-finance-interface-holding-v4</c>,
/// <c>daml-finance-interface-instrument-base-v4</c>) failed to pack because a
/// <c>Reference</c> template choice returning <c>ContractId Factory</c> /
/// <c>ContractId Instrument</c> (a local Daml interface) made the generated
/// choice projector read <c>IFactory.TemplateId</c> — an interface marker exposes no
/// <c>TemplateId</c>. The projector now hands the choice descriptor to the runtime's
/// exercise-result projection and reads no identity off the marker; these tests keep the
/// exact reported shapes, and the optional/list cardinalities, compiling.
/// </summary>
public class InterfaceChoiceResultCs0117RegressionTests
{
    private static readonly DamlPackage DamlPrim = new()
    {
        PackageId = "daml-prim",
        Name = "daml-prim",
        Version = new Version(0, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    private static DamlType ContractIdOf(string module, string name) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef("", module, name)]);

    private static DamlType ListOf(DamlType inner) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.List), [inner]);

    private static DarModel ReferenceHoldingLocalInterface(
        string packageName,
        string moduleName,
        string interfaceName,
        DamlType getCidReturnType)
    {
        var module = new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Reference",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetCid",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = getCidReturnType,
                        },
                        new DamlChoice
                        {
                            Name = "SetCid",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf(moduleName, "Reference"),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Reference",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("cid", ContractIdOf(moduleName, interfaceName))]),
                },
                new DamlDataType { Name = interfaceName, Definition = new DamlRecordDefinition([]) },
                new DamlDataType
                {
                    Name = "View",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [new DamlInterface { Name = interfaceName, ViewType = new DamlTypeRef("", moduleName, "View"), Choices = [] }],
        };

        var package = new DamlPackage
        {
            PackageId = $"{packageName}-id",
            Name = packageName,
            Version = new Version(4, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        return new DarModel { MainPackage = package, Dependencies = [] };
    }

    private static void CompilesCleanly(DarModel dar, string because)
    {
        var files = CreateGenerator().Generate(dar);
        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(because + ", but got: {0}", string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_finance_holding_reference_choice_returning_local_factory_interface_cid_compiles()
    {
        var dar = ReferenceHoldingLocalInterface(
            "daml-finance-interface-holding-v4",
            "Daml.Finance.Interface.Holding.V4.Factory",
            "Factory",
            ContractIdOf("Daml.Finance.Interface.Holding.V4.Factory", "Factory"));

        CompilesCleanly(dar, "a Reference/GetCid choice returning ContractId Factory (a local interface) must compile without reading IFactory.TemplateId");
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_finance_instrument_base_reference_choice_returning_local_instrument_interface_cid_compiles()
    {
        var dar = ReferenceHoldingLocalInterface(
            "daml-finance-interface-instrument-base-v4",
            "Daml.Finance.Interface.Instrument.Base.V4.Instrument",
            "Instrument",
            ContractIdOf("Daml.Finance.Interface.Instrument.Base.V4.Instrument", "Instrument"));

        CompilesCleanly(dar, "a Reference/GetCid choice returning ContractId Instrument (a local interface) must compile without reading IInstrument.TemplateId");
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_template_choice_returning_optional_local_interface_cid_compiles()
    {
        var module = "Daml.Finance.Interface.Holding.V4.Factory";
        var dar = ReferenceHoldingLocalInterface(
            "daml-finance-interface-holding-v4",
            module,
            "Factory",
            OptionalOf(ContractIdOf(module, "Factory")));

        CompilesCleanly(dar, "an optional interface-typed contract id result must compile");
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_template_choice_returning_list_of_local_interface_cid_compiles()
    {
        var module = "Daml.Finance.Interface.Holding.V4.Factory";
        var dar = ReferenceHoldingLocalInterface(
            "daml-finance-interface-holding-v4",
            module,
            "Factory",
            ListOf(ContractIdOf(module, "Factory")));

        CompilesCleanly(dar, "a list of interface-typed contract ids result must compile");
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_template_choice_returning_foreign_package_interface_cid_compiles()
    {
        const string foreignPackageId = "foreign-holding-pkg-id";
        var foreignModule = new DamlModule
        {
            Name = "Foreign.Holding",
            Templates = [],
            DataTypes = [new DamlDataType { Name = "Holding", Definition = new DamlRecordDefinition([]) }],
            Interfaces = [new DamlInterface { Name = "Holding", ViewType = null, Choices = [] }],
        };
        var foreignPackage = new DamlPackage
        {
            PackageId = foreignPackageId,
            Name = "foreign-holding-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
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
                            Name = "GetHolding",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef(foreignPackageId, "Foreign.Holding", "Holding")]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "main-vault-pkg-id",
            Name = "main-vault-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            IncludeDependencies = true,
        };
        var files = CreateGenerator(options).Generate(dar);
        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a choice returning ContractId of a foreign-package interface must project via the foreign marker's generated InterfaceId symbol, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_template_choice_returning_template_and_interface_cid_tuple_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Widget",
                    Choices = [],
                },
                new DamlTemplate
                {
                    Name = "Vault",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "IssueBoth",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = TupleType(ContractIdOf("Test.Module", "Widget"), ContractIdOf("Test.Module", "Factory")),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType { Name = "Widget", Definition = new DamlRecordDefinition([]) },
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType { Name = "Factory", Definition = new DamlRecordDefinition([]) },
            ],
            Interfaces = [new DamlInterface { Name = "Factory", ViewType = null, Choices = [] }],
        };

        var package = new DamlPackage
        {
            PackageId = "mixed-slot-pkg-id",
            Name = "mixed-slot-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [DamlPrim] };

        CompilesCleanly(dar, "a choice returning a tuple of a same-named template cid and a local interface cid must compile both the TemplateId and InterfaceIds projector branches");
    }

    private static DarModel TemplateMarkerCollisionDar()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "IFactory",
                    Choices = [],
                },
                new DamlTemplate
                {
                    Name = "Vault",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "IssueBoth",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = TupleType(ContractIdOf("Test.Module", "IFactory"), ContractIdOf("Test.Module", "Factory")),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType { Name = "IFactory", Definition = new DamlRecordDefinition([]) },
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType { Name = "Factory", Definition = new DamlRecordDefinition([]) },
            ],
            Interfaces = [new DamlInterface { Name = "Factory", ViewType = null, Choices = [] }],
        };

        var package = new DamlPackage
        {
            PackageId = "template-marker-collision-pkg-id",
            Name = "template-marker-collision-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        return new DarModel { MainPackage = package, Dependencies = [DamlPrim] };
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_disambiguates_template_colliding_with_interface_marker_name()
    {
        CompilesCleanly(TemplateMarkerCollisionDar(), "a template literally named IFactory and an interface Factory (whose generated marker is also IFactory) must not both declare a public IFactory type in the same namespace");
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_writes_the_disambiguated_marker_file_for_a_template_colliding_with_it()
    {
        var files = CreateGenerator().Generate(TemplateMarkerCollisionDar()).ToList();

        files.Select(f => f.RelativePath).Should().Contain(p => p.EndsWith("IFactory_.cs"));
    }

    private static DarModel RecordMarkerCollisionDar()
    {
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
                            Name = "GetFactory",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Test.Module", "Factory"),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType { Name = "IFactory", Definition = new DamlRecordDefinition([]) },
                new DamlDataType { Name = "Factory", Definition = new DamlRecordDefinition([]) },
            ],
            Interfaces = [new DamlInterface { Name = "Factory", ViewType = null, Choices = [] }],
        };

        var package = new DamlPackage
        {
            PackageId = "record-marker-collision-pkg-id",
            Name = "record-marker-collision-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        return new DarModel { MainPackage = package, Dependencies = [DamlPrim] };
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_disambiguates_record_colliding_with_interface_marker_name()
    {
        CompilesCleanly(RecordMarkerCollisionDar(), "a record literally named IFactory and an interface Factory (whose generated marker is also IFactory) must not both declare a public IFactory type in the same namespace");
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_writes_the_disambiguated_marker_file_for_a_record_colliding_with_it()
    {
        var files = CreateGenerator().Generate(RecordMarkerCollisionDar()).ToList();

        files.Select(f => f.RelativePath).Should().Contain(p => p.EndsWith("IFactory_.cs"));
    }

    private static DarModel RecordMarkerCollisionWithFirstRoundDisambiguatedTemplateDar()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "IFactory",
                    Choices = [],
                },
                new DamlTemplate
                {
                    Name = "Vault",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "IssueAll",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = TupleType(
                                ContractIdOf("Test.Module", "IFactory"),
                                ContractIdOf("Test.Module", "Factory")),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType { Name = "IFactory", Definition = new DamlRecordDefinition([]) },
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType { Name = "IFactory_", Definition = new DamlRecordDefinition([]) },
                new DamlDataType { Name = "Factory", Definition = new DamlRecordDefinition([]) },
            ],
            Interfaces = [new DamlInterface { Name = "Factory", ViewType = null, Choices = [] }],
        };

        var package = new DamlPackage
        {
            PackageId = "record-first-round-collision-pkg-id",
            Name = "record-first-round-collision-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        return new DarModel { MainPackage = package, Dependencies = [DamlPrim] };
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_disambiguates_record_colliding_with_the_first_round_disambiguated_marker()
    {
        CompilesCleanly(
            RecordMarkerCollisionWithFirstRoundDisambiguatedTemplateDar(),
            "a template IFactory, a record IFactory_, and an interface Factory (marker IFactory, then IFactory_) must each get a distinct public type in the same namespace");
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_writes_the_second_round_disambiguated_marker_file_when_both_rounds_collide()
    {
        var files = CreateGenerator().Generate(RecordMarkerCollisionWithFirstRoundDisambiguatedTemplateDar()).ToList();

        files.Select(f => f.RelativePath).Should().Contain(p => p.EndsWith("IFactory__.cs"));
    }

    // Deliberately declares each interface with no matching interface-placeholder
    // record: the real Daml-LF compiler always emits one alongside every interface,
    // but that placeholder record's own name is disambiguated only by its module (a
    // separate, pre-existing gap, not part of this marker-name fix). Omitting it
    // isolates this DAR to exactly the marker-collision family under test.
    private static DarModel TwoModuleInterfaceMarkerCollisionDar(string firstModuleName, string secondModuleName)
    {
        DamlModule InterfaceOnlyModule(string moduleName) => new()
        {
            Name = moduleName,
            Templates = [],
            DataTypes = [],
            Interfaces = [new DamlInterface { Name = "Factory", ViewType = null, Choices = [] }],
        };

        var package = new DamlPackage
        {
            PackageId = "two-module-interface-collision-pkg-id",
            Name = "two-module-interface-collision-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [InterfaceOnlyModule(firstModuleName), InterfaceOnlyModule(secondModuleName)],
            DependencyReferences = [],
        };

        return new DarModel { MainPackage = package, Dependencies = [] };
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_two_same_named_interfaces_in_different_modules_compile_cleanly_with_distinct_markers()
    {
        CompilesCleanly(
            TwoModuleInterfaceMarkerCollisionDar("Alpha.Module", "Beta.Module"),
            "two interfaces named Factory in different modules, both sanitising to marker IFactory, must not both declare a public IFactory type in the same flat namespace");
    }

    [Fact]
    public void InterfaceChoiceResultCs0117Regression_two_same_named_interfaces_in_different_modules_deterministically_assign_the_same_winner_regardless_of_module_order()
    {
        string WinnerModuleName(string firstModuleName, string secondModuleName)
        {
            var files = CreateGenerator().Generate(TwoModuleInterfaceMarkerCollisionDar(firstModuleName, secondModuleName));
            var winnerFile = files.Single(f => f.RelativePath.EndsWith("IFactory.cs", StringComparison.Ordinal));
            return winnerFile.Content.Contains("\"Alpha.Module\"", StringComparison.Ordinal) ? "Alpha.Module" : "Beta.Module";
        }

        WinnerModuleName("Alpha.Module", "Beta.Module").Should().Be("Alpha.Module");
        WinnerModuleName("Beta.Module", "Alpha.Module").Should().Be("Alpha.Module");
    }
}
