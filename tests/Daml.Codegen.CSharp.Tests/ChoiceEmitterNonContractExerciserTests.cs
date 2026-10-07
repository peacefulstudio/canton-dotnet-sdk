// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterNonContractExerciserTests
{
    private const string LocalPackageId = "pkg-id";

    private static DamlPackage Package(DamlTemplate template) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Main",
                    Templates = [template],
                    DataTypes = [],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static DamlChoice Choice(string name, DamlType returnType) =>
        new()
        {
            Name = name,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Consuming = false,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlTemplate Template(params DamlChoice[] choices) =>
        new()
        {
            Name = "Vault",
            Choices = choices,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static string Emit(DamlTemplate template, params DamlPackage[] dependencies)
    {
        var package = Package(template);
        var resolution = RealResolution.Of(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, dependencies);
        var context = resolution.Context;
        var resolver = resolution.Resolver;
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(indent, template);
        return sb.ToString();
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_emits_a_typed_async_exerciser_over_the_return_type()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("public static class VaultNonContractExtensions");
        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<decimal>> TryQuoteAsync(");
        output.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Vault> contractId,");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_hands_the_descriptor_to_the_runtime_projection()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain(
            "private static global::Daml.Runtime.Outcomes.ExerciseOutcome<decimal> ProjectQuoteResult(global::Daml.Runtime.Contracts.TransactionResult tx, string contractId) =>\n" +
            "        tx.ProjectChoiceResult(global::Test.Package.Main.Vault.ChoiceQuote, contractId);");
        output.Should().NotContain("tx.ExercisedEvents");
        output.Should().NotContain("InvalidOperationException");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_unit_returning_choice_returns_DamlUnit()
    {
        var output = Emit(Template(Choice("Touch", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryTouchAsync(");
        output.Should().Contain("ProjectTouchResult");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_contract_id_returning_choice_is_not_handled_here()
    {
        var cidReturn = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId),
            [new DamlTypeRef(LocalPackageId, "Main", "Token")]);

        var output = Emit(Template(Choice("Spawn", cidReturn)));

        output.Should().BeEmpty();
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_exerciser_accepts_optional_command_id_override()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("CommandId? commandId = null,");
        output.Should().Contain(TrySubmitSingleArgumentOrder);

        var idxWorkflowId = output.IndexOf("string? workflowId = null,", global::System.StringComparison.Ordinal);
        var idxCommandId = output.IndexOf("CommandId? commandId = null,", global::System.StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("global::System.Threading.CancellationToken cancellationToken = default)", global::System.StringComparison.Ordinal);
        idxWorkflowId.Should().BeLessThan(idxCommandId);
        idxCommandId.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_exerciser_forwards_optional_timeout()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("global::System.TimeSpan? timeout = null,");
        output.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        var idxCommandId = output.IndexOf("CommandId? commandId = null,", global::System.StringComparison.Ordinal);
        var idxTimeout = output.IndexOf("global::System.TimeSpan? timeout = null,", global::System.StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("global::System.Threading.CancellationToken cancellationToken = default)", global::System.StringComparison.Ordinal);
        idxCommandId.Should().BeLessThan(idxTimeout);
        idxTimeout.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_emits_a_command_builder_that_returns_an_exercise_command()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseCommand QuoteCommand(");
        output.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Vault> contractId)");
        output.Should().Contain("return new global::Daml.Runtime.Commands.ExerciseCommand(");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_async_method_delegates_to_the_command_builder_instead_of_building_inline()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("var command = contractId.QuoteCommand();");
        output.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        const string inlineConstructionMarker = "var command = new global::Daml.Runtime.Commands.ExerciseCommand(";
        output.Should().NotContain(inlineConstructionMarker);

        const string commandConstructionMarker = "new global::Daml.Runtime.Commands.ExerciseCommand(";
        var firstConstruction = output.IndexOf(commandConstructionMarker, StringComparison.Ordinal);
        firstConstruction.Should().BeGreaterThanOrEqualTo(0);
        output.IndexOf(commandConstructionMarker, firstConstruction + 1, StringComparison.Ordinal).Should().Be(-1);
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_dictionary_returning_choice_escapes_the_return_type_in_its_doc_comment()
    {
        var dictionaryReturn = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.TextMap),
            [new DamlPrimitiveType(DamlPrimitive.Int64)]);

        var output = Emit(Template(Choice("LabelCounts", dictionaryReturn)));

        output.Should().Contain("<c>global::System.Collections.Generic.IReadOnlyDictionary&lt;string, long&gt;</c>");
        output.Should().NotContain("<c>IReadOnlyDictionary<string, long></c>");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_tuple_returning_choice_escapes_the_return_type_in_its_doc_comment()
    {
        var tupleReturn = TupleType(new DamlPrimitiveType(DamlPrimitive.Party), new DamlPrimitiveType(DamlPrimitive.Int64));

        var output = Emit(Template(Choice("OwnerAndCount", tupleReturn)), TestPackages.DamlPrim());

        output.Should().Contain("<c>global::Daml.Runtime.Stdlib.Tuple2&lt;global::Daml.Runtime.Data.Party, long&gt;</c>");
        output.Should().NotContain("<c>Tuple2<Party, long></c>");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_nested_generic_returning_choice_escapes_the_return_type_in_its_doc_comment()
    {
        var nestedReturn = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.List),
            [new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.TextMap),
                [new DamlPrimitiveType(DamlPrimitive.Int64)])]);

        var output = Emit(Template(Choice("RankByOwner", nestedReturn)));

        output.Should().Contain("<c>global::System.Collections.Generic.IReadOnlyList&lt;global::System.Collections.Generic.IReadOnlyDictionary&lt;string, long&gt;&gt;</c>");
        output.Should().NotContain("<c>IReadOnlyList<IReadOnlyDictionary<string, long>></c>");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_exerciser_declares_configure_between_timeout_and_cancellation_token()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().MatchRegex(@"TimeSpan\? timeout = null,\s*" + Regex.Escape(ConfigureParameter) + @"\s*global::System.Threading.CancellationToken cancellationToken = default\)");
        Regex.Matches(output, Regex.Escape(ConfigureParameter)).Should().HaveCount(1);
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_exerciser_documents_configure_with_a_disclosure_example()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("/// <param name=\"configure\">");
        output.Should().Contain("s => s.WithDisclosedContracts(holding.Disclosure!)");
    }
}
