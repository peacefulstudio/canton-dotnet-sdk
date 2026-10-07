// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Codegen-shape tests for typed TryCreateAsync / Try&lt;Choice&gt;Async with one
/// parameter per signatory / controller. The static analyzer in the
/// <c>DarReader</c> namespace walks the Daml-LF expression tree; in unit
/// tests we pre-build the analysis directly on the model classes (bypassing
/// the proto layer) and assert on the emitted source.
///
/// <para>
/// The assertions focus on the public surface (signatures, parameter names,
/// presence/absence of payload-derived <c>actAs</c>) rather than the internal
/// command-construction wording, which can be polished without breaking
/// consumers.
/// </para>
/// </summary>
public class NamedSubmitterTests
{
    private static readonly DamlPackage StdlibStub = new()
    {
        PackageId = "daml-prim-pkg-id",
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    private static DarModel CreateDar(DamlModule module) =>
        new()
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [StdlibStub],
        };

    /// <summary>
    /// Helper for the common shape: a template with three Party signatories,
    /// all referenced as payload fields. Mirrors the canonical Acme
    /// <c>Agreement</c> template (<c>signatory platform, initiator, counterparty</c>).
    /// </summary>
    private static DamlModule MakeAgreementModule(DamlPartyAnalysis signatories, DamlPartyAnalysis? archiveControllers = null)
    {
        var fields = new[]
        {
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("initiator", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("totalAmount", new DamlPrimitiveType(DamlPrimitive.Numeric)),
        };

        return new DamlModule
        {
            Name = "Acme.Agreements",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Choices = [],
                    Signatories = signatories,
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(fields),
                }
            ],
            Interfaces = [],
        };
    }

    #region TryCreateAsync — payload-derived signatories

    [Fact]
    public void TryCreateAsync_with_payload_derived_signatories_omits_actAs_parameter()
    {
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("platform"),
            new DamlPartyPayloadField("initiator"),
            new DamlPartyPayloadField("counterparty"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Public surface: extension class, payload-only TryCreateAsync.
        content.Should().Contain("public static class AgreementSubmissionExtensions");
        content.Should().Contain("public static global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<Agreement>>> TryCreateAsync(");
        content.Should().Contain("this global::Daml.Ledger.Abstractions.ILedgerWriter client,");
        content.Should().Contain("Agreement payload,");
        // No explicit actAs parameter — the payload is sufficient.
        content.Should().NotContain("string actAs,");
    }

    [Fact]
    public void TryCreateAsync_with_payload_derived_signatories_unions_payload_party_fields_into_submitter()
    {
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("platform"),
            new DamlPartyPayloadField("initiator"),
            new DamlPartyPayloadField("counterparty"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Each payload-derived party becomes a payload-property reference inside
        // the SubmitterInfo's HashSet<Party>. We assert on the property names
        // (PascalCased) rather than the surrounding HashSet boilerplate, which
        // could be polished later without breaking consumers.
        content.Should().Contain("new global::Daml.Runtime.Commands.SubmitterInfo(new global::System.Collections.Generic.HashSet<global::Daml.Runtime.Data.Party>");
        content.Should().Contain("payload.Platform");
        content.Should().Contain("payload.Initiator");
        content.Should().Contain("payload.Counterparty");
        content.Should().Contain("SingleCommandExtensions.TryCreateAsync<Agreement>(client, payload, submitter");
    }

    [Fact]
    public void TryCreateAsync_with_single_payload_derived_signatory_passes_party_directly()
    {
        // Single-signatory templates don't allocate a HashSet — the wrapper
        // passes the Party value, relying on the implicit conversion to
        // SubmitterInfo.
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("platform"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Single-party fast-path: SubmitterInfo submitter = payload.Platform;
        content.Should().Contain("SubmitterInfo submitter = payload.Platform;");
        content.Should().NotContain("new global::System.Collections.Generic.HashSet<global::Daml.Runtime.Data.Party>");
    }

    [Fact]
    public void TryCreateAsync_with_dynamic_signatories_keeps_explicit_submitter_parameter()
    {
        // Dynamic = the analyzer couldn't resolve the signatory expression to
        // payload-field references. Codegen falls back to an explicit
        // SubmitterInfo parameter (which preserves single-party ergonomics
        // via implicit conversion from string/Party).
        var module = MakeAgreementModule(DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().Contain("public static global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<Agreement>>> TryCreateAsync(");
        content.Should().Contain("SubmitterInfo submitter,");
        // No payload-derived `var submitter = ...` line.
        content.Should().NotContain("payload.Platform,");
    }

    [Fact]
    public void TryCreateAsync_with_unresolvable_payload_field_falls_back_to_dynamic()
    {
        // Analyzer claims `payload.unknownField` but no such field exists.
        // Codegen must demote to Dynamic — emitting `payload.UnknownField`
        // would not compile against the generated record.
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("unknownField"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Demoted to dynamic — explicit submitter parameter, no bogus field.
        content.Should().Contain("SubmitterInfo submitter,");
        content.Should().NotContain("payload.UnknownField");
    }

    [Fact]
    public void TryCreateAsync_emits_extension_method_taking_ILedgerWriter_as_this()
    {
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("platform"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // The wrapper is an extension method on ILedgerWriter — the call site
        // reads `client.TryCreateAsync(payload)`.
        content.Should().Contain("this global::Daml.Ledger.Abstractions.ILedgerWriter client");
    }

    #endregion

    #region Try<Choice>Async — payload-derived controllers

    [Fact]
    public void TryChoiceAsync_with_single_payload_derived_controller_emits_one_party_parameter()
    {
        // The typed-controller <Choice>Async surface is emitted on the
        // sibling <TemplateName>Extensions class (ChoiceEmitter.ContractIdExercisers.cs)
        // — its method takes one named Party parameter per declared
        // controller. Accept's controller list is `[counterparty]`, so the
        // wrapper signature carries a single `Party counterparty` parameter
        // and no fallback `SubmitterInfo submitter`.
        var module = new DamlModule
        {
            Name = "Acme.Agreements",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Offer",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Accept",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Acme.Agreements", "Agreement")]),
                            Controllers = DamlPartyAnalysis.Static(
                                [new DamlPartyPayloadField("counterparty")]),
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                    [
                        new DamlPartyPayloadField("platform"),
                        new DamlPartyPayloadField("counterparty"),
                    ]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Offer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition([]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var offer = files.First(f => f.RelativePath.EndsWith("Offer.cs", global::System.StringComparison.Ordinal)).Content;

        // The choice has a single Party-typed controller (counterparty). The
        // ergonomic wrapper carries one named Party parameter — no string actAs.
        // A readAs-capable SubmitterInfo overload is emitted alongside it, so a
        // submitter that must read contracts it does not act as stays expressible.
        offer.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement>>> TryAcceptAsync(");
        offer.Should().Contain("Party counterparty,");
        offer.Should().NotContain("string actAs,");
        offer.Should().Contain("SubmitterInfo submitter,");

        // Every emitted controller Party parameter carries a matching XML doc
        // <param> tag, or a doc-generating consumer project fails with CS1573.
        offer.Should().Contain("/// <param name=\"counterparty\">");
    }

    [Fact]
    public void TryChoiceAsync_with_multiple_payload_derived_controllers_emits_one_party_per_controller()
    {
        // A choice declared `controller initiator, counterparty` should accept
        // both parties as separate Party arguments. The typed-result
        // <Choice>Async wrapper (sibling <TemplateName>Extensions class) is
        // emitted only for create-bearing choices, so the test choice's return
        // type is a list of created contracts.
        var module = new DamlModule
        {
            Name = "Acme.Agreements",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Cancel",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Acme.Agreements", "Agreement")]),
                            Controllers = DamlPartyAnalysis.Static(
                            [
                                new DamlPartyPayloadField("initiator"),
                                new DamlPartyPayloadField("counterparty"),
                            ]),
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                    [
                        new DamlPartyPayloadField("platform"),
                        new DamlPartyPayloadField("initiator"),
                        new DamlPartyPayloadField("counterparty"),
                    ]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("initiator", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Both controllers surface as named parameters in declaration order.
        content.Should().Contain("Party initiator,");
        content.Should().Contain("Party counterparty,");
        // Order: initiator must appear before counterparty in the signature.
        var idxInit = content.IndexOf("Party initiator,", global::System.StringComparison.Ordinal);
        var idxCp = content.IndexOf("Party counterparty,", global::System.StringComparison.Ordinal);
        idxInit.Should().BeLessThan(idxCp);
        // SubmitterInfo unions both controllers in actAs.
        content.Should().Contain("new global::Daml.Runtime.Commands.SubmitterInfo(new global::System.Collections.Generic.HashSet<global::Daml.Runtime.Data.Party> { initiator, counterparty });");
    }

    [Fact]
    public void TryChoiceAsync_with_dynamic_controllers_keeps_explicit_submitter_parameter()
    {
        // When the analyzer can't resolve controllers (e.g. they reference the
        // choice argument), codegen falls back to an explicit SubmitterInfo
        // parameter — preserving single-party callers via implicit conversion.
        var module = new DamlModule
        {
            Name = "Test",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Dynamic,
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Dynamic,
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Holding.cs", global::System.StringComparison.Ordinal)).Content;

        // Both surfaces fall back to the explicit submitter shape.
        content.Should().Contain("SubmitterInfo submitter,");
    }

    [Fact]
    public void TryChoiceAsync_for_archive_choice_is_emitted()
    {
        var module = new DamlModule
        {
            Name = "Test",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(
                                StdlibStub.PackageId,
                                "DA.Internal.Template",
                                "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                        [new DamlPartyPayloadField("owner")]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Asset.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().Contain("public static class AssetSubmissionExtensions");
        content.Should().Contain("public static global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<Asset>>> TryCreateAsync(");
        content.Should().Contain("public static class AssetNonContractExtensions");
        content.Should().Contain("TryArchiveAsync(");
        content.Should().Contain("DamlRecord.Create()");
    }

    #endregion

    #region Try<Choice>Async — Contract&lt;T&gt; sibling overload

    [Fact]
    public void TryChoiceAsync_with_static_controllers_emits_contract_sibling_overload()
    {
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("holder")]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement> contractId,");
        content.Should().Contain("this global::Daml.Runtime.Contracts.IContract<global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement>, global::Acme.Agreements.Agreement> contract,");
        content.Should().Contain("return contract.Id.TryRenewAsync(");
        content.Should().Contain("global::System.ArgumentNullException.ThrowIfNull(client);");
        content.Should().Contain("contract.Data.Platform,");
        content.Should().Contain("contract.Data.Holder,");
        content.Should().Contain("contract.Data.Issuer,");
        var idxController = content.IndexOf("contract.Data.Platform,", global::System.StringComparison.Ordinal);
        var idxObserver = content.IndexOf("contract.Data.Holder,", global::System.StringComparison.Ordinal);
        idxController.Should().BeLessThan(idxObserver);
    }

    [Fact]
    public void TryChoiceAsync_contract_sibling_passes_command_id_through()
    {
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("holder")]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().Contain("this global::Daml.Runtime.Contracts.IContract<global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement>, global::Acme.Agreements.Agreement> contract,");
        var idxContractParam = content.IndexOf("this global::Daml.Runtime.Contracts.IContract<global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement>, global::Acme.Agreements.Agreement> contract,", global::System.StringComparison.Ordinal);
        var idxDelegate = content.IndexOf("return contract.Id.TryRenewAsync(", global::System.StringComparison.Ordinal);
        idxDelegate.Should().BeGreaterThan(0);
        content[idxContractParam..idxDelegate].Should().Contain("CommandId? commandId = null,");
        var delegateBody = content[idxDelegate..];
        var idxWorkflowId = delegateBody.IndexOf("workflowId,", global::System.StringComparison.Ordinal);
        var idxCommandId = delegateBody.IndexOf("commandId,", global::System.StringComparison.Ordinal);
        var idxCancellationToken = delegateBody.IndexOf("cancellationToken);", global::System.StringComparison.Ordinal);
        idxWorkflowId.Should().BeLessThan(idxCommandId);
        idxCommandId.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void TryChoiceAsync_with_dynamic_controllers_does_not_emit_contract_sibling_overload()
    {
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("holder")]),
            choiceControllers: DamlPartyAnalysis.Dynamic,
            choiceObservers: DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement> contractId,");
        content.Should().NotContain("this global::Daml.Runtime.Contracts.IContract<global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement>, global::Acme.Agreements.Agreement> contract,");
    }

    [Fact]
    public void TryChoiceAsync_contract_sibling_passes_choice_argument_through()
    {
        var module = new DamlModule
        {
            Name = "Acme.Agreements",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Offer",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Accept",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("test-pkg", "Acme.Agreements", "AcceptArgs"),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Acme.Agreements", "Agreement")]),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Offer",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "AcceptArgs",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("memo", new DamlPrimitiveType(DamlPrimitive.Text))]),
                },
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition([]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Offer.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().Contain("this global::Daml.Runtime.Contracts.IContract<global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Offer>, global::Acme.Agreements.Offer> contract,");
        content.Should().Contain("Offer.Accept argument,");
        content.Should().Contain("global::System.ArgumentNullException.ThrowIfNull(argument);");
        var idxArg = content.IndexOf("return contract.Id.TryAcceptAsync(", global::System.StringComparison.Ordinal);
        idxArg.Should().BeGreaterThan(0);
        var delegateBody = content[idxArg..];
        delegateBody.Should().Contain("argument,");
        delegateBody.Should().Contain("contract.Data.Counterparty,");
    }

    #endregion

    #region Mixed signatory shapes

    [Fact]
    public void Generate_brings_in_Daml_Ledger_Abstractions_using_for_named_submitter_extensions()
    {
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
            [new DamlPartyPayloadField("platform")]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().Contain("using Daml.Ledger.Abstractions;");
        content.Should().NotContain(".Grpc.Client;");
    }

    #endregion

    #region Observer wiring (template- and choice-level)

    /// <summary>
    /// Builds a fixture template with three Party fields and configurable
    /// signatory / observer / controller analyses. Used by the observer-wiring
    /// tests below to construct minimal DARs that exercise the full
    /// payload-derived submitter code path.
    /// </summary>
    private static DamlModule MakeAgreementWithObservers(
        DamlPartyAnalysis signatories,
        DamlPartyAnalysis templateObservers,
        DamlPartyAnalysis choiceControllers,
        DamlPartyAnalysis choiceObservers)
    {
        var fields = new[]
        {
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("holder", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        return new DamlModule
        {
            Name = "Acme.Agreements",
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
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Acme.Agreements", "Agreement")]),
                            Controllers = choiceControllers,
                            Observers = choiceObservers,
                        }
                    ],
                    Signatories = signatories,
                    Observers = templateObservers,
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(fields),
                }
            ],
            Interfaces = [],
        };
    }

    [Fact]
    public void Generate_emits_observers_helper_for_static_template_observer()
    {
        // Template with `observer holder, issuer` (both payload-field refs).
        // The SubmissionExtensions class should expose an
        // Observers(payload) helper that returns the derived party set.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static(
                [new DamlPartyPayloadField("holder"), new DamlPartyPayloadField("issuer")]),
            choiceControllers: DamlPartyAnalysis.Dynamic,
            choiceObservers: DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Helper signature plus payload-derived body — declaration order preserved.
        content.Should().Contain("public static global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Data.Party> Observers(Agreement payload)");
        content.Should().Contain("payload.Holder");
        content.Should().Contain("payload.Issuer");
        // Helper returns a Party[] literal, not a SubmitterInfo.
        content.Should().Contain("return new global::Daml.Runtime.Data.Party[]");
    }

    [Fact]
    public void Generate_does_not_emit_observers_helper_for_dynamic_observer_expression()
    {
        // Dynamic observer expression — codegen can't statically derive the
        // observer set, so emitting a payload-only helper would either lie
        // (omit non-payload observers) or throw at runtime. Skip emission.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Dynamic,
            choiceControllers: DamlPartyAnalysis.Dynamic,
            choiceObservers: DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // No documentation helper — caller is on the hook for figuring out
        // the observer set themselves, just as they are for the actAs set
        // when signatories are dynamic.
        content.Should().NotContain("Observers(Agreement payload)");
    }

    [Fact]
    public void Generate_does_not_emit_observers_helper_for_static_empty_observer_list()
    {
        // Daml's `observer []` literal — a deliberate "no observers" — resolves
        // statically but with an empty parties list. Emitting a helper that
        // always returns [] would be noise; skip it.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([]),
            choiceControllers: DamlPartyAnalysis.Dynamic,
            choiceObservers: DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().NotContain("Observers(Agreement payload)");
    }

    [Fact]
    public void Generate_choice_async_threads_template_observers_into_readAs()
    {
        // Template observers `holder, issuer` plus controllers `[platform]`.
        // The choice async wrapper must:
        // - emit Party platform (the controller),
        // - emit Party holder, Party issuer (observers, which become readAs),
        // - build a SubmitterInfo with actAs={platform} and readAs={holder,issuer}.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static(
                [new DamlPartyPayloadField("holder"), new DamlPartyPayloadField("issuer")]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Method signature carries the controller and both observer parties.
        content.Should().Contain("Party platform,");
        content.Should().Contain("Party holder,");
        content.Should().Contain("Party issuer,");
        // Body builds a SubmitterInfo that routes platform into actAs and
        // holder/issuer into readAs.
        content.Should().Contain("actAs: new global::System.Collections.Generic.HashSet<global::Daml.Runtime.Data.Party> { platform }");
        content.Should().Contain("readAs: new global::System.Collections.Generic.HashSet<global::Daml.Runtime.Data.Party> { holder, issuer }");
        content.Should().Contain("client." + TrySubmitSingleArgumentOrder);
    }

    [Fact]
    public void Generate_choice_async_unions_choice_level_observer_into_readAs()
    {
        // Choice-level `observer issuer` adds issuer to the effective readAs.
        // When combined with template-level observer `holder`, the union is
        // {holder, issuer} (deduplicated), and any party already in actAs
        // (e.g. the controller) is excluded from readAs — the wire format
        // reflects act-as authorisation cleanly.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("holder")]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Both observer-only parties surface as readAs entries (declaration order:
        // template-level first, then choice-level).
        content.Should().Contain("readAs: new global::System.Collections.Generic.HashSet<global::Daml.Runtime.Data.Party> { holder, issuer }");
    }

    [Fact]
    public void Generate_choice_async_with_no_observers_emits_no_readAs_contribution()
    {
        // No observers anywhere. The wrapper still uses SubmitterInfo (and the
        // SubmitterInfo overload on ILedgerWriter) — readAs stays empty by
        // construction. The single-controller fast-path lets us pass the Party
        // directly via implicit conversion.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // Single-controller fast-path: SubmitterInfo derived directly from
        // the named Party param (no HashSet allocation, no readAs argument).
        content.Should().Contain("SubmitterInfo submitter = platform;");
        content.Should().NotContain("readAs:");
        content.Should().Contain("client." + TrySubmitSingleArgumentOrder);
    }

    [Fact]
    public void Generate_choice_async_with_static_controllers_also_emits_readAs_capable_submitter_overload()
    {
        // The ergonomic named-Party overload is an addition, not a replacement.
        // A choice whose created contracts are visible to an observer but not the
        // submitter can only be exercised when the caller supplies a full
        // SubmitterInfo (actAs + readAs). The static-controller wrapper must emit
        // both a named-Party overload and a SubmitterInfo overload on the
        // ContractId<T> receiver.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        content.Should().Contain("Party platform,");
        content.Should().Contain("SubmitterInfo submitter,");
        content.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        var contractIdOverloads = content
            .Split("TryRenewAsync(\n        this global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement> contractId,")
            .Length - 1;
        contractIdOverloads.Should().Be(2);
        content.Should().Contain("public static global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement>>> TryRenewAsync(");
        content.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<global::Acme.Agreements.Agreement>>> TryRenewAsync(");
    }

    [Fact]
    public void Generate_choice_async_with_observer_subset_of_controllers_does_not_duplicate_readAs()
    {
        // Edge case: an observer party that's also a controller. Daml allows
        // this (a signatory can also be observed, etc.) but the readAs set
        // shouldn't duplicate parties already in actAs — the analyzer/codegen
        // dedupes via the partition step.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", global::System.StringComparison.Ordinal)).Content;

        // platform is already in actAs as the controller — no separate
        // readAs param, no readAs entry, the single-controller fast-path
        // takes over.
        content.Should().Contain("SubmitterInfo submitter = platform;");
        content.Should().NotContain("readAs:");
    }

    #endregion
}
