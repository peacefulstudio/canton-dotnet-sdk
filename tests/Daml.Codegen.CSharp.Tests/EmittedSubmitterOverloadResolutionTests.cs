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
/// Compiles the emitted surface together with hand-written call sites that pass a
/// multi-party <c>SubmitterInfo</c> to every generated <c>&lt;Choice&gt;Async</c>
/// flavour — the create-bearing <c>ContractId&lt;T&gt;</c> exerciser, its
/// <c>IContract&lt;ContractId&lt;T&gt;, T&gt;</c> sibling, the value-returning exerciser, and the interface
/// exerciser. A missing overload fails the compilation with CS1503/CS1501, and an
/// overload that collides with the ergonomic named-<c>Party</c> shape fails it with
/// CS0121 — neither of which a string-shape assertion or a declaration-only compile
/// gate would catch.
/// </summary>
public class EmittedSubmitterOverloadResolutionTests
{
    [Fact]
    public void Every_choice_async_flavour_accepts_a_readAs_bearing_SubmitterInfo()
    {
        var files = GenerateOfferModule();
        var diagnostics = CompileEmittedFiles([.. files, CallSites(files)]);

        diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Should().BeEmpty();
    }

    [Fact]
    public void Named_party_call_sites_still_resolve_alongside_the_SubmitterInfo_overloads()
    {
        var files = GenerateOfferModule();
        var diagnostics = CompileEmittedFiles([.. files, NamedPartyCallSites(files)]);

        diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("PartyPositional_CreateBearingByContractId", "Party")]
    [InlineData("PartyPositional_ValueReturning", "SubmitterInfo")]
    [InlineData("PartyPositional_InterfaceChoice", "SubmitterInfo")]
    [InlineData("SubmitterPositional_CreateBearingByContractId", "SubmitterInfo")]
    [InlineData("SubmitterPositional_CreateBearingByContract", "SubmitterInfo")]
    [InlineData("SubmitterPositional_ValueReturning", "SubmitterInfo")]
    [InlineData("SubmitterPositional_InterfaceChoice", "SubmitterInfo")]
    [InlineData("PartyPositional_ObserverBearing_AllPartiesNamed", "Party")]
    public void Positional_call_binds_to_the_overload_matching_its_submitter_argument(
        string callSite,
        string expectedSubmitterParameterType)
    {
        BoundSubmitterParameterType(callSite).Should().Be(
            expectedSubmitterParameterType,
            "an overload reachable only through the implicit Party-to-SubmitterInfo conversion "
            + "would be decorative, and a Party call site silently rebound to the SubmitterInfo "
            + "overload would drop the observer-derived readAs the named-Party shape builds");
    }

    [Fact]
    public void Observer_bearing_choice_called_with_the_controller_alone_falls_through_to_SubmitterInfo()
    {
        BoundSubmitterParameterType("PartyPositional_ObserverBearing_ControllerOnly")
            .Should().Be(
                "SubmitterInfo",
                "the named-Party overload of an observer-bearing choice takes one Party per controller "
                + "and one per observer, none optional, so a call naming only the controller is not "
                + "applicable to it and lands on the SubmitterInfo overload with an empty readAs — "
                + "pinned rather than fixed, because it predates the overloads added here and the "
                + "readAs a caller did not name is not one codegen can invent");
    }

    private static string BoundSubmitterParameterType(string callSiteMethodName)
    {
        var files = GenerateOfferModule();
        var compilation = CompileEmittedFilesToCompilation(
            [.. files, CallSites(files), NamedPartyCallSites(files)],
            DocumentationMode.Parse);

        var declarations = compilation.SyntaxTrees
            .Select(tree => (tree, declaration: tree.GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .SingleOrDefault(m => m.Identifier.ValueText == callSiteMethodName)))
            .Where(pair => pair.declaration is not null)
            .ToList();

        var (callerTree, callerDeclaration) = declarations.Should().ContainSingle().Subject;

        var invocation = callerDeclaration!.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(i => i.Expression is MemberAccessExpressionSyntax access
                         && access.Name.Identifier.ValueText.EndsWith("Async", StringComparison.Ordinal));

        var bound = compilation.GetSemanticModel(callerTree).GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        bound.Should().NotBeNull($"'{callSiteMethodName}' must bind to exactly one emitted overload");

        return SubmitterParameterLocatedByTypeNotName(bound!).Type.Name;
    }

    private static IParameterSymbol SubmitterParameterLocatedByTypeNotName(IMethodSymbol overload) =>
        overload.Parameters.First(p => p.Type.Name is "Party" or "SubmitterInfo");

    [Fact]
    public void Every_submitter_parameter_carries_a_matching_param_doc_tag()
    {
        var docDiagnostics = CompileEmittedFilesWithDocDiagnostics(GenerateOfferModule())
            .Where(d => d.Id is "CS1572" or "CS1573")
            .Select(d => d.ToString())
            .ToList();

        docDiagnostics.Should().BeEmpty(
            "a submitter parameter whose <param> tag still names the other overload's parameter "
            + "fails a doc-generating consumer project with CS1572/CS1573");
    }

    private static GeneratedFile CallSites(IReadOnlyList<GeneratedFile> files) =>
        GeneratedFile.Text("CallSites.cs", $$"""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Daml.Ledger.Abstractions;
            using Daml.Runtime.Commands;
            using Daml.Runtime.Contracts;
            using Daml.Runtime.Data;
            using {{GeneratedNamespace(files)}};

            internal static class SubmitterInfoCallSites
            {
                private static SubmitterInfo ReadAsBearing() => new SubmitterInfo(
                    actAs: new HashSet<Party> { new Party("alice") },
                    readAs: new HashSet<Party> { new Party("bob") });

                internal static Task SubmitterPositional_CreateBearingByContractId(ILedgerWriter client, ContractId<Offer> contractId) =>
                    contractId.TryAcceptAsync(client, ReadAsBearing());

                internal static Task SubmitterPositional_CreateBearingByContract(ILedgerWriter client, Contract<Offer> contract) =>
                    contract.TryAcceptAsync(client, ReadAsBearing());

                internal static Task SubmitterPositional_ValueReturning(ILedgerWriter client, ContractId<Offer> contractId) =>
                    contractId.TryDescribeAsync(client, ReadAsBearing());

                internal static Task SubmitterPositional_InterfaceChoice(ILedgerWriter client, ContractId<ICustody> contractId) =>
                    contractId.TryTransferAsync(client, ReadAsBearing());
            }
            """);

    private static GeneratedFile NamedPartyCallSites(IReadOnlyList<GeneratedFile> files) =>
        GeneratedFile.Text("NamedPartyCallSites.cs", $$"""
            using System.Threading.Tasks;
            using Daml.Ledger.Abstractions;
            using Daml.Runtime.Contracts;
            using Daml.Runtime.Data;
            using {{GeneratedNamespace(files)}};

            internal static class NamedPartyCallSites
            {
                internal static Task PartyPositional_CreateBearingByContractId(ILedgerWriter client, ContractId<Offer> contractId) =>
                    contractId.TryAcceptAsync(client, new Party("alice"));

                internal static Task PartyPositional_CreateBearingByContract(ILedgerWriter client, Contract<Offer> contract) =>
                    contract.TryAcceptAsync(client);

                internal static Task PartyPositional_ValueReturning(ILedgerWriter client, ContractId<Offer> contractId) =>
                    contractId.TryDescribeAsync(client, new Party("alice"));

                internal static Task PartyPositional_InterfaceChoice(ILedgerWriter client, ContractId<ICustody> contractId) =>
                    contractId.TryTransferAsync(client, new Party("alice"));

                internal static Task PartyPositional_ObserverBearing_AllPartiesNamed(ILedgerWriter client, ContractId<Offer> contractId) =>
                    contractId.TryRenewAsync(client, new Party("alice"), new Party("bob"));

                internal static Task PartyPositional_ObserverBearing_ControllerOnly(ILedgerWriter client, ContractId<Offer> contractId) =>
                    contractId.TryRenewAsync(client, new Party("alice"));
            }
            """);

    private static string GeneratedNamespace(IReadOnlyList<GeneratedFile> files)
    {
        var offer = files.First(f => f.RelativePath.EndsWith("Offer.cs", StringComparison.Ordinal)).Content;
        var declaration = offer
            .Split('\n')
            .First(line => line.StartsWith("namespace ", StringComparison.Ordinal));
        return declaration.Trim().TrimEnd(';')["namespace ".Length..];
    }

    private static IReadOnlyList<GeneratedFile> GenerateOfferModule()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Acme.Offers",
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
                                [new DamlTypeRef("test-pkg", "Acme.Offers", "Offer")]),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Acme.Offers", "Offer")]),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                            Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                        },
                        new DamlChoice
                        {
                            Name = "Describe",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Text),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                    Observers = DamlPartyAnalysis.Static([]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Offer",
                    Definition = new DamlRecordDefinition(fields),
                }
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
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Dynamic,
                            Observers = DamlPartyAnalysis.Dynamic,
                        },
                    ],
                }
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
            Dependencies = [],
        };

        return CreateGenerator().Generate(dar);
    }
}
