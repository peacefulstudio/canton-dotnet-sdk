// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterValueReturnExerciserTests
{
    private const string LocalPackageId = "pkg-id";

    private static DamlPackage Package(DamlTemplate template, params DamlModule[] otherModules) =>
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
                .. otherModules,
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

    private static string EmitNonContract(DamlTemplate template, params DamlModule[] otherModules)
    {
        var package = Package(template, otherModules);
        var resolution = RealResolution.Of(package, new CodeGenOptions { NamespacePrefix = "Test.Package" });
        var context = resolution.Contexts.Single(c => c.Module.Name == "Main");
        var resolver = resolution.Resolver;
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(indent, template);
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
        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<decimal>> TryGetTrailingTwapAsync(");
        output.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Oracle> contractId,");
        output.Should().Contain("ILedgerWriter client,");
        output.Should().Contain("SubmitterInfo submitter,");
        output.Should().Contain("client." + TrySubmitSingleArgumentOrder);
        output.Should().Contain("return outcome.ProjectCommitted(tx => ProjectGetTrailingTwapResult(tx, contractId.Value));");
        output.Should().NotContain("Unhandled outcome");
        output.Should().NotContain("global::Daml.Runtime.Outcomes.ExerciseOutcome<TransactionResult>.DamlError");
        output.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Oracle.ChoiceGetTrailingTwap, contractId);");
        output.Should().NotContain("tx.ExercisedEvents");
    }

    [Fact]
    public void ChoiceEmitterValueReturnExerciser_non_contract_exerciser_emits_async_wrapper_for_record_returning_choice()
    {
        var template = Template(
            "Reporter",
            Choice("ComputeReport", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlTypeRef("", "Test.Reports", "Report")));

        var output = EmitNonContract(template, TestPackages.ModuleOf("Test.Reports", TestPackages.Record("Report")));

        output.Should().Contain("public static class ReporterNonContractExtensions");
        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Test.Package.Test.Reports.Report>> TryComputeReportAsync(");
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

        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::System.Collections.Generic.IReadOnlyList<decimal>>> TryRecentTwapsAsync(");
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

        emitted.Should().Contain("global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<string>>");
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

        emitted.Should().Contain("global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Data.DamlUnit>>> ProjectMaybeMaybeUnitResult(");
        emitted.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Sink.ChoiceMaybeMaybeUnit, contractId);");
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
