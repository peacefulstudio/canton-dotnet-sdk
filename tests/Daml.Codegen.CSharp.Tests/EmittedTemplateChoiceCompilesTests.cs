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

public class EmittedTemplateChoiceCompilesTests
{
    [Fact]
    public void Emitted_template_with_unmappable_choice_argument_fails_generation()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Process",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Text),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        FluentActions.Invoking(() => CreateGenerator().Generate(dar))
            .Should().Throw<CodegenException>(
                "a choice argument the codegen cannot map to an argument record must fail generation instead of emitting an empty stub record")
            .WithMessage("*Process*");
    }

    [Fact]
    public void Emitted_template_with_create_bearing_choice_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Agreement"),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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
            "emitted code should compile against Daml.Runtime + Daml.Ledger.Abstractions, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choice_returning_an_interface_contract_id_compiles()
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
                            Name = "IssueHoldable",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Holdable"),
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
                // Interface marker `Holdable` also surfaces as a serializable placeholder
                // record of the same name — this is what flags the type as an interface.
                new DamlDataType
                {
                    Name = "Holdable",
                    Definition = new DamlRecordDefinition([]),
                },
            ],
            Interfaces = [new DamlInterface { Name = "Holdable", Choices = [], ViewType = null }],
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
            "a choice returning an interface-typed ContractId must match created contracts by InterfaceIds, not TemplateId, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choice_returning_a_local_interface_contract_id_compiles_and_projects_the_exercise_result()
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
                            Name = "IssueHoldable",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Holdable"),
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
                new DamlDataType
                {
                    Name = "Holdable",
                    Definition = new DamlRecordDefinition([]),
                },
            ],
            Interfaces = [new DamlInterface { Name = "Holdable", Choices = [], ViewType = null }],
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
        var files = CreateGenerator().Generate(dar).ToList();

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a choice returning a local interface-typed ContractId resolves to the interface's marker, so the projector's InterfaceId reads must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));

        var code = files.First(f => f.RelativePath.EndsWith("Vault.cs", global::System.StringComparison.Ordinal)).Content;
        code.Should().Contain("tx.ProjectChoiceResult(global::Test.Module.Vault.ChoiceIssueHoldable, contractId);");
        code.Should().NotContain("InterfaceIds");
        code.Should().NotContain("CreatedContracts");
    }

    [Fact]
    public void Emitted_template_implementing_a_local_interface_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Vault",
                    Choices = [],
                    Implements = [new DamlTypeRef("", "Test.Module", "Holdable")],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Holdable",
                    Definition = new DamlRecordDefinition([]),
                },
            ],
            Interfaces = [new DamlInterface { Name = "Holdable", Choices = [], ViewType = null }],
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
        var files = CreateGenerator().Generate(dar).ToList();

        var vault = files.First(f => f.RelativePath.EndsWith("Vault.cs", global::System.StringComparison.Ordinal)).Content;
        vault.Should().Contain("global::Daml.Runtime.Contracts.IImplements<global::Test.Module.IHoldable>");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template implementing a local interface must emit IImplements<IHoldable> and satisfy the where TInterface : IDamlInterface constraint, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_create_bearing_choice_with_static_controllers_compiles_both_contractid_and_contract_overloads()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Agreement"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(fields),
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

        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;
        content.Should().Contain("this global::Daml.Runtime.Contracts.IContract<global::Daml.Runtime.Contracts.ContractId<global::Test.Module.Agreement>, global::Test.Module.Agreement> contract,");
        content.Should().Contain("return contract.Id.TryRenewAsync(");

        var consumerHoldingFromCreatedEventResult = GeneratedFile.Text(
            "ReachabilityProbe.cs",
            """
            namespace Test.Module
            {
                internal static class ReachabilityProbe
                {
                    internal static System.Threading.Tasks.Task Use(
                        global::Daml.Runtime.Contracts.Contract<Agreement> contract,
                        global::Daml.Ledger.Abstractions.ILedgerClient client) =>
                        contract.TryRenewAsync(client);
                }
            }
            """);

        var diagnostics = CompileEmittedFiles([.. files, consumerHoldingFromCreatedEventResult]);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the payload-bearing overload must be reachable from the runtime Contract<Agreement> (the type FromCreatedEvent returns), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_contract_overload_with_record_argument_and_multiple_controllers_compiles()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("buyer", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("seller", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("broker", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Settle",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "SettleArgs"),
                            ReturnType = ContractIdOf("Agreement"),
                            Controllers = DamlPartyAnalysis.Static(
                                [new DamlPartyPayloadField("buyer"), new DamlPartyPayloadField("seller")]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("buyer")]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("broker")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(fields),
                },
                new DamlDataType
                {
                    Name = "SettleArgs",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("memo", new DamlPrimitiveType(DamlPrimitive.Text))]),
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

        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;
        content.Should().Contain("this global::Daml.Runtime.Contracts.IContract<global::Daml.Runtime.Contracts.ContractId<global::Test.Module.Agreement>, global::Test.Module.Agreement> contract,");
        content.Should().Contain("return contract.Id.TrySettleAsync(");
        var idxArg = content.IndexOf("return contract.Id.TrySettleAsync(", global::System.StringComparison.Ordinal);
        var delegateBody = content[idxArg..];
        var idxArgument = delegateBody.IndexOf("argument,", global::System.StringComparison.Ordinal);
        var idxBuyer = delegateBody.IndexOf("contract.Data.Buyer,", global::System.StringComparison.Ordinal);
        var idxSeller = delegateBody.IndexOf("contract.Data.Seller,", global::System.StringComparison.Ordinal);
        idxArgument.Should().BeGreaterThan(0);
        idxArgument.Should().BeLessThan(idxBuyer);
        idxBuyer.Should().BeLessThan(idxSeller);

        var consumerHoldingFromCreatedEventResult = GeneratedFile.Text(
            "ReachabilityProbe.cs",
            """
            namespace Test.Module
            {
                internal static class ReachabilityProbe
                {
                    internal static System.Threading.Tasks.Task Use(
                        global::Daml.Runtime.Contracts.Contract<Agreement> contract,
                        global::Daml.Ledger.Abstractions.ILedgerClient client) =>
                        contract.TrySettleAsync(client, default!);
                }
            }
            """);

        var diagnostics = CompileEmittedFiles([.. files, consumerHoldingFromCreatedEventResult]);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the contract overload forwarding a record argument and multiple controllers should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_template_with_payload_derived_observers_compiles()
    {
        // End-to-end: template with payload-derived signatories, controllers,
        // AND observers. The codegen should emit:
        //   - SubmissionExtensions.TryCreateAsync (payload-only)
        //   - SubmissionExtensions.Observers(payload) doc helper
        //   - <Template>Extensions.<Choice>Async with Party params for both
        //     controllers (actAs) and non-controller observers (readAs)
        //   - SubmitterInfo built locally with both actAs and readAs
        //   - submitter passed straight to TrySubmitAndWaitForTransactionAsync
        // All three concerns compile cleanly against the real
        // Daml.Runtime + Daml.Ledger.Abstractions surface.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Agreement"),
                            Controllers = DamlPartyAnalysis.Static(
                                [new DamlPartyPayloadField("platform")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                        [new DamlPartyPayloadField("platform")]),
                    Observers = DamlPartyAnalysis.Static(
                        [new DamlPartyPayloadField("holder"), new DamlPartyPayloadField("issuer")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("holder", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
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
            "observer-aware emitted code should compile cleanly, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
