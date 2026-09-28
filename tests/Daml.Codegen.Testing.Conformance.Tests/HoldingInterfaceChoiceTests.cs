// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class HoldingInterfaceChoiceTests
{
    private static readonly ContractId<IHolding> Target = new("holding-cid");

    private static TransactionResult EmptyTransaction() =>
        new(
            UpdateId: "upd-1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: default);

    private static TransactionResult TransactionWith(params ExercisedEvent[] events) =>
        EmptyTransaction() with { ExercisedEvents = [.. events] };

    private static ExercisedEvent DescribeExercisedEvent(DamlValue exerciseResult) =>
        new(
            ContractId: "holding-cid",
            TemplateId: new Identifier("impl-pkg-id", "Impl.Holding", "Holding"),
            InterfaceId: IHolding.InterfaceId,
            ChoiceName: new ChoiceName("Describe"),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: exerciseResult,
            Consuming: false,
            ActingParties: [],
            WitnessParties: []);

    [Fact]
    public void Describe_argument_round_trips_through_its_record()
    {
        var argument = new Describe("balance: ");

        var restored = Describe.FromRecord(argument.ToRecord());

        restored.Should().Be(argument);
    }

    [Fact]
    public void DescribeCommand_carries_the_interface_id_and_the_choice_argument()
    {
        var command = Target.DescribeCommand(new Describe("balance: "));

        command.TemplateId.Should().Be(IHolding.InterfaceId);
        command.Choice.Should().Be(new ChoiceName("Describe"));
        command.ContractId.Value.Should().Be("holding-cid");
        command.ChoiceArgument.As<DamlRecord>()
            .GetRequiredField("prefix").As<DamlText>().Value.Should().Be("balance: ");
    }

    [Fact]
    public void ReissueCommand_carries_the_interface_id_and_the_choice_argument()
    {
        var command = Target.ReissueCommand(new Reissue(12.5m));

        command.TemplateId.Should().Be(IHolding.InterfaceId);
        command.Choice.Should().Be(new ChoiceName("Reissue"));
        command.ChoiceArgument.As<DamlRecord>()
            .GetRequiredField("newAmount").As<DamlNumeric>().Value.Should().Be(12.5m);
    }

    [Fact]
    public async Task TryDescribeAsync_submits_the_interface_typed_exercise_command()
    {
        var tx = TransactionWith(DescribeExercisedEvent(new DamlText("balance: 42")));
        using var client = new FakeLedgerClient(_ => new ExerciseOutcome<TransactionResult>.One(tx));

        var outcome = await Target.TryDescribeAsync(client, new Describe("balance: "), new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<string>.One>();
        client.LastSubmission!.CommandId.Should().NotBeNull(
            "a generated interface exerciser must route through the shared submission helper, which assigns a command id");
        var command = client.LastSubmission!.Commands.Should().ContainSingle().Which
            .Should().BeOfType<ExerciseCommand>().Subject;
        command.TemplateId.Should().Be(IHolding.InterfaceId);
        command.Choice.Should().Be(new ChoiceName("Describe"));
    }

    [Fact]
    public async Task TryDescribeAsync_decodes_the_committed_exercise_result_into_the_choices_typed_return()
    {
        var tx = TransactionWith(DescribeExercisedEvent(new DamlText("balance: 42")));
        using var client = new FakeLedgerClient(_ => new ExerciseOutcome<TransactionResult>.One(tx));

        var outcome = await Target.TryDescribeAsync(client, new Describe("balance: "), new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        var one = outcome.Should().BeOfType<ExerciseOutcome<string>.One>().Subject;
        one.Result.Should().Be("balance: 42");
    }

    [Fact]
    public async Task TryDescribeAsync_returns_CommittedUndecodable_when_the_committed_exercise_result_has_the_wrong_shape()
    {
        var tx = TransactionWith(DescribeExercisedEvent(new DamlInt64(42)));
        using var client = new FakeLedgerClient(_ => new ExerciseOutcome<TransactionResult>.One(tx));

        var outcome = await Target.TryDescribeAsync(client, new Describe("balance: "), new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<string>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.Message.Should().Be("Cannot cast DamlInt64 to DamlText");
        undecodable.SourceException.Should().BeOfType<InvalidCastException>();
    }

    [Fact]
    public void HoldingView_round_trips_through_its_record()
    {
        var view = new HoldingView(42m);

        HoldingView.FromRecord(view.ToRecord()).Should().Be(view);
    }
}
