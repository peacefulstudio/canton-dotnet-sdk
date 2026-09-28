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

public class EmittedNamespaceCollisionShadowingCompilesTests
{
    [Theory]
    [InlineData("Acme.IHasView")]
    [InlineData("Acme.IDamlInterface")]
    [InlineData("Acme.ExerciseCommand")]
    [InlineData("Daml")]
    public void Emitted_interface_code_compiles_when_module_namespace_shadows_a_runtime_type(string moduleName)
    {
        var module = new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Reissue",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf(moduleName, "Asset"),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "HoldingView",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    ViewType = new DamlTypeRef("", moduleName, "HoldingView"),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Party),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-iface-shadow-id",
            Name = "acme-iface-shadow",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains($"namespace {moduleName}", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the module namespace actually ends in the runtime type name");

        var iface = files.First(f => f.RelativePath.EndsWith("IHolding.cs", StringComparison.Ordinal));
        iface.Content.Should().NotContain(
            "  Daml.Runtime.Commands.ExerciseCommand.For<",
            "the interface-choice ExerciseCommand head must route through the qualifier, never the hard-coded non-global fully-qualified form");
        iface.Content.Should().Contain(
            "ExerciseCommand.For<",
            "the interface-choice site still calls ExerciseCommand.For, qualified by the central qualifier (bare or global::-prefixed depending on the surrounding namespace)");
        iface.Content.Should().NotContain(
            "cref=\"Daml.Runtime.",
            "interface doc crefs must use global:: so they resolve correctly when the generated namespace shadows the Daml root (e.g. package 'daml' -> namespace Daml.*)");
        iface.Content.Should().NotContain(
            "cref=\"Daml.Ledger.",
            "interface doc crefs must use global:: so they resolve correctly when the generated namespace shadows the Daml root (e.g. package 'daml' -> namespace Daml.*)");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().NotContain(
            "cref=\"Daml.Ledger.",
            "template extension doc crefs must use global:: so they resolve correctly when the generated namespace shadows the Daml root");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted interface code whose namespace shadows a runtime type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_key_bearing_template_in_party_colliding_namespace_has_no_cref_diagnostics()
    {
        var module = new DamlModule
        {
            Name = "Canton.Party",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Key = new DamlPrimitiveType(DamlPrimitive.Party),
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Reissue",
                            Consuming = true,
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
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "canton-party-id",
            Name = "canton-party",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Canton.Party", StringComparison.Ordinal),
            "the test only guards the cref-shadowing path if the module namespace actually ends in .Party");

        var diagnostics = CompileEmittedFilesWithDocDiagnostics(files);
        var crefDiagnostics = diagnostics
            .Where(d => d.Id is "CS1574" or "CS1580" or "CS1584" or "CS1658")
            .ToList();
        crefDiagnostics.Should().BeEmpty(
            "emitted XML-doc crefs must be single-global::, well-formed names so no malformed-cref diagnostic surfaces under DocumentationMode.Diagnose, but got: {0}",
            string.Join("\n", crefDiagnostics.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_key_bearing_template_with_nested_generic_party_key_has_no_cref_diagnostics()
    {
        var module = new DamlModule
        {
            Name = "Canton.Party",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Key = new DamlTypeApp(
                        new DamlPrimitiveType(DamlPrimitive.List),
                        [new DamlPrimitiveType(DamlPrimitive.Party)]),
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Reissue",
                            Consuming = true,
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
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "canton-party-id",
            Name = "canton-party",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var holding = files.First(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal));
        holding.Content.Should().Contain(
            "public static KeyDescriptor<Holding, IReadOnlyList<global::Daml.Runtime.Data.Party>> Key { get; }",
            "the nested-generic key witness qualifies the shadowed imported type");
        holding.Content.Should().Contain(
            "IReadOnlyList<global::Daml.Runtime.Data.Party> key)",
            "the by-key command builder's key parameter qualifies the shadowed imported type too");

        var diagnostics = CompileEmittedFilesWithDocDiagnostics(files);
        var crefDiagnostics = diagnostics
            .Where(d => d.Id is "CS1574" or "CS1580" or "CS1584" or "CS1658")
            .ToList();
        crefDiagnostics.Should().BeEmpty(
            "a nested-generic key over an imported type must produce a well-formed cref under DocumentationMode.Diagnose, but got: {0}",
            string.Join("\n", crefDiagnostics.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Theory]
    [InlineData("Acme.Daml")]
    [InlineData("Acme.Stdlib.V1")]
    [InlineData("Acme.Either.V1")]
    [InlineData("Acme.RelTime.V1")]
    [InlineData("Acme.Unit.V1")]
    [InlineData("Acme.Set.V1")]
    [InlineData("Acme.Map.V1")]
    [InlineData("Acme.Tuple2.V1")]
    [InlineData("Acme.Tuple3.V1")]
    [InlineData("Acme.NonEmpty.V1")]
    public void Emitted_stdlib_types_compile_when_module_namespace_shadows_a_stdlib_type(string moduleName)
    {
        // Phase 2 of routing Daml.Runtime.Stdlib.* through the central qualifier:
        // a record field typed as RelTime plus parametric stdlib types
        // (Either/Tuple2/Set/Map/NonEmpty) and a Unit-returning choice, all emitted
        // into a namespace whose tail segment shadows a stdlib simple name. The
        // qualifier must global::-qualify the shadowed names and the file must carry
        // `using Daml.Runtime.Stdlib;`, so the emitted code compiles with no CS0118.
        var stdlibPackage = new DamlPackage
        {
            PackageId = "daml-prim-id",
            Name = "daml-prim",
            Version = new Version(0, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Time.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "RelTime", Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Tuple2", TypeParams = ["a", "b"], Definition = new DamlRecordDefinition([]) },
                        new DamlDataType { Name = "Tuple3", TypeParams = ["a", "b", "c"], Definition = new DamlRecordDefinition([]) },
                        new DamlDataType { Name = "Either", TypeParams = ["a", "b"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.Set.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Set", TypeParams = ["a"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.Map.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Map", TypeParams = ["a", "b"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.Internal.Map",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Map", TypeParams = ["a", "b"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.NonEmpty.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "NonEmpty", TypeParams = ["a"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var relTime = new DamlTypeRef("daml-prim-id", "DA.Time.Types", "RelTime");
        var eitherTextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Either"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var tuple2TextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Tuple2"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var setText = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Set.Types", "Set"),
            [new DamlPrimitiveType(DamlPrimitive.Text)]);
        var mapTextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Map.Types", "Map"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var nonEmptyText = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.NonEmpty.Types", "NonEmpty"),
            [new DamlPrimitiveType(DamlPrimitive.Text)]);
        var tuple3TextIntText = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Tuple3"),
            [
                new DamlPrimitiveType(DamlPrimitive.Text),
                new DamlPrimitiveType(DamlPrimitive.Int64),
                new DamlPrimitiveType(DamlPrimitive.Text),
            ]);
        var internalMapTextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Internal.Map", "Map"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var listOfEither = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.List),
            [eitherTextInt]);
        var setOfTuple2 = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Set.Types", "Set"),
            [tuple2TextInt]);

        var module = new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Lock",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Release",
                            Consuming = true,
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
                    Name = "Lock",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("duration", relTime),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Bag",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("outcome", eitherTextInt),
                        new DamlFieldDefinition("pair", tuple2TextInt),
                        new DamlFieldDefinition("tags", setText),
                        new DamlFieldDefinition("scores", mapTextInt),
                        new DamlFieldDefinition("required", nonEmptyText),
                        new DamlFieldDefinition("triple", tuple3TextIntText),
                        new DamlFieldDefinition("internalScores", internalMapTextInt),
                        new DamlFieldDefinition("outcomes", listOfEither),
                        new DamlFieldDefinition("pairSet", setOfTuple2),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "acme-stdlib-shadow",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [stdlibPackage] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted stdlib types must global::-qualify (no CS0118) and import Daml.Runtime.Stdlib when the namespace shadows a stdlib simple name, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
