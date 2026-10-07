// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterInterfaceExtensionTests
{
    private const string LocalPackageId = "pkg-id";

    private static DamlPackage Package() =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [new DamlModule { Name = "Main", Templates = [], DataTypes = [], Interfaces = [] }],
            DependencyReferences = [],
        };

    private static DamlChoice Choice(string name, DamlType argumentType) =>
        new()
        {
            Name = name,
            ArgumentType = argumentType,
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
            Consuming = false,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static ChoiceEmitter Emitter()
    {
        var resolution = RealResolution.Of(Package(), new CodeGenOptions { NamespacePrefix = "Test.Package" });
        var context = resolution.Context;
        var resolver = resolution.Resolver;
        return new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
    }

    private static string EmitExtensions(DamlInterface iface)
    {
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);
        Emitter().WriteInterfaceChoiceExtensions(indent, iface, "I" + iface.Name);
        return sb.ToString();
    }

    private static DamlInterface Interface(params DamlChoice[] choices) =>
        new()
        {
            Name = "Asset",
            Choices = choices,
        };

    [Fact]
    public void ChoiceEmitterInterfaceExtension_emits_an_extensions_class_with_one_async_method_per_interface_choice()
    {
        var output = EmitExtensions(Interface(
            Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit)),
            Choice("Freeze", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("public static class IAssetExtensions");
        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryTransferAsync(");
        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryFreezeAsync(");
        output.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<IAsset> contractId,");
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_async_method_awaits_the_submission_and_projects_the_committed_result()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain(
            "public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryTransferAsync(\n"
            + "        this global::Daml.Runtime.Contracts.ContractId<IAsset> contractId,\n"
            + "        global::Daml.Ledger.Abstractions.ILedgerWriter client,\n"
            + "        global::Daml.Runtime.Commands.SubmitterInfo submitter,");
        output.Should().Contain("var outcome = await client." + TrySubmitSingleArgumentOrder + ".ConfigureAwait(false);");
        output.Should().Contain("return outcome.ProjectCommitted(tx => ProjectTransferResult(tx, contractId.Value));");
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_emits_a_private_projector_per_choice_that_hands_the_choice_descriptor_to_the_runtime()
    {
        var textReturningChoice = new DamlChoice
        {
            Name = "Transfer",
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Text),
            Consuming = false,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };
        var output = EmitExtensions(Interface(textReturningChoice));

        output.Should().Contain("private static global::Daml.Runtime.Outcomes.ExerciseOutcome<string> ProjectTransferResult(global::Daml.Runtime.Contracts.TransactionResult tx, string contractId)");
        output.Should().Contain("tx.ProjectChoiceResult(IAsset.ChoiceTransfer, contractId);");
        output.Should().NotContain("tx.ExercisedEvents");
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_exerciser_builds_an_interface_typed_exercise_command()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("global::Daml.Runtime.Commands.ExerciseCommand.For<IAsset>(contractId, new global::Daml.Runtime.Commands.ChoiceName(\"Transfer\"), global::Daml.Runtime.Data.DamlUnit.Instance)");
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_with_no_choices_emits_no_extensions_class()
    {
        EmitExtensions(Interface()).Should().NotContain("public static class");
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_exerciser_accepts_optional_command_id_override()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("CommandId? commandId = null,");
        output.Should().Contain(TrySubmitSingleArgumentOrder);

        var idxWorkflowId = output.IndexOf("string? workflowId = null,", global::System.StringComparison.Ordinal);
        var idxCommandId = output.IndexOf("CommandId? commandId = null,", global::System.StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("global::System.Threading.CancellationToken cancellationToken = default)", global::System.StringComparison.Ordinal);
        idxWorkflowId.Should().BeLessThan(idxCommandId);
        idxCommandId.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_exerciser_forwards_optional_timeout()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("global::System.TimeSpan? timeout = null,");
        output.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        var idxCommandId = output.IndexOf("CommandId? commandId = null,", global::System.StringComparison.Ordinal);
        var idxTimeout = output.IndexOf("global::System.TimeSpan? timeout = null,", global::System.StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("global::System.Threading.CancellationToken cancellationToken = default)", global::System.StringComparison.Ordinal);
        idxCommandId.Should().BeLessThan(idxTimeout);
        idxTimeout.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_choice_emits_a_command_builder_that_returns_an_exercise_command()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseCommand TransferCommand(");
        output.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<IAsset> contractId)");
        output.Should().Contain("return global::Daml.Runtime.Commands.ExerciseCommand.For<IAsset>(contractId, new global::Daml.Runtime.Commands.ChoiceName(\"Transfer\"), global::Daml.Runtime.Data.DamlUnit.Instance);");
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_choice_async_method_delegates_to_the_command_builder_instead_of_building_inline()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("var command = contractId.TransferCommand();");
        output.Should().Contain("client." + TrySubmitSingleArgumentOrder);
        output.Should().NotContain("var command = global::Daml.Runtime.Commands.ExerciseCommand.For<IAsset>(contractId, new ChoiceName(\"Transfer\")");
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_choice_command_builder_accepts_the_typed_argument_when_the_choice_has_one()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"))));

        output.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseCommand TransferCommand(");
        output.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<IAsset> contractId,");
        output.Should().Contain("global::Test.Package.Main.TransferArg argument)");
        output.Should().Contain("return global::Daml.Runtime.Commands.ExerciseCommand.For<IAsset>(contractId, new global::Daml.Runtime.Commands.ChoiceName(\"Transfer\"), argument.ToRecord());");
        output.Should().Contain("var command = contractId.TransferCommand(argument);");
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_exerciser_declares_configure_between_timeout_and_cancellation_token()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().MatchRegex(@"TimeSpan\? timeout = null,\s*" + Regex.Escape(ConfigureParameter) + @"\s*global::System.Threading.CancellationToken cancellationToken = default\)");
        Regex.Matches(output, Regex.Escape(ConfigureParameter)).Should().HaveCount(1);
    }

    [Fact]
    public void ChoiceEmitterInterfaceExtension_interface_exerciser_documents_configure_with_a_disclosure_example()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("/// <param name=\"configure\">");
        output.Should().Contain("s => s.WithDisclosedContracts(holding.Disclosure!)");
    }
}
