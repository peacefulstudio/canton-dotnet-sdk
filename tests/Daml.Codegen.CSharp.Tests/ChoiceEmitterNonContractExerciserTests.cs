// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterNonContractExerciserTests
{
    private const string LocalPackageId = "pkg-id";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

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

    private static string Emit(DamlTemplate template)
    {
        var package = Package(template);
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var resolver = new StubResolver();
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(indent, template, context.DataTypes);
        return sb.ToString();
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_emits_a_typed_async_exerciser_over_the_return_type()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("public static class VaultNonContractExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<decimal>> TryQuoteAsync(");
        output.Should().Contain("this ContractId<Vault> contractId,");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_emits_an_exercised_events_projector()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("private static ExerciseOutcome<decimal> ProjectQuoteResult(TransactionResult tx, string contractId)");
        output.Should().Contain("foreach (var exercised in tx.ExercisedEvents)");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_unit_returning_choice_returns_DamlUnit()
    {
        var output = Emit(Template(Choice("Touch", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("public static async Task<ExerciseOutcome<DamlUnit>> TryTouchAsync(");
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

        var idxWorkflowId = output.IndexOf("string? workflowId = null,", StringComparison.Ordinal);
        var idxCommandId = output.IndexOf("CommandId? commandId = null,", StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("CancellationToken cancellationToken = default)", StringComparison.Ordinal);
        idxWorkflowId.Should().BeLessThan(idxCommandId);
        idxCommandId.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_exerciser_forwards_optional_timeout()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("TimeSpan? timeout = null,");
        output.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        var idxCommandId = output.IndexOf("CommandId? commandId = null,", StringComparison.Ordinal);
        var idxTimeout = output.IndexOf("TimeSpan? timeout = null,", StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("CancellationToken cancellationToken = default)", StringComparison.Ordinal);
        idxCommandId.Should().BeLessThan(idxTimeout);
        idxTimeout.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_emits_a_command_builder_that_returns_an_exercise_command()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("public static ExerciseCommand QuoteCommand(");
        output.Should().Contain("this ContractId<Vault> contractId)");
        output.Should().Contain("return new ExerciseCommand(");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_value_returning_choice_async_method_delegates_to_the_command_builder_instead_of_building_inline()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("var command = contractId.QuoteCommand();");
        output.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        const string inlineConstructionMarker = "var command = new ExerciseCommand(";
        output.Should().NotContain(inlineConstructionMarker);

        const string commandConstructionMarker = "new ExerciseCommand(";
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

        output.Should().Contain("<c>IReadOnlyDictionary&lt;string, long&gt;</c>");
        output.Should().NotContain("<c>IReadOnlyDictionary<string, long></c>");
    }

    [Fact]
    public void ChoiceEmitterNonContractExerciser_tuple_returning_choice_escapes_the_return_type_in_its_doc_comment()
    {
        var tupleReturn = TupleType(new DamlPrimitiveType(DamlPrimitive.Party), new DamlPrimitiveType(DamlPrimitive.Int64));

        var output = Emit(Template(Choice("OwnerAndCount", tupleReturn)));

        output.Should().Contain("<c>Tuple2&lt;Party, long&gt;</c>");
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

        output.Should().Contain("<c>IReadOnlyList&lt;IReadOnlyDictionary&lt;string, long&gt;&gt;</c>");
        output.Should().NotContain("<c>IReadOnlyList<IReadOnlyDictionary<string, long>></c>");
    }
}
