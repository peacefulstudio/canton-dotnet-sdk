// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public partial class CodeGenEdgeCaseTests
{
    #region Interface-shadowed record tests

    // Daml-LF emits a same-named empty record for every `interface I where ...`
    // declaration. The codegen emits no C# for those records: the interface's marker
    // carries the type's identity, and `ContractId<IMarker>` serves the contract-id
    // fields and choice extensions that would otherwise need one.

    [Fact]
    public void Generate_should_not_emit_a_record_for_an_interface_shadowed_declaration()
    {
        // Arrange — declare an interface and the LF record that always accompanies it.
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();

        // Assert
        files.Should().NotContain(
            f => f.RelativePath.EndsWith("/Holding.cs", global::System.StringComparison.Ordinal),
            "the marker carries the interface's identity, so the LF record beside it has nothing left to emit");
        files.Should().Contain(
            f => f.RelativePath.EndsWith("/IHolding.cs", global::System.StringComparison.Ordinal),
            "dropping the record must not drop the marker with it");
        files.Should().NotContain(
            f => f.Content.Contains("record Holding :", global::System.StringComparison.Ordinal),
            "no other file may pick the declaration up either");
    }

    [Fact]
    public void Generate_should_emit_regular_record_when_no_matching_interface_in_same_module()
    {
        // Arrange — record name matches an interface in a DIFFERENT module; should
        // NOT be treated as a placeholder.
        var module = new DamlModule
        {
            Name = "Test.Records",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                }
            ],
            Interfaces = []  // No interface in *this* module — the simple-name match in
                             // some other module must not leak in.
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holdingFile = files.FirstOrDefault(f =>
            f.RelativePath.EndsWith("Holding.cs", global::System.StringComparison.Ordinal));

        // Assert
        holdingFile.Should().NotBeNull();
        var code = holdingFile!.Content;
        code.Should().Contain("public sealed record Holding(\n    [property: global::Daml.Runtime.Data.DamlFieldAttribute(\"amount\")] decimal Amount\n) : global::Daml.Runtime.Data.IDamlRecord");
        code.Should().NotContain(": ITemplate");
        code.Should().NotContain("InvalidOperationException");
    }

    [Fact]
    public void Generate_should_distinguish_interface_shadowed_records_across_modules_with_same_name()
    {
        // Arrange — module A has an interface `Token` (so the same-named record is
        // shadowed and not emitted); module B has an unrelated record `Token` that
        // must still be emitted.
        var modA = new DamlModule
        {
            Name = "App.A",
            Templates = [],
            DataTypes =
            [
                new DamlDataType { Name = "Token", Definition = new DamlRecordDefinition([]) }
            ],
            Interfaces =
            [
                new DamlInterface { Name = "Token", Choices = [], ViewType = null }
            ]
        };
        var modB = new DamlModule
        {
            Name = "App.B",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("symbol", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [modA, modB],
            DependencyReferences = []
        };
        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();
        var tokenFiles = files.Where(f => f.RelativePath.EndsWith("/Token.cs", global::System.StringComparison.Ordinal)).ToList();

        // Assert — the codegen flattens all modules into one namespace, so the two
        // Token declarations would collide on a file path. Skipping the shadowed one
        // leaves exactly module B's regular record.
        tokenFiles.Should().ContainSingle(
            "module A's Token is shadowed by its interface and emits nothing, so only module B's is left");
        tokenFiles[0].Content.Should().Contain(
            "public sealed record Token(\n    [property: global::Daml.Runtime.Data.DamlFieldAttribute(\"symbol\")] string Symbol\n) : global::Daml.Runtime.Data.IDamlRecord",
            "module B's Token must keep its IDamlRecord regular-record shape");
        files.Should().Contain(
            f => f.RelativePath.EndsWith("/IToken.cs", global::System.StringComparison.Ordinal),
            "module A's interface marker is still emitted");
    }

    // -------------------------------------------------------------------
    // Interface choice extension method tests — for every Daml interface
    // choice, codegen now emits a typed `<Choice>Async`-style helper on
    // `ContractId<I>` so consumers can do `await cid.TryTransferAsync(arg)`
    // without naming the concrete template. The generated extension class
    // sits beside the interface declaration in the same file.
    // -------------------------------------------------------------------

    [Fact]
    public void Generate_should_emit_extension_class_for_interface_choices()
    {
        // Arrange — interface with one record-argument choice and one Unit choice.
        // Both shapes are common: Splice's IHolding has both styles.
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([])
                },
                new DamlDataType
                {
                    Name = "Transfer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                },
                new DamlDataType
                {
                    Name = "Transfer_Result",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    ViewType = null,
                    Choices =                     [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Holding", "Transfer"),
                            ReturnType = new DamlTypeRef("", "Test.Holding", "Transfer_Result")
                        },
                        new DamlChoice
                        {
                            Name = "Lock",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var ifaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IHolding.cs", global::System.StringComparison.Ordinal));

        // Assert — the file contains both the interface declaration AND a
        // sibling static extensions class with one method per choice.
        ifaceFile.Should().NotBeNull();
        var code = ifaceFile!.Content;

        // Marker-typed interface declaration is unchanged
        code.Should().Contain("public interface IHolding : global::Daml.Runtime.Contracts.IDamlInterface");

        // Sibling extensions class with one method per choice
        code.Should().Contain("public static class IHoldingExtensions");

        code.Should().Contain(
            "public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Test.Holding.Transfer_Result>> TryTransferAsync(",
            "a record-argument choice returns ExerciseOutcome<Transfer_Result> — the choice's own return type, decoded through the choice descriptor's ResultDecoder rather than the raw ExerciseOutcome<TransactionResult>");
        code.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<IHolding> contractId,");
        code.Should().Contain("ILedgerWriter client,");
        code.Should().Contain("Transfer argument,");
        code.Should().Contain("SubmitterInfo submitter,");
        // Internally builds the command via the runtime For<TOwner> helper — the
        // wire-level template_id slot carries IHolding.InterfaceId, and the choice
        // argument is serialised via argument.ToRecord().
        code.Should().Contain("global::Daml.Runtime.Commands.ExerciseCommand.For<IHolding>(contractId, new global::Daml.Runtime.Commands.ChoiceName(\"Transfer\"), argument.ToRecord())");
        code.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        // Unit-argument choice: no `argument` parameter, DamlUnit.Instance is passed
        code.Should().Contain(
            "public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryLockAsync(\n        this global::Daml.Runtime.Contracts.ContractId<IHolding> contractId,\n        global::Daml.Ledger.Abstractions.ILedgerWriter client,\n        global::Daml.Runtime.Commands.SubmitterInfo submitter,",
            "a single-line signature assertion cannot tell one emitted parameter list from another, so it is pinned verbatim — a failure here means the exerciser signature changed or the emitter's indentation did, not that the assertion is wrong");
        code.Should().Contain("global::Daml.Runtime.Commands.ExerciseCommand.For<IHolding>(contractId, new global::Daml.Runtime.Commands.ChoiceName(\"Lock\"), global::Daml.Runtime.Data.DamlUnit.Instance)");
    }

    [Fact]
    public void Generate_should_skip_extension_class_when_interface_has_no_methods()
    {
        // Arrange — view-only interface with no choices. No exerciser methods to
        // emit, so the extension class is suppressed (avoids an empty static
        // class littering the namespace).
        var module = new DamlModule
        {
            Name = "Test.Marker",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Marker",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface { Name = "Marker", Choices = [], ViewType = null }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var ifaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IMarker.cs", global::System.StringComparison.Ordinal));

        // Assert
        ifaceFile.Should().NotBeNull();
        ifaceFile!.Content.Should().Contain("public interface IMarker : global::Daml.Runtime.Contracts.IDamlInterface");
        ifaceFile.Content.Should().NotContain("IMarkerExtensions");
    }

    [Fact]
    public void Generate_should_map_interface_typed_contract_id_fields_to_the_marker()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates =
            [
                new DamlTemplate { Name = "Account", Choices = [] }
            ],
            DataTypes =
            [
                new DamlDataType { Name = "Holding", Definition = new DamlRecordDefinition([]) },
                new DamlDataType { Name = "Account", Definition = new DamlRecordDefinition([]) },
                new DamlDataType
                {
                    Name = "Wallet",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("holding", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Holding", "Holding")])),
                        new DamlFieldDefinition("holdings", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("", "Test.Holding", "Holding")])])),
                        new DamlFieldDefinition("maybeHolding", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.Optional),
                            [new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("", "Test.Holding", "Holding")])])),
                        new DamlFieldDefinition("account", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Holding", "Account")]))
                    ])
                }
            ],
            Interfaces =
            [
                new DamlInterface { Name = "Holding", Choices = [], ViewType = null }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var walletFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wallet.cs", global::System.StringComparison.Ordinal));

        // Assert
        walletFile.Should().NotBeNull();
        var code = walletFile!.Content;

        code.Should().Contain("global::Daml.Runtime.Contracts.ContractId<global::Test.Holding.IHolding>");
        code.Should().Contain("global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Contracts.ContractId<global::Test.Holding.IHolding>>");
        code.Should().Contain("global::Daml.Runtime.Contracts.ContractId<global::Test.Holding.IHolding>?", "an Optional interface contract id maps to the nullable marker");
        code.Should().NotContain("global::Daml.Runtime.Contracts.ContractId<Holding>", "the interface-shadowed record name must never appear as a contract-id type argument");
        code.Should().Contain("global::Daml.Runtime.Contracts.ContractId<global::Test.Holding.Account>", "contract ids to a real template are unchanged");
    }

    #endregion
}
