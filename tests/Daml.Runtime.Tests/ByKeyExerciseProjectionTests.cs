// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ByKeyExerciseProjectionTests
{
    private sealed class LedgerAccount : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(new Identifier("pkg-v1", "Test.Ledger", "Account"), DamlTypeKind.Template, "ledger");
    }

    private static Choice<LedgerAccount, DamlUnit, long> GetBalance { get; } = new()
    {
        Name = new ChoiceName("GetBalance"),
        Consuming = false,
        ArgumentEncoder = _ => DamlUnit.Instance,
        ArgumentDecoder = _ => DamlUnit.Instance,
        ResultDecoder = value => value.As<DamlInt64>().Value,
        ArgumentJsonReader = (_, _) => throw new NotSupportedException(),
        ResultJsonReader = (_, _) => throw new NotSupportedException(),
    };

    private static ExercisedEvent Exercised(
        string contractId,
        string choiceName,
        DamlValue result,
        int? nodeId = null,
        int? lastDescendantNodeId = null,
        string entityName = "Account",
        string? packageName = "ledger") =>
        new(
            ContractId: contractId,
            TemplateId: new Identifier("pkg-v1", "Test.Ledger", entityName),
            InterfaceId: null,
            ChoiceName: new ChoiceName(choiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: result,
            Consuming: false,
            ActingParties: [],
            WitnessParties: [])
        {
            PackageName = packageName,
            NodeId = nodeId,
            LastDescendantNodeId = lastDescendantNodeId,
        };

    private static TransactionResult TransactionWith(params ExercisedEvent[] events) =>
        new("update-9", LedgerOffset.At(1), [], [], default)
        {
            ExercisedEvents = [.. events],
        };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProjectByKeyExercise_picks_the_root_exercise_over_a_nested_one_of_the_same_choice_whatever_the_event_order(
        bool nestedListedFirst)
    {
        var root = Exercised("root-contract", "GetBalance", new DamlInt64(10), nodeId: 0, lastDescendantNodeId: 1);
        var nested = Exercised("nested-contract", "GetBalance", new DamlInt64(99), nodeId: 1, lastDescendantNodeId: 1);
        var tx = nestedListedFirst ? TransactionWith(nested, root) : TransactionWith(root, nested);

        var outcome = tx.ProjectByKeyExercise(GetBalance);

        var exercise = outcome.Should().BeOfType<ExerciseOutcome<ByKeyExercise<LedgerAccount, long>>.One>().Which.Result;
        exercise.ResolvedContractId.Value.Should().Be("root-contract");
        exercise.Result.Should().Be(10L);
    }

    [Fact]
    public void ProjectByKeyExercise_resolves_a_lone_exercise_that_carries_no_node_ids()
    {
        var tx = TransactionWith(Exercised("only-contract", "GetBalance", new DamlInt64(7)));

        var outcome = tx.ProjectByKeyExercise(GetBalance);

        outcome.Should().BeOfType<ExerciseOutcome<ByKeyExercise<LedgerAccount, long>>.One>()
            .Which.Result.ResolvedContractId.Value.Should().Be("only-contract");
    }

    [Fact]
    public void ProjectByKeyExercise_throws_when_two_exercises_are_both_roots()
    {
        var first = Exercised("first-contract", "GetBalance", new DamlInt64(1), nodeId: 0, lastDescendantNodeId: 0);
        var second = Exercised("second-contract", "GetBalance", new DamlInt64(2), nodeId: 1, lastDescendantNodeId: 1);

        var act = () => TransactionWith(first, second).ProjectByKeyExercise(GetBalance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no single root exercise of 'GetBalance'*");
    }

    [Fact]
    public void ProjectByKeyExercise_throws_when_the_transaction_has_no_exercised_events()
    {
        var act = () => TransactionWith().ProjectByKeyExercise(GetBalance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*transaction update-9 has no single root exercise*");
    }

    [Fact]
    public void ProjectByKeyExercise_throws_when_the_root_exercises_another_choice()
    {
        var root = Exercised("root-contract", "Reset", new DamlInt64(10), nodeId: 0, lastDescendantNodeId: 1);
        var nested = Exercised("nested-contract", "GetBalance", new DamlInt64(99), nodeId: 1, lastDescendantNodeId: 1);

        var act = () => TransactionWith(root, nested).ProjectByKeyExercise(GetBalance);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProjectByKeyExercise_throws_when_the_root_belongs_to_another_template_of_the_same_package()
    {
        var root = Exercised("root-contract", "GetBalance", new DamlInt64(10), entityName: "Ledger");

        var act = () => TransactionWith(root).ProjectByKeyExercise(GetBalance);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProjectByKeyExercise_throws_when_the_root_names_no_package()
    {
        var root = Exercised("root-contract", "GetBalance", new DamlInt64(10), packageName: null);

        var act = () => TransactionWith(root).ProjectByKeyExercise(GetBalance);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProjectByKeyExercise_reports_CommittedUndecodable_when_the_root_result_does_not_decode()
    {
        var root = Exercised("root-contract", "GetBalance", new DamlText("not a number"));

        var outcome = TransactionWith(root).ProjectByKeyExercise(GetBalance);

        outcome.Should().BeOfType<ExerciseOutcome<ByKeyExercise<LedgerAccount, long>>.CommittedUndecodable>()
            .Which.UpdateId.Should().Be("update-9");
    }

    [Fact]
    public void ProjectByKeyExercise_decodes_a_carried_result_through_the_choice_json_reader()
    {
        var root = Exercised("root-contract", "GetBalance", new DamlUndecodedJson("\"42\""));
        var readingJson = new Choice<LedgerAccount, DamlUnit, long>
        {
            Name = new ChoiceName("GetBalance"),
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = value => value.As<DamlInt64>().Value,
            ArgumentJsonReader = (_, _) => throw new NotSupportedException(),
            ResultJsonReader = Serialization.DamlLfJsonDecoders.ReadInt64,
        };

        var outcome = TransactionWith(root).ProjectByKeyExercise(readingJson);

        outcome.Should().BeOfType<ExerciseOutcome<ByKeyExercise<LedgerAccount, long>>.One>()
            .Which.Result.Result.Should().Be(42L);
    }
}
