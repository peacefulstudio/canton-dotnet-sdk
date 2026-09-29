// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;
using DamlInt64 = Daml.Runtime.Data.DamlInt64;
using DamlText = Daml.Runtime.Data.DamlText;
using DamlUnit = Daml.Runtime.Data.DamlUnit;
using DamlValue = Daml.Runtime.Data.DamlValue;
using Identifier = Daml.Runtime.Data.Identifier;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Behavior tests for the interface-choice exercise projector emitted by
/// <c>ChoiceEmitter.WriteInterfaceChoiceExerciseProjector</c>. Generates a standalone
/// interface with an Int64-returning choice, compiles it through Roslyn into an
/// in-memory assembly, then reflectively invokes the emitted
/// <c>Project&lt;Choice&gt;Result</c> helper against hand-built
/// <see cref="TransactionResult"/> fixtures — the interface-choice counterpart to
/// <see cref="NonContractChoiceProjectorTests"/>. Also pins that matching keys on
/// <see cref="ExercisedEvent.InterfaceId"/> rather than <c>TemplateId</c>, and that a
/// decode failure after a successful commit surfaces as
/// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/>, never
/// <see cref="ExerciseOutcome{T}.DamlError"/> or <see cref="ExerciseOutcome{T}.InfraError"/>.
/// </summary>
public class InterfaceChoiceProjectorTests
{
    private const string ModuleName = "Test.Oracle";
    private const string InterfaceEntityName = "Oracle";
    private const string ChoiceName = "GetCount";
    private const string PeekChoiceName = "Peek";
    private const string TouchChoiceName = "Touch";
    private const string PackageId = "test-package-id";

    private const string GeneratedNamespace = ModuleName;
    private const string InterfaceClassName = "IOracle";
    private const string InterfaceExtensionsClassName = "IOracleExtensions";
    private const string ProjectorMethodPrefix = "Project";
    private const string ProjectorMethodSuffix = "Result";

    private static readonly Identifier OracleInterfaceId = new(PackageId, ModuleName, InterfaceEntityName);
    private static readonly Identifier ConcreteTemplateId = new(PackageId, ModuleName, "OracleImpl");

    private static DamlChoice UnitArgChoice(string name, DamlType returnType) =>
        new()
        {
            Name = name,
            Consuming = false,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static IReadOnlyList<GeneratedFile> GenerateWrapperFiles()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceEntityName,
                    Choices =
                    [
                        UnitArgChoice(ChoiceName, new DamlPrimitiveType(DamlPrimitive.Int64)),
                        UnitArgChoice(PeekChoiceName, new DamlPrimitiveType(DamlPrimitive.Int64)),
                        UnitArgChoice(TouchChoiceName, new DamlPrimitiveType(DamlPrimitive.Unit)),
                    ],
                }
            ],
        };

        var package = new DamlPackage
        {
            PackageId = PackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        return CreateGenerator().Generate(dar);
    }

    private static ExerciseOutcome<TResult> InvokeProjector<TResult>(
        Assembly assembly, string choiceName, TransactionResult tx, string contractId)
    {
        var extensionsTypeName = $"{GeneratedNamespace}.{InterfaceExtensionsClassName}";
        var projectorMethodName = $"{ProjectorMethodPrefix}{choiceName}{ProjectorMethodSuffix}";

        var extensionsType = assembly.GetType(extensionsTypeName, throwOnError: true)!;
        var projector = extensionsType.GetMethod(
            projectorMethodName,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{projectorMethodName} not found on emitted extensions class");

        try
        {
            return (ExerciseOutcome<TResult>)projector.Invoke(null, [tx, contractId])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static ExercisedEvent InterfaceExercised(
        string contractId,
        string choiceName,
        DamlValue exerciseResult,
        Identifier? interfaceIdOverride = null) =>
        new(
            ContractId: contractId,
            TemplateId: ConcreteTemplateId,
            InterfaceId: interfaceIdOverride ?? OracleInterfaceId,
            ChoiceName: new Daml.Runtime.Commands.ChoiceName(choiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: exerciseResult,
            Consuming: false,
            ActingParties: [],
            WitnessParties: []);

    private static ExercisedEvent InterfaceExercisedGetCount(
        string contractId,
        DamlValue exerciseResult,
        Identifier? interfaceIdOverride = null) =>
        InterfaceExercised(contractId, ChoiceName, exerciseResult, interfaceIdOverride);

    private static ExercisedEvent TemplateOnlyExercisedGetCount(string contractId, DamlValue exerciseResult) =>
        new(
            ContractId: contractId,
            TemplateId: ConcreteTemplateId,
            InterfaceId: null,
            ChoiceName: new Daml.Runtime.Commands.ChoiceName(ChoiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: exerciseResult,
            Consuming: false,
            ActingParties: [],
            WitnessParties: []);

    private static TransactionResult TransactionWith(params ExercisedEvent[] events) =>
        new(
            UpdateId: "update-1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: default)
        {
            ExercisedEvents = [.. events],
        };

    private static readonly IReadOnlyList<GeneratedFile> WrapperFiles = GenerateWrapperFiles();
    private static readonly Assembly WrapperAssembly = EmitToAssembly(WrapperFiles);
    private static readonly string WrapperCode = WrapperFiles
        .Single(f => f.RelativePath.EndsWith($"{InterfaceClassName}.cs", StringComparison.Ordinal))
        .Content;

    [Fact]
    public void InterfaceChoiceProjector_projector_returns_One_when_matching_exercise_event_present()
    {
        var tx = TransactionWith(InterfaceExercisedGetCount("contract-1", new DamlInt64(42)));

        var outcome = InvokeProjector<long>(WrapperAssembly, ChoiceName, tx, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Subject;
        one.Result.Should().Be(42L);
    }

    [Fact]
    public void InterfaceChoiceProjector_projector_matches_through_interface_package_id_drift()
    {
        var driftedInterfaceId = new Identifier("upgraded-package-id", ModuleName, InterfaceEntityName);
        var tx = TransactionWith(InterfaceExercisedGetCount("contract-1", new DamlInt64(42), interfaceIdOverride: driftedInterfaceId));

        var outcome = InvokeProjector<long>(WrapperAssembly, ChoiceName, tx, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Subject;
        one.Result.Should().Be(42L);
    }

    [Fact]
    public void InterfaceChoiceProjector_projector_ignores_a_same_named_choice_exercised_directly_on_the_template()
    {
        var tx = TransactionWith(
            TemplateOnlyExercisedGetCount("contract-1", new DamlInt64(99)),
            InterfaceExercisedGetCount("contract-1", new DamlInt64(42)));

        var outcome = InvokeProjector<long>(WrapperAssembly, ChoiceName, tx, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Subject;
        one.Result.Should().Be(
            42L,
            "the template-level exercise carries no InterfaceId, so it must not satisfy the interface-typed projector even though the choice name and contract id both match");
    }

    [Fact]
    public void InterfaceChoiceProjector_projector_selects_only_the_event_whose_ChoiceName_matches_when_contract_and_interface_both_match()
    {
        var tx = TransactionWith(
            InterfaceExercised("contract-1", PeekChoiceName, new DamlInt64(99)),
            InterfaceExercisedGetCount("contract-1", new DamlInt64(42)));

        var outcome = InvokeProjector<long>(WrapperAssembly, ChoiceName, tx, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Subject;
        one.Result.Should().Be(
            42L,
            "the Peek event shares the contract id and interface id with GetCount but not the choice name, and is listed first, so it must not satisfy the GetCount projector");
    }

    [Fact]
    public void InterfaceChoiceProjector_projector_returns_CommittedUndecodable_when_the_exercise_result_has_the_wrong_shape()
    {
        var tx = TransactionWith(InterfaceExercisedGetCount("contract-1", new DamlText("not-a-number")));

        var outcome = InvokeProjector<long>(WrapperAssembly, ChoiceName, tx, "contract-1");

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("update-1");
        undecodable.Message.Should().Be("Cannot cast DamlText to DamlInt64");
        undecodable.SourceException.Should().BeOfType<InvalidCastException>();
    }

    [Fact]
    public void InterfaceChoiceProjector_projector_throws_self_contained_ExercisedEvents_diagnostic_when_no_exercise_event_present()
    {
        var tx = TransactionWith();

        var act = () => InvokeProjector<long>(WrapperAssembly, ChoiceName, tx, "contract-1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no 'GetCount' exercise on contract 'contract-1'*")
            .WithMessage("*TransactionResult.ExercisedEvents*")
            .WithMessage("*a custom ILedgerWriter did not project the transaction's exercised events*");
    }

    [Fact]
    public void InterfaceChoiceProjector_projector_throws_when_no_matching_contract_in_non_empty_events()
    {
        var tx = TransactionWith(InterfaceExercisedGetCount("other-contract", new DamlInt64(99)));

        var act = () => InvokeProjector<long>(WrapperAssembly, ChoiceName, tx, "contract-1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no 'GetCount' exercise on contract 'contract-1'*")
            .WithMessage("*TransactionResult.ExercisedEvents*")
            .WithMessage("*a custom ILedgerWriter did not project the transaction's exercised events*");
    }

    [Fact]
    public void InterfaceChoiceProjector_catch_clause_and_diagnostic_throw_root_qualify_System_exception_types()
    {
        WrapperCode.Should().Contain(
            "catch (global::System.Exception ex) when (ex is not global::System.OperationCanceledException)",
            "an unqualified catch clause would bind to a same-named Daml-defined type in this namespace instead of System.Exception");
        WrapperCode.Should().Contain(
            "throw new global::System.InvalidOperationException(",
            "an unqualified throw would bind to a same-named Daml-defined type in this namespace instead of System.InvalidOperationException");
        WrapperCode.Should().NotContain("catch (Exception ex)");
        WrapperCode.Should().NotContain("throw new InvalidOperationException(");
    }

    [Fact]
    public void InterfaceChoiceProjector_emits_DamlUnit_signature_for_a_Unit_returning_choice()
    {
        WrapperCode.Should().Contain($"public static async Task<ExerciseOutcome<DamlUnit>> Try{TouchChoiceName}Async(");
        WrapperCode.Should().Contain($"private static ExerciseOutcome<DamlUnit> Project{TouchChoiceName}Result(");
        WrapperCode.Should().Contain($"var decoded = {InterfaceClassName}.Choice{TouchChoiceName}.ResultDecoder!(exercised.ExerciseResult);");
        WrapperCode.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void InterfaceChoiceProjector_projector_decodes_a_Unit_returning_choice_to_the_DamlUnit_singleton_without_a_cast_failure()
    {
        var tx = TransactionWith(InterfaceExercised("contract-1", TouchChoiceName, DamlUnit.Instance));

        var outcome = InvokeProjector<DamlUnit>(WrapperAssembly, TouchChoiceName, tx, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.One>().Subject;
        one.Result.Should().Be(DamlUnit.Instance);
    }
}
