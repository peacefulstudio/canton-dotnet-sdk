// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Tests for the codegen-emitted <c>&lt;Choice&gt;Async(...)</c> extension methods —
/// Each choice with a typed
/// <c>&lt;Choice&gt;Result</c> gets a static extension on
/// <c>ContractId&lt;TemplateName&gt;</c> that calls
/// <c>ILedgerWriter.TrySubmitAndWaitForTransactionAsync</c> and projects the
/// resulting transaction's created contracts to
/// <c>ExerciseOutcome&lt;&lt;Choice&gt;Result&gt;</c>. Errors pass through.
/// </summary>
public class ChoiceAsyncExerciserTests
{
    private static readonly DamlPackage DamlPrim = new()
    {
        PackageId = "daml-prim",
        Name = "daml-prim",
        Version = new Version(0, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = []
    };

    private static DamlType TupleType(params DamlType[] componentTypes) =>
        new DamlTypeApp(
            new DamlTypeRef("daml-prim", "DA.Types", $"Tuple{componentTypes.Length}"),
            componentTypes);

    private static DamlType ContractIdOf(string templateName) =>
        new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId),
            [new DamlTypeRef("", "Test.Module", templateName)]);

    private static DamlTemplate Template(string name, DamlType returnType, string choiceName, DamlType? argType = null) =>
        new()
        {
            Name = name,
            Choices =
            [
                new DamlChoice
                {
                    Name = choiceName,
                    Consuming = true,
                    ArgumentType = argType ?? new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = returnType
                }
            ]
        };

    private static DamlModule ModuleWith(DamlTemplate template, params string[] siblingTemplateNames)
    {
        var dataTypes = new List<DamlDataType>
        {
            new()
            {
                Name = template.Name,
                Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
            }
        };
        foreach (var sibling in siblingTemplateNames)
        {
            dataTypes.Add(new DamlDataType
            {
                Name = sibling,
                Definition = new DamlRecordDefinition([])
            });
        }

        var templates = new List<DamlTemplate> { template };
        foreach (var sibling in siblingTemplateNames)
        {
            templates.Add(new DamlTemplate
            {
                Name = sibling,
                Choices = []
            });
        }

        return new DamlModule
        {
            Name = "Test.Module",
            Templates = templates,
            DataTypes = dataTypes,
            Interfaces = []
        };
    }

    private static string GenerateAndReadTemplate(DamlModule module, string templateName)
    {
        var dar = new DamlModelBuilder().WithModule(module).WithDependency(DamlPrim).Build();
        var generator = CreateGenerator();
        var files = generator.Generate(dar);
        var file = files.FirstOrDefault(f => f.RelativePath.EndsWith($"{templateName}.cs", StringComparison.Ordinal));
        file.Should().NotBeNull("the codegen should emit a file for template '{0}'", templateName);
        return file!.Content;
    }

    [Fact]
    public void Generate_should_emit_static_extensions_class_for_template_with_create_bearing_choice()
    {
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("public static class AgreementExtensions");
    }

    [Fact]
    public void Generate_should_not_emit_create_extensions_class_when_no_create_bearing_choices()
    {
        // Choices that return Unit / primitives don't go through the create-bearing
        // <Choice>Async path (which projects via FromCreatedContracts). Non-CID
        // returns are routed to a separate NonContractExtensions class, which
        // is verified by NonContractChoiceWrapperTests.
        var module = ModuleWith(
            Template("Counter", new DamlPrimitiveType(DamlPrimitive.Int64), choiceName: "GetCount"));

        var code = GenerateAndReadTemplate(module, "Counter");

        // The plain <TemplateName>Extensions class is only emitted for create-bearing
        // choices; primitive returns get the <TemplateName>NonContractExtensions class.
        code.Should().NotContain("public static class CounterExtensions");
        // GetCount's emission lives in CounterNonContractExtensions.
        code.Should().Contain("public static class CounterNonContractExtensions");
    }

    [Fact]
    public void Generate_should_emit_async_extension_with_contract_id_receiver()
    {
        // Default Controllers = Dynamic on the helper template — the analyzer
        // couldn't resolve them, so the wrapper falls back to a single
        // SubmitterInfo parameter (which implicitly converts from string /
        // Party for single-party callers).
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("public static async Task<ExerciseOutcome<RenewResult>> TryRenewAsync(");
        code.Should().Contain("this ContractId<Agreement> contractId");
        code.Should().Contain("ILedgerWriter client");
        // Dynamic-controller fallback shape: the wrapper takes a SubmitterInfo
        // parameter, never a string. Single-party callers stay one-liners via
        // SubmitterInfo's implicit conversion from string / Party.
        code.Should().Contain("SubmitterInfo submitter");
        code.Should().NotContain("string actAs");
        code.Should().NotContain("(Party)actAs");
    }

    [Fact]
    public void Generate_should_not_emit_default_workflow_id()
    {
        // B1: workflow IDs are correlation keys. A choice-derived default would bucket
        // every submission of the same choice under one ID, breaking observability.
        // The codegen must omit .WithWorkflowId(...) when the caller doesn't supply one,
        // not synthesise a default.
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().NotContain("\"exercise-renew\"");
        code.Should().NotContain("workflowId ??");
        code.Should().NotContain(".WithWorkflowId(");
        code.Should().Contain("client." + TrySubmitSingleArgumentOrder);
    }

    [Fact]
    public void Generate_should_fail_loudly_when_a_choice_argument_type_is_unmappable()
    {
        var unresolvedArgType = new DamlTypeApp(
            new DamlTypeRef("", "Test.Module", "Unresolved"),
            [new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var template = Template(
            "Agreement",
            ContractIdOf("Agreement"),
            choiceName: "Weird",
            argType: unresolvedArgType);
        var module = ModuleWith(template);

        FluentActions.Invoking(() => GenerateAndReadTemplate(module, "Agreement"))
            .Should().Throw<CodegenException>()
            .WithMessage("*Weird*");
    }

    [Fact]
    public void Generate_should_call_TrySubmitAndWaitForTransactionAsync_in_emitted_extension()
    {
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("client." + TrySubmitSingleArgumentOrder);
    }

    [Fact]
    public void Generate_should_project_success_through_the_choice_exercise_result_projector()
    {
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("private static ExerciseOutcome<RenewResult> ProjectRenewResult(TransactionResult tx, string contractId)");
        code.Should().Contain("return DecodeRenewResult(exercised.ExerciseResult);");
    }

    [Fact]
    public void Generate_should_fall_back_to_FromCreatedContracts_when_no_exercise_result_matches()
    {
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("var fromCreatedContracts = RenewResult.FromCreatedContracts(tx.CreatedContracts);");
    }

    [Fact]
    public void Generate_should_delegate_outcome_mapping_to_ProjectCommitted()
    {
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("return outcome.ProjectCommitted(tx => ProjectRenewResult(tx, contractId.Value));");
    }

    [Fact]
    public void Generate_should_not_emit_a_blind_unhandled_outcome_throw()
    {
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().NotContain("Unhandled outcome");
        code.Should().NotContain("ExerciseOutcome<TransactionResult>.DamlError");
    }

    [Fact]
    public void Generate_should_emit_async_extension_for_each_create_bearing_choice()
    {
        // Agreement template with two create-bearing choices.
        var template = new DamlTemplate
        {
            Name = "Agreement",
            Choices =
            [
                new DamlChoice
                {
                    Name = "ExecuteSwap",
                    Consuming = true,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = TupleType(ContractIdOf("Agreement"), ContractIdOf("SwapRecord"))
                },
                new DamlChoice
                {
                    Name = "Cancel",
                    Consuming = true,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = ContractIdOf("AgreementRecord")
                },
                new DamlChoice
                {
                    Name = "GetCount",
                    Consuming = false,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64)
                },
            ]
        };
        var module = ModuleWith(template, siblingTemplateNames: ["SwapRecord", "AgreementRecord"]);

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("public static async Task<ExerciseOutcome<ExecuteSwapResult>> TryExecuteSwapAsync(");
        code.Should().Contain("public static async Task<ExerciseOutcome<CancelResult>> TryCancelAsync(");
        // Non-creating choice (returns Int64, not a ContractId) is routed to the
        // NonContractExtensions class — not skipped — so it does emit
        // an async wrapper, just via the ExercisedEvents projector path. The
        // create-bearing AgreementExtensions class still excludes it.
        code.Should().Contain("public static class AgreementNonContractExtensions");
        code.Should().Contain("public static async Task<ExerciseOutcome<long>> TryGetCountAsync(");
    }

    [Fact]
    public void Generate_should_emit_ledger_abstractions_using_for_extensions()
    {
        // The emitted file must `using Daml.Ledger.Abstractions;` so the
        // ILedgerWriter parameter type resolves at consumer compile time.
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("using Daml.Ledger.Abstractions;");
    }
}
