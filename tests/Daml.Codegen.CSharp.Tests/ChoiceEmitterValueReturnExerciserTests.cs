// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterValueReturnExerciserTests
{
    private const string LocalPackageId = "pkg-id";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(DamlTemplate template, params DamlDataType[] dataTypes) =>
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
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static DamlChoice Choice(string name, DamlType argumentType, DamlType returnType) =>
        new()
        {
            Name = name,
            ArgumentType = argumentType,
            ReturnType = returnType,
            Consuming = false,
        };

    private static DamlTemplate Template(string name, params DamlChoice[] choices) =>
        new()
        {
            Name = name,
            Choices = choices,
        };

    private static string EmitNonContract(DamlTemplate template)
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
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_emits_typed_wrapper_delegating_to_ProjectCommitted_with_filtering_projector_for_decimal_return()
    {
        var template = Template(
            "Oracle",
            Choice("GetTrailingTwap", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Numeric)));

        var output = EmitNonContract(template);

        output.Should().Contain("public static class OracleNonContractExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<decimal>> TryGetTrailingTwapAsync(");
        output.Should().Contain("this ContractId<Oracle> contractId,");
        output.Should().Contain("ILedgerWriter client,");
        output.Should().Contain("SubmitterInfo submitter,");
        output.Should().Contain("client." + TrySubmitSingleArgumentOrder);
        output.Should().Contain("return outcome.ProjectCommitted(tx => ProjectGetTrailingTwapResult(tx, contractId.Value));");
        output.Should().NotContain("Unhandled outcome");
        output.Should().NotContain("ExerciseOutcome<TransactionResult>.DamlError");
        output.Should().Contain("string.Equals(exercised.ContractId, contractId, StringComparison.Ordinal)");
        output.Should().Contain("string.Equals(exercised.TemplateId.ModuleName, Oracle.TemplateId.ModuleName, StringComparison.Ordinal)");
        output.Should().Contain("string.Equals(exercised.TemplateId.EntityName, Oracle.TemplateId.EntityName, StringComparison.Ordinal)");
        output.Should().NotContain("exercised.TemplateId.Equals(");
    }

    [Fact]
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_wraps_ResultDecoder_in_a_CommittedUndecodable_guard()
    {
        var template = Template(
            "Oracle",
            Choice("GetTrailingTwap", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Numeric)));

        var output = EmitNonContract(template);

        output.Should().Contain("var decoded = Oracle.ChoiceGetTrailingTwap.ResultDecoder!(exercised.ExerciseResult);");
        output.Should().Contain("return new ExerciseOutcome<decimal>.One(decoded);");
        output.Should().Contain("catch (global::System.Exception ex) when (ex is not global::System.OperationCanceledException)");
        output.Should().Contain("return new ExerciseOutcome<decimal>.CommittedUndecodable(tx.UpdateId, ex.Message, ex);");
    }

    [Fact]
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_emits_async_wrapper_for_record_returning_choice()
    {
        var template = Template(
            "Reporter",
            Choice("ComputeReport", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlTypeRef("", "Test.Reports", "Report")));

        var output = EmitNonContract(template);

        output.Should().Contain("public static class ReporterNonContractExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<Report>> TryComputeReportAsync(");
    }

    [Fact]
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_emits_async_wrapper_for_list_returning_choice()
    {
        var template = Template(
            "Oracle",
            Choice(
                "RecentTwaps",
                new DamlPrimitiveType(DamlPrimitive.Unit),
                new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.List), [new DamlPrimitiveType(DamlPrimitive.Numeric)])));

        var output = EmitNonContract(template);

        output.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyList<decimal>>> TryRecentTwapsAsync(");
    }

    [Fact]
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_throws_for_an_unmappable_choice_argument_shape()
    {
        var template = Template(
            "Trader",
            Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Numeric)));

        FluentActions.Invoking(() => EmitNonContract(template))
            .Should().Throw<CodegenException>()
            .WithMessage("*Quote*");
    }

    [Fact]
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_emits_the_wrapper_for_a_nested_optional_return_type()
    {
        var template = Template(
            "Sink",
            Choice(
                "MaybeMaybe",
                new DamlPrimitiveType(DamlPrimitive.Unit),
                new DamlTypeApp(
                    new DamlPrimitiveType(DamlPrimitive.Optional),
                    [new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [new DamlPrimitiveType(DamlPrimitive.Text)])])));

        var emitted = EmitNonContract(template);

        emitted.Should().Contain("Optional<Optional<string>>");
        emitted.Should().NotContain("string??");
    }

    [Fact]
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_decodes_a_nested_optional_of_unit_return()
    {
        var template = Template(
            "Sink",
            Choice(
                "MaybeMaybeUnit",
                new DamlPrimitiveType(DamlPrimitive.Unit),
                new DamlTypeApp(
                    new DamlPrimitiveType(DamlPrimitive.Optional),
                    [new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [new DamlPrimitiveType(DamlPrimitive.Unit)])])));

        var emitted = EmitNonContract(template);

        emitted.Should().Contain("ExerciseOutcome<Optional<Optional<DamlUnit>>> ProjectMaybeMaybeUnitResult(");
        emitted.Should().Contain("var decoded = Sink.ChoiceMaybeMaybeUnit.ResultDecoder!(exercised.ExerciseResult);");
    }

    [Fact]
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_emits_the_wrapper_for_an_optional_type_variable_return()
    {
        var template = Template(
            "Sink",
            Choice(
                "MaybeOf",
                new DamlPrimitiveType(DamlPrimitive.Unit),
                new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [new DamlTypeVar("a")])));

        EmitNonContract(template).Should().Contain("Optional<TA>");
    }
}
