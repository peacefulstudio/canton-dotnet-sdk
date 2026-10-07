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

public class ChoiceEmitterContractIdExerciserTests
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

    private static string Emit(TemplateFixture fixture, params DamlPackage[] dependencies)
    {
        var template = fixture.Template;
        var package = Package(template);
        var resolution = RealResolution.Of(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, dependencies);
        var context = resolution.Context;
        var resolver = resolution.Resolver;
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());

        var exerciserSb = new StringBuilder();
        var exerciserIndent = new IndentWriter(exerciserSb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceAsyncExercisersClass(exerciserIndent, template, template.Name, fixture.Fields);

        return exerciserSb.ToString();
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_single_contract_id_choice_returns_the_contract_id_through_the_descriptor_projection()
    {
        var template = Template([], Choice("Spawn", ContractIdOf("Token")));

        var exercisers = Emit(template);

        exercisers.Should().Contain("Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Token>>> TrySpawnAsync(");
        exercisers.Should().Contain("private static global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Token>> ProjectSpawnResult(global::Daml.Runtime.Contracts.TransactionResult tx, string contractId) =>\n        tx.ProjectChoiceResult(global::Test.Package.Main.Vault.ChoiceSpawn, contractId);");
        exercisers.Should().NotContain("SpawnResult.FromCreatedContracts");
        exercisers.Should().NotContain("DecodeSpawnResult");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_optional_contract_id_choice_returns_a_nullable_contract_id()
    {
        var template = Template([], Choice("Spawn", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [ContractIdOf("Token")])));

        var exercisers = Emit(template);

        exercisers.Should().Contain("ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Token>?>> TrySpawnAsync(");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_list_contract_id_choice_returns_the_read_only_list()
    {
        var template = Template([], Choice("Spawn", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.List), [ContractIdOf("Token")])));

        var exercisers = Emit(template);

        exercisers.Should().Contain("ExerciseOutcome<global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Token>>>> TrySpawnAsync(");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_tuple_contract_id_choice_returns_the_stdlib_tuple()
    {
        var tuple = new DamlTypeApp(
            new DamlTypeRef("daml-prim", "DA.Types", "Tuple2"),
            [ContractIdOf("Token"), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var template = Template([], Choice("Spawn", tuple));

        var exercisers = Emit(template, TestPackages.DamlPrim());

        exercisers.Should().Contain("ExerciseOutcome<global::Daml.Runtime.Stdlib.Tuple2<global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Token>, long>>> TrySpawnAsync(");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_create_bearing_choice_emits_a_typed_async_exerciser()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var exercisers = Emit(template);

        exercisers.Should().Contain("public static class VaultExtensions");
        exercisers.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Token>>> TrySpawnAsync(");
        exercisers.Should().Contain("public static global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Token>>> TrySpawnAsync(");
        exercisers.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Vault> contractId,");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_non_creating_choice_emits_no_exerciser_class()
    {
        var template = Template([], Choice("Touch", new DamlPrimitiveType(DamlPrimitive.Unit)));

        var exercisers = Emit(template);

        exercisers.Should().BeEmpty();
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_create_bearing_choice_exerciser_accepts_optional_command_id_override()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var exercisers = Emit(template);

        exercisers.Should().Contain("CommandId? commandId = null,");
        exercisers.Should().Contain(TrySubmitSingleArgumentOrder);

        var idxWorkflowId = exercisers.IndexOf("string? workflowId = null,", global::System.StringComparison.Ordinal);
        var idxCommandId = exercisers.IndexOf("CommandId? commandId = null,", global::System.StringComparison.Ordinal);
        var idxCancellationToken = exercisers.IndexOf("global::System.Threading.CancellationToken cancellationToken = default)", global::System.StringComparison.Ordinal);
        idxWorkflowId.Should().BeLessThan(idxCommandId);
        idxCommandId.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_create_bearing_choice_exerciser_forwards_optional_timeout()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var exercisers = Emit(template);

        exercisers.Should().Contain("global::System.TimeSpan? timeout = null,");
        exercisers.Should().Contain("client." + TrySubmitSingleArgumentOrder);

        var idxCommandId = exercisers.IndexOf("CommandId? commandId = null,", global::System.StringComparison.Ordinal);
        var idxTimeout = exercisers.IndexOf("global::System.TimeSpan? timeout = null,", global::System.StringComparison.Ordinal);
        var idxCancellationToken = exercisers.IndexOf("global::System.Threading.CancellationToken cancellationToken = default)", global::System.StringComparison.Ordinal);
        idxCommandId.Should().BeLessThan(idxTimeout);
        idxTimeout.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_create_bearing_choice_emits_one_submission_body_reached_by_both_overloads()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var exercisers = Emit(template);

        exercisers.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseCommand SpawnCommand(");
        exercisers.Should().Contain("this global::Daml.Runtime.Contracts.ContractId<global::Test.Package.Main.Vault> contractId)");

        const string commandConstructionMarker = "new global::Daml.Runtime.Commands.ExerciseCommand(";
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

        var exercisers = Emit(template);

        var delegationStart = exercisers.IndexOf("return contract.Id.TrySpawnAsync(", global::System.StringComparison.Ordinal);
        delegationStart.Should().BeGreaterThanOrEqualTo(0);
        var delegation = exercisers.Substring(delegationStart);

        var idxCommandId = delegation.IndexOf("commandId,", global::System.StringComparison.Ordinal);
        var idxTimeout = delegation.IndexOf("timeout,", global::System.StringComparison.Ordinal);
        var idxCancellationToken = delegation.IndexOf("cancellationToken);", global::System.StringComparison.Ordinal);
        idxCommandId.Should().BeGreaterThanOrEqualTo(0);
        idxTimeout.Should().BeGreaterThan(idxCommandId);
        idxCancellationToken.Should().BeGreaterThan(idxTimeout);
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_every_overload_declares_configure_between_timeout_and_cancellation_token()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var exercisers = Emit(template);

        Regex.Matches(exercisers, @"TimeSpan\? timeout = null,\s*" + Regex.Escape(ConfigureParameter) + @"\s*global::System.Threading.CancellationToken cancellationToken = default\)")
            .Should().HaveCount(4);
        Regex.Matches(exercisers, "global::System.Threading.CancellationToken cancellationToken = default\\)").Should().HaveCount(4);
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_submitter_info_overload_declares_configure_between_timeout_and_cancellation_token()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token")));

        var exercisers = Emit(template);

        exercisers.Should().Contain("SubmitterInfo submitter,");
        Regex.Matches(exercisers, @"TimeSpan\? timeout = null,\s*" + Regex.Escape(ConfigureParameter) + @"\s*global::System.Threading.CancellationToken cancellationToken = default\)")
            .Should().HaveCount(1);
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_party_overload_forwards_configure_to_the_submitter_info_overload()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var exercisers = Emit(template);

        exercisers.Should().MatchRegex(@"return contractId\.TrySpawnAsync\(\s*client,\s*submitter,\s*workflowId,\s*commandId,\s*timeout,\s*configure,\s*cancellationToken\);");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_contract_overload_forwards_configure_to_the_contract_id_overload()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var exercisers = Emit(template);

        exercisers.Should().MatchRegex(@"return contract\.Id\.TrySpawnAsync\(\s*client,[^;]*timeout,\s*configure,\s*cancellationToken\);");
    }

    [Fact]
    public void ChoiceEmitterContractIdExerciser_documents_configure_with_a_disclosure_example()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var exercisers = Emit(template);

        exercisers.Should().Contain("/// <param name=\"configure\">");
        exercisers.Should().Contain("s => s.WithDisclosedContracts(holding.Disclosure!)");
    }
}
