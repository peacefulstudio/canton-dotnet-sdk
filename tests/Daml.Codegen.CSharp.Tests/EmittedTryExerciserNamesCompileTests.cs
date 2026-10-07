// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins the runtime naming rule on the generated surface: a method returning
/// <c>Task&lt;ExerciseOutcome&lt;T&gt;&gt;</c> is named <c>Try…Async</c>, so a caller never
/// reads an outcome-returning exerciser as one that throws on failure. Covers every emitter
/// that produces such a method — the create helper, the create-bearing contract-id and
/// contract exercisers, the non-contract exercisers (including the synthetic stdlib
/// <c>Archive</c>), the interface exercisers — on a keyless template, a keyed template and an
/// interface, plus choices literally named <c>Create</c> and <c>Try…</c>.
/// </summary>
public class EmittedTryExerciserNamesCompileTests
{
    private const string StdlibPackageId = "daml-prim-pkg-id";

    [Fact]
    public void ExerciseOutcome_returning_methods_are_exactly_the_Try_prefixed_set()
    {
        var compilation = CompileEmittedFilesToCompilation(Generate(), DocumentationMode.Parse);

        OutcomeReturningMethodNames(compilation).Should().BeEquivalentTo(
        [
            "TryArchiveAsync",
            "TryCreateAsync",
            "TryDescribeAsync",
            "TryFreezeAsync",
            "TryRedeemAsync",
            "TryReissueAsync",
            "TryTransferAsync",
            "TryTryRedeemAsync",
        ]);
    }

    [Fact]
    public void Try_prefixed_call_sites_compile_against_the_emitted_surface()
    {
        var files = Generate();
        var diagnostics = CompileEmittedFiles([.. files, CallSites()]);

        diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Should().BeEmpty();
    }

    private static IReadOnlyList<string> OutcomeReturningMethodNames(Compilation compilation) =>
        [.. compilation.SyntaxTrees
            .SelectMany(tree => DeclaredMethods(compilation.GetSemanticModel(tree), tree))
            .Where(ReturnsTaskOfExerciseOutcome)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)];

    private static bool ReturnsTaskOfExerciseOutcome(IMethodSymbol method) =>
        method.ReturnType is INamedTypeSymbol { Name: "Task", TypeArguments: [INamedTypeSymbol { Name: "ExerciseOutcome" }] };

    private static IEnumerable<IMethodSymbol> DeclaredMethods(SemanticModel model, SyntaxTree tree) =>
        tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(declaration => model.GetDeclaredSymbol(declaration))
            .OfType<IMethodSymbol>();

    private static GeneratedFile CallSites() =>
        GeneratedFile.Text("TryCallSites.cs", """
            using System.Threading.Tasks;
            using Daml.Ledger.Abstractions;
            using Daml.Runtime.Commands;
            using Daml.Runtime.Contracts;
            using Daml.Runtime.Data;
            using Daml.Runtime.Outcomes;
            using Daml.Runtime.Stdlib;
            using Test.Package.Acme.Vaults;

            internal static class TryCallSites
            {
                internal static Task<ExerciseOutcome<ContractId<Iou>>> CreateKeyless(ILedgerWriter client, Iou iou) =>
                    client.TryCreateAsync(iou);

                internal static Task<ExerciseOutcome<ContractId<Vault>>> CreateKeyed(ILedgerWriter client, Vault vault) =>
                    client.TryCreateAsync(vault, new Party("alice"));

                internal static Task<ExerciseOutcome<ContractId<Iou>>> ExerciseKeylessById(ILedgerWriter client, ContractId<Iou> iouId) =>
                    iouId.TryTransferAsync(client, new Party("owner"));

                internal static Task<ExerciseOutcome<ContractId<Iou>>> ExerciseKeylessByContract(ILedgerWriter client, Contract<Iou> iou) =>
                    iou.TryTransferAsync(client);

                internal static Task<ExerciseOutcome<string>> ExerciseValueReturning(ILedgerWriter client, ContractId<Iou> iouId) =>
                    iouId.TryDescribeAsync(client, new Party("owner"));

                internal static Task<ExerciseOutcome<DamlUnit>> Archive(ILedgerWriter client, ContractId<Iou> iouId) =>
                    iouId.TryArchiveAsync(client, new Party("issuer"));

                internal static Task<ExerciseOutcome<ContractId<Vault>>> ExerciseKeyed(ILedgerWriter client, ContractId<Vault> vaultId) =>
                    vaultId.TryReissueAsync(client, new Party("alice"));

                internal static Task<ExerciseOutcome<DamlUnit>> ExerciseInterface(ILedgerWriter client, ContractId<ICustody> custodyId) =>
                    custodyId.TryFreezeAsync(client, new Party("alice"));

                internal static Task<ExerciseOutcome<ContractId<Registry>>> ExerciseChoiceNamedCreate(ILedgerWriter client, ContractId<Registry> registryId) =>
                    registryId.TryCreateAsync(client, new Party("registrar"));

                internal static Task<ExerciseOutcome<ContractId<Registry>>> CreateTemplateWithChoiceNamedCreate(ILedgerWriter client, Registry registry) =>
                    client.TryCreateAsync(registry);

                internal static Task<ExerciseOutcome<ContractId<Registry>>> ExerciseRedeem(ILedgerWriter client, ContractId<Registry> registryId) =>
                    registryId.TryRedeemAsync(client, new Party("registrar"));

                internal static Task<ExerciseOutcome<ContractId<Registry>>> ExerciseChoiceNamedTryRedeem(ILedgerWriter client, ContractId<Registry> registryId) =>
                    registryId.TryTryRedeemAsync(client, new Party("registrar"));
            }
            """);

    private static readonly DamlPackage StdlibStub = new()
    {
        PackageId = StdlibPackageId,
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    private static DamlType ContractIdIn(string templateName) => ContractIdOf("Acme.Vaults", templateName);

    private static DamlFieldDefinition PartyField(string name) => new(name, new DamlPrimitiveType(DamlPrimitive.Party));

    private static DamlChoice CreateBearing(string name, string templateName, string controller) =>
        new()
        {
            Name = name,
            Consuming = true,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = ContractIdIn(templateName),
            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField(controller)]),
            Observers = DamlPartyAnalysis.Static([]),
        };

    private static IReadOnlyList<GeneratedFile> Generate()
    {
        var module = new DamlModule
        {
            Name = "Acme.Vaults",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Iou",
                    Choices =
                    [
                        CreateBearing("Transfer", "Iou", "owner"),
                        new DamlChoice
                        {
                            Name = "Describe",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Text),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(StdlibPackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]),
                    Observers = DamlPartyAnalysis.Static([]),
                },
                new DamlTemplate
                {
                    Name = "Vault",
                    Choices = [CreateBearing("Reissue", "Vault", "keeper")],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text),
                    Signatories = DamlPartyAnalysis.Dynamic,
                    Observers = DamlPartyAnalysis.Static([]),
                },
                new DamlTemplate
                {
                    Name = "Registry",
                    Choices =
                    [
                        CreateBearing("Create", "Registry", "registrar"),
                        CreateBearing("Redeem", "Registry", "registrar"),
                        CreateBearing("TryRedeem", "Registry", "registrar"),
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("registrar")]),
                    Observers = DamlPartyAnalysis.Static([]),
                },
            ],
            DataTypes =
            [
                new DamlDataType { Name = "Iou", Definition = new DamlRecordDefinition([PartyField("issuer"), PartyField("owner")]) },
                new DamlDataType { Name = "Vault", Definition = new DamlRecordDefinition([PartyField("keeper")]) },
                new DamlDataType { Name = "Registry", Definition = new DamlRecordDefinition([PartyField("registrar")]) },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Custody",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Freeze",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Dynamic,
                            Observers = DamlPartyAnalysis.Dynamic,
                        },
                    ],
                },
            ],
        };

        var dar = new DarModel
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

        return CreateGenerator(new CodeGenOptions
        {
            NamespacePrefix = "Test.Package",
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
        }).Generate(dar);
    }
}
