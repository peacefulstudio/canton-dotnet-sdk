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

public class EmittedNamespaceCollisionGlobalQualificationCompilesTests
{
    [Fact]
    public void Emitted_contract_id_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Acme.ContractId",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Vault",
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("custodian", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "VaultRef",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("vaultCid", ContractIdOf("Acme.ContractId", "Vault"))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-contractid-id",
            Name = "acme-ContractId",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.ContractId", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the module namespace actually ends in .ContractId");

        var vaultRef = files.First(f => f.RelativePath.EndsWith("VaultRef.cs", StringComparison.Ordinal));
        vaultRef.Content.Should().Contain(
            "global::Daml.Runtime.Contracts.ContractId<",
            "the ContractId head must be global::-qualified when the surrounding namespace tail is `ContractId`, otherwise an unqualified `ContractId` is ambiguous with the enclosing namespace");
        vaultRef.Content.Should().NotContain(
            "(ContractId<",
            "no bare ContractId head should survive in the shadowing namespace");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .ContractId must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_read_only_list_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Acme.Collections.Generic.IReadOnlyList",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Bag",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("items", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlPrimitiveType(DamlPrimitive.Text)])),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-collections-generic-id",
            Name = "acme-collections-generic-IReadOnlyList",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Collections.Generic.IReadOnlyList", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the module namespace actually ends in .IReadOnlyList");

        var bag = files.First(f => f.RelativePath.EndsWith("Bag.cs", StringComparison.Ordinal));
        bag.Content.Should().Contain(
            "global::System.Collections.Generic.IReadOnlyList<",
            "the IReadOnlyList head must be global::-qualified when the surrounding namespace tail is `IReadOnlyList`");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IReadOnlyList must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choices_witness_read_only_list_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Acme.Collections.Generic.IReadOnlyList",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-collections-generic-choices-id",
            Name = "acme-collections-generic-choices-IReadOnlyList",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Collections.Generic.IReadOnlyList", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the module namespace actually ends in .IReadOnlyList");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::System.Collections.Generic.IReadOnlyList<IChoice> Choices { get; } = [ChoiceTouch];",
            "the Choices witness's IReadOnlyList head must be global::-qualified when the surrounding namespace tail is `IReadOnlyList`, even though its IChoice type argument has no such collision here");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IReadOnlyList must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_arity_protected_generic_heads_are_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Acme.Choice",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Acme.Choice", "Asset"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-choice-id",
            Name = "acme-Choice",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Choice", StringComparison.Ordinal),
            "the test only guards the shadowing path if the module namespace actually ends in .Choice");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Commands.Choice<",
            "the Choice<> head must be global::-qualified when the surrounding namespace tail is `Choice`");
        asset.Content.Should().NotContain(
            " Choice<",
            "no bare Choice<> head should survive in the shadowing namespace");
        asset.Content.Should().Contain(
            "IContract<ContractId<Asset>, Asset>",
            "IContract<> is routed through the qualifier; with no .IContract namespace collision it stays bare (collision-aware no-op)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .Choice must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_contract_interface_heads_are_global_qualified_when_namespace_ends_in_icontract()
    {
        var module = new DamlModule
        {
            Name = "Acme.IContract",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Acme.IContract", "Asset"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-icontract-id",
            Name = "acme-IContract",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.IContract", StringComparison.Ordinal),
            "the test only guards the shadowing path if the module namespace actually ends in .IContract");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Contracts.IContract<",
            "the IContract<> head must be global::-qualified when the surrounding namespace tail is `IContract`");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IContract must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_exercise_outcome_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Acme.ExerciseOutcome",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-exerciseoutcome-id",
            Name = "acme-ExerciseOutcome",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.ExerciseOutcome", StringComparison.Ordinal),
            "the test only guards the shadowing path if the module namespace actually ends in .ExerciseOutcome");

        var emitted = string.Join("\n", files.Select(f => f.Content));
        emitted.Should().Contain(
            "global::Daml.Runtime.Outcomes.ExerciseOutcome<",
            "the ExerciseOutcome<> head must be global::-qualified when the surrounding namespace tail is `ExerciseOutcome` (arity protects the type lookup, but the qualifier must still emit the global:: form for shape consistency)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .ExerciseOutcome must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_interface_choice_codec_receiver_is_global_qualified_when_stamped_view_field_shadows_it()
    {
        // When an interface's view record has a field named 'party' (party : Party), the
        // emitted marker IAsset gains a property 'Party Party { get; }' via view-field
        // mirroring. Inside the marker body a choice descriptor lambda emits:
        //   ArgumentDecoder = val => Party.FromDamlValue(...)
        // Without qualification, 'Party' resolves to the property (CS0120 — non-static
        // reference on a type).  ShadowingTypeNames must add view-field member names to
        // the shadow set so the qualifier root-qualifies the codec receiver.
        var module = new DamlModule
        {
            Name = "Acme.Assets",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AssetView",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("party", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Asset",
                    ViewType = new DamlTypeRef("", "Acme.Assets", "AssetView"),
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
            PackageId = "acme-assets-view-shadow-id",
            Name = "acme-Assets",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var iAsset = files.First(f => f.RelativePath.EndsWith("IAsset.cs", StringComparison.Ordinal));
        iAsset.Content.Should().Contain(
            "global::Daml.Runtime.Data.Party.FromDamlValue(",
            "the codec receiver Party must be global::-qualified inside IAsset when the stamped view field 'party' mirrors as property 'Party' that shadows the runtime Party type in expression position");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code for an interface with a stamped view field shadowing a runtime type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choices_witness_i_choice_element_is_global_qualified_when_dependency_interface_marker_shadows_it()
    {
        // A dependency package's module "Acme" declares a Daml interface "Choice", emitting
        // marker "IChoice" in namespace "Acme".  The main package's module "Acme.Assets"
        // sits in namespace "Acme.Assets", whose enclosing namespace "Acme" contains the
        // dependency's "IChoice".  A bare IChoice reference in Acme.Assets then binds to
        // Acme.IChoice instead of Daml.Runtime.Commands.IChoice, making the Choices
        // witness type invalid.  ForPackage must include cross-package marker names from
        // sibling packages in the shadow set so the qualifier root-qualifies IChoice.
        var depModule = new DamlModule
        {
            Name = "Acme",
            Templates = [],
            DataTypes = [],
            Interfaces = [new DamlInterface { Name = "Choice", Choices = [], ViewType = null }],
        };
        var depPackage = new DamlPackage
        {
            PackageId = "acme-dep-choice-shadow-id",
            Name = "acme-dep",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [depModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Acme.Assets",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
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
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "acme-main-assets-id",
            Name = "acme-Assets",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [depPackage] };
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
            IncludeDependencies = true,
        };
        var files = CreateGenerator(options).Generate(dar);

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Commands.IChoice",
            "the IChoice element type in the Choices witness must be global::-qualified when a dependency module 'Acme' emits marker 'IChoice' whose namespace 'Acme' is an ancestor of 'Acme.Assets'");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code for a main module nested under a dependency module that declares a Daml interface 'Choice' must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choices_witness_i_choice_element_is_global_qualified_when_an_interface_marker_shadows_it()
    {
        // A Daml interface named "Choice" generates a C# marker "IChoice" in the module's
        // namespace. A bare IChoice reference in the same namespace then binds to that
        // marker instead of Daml.Runtime.Commands.IChoice, making IReadOnlyList<IChoice>
        // invalid. The qualifier's shadow set must include interface marker names so it
        // root-qualifies IChoice in the Choices witness.
        var module = new DamlModule
        {
            Name = "Acme.Tokens",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Token",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [new DamlInterface { Name = "Choice", Choices = [], ViewType = null }],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-tokens-interface-shadow-id",
            Name = "acme-Tokens",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var token = files.First(f => f.RelativePath.EndsWith("Token.cs", StringComparison.Ordinal));
        token.Content.Should().Contain(
            "global::Daml.Runtime.Commands.IChoice",
            "the IChoice element type in the Choices witness must be global::-qualified when the Daml interface 'Choice' emits a marker 'IChoice' that shadows Daml.Runtime.Commands.IChoice in the same namespace");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code for a module with a Daml interface named 'Choice' must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_dependency_choices_witness_is_global_qualified_when_prefixed_main_package_marker_shadows_it()
    {
        // When the main package has a NamespacePrefix, its modules get a qualified namespace.
        // E.g. prefix "Corp", module "Finance" → namespace "Corp.Finance".
        // A dependency module "Corp.Finance.Tokens" has namespace "Corp.Finance.Tokens",
        // which starts with "Corp.Finance" — so an interface marker "IChoice" in the main
        // package's "Corp.Finance" namespace shadows Daml.Runtime.Commands.IChoice in the
        // dependency.
        //
        // CrossPackageInterfaceMarkers must use isMainPackage:true for the main-package
        // sibling so it resolves "Corp.Finance" (not just "Finance"); without that fix,
        // StartsWithSegments("Corp.Finance.Tokens", "Finance") == false and the shadow
        // is missed, leaving a bare IChoice that resolves to Corp.Finance.IChoice.
        var mainModule = new DamlModule
        {
            Name = "Finance",
            Templates = [],
            DataTypes = [],
            Interfaces = [new DamlInterface { Name = "Choice", Choices = [], ViewType = null }],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "corp-finance-main-id",
            Name = "corp-finance",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var depModule = new DamlModule
        {
            Name = "Corp.Finance.Tokens",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Token",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
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
                    Name = "Token",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };
        var depPackage = new DamlPackage
        {
            PackageId = "corp-finance-tokens-dep-id",
            Name = "corp-finance-tokens",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [depModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [depPackage] };
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
            IncludeDependencies = true,
            NamespacePrefix = "Corp",
        };
        var files = CreateGenerator(options).Generate(dar);

        var token = files.First(f => f.RelativePath.EndsWith("Token.cs", StringComparison.Ordinal));
        token.Content.Should().Contain(
            "global::Daml.Runtime.Commands.IChoice",
            "the IChoice element type in the Choices witness must be global::-qualified when the prefixed main-package namespace 'Corp.Finance' is an ancestor of the dependency namespace 'Corp.Finance.Tokens' and the main package declares a Daml interface 'Choice'");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code for a dependency module nested under a prefixed main-package namespace that declares a Daml interface 'Choice' must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choices_witness_i_choice_element_is_global_qualified_when_non_marker_sibling_type_shadows_it()
    {
        // A dependency module "Acme" declares an ordinary record named "IChoice" (not a Daml
        // interface — no marker is generated). The main module "Acme.Assets" has namespace
        // "Acme.Assets", which starts with "Acme", so "Acme.IChoice" (the dep record) is
        // visible there without qualification. CrossPackageShadowTypes must include every
        // top-level type name from sibling packages, not just interface markers, so the
        // qualifier detects the shadowing and root-qualifies Daml.Runtime.Commands.IChoice.
        var depModule = new DamlModule
        {
            Name = "Acme",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "IChoice",
                    Definition = new DamlRecordDefinition([]),
                },
            ],
            Interfaces = [],
        };
        var depPackage = new DamlPackage
        {
            PackageId = "acme-dep-non-marker-id",
            Name = "acme-dep",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [depModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Acme.Assets",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "acme-assets-main-id",
            Name = "acme-assets",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [depPackage] };
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
            IncludeDependencies = true,
        };
        var files = CreateGenerator(options).Generate(dar);

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Commands.IChoice",
            "the IChoice element type in the Choices witness must be global::-qualified when a dependency module 'Acme' emits an ordinary record 'IChoice' whose namespace 'Acme' is an ancestor of 'Acme.Assets'");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code for a main module nested under a dependency module that declares a non-interface record 'IChoice' must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_interface_choice_projector_exception_types_are_global_qualified_when_daml_records_shadow_them()
    {
        var module = new DamlModule
        {
            Name = "Acme.Vault",
            Templates = [],
            DataTypes =
            [
                new DamlDataType { Name = "Exception", Definition = new DamlRecordDefinition([]) },
                new DamlDataType { Name = "OperationCanceledException", Definition = new DamlRecordDefinition([]) },
                new DamlDataType { Name = "InvalidOperationException", Definition = new DamlRecordDefinition([]) },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Oracle",
                    ViewType = null,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetCount",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64),
                        },
                    ],
                },
            ],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-vault-exception-shadow-id",
            Name = "acme-vault",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Vault", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the emitted interface actually shares a namespace with the colliding Daml records");

        var iOracle = files.First(f => f.RelativePath.EndsWith("IOracle.cs", StringComparison.Ordinal));
        iOracle.Content.Should().Contain(
            "catch (global::System.Exception ex) when (ex is not global::System.OperationCanceledException)",
            "the projector's catch clause must be global::-qualified when Daml records named Exception/OperationCanceledException share the emitted namespace");
        iOracle.Content.Should().Contain(
            "throw new global::System.InvalidOperationException(",
            "the projector's no-matching-event diagnostic throw must be global::-qualified when a Daml record named InvalidOperationException shares the emitted namespace");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code for an interface choice projector sharing a namespace with Daml records named Exception, OperationCanceledException and InvalidOperationException must still compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_interface_choice_projector_string_comparison_is_global_qualified_when_a_daml_record_shadows_it()
    {
        var module = new DamlModule
        {
            Name = "Acme.Ledger",
            Templates = [],
            DataTypes = [new DamlDataType { Name = "StringComparison", Definition = new DamlRecordDefinition([]) }],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Oracle",
                    ViewType = null,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetCount",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64),
                        },
                    ],
                },
            ],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-ledger-stringcomparison-shadow-id",
            Name = "acme-ledger",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Ledger", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the emitted interface actually shares a namespace with the colliding Daml record");

        var iOracle = files.First(f => f.RelativePath.EndsWith("IOracle.cs", StringComparison.Ordinal));
        iOracle.Content.Should().Contain(
            "string.Equals(exercised.ContractId, contractId, global::System.StringComparison.Ordinal)",
            "the projector's contract-id comparison must be global::-qualified when a Daml record named StringComparison shares the emitted namespace");
        iOracle.Content.Should().Contain(
            "string.Equals(interfaceId.ModuleName, IOracle.InterfaceId.ModuleName, global::System.StringComparison.Ordinal)",
            "the projector's module-name comparison must be global::-qualified when a Daml record named StringComparison shares the emitted namespace");
        iOracle.Content.Should().Contain(
            "string.Equals(interfaceId.EntityName, IOracle.InterfaceId.EntityName, global::System.StringComparison.Ordinal)",
            "the projector's entity-name comparison must be global::-qualified when a Daml record named StringComparison shares the emitted namespace");
        iOracle.Content.Should().Contain(
            "string.Equals(exercised.ChoiceName.Value, \"GetCount\", global::System.StringComparison.Ordinal))",
            "the projector's choice-name comparison must be global::-qualified when a Daml record named StringComparison shares the emitted namespace");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code for an interface choice projector sharing a namespace with a Daml record named StringComparison must still compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
