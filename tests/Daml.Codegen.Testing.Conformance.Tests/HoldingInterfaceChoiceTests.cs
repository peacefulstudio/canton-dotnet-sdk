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
    public async Task TryDescribeAsync_ignores_the_same_choice_exercised_on_the_template_without_the_interface()
    {
        var templateOnly = DescribeExercisedEvent(new DamlText("wrong")) with { InterfaceId = null };
        var tx = TransactionWith(templateOnly, DescribeExercisedEvent(new DamlText("balance: 42")));
        using var client = new FakeLedgerClient(_ => new ExerciseOutcome<TransactionResult>.One(tx));

        var outcome = await Target.TryDescribeAsync(client, new Describe("balance: "), new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<string>.One>().Which.Result.Should().Be("balance: 42");
    }

    [Fact]
    public async Task TryDescribeAsync_throws_when_the_transaction_carries_no_exercised_event()
    {
        using var client = new FakeLedgerClient(_ => new ExerciseOutcome<TransactionResult>.One(EmptyTransaction()));

        var act = () => Target.TryDescribeAsync(client, new Describe("balance: "), new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Submission succeeded but no 'Describe' exercise on contract 'holding-cid' was recorded on transaction upd-1. " +
                "The transaction returned for this submission carries no exercised event for it. " +
                "Either a custom ILedgerWriter did not project the transaction's exercised events into TransactionResult.ExercisedEvents, " +
                "or the transaction was requested in a shape without exercised events (ACS_DELTA); " +
                "request the LEDGER_EFFECTS shape with verbose events.");
    }

    [Fact]
    public async Task TryReissueAsync_decodes_the_returned_contract_id_through_the_choice_descriptor()
    {
        var reissued = DescribeExercisedEvent(new DamlContractId("reissued-cid")) with { ChoiceName = new ChoiceName("Reissue") };
        using var client = new FakeLedgerClient(_ => new ExerciseOutcome<TransactionResult>.One(TransactionWith(reissued)));

        var outcome = await Target.TryReissueAsync(client, new Reissue(12.5m), new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<IHolding>>.One>()
            .Which.Result.Value.Should().Be("reissued-cid");
    }

    [Fact]
    public async Task TryArchiveAsync_decodes_a_unit_result_to_the_DamlUnit_singleton()
    {
        var archived = DescribeExercisedEvent(DamlUnit.Instance) with { ChoiceName = new ChoiceName("Archive"), Consuming = true };
        using var client = new FakeLedgerClient(_ => new ExerciseOutcome<TransactionResult>.One(TransactionWith(archived)));

        var outcome = await Target.TryArchiveAsync(client, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.One>().Which.Result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void HoldingView_round_trips_through_its_record()
    {
        var view = new HoldingView(42m);

        HoldingView.FromRecord(view.ToRecord()).Should().Be(view);
    }
}
