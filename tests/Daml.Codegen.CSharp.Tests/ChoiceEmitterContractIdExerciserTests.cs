// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterContractIdExerciserTests
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

    private static DamlPartyAnalysis StaticParties(params string[] fieldNames) =>
        DamlPartyAnalysis.Static(fieldNames.Select(n => (DamlPartyReference)new DamlPartyPayloadField(n)).ToList());

    private static DamlChoice Choice(string name, DamlType returnType, DamlPartyAnalysis? controllers = null) =>
        new()
        {
            Name = name,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Consuming = false,
            Controllers = controllers ?? DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private sealed record TemplateFixture(DamlTemplate Template, IReadOnlyList<DamlFieldDefinition> Fields);

    private static TemplateFixture Template(IReadOnlyList<DamlFieldDefinition> fields, params DamlChoice[] choices) =>
        new(
            new DamlTemplate
            {
                Name = "Vault",
                Choices = choices,
                Signatories = DamlPartyAnalysis.Dynamic,
                Observers = DamlPartyAnalysis.Dynamic,
            },
            fields);

    private static DamlTypeApp ContractIdOf(string templateName) =>
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef(LocalPackageId, "Main", templateName)]);

    private static (string ResultStructs, string Exercisers) Emit(TemplateFixture fixture)
    {
        var template = fixture.Template;
        var package = Package(template);
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var resolver = new StubResolver();
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());

        var structsSb = new StringBuilder();
        var structsIndent = new IndentWriter(structsSb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceResultStructs(structsIndent, template);

        var exerciserSb = new StringBuilder();
        var exerciserIndent = new IndentWriter(exerciserSb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceAsyncExercisersClass(exerciserIndent, template, template.Name, fixture.Fields, context.DataTypes);

        return (structsSb.ToString(), exerciserSb.ToString());
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_single_contract_id_choice_emits_a_single_cardinality_slot_property()
    {
        var template = Template([], Choice("Spawn", ContractIdOf("Token")));

        var (structs, _) = Emit(template);

        structs.Should().Contain("public sealed record SpawnResult(\n    ContractId<Token> Token\n)");
        structs.Should().Contain("public static ExerciseOutcome<SpawnResult> FromCreatedContracts");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_optional_contract_id_choice_emits_a_nullable_slot_property()
    {
        var template = Template([], Choice("Spawn", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [ContractIdOf("Token")])));

        var (structs, _) = Emit(template);

        structs.Should().Contain("public sealed record SpawnResult(\n    ContractId<Token>? Token\n)");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_list_contract_id_choice_emits_a_list_slot_property()
    {
        var template = Template([], Choice("Spawn", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.List), [ContractIdOf("Token")])));

        var (structs, _) = Emit(template);

        structs.Should().Contain("public sealed record SpawnResult(\n    IReadOnlyList<ContractId<Token>> Token\n)");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_create_bearing_choice_emits_a_typed_async_exerciser()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var (_, exercisers) = Emit(template);

        exercisers.Should().Contain("public static class VaultExtensions");
        exercisers.Should().Contain("public static async Task<ExerciseOutcome<SpawnResult>> TrySpawnAsync(");
        exercisers.Should().Contain("public static Task<ExerciseOutcome<SpawnResult>> TrySpawnAsync(");
        exercisers.Should().Contain("this ContractId<Vault> contractId,");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_non_creating_choice_emits_no_exerciser_class()
    {
        var template = Template([], Choice("Touch", new DamlPrimitiveType(DamlPrimitive.Unit)));

        var (structs, exercisers) = Emit(template);

        structs.Should().BeEmpty();
        exercisers.Should().BeEmpty();
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_create_bearing_choice_exerciser_accepts_optional_command_id_override()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var (_, exercisers) = Emit(template);

        exercisers.Should().Contain("CommandId? commandId = null,");
        exercisers.Should().Contain(TrySubmitSingleArgumentOrder);

        var idxWorkflowId = exercisers.IndexOf("string? workflowId = null,", StringComparison.Ordinal);
        var idxCommandId = exercisers.IndexOf("CommandId? commandId = null,", StringComparison.Ordinal);
        var idxCancellationToken = exercisers.IndexOf("CancellationToken cancellationToken = default)", StringComparison.Ordinal);
        idxWorkflowId.Should().BeLessThan(idxCommandId);
        idxCommandId.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_create_bearing_choice_exerciser_forwards_optional_timeout()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var (_, exercisers) = Emit(template);

        exercisers.Should().Contain("TimeSpan? timeout = null,");
        exercisers.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        var idxCommandId = exercisers.IndexOf("CommandId? commandId = null,", StringComparison.Ordinal);
        var idxTimeout = exercisers.IndexOf("TimeSpan? timeout = null,", StringComparison.Ordinal);
        var idxCancellationToken = exercisers.IndexOf("CancellationToken cancellationToken = default)", StringComparison.Ordinal);
        idxCommandId.Should().BeLessThan(idxTimeout);
        idxTimeout.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_create_bearing_choice_emits_one_submission_body_reached_by_both_overloads()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var (_, exercisers) = Emit(template);

        exercisers.Should().Contain("public static ExerciseCommand SpawnCommand(");
        exercisers.Should().Contain("this ContractId<Vault> contractId)");

        const string commandConstructionMarker = "new ExerciseCommand(";
        var firstConstruction = exercisers.IndexOf(commandConstructionMarker, StringComparison.Ordinal);
        firstConstruction.Should().BeGreaterThanOrEqualTo(0);
        exercisers.IndexOf(commandConstructionMarker, firstConstruction + 1, StringComparison.Ordinal).Should().Be(-1);

        const string commandCallMarker = "var command = contractId.SpawnCommand();";
        var firstCall = exercisers.IndexOf(commandCallMarker, StringComparison.Ordinal);
        firstCall.Should().BeGreaterThanOrEqualTo(0);
        exercisers.IndexOf(commandCallMarker, firstCall + 1, StringComparison.Ordinal).Should().Be(-1);

        exercisers.Should().Contain("SubmitterInfo submitter = owner;");
        exercisers.Should().Contain("return contractId.TrySpawnAsync(");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_contract_overload_forwards_timeout_positionally_to_the_contract_id_overload()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var (_, exercisers) = Emit(template);

        var delegationStart = exercisers.IndexOf("return contract.Id.TrySpawnAsync(", StringComparison.Ordinal);
        delegationStart.Should().BeGreaterThanOrEqualTo(0);
        var delegation = exercisers.Substring(delegationStart);

        var idxCommandId = delegation.IndexOf("commandId,", StringComparison.Ordinal);
        var idxTimeout = delegation.IndexOf("timeout,", StringComparison.Ordinal);
        var idxCancellationToken = delegation.IndexOf("cancellationToken);", StringComparison.Ordinal);
        idxCommandId.Should().BeGreaterThanOrEqualTo(0);
        idxTimeout.Should().BeGreaterThan(idxCommandId);
        idxCancellationToken.Should().BeGreaterThan(idxTimeout);
    }
}
