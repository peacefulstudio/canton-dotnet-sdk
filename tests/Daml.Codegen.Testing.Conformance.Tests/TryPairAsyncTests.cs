// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.SubmitShapes;
using Xunit;
using PairTuple = Daml.Runtime.Stdlib.Tuple2<Daml.Runtime.Contracts.ContractId<Daml.Codegen.Testing.Conformance.SubmitShapes.Ticket>, Daml.Runtime.Stdlib.Optional<Daml.Runtime.Contracts.ContractId<Daml.Codegen.Testing.Conformance.SubmitShapes.Ephemeral>>>;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class TryPairAsyncTests
{
    private static readonly ContractId<TicketDesk> Desk = new("desk-cid");
    private static readonly Party Patron = new("patron");

    private static ExercisedEvent PairExercised(DamlValue result) =>
        new(
            ContractId: "desk-cid",
            TemplateId: new Identifier("any-package", "SubmitShapes", "TicketDesk"),
            InterfaceId: null,
            ChoiceName: new ChoiceName("Pair"),
            ChoiceArgument: DamlRecord.Create(),
            ExerciseResult: result,
            Consuming: false,
            ActingParties: [Patron],
            WitnessParties: [Patron]);

    private static FakeLedgerClient ClientCommitting(ExercisedEvent exercised) =>
        new(_ => new ExerciseOutcome<TransactionResult>.One(
            new TransactionResult(
                UpdateId: "upd-pair",
                CompletionOffset: LedgerOffset.At(1),
                CreatedContracts: [],
                ArchivedContractIds: [],
                CommandId: default)
            {
                ExercisedEvents = [exercised],
            }));

    [Fact]
    public async Task TryPairAsync_returns_the_tuple_with_an_empty_Optional_when_the_ledger_omits_the_trailing_None()
    {
        var wireResult = DamlRecord.Create(new DamlField("_1", new DamlContractId("first-ticket")));
        using var client = ClientCommitting(PairExercised(wireResult));

        var outcome = await Desk.TryPairAsync(client, new TicketDesk.Pair(false), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<PairTuple>.One>().Subject.Result;
        result._1.Value.Should().Be("first-ticket");
        result._2.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task TryPairAsync_returns_the_tuple_with_an_empty_Optional_when_the_ledger_sends_an_explicit_None()
    {
        var wireResult = DamlRecord.Create(
            new DamlField("_1", new DamlContractId("first-ticket")),
            new DamlField("_2", DamlOptional.None));
        using var client = ClientCommitting(PairExercised(wireResult));

        var outcome = await Desk.TryPairAsync(client, new TicketDesk.Pair(false), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<PairTuple>.One>().Subject.Result;
        result._1.Value.Should().Be("first-ticket");
        result._2.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task TryPairAsync_returns_both_contract_ids_when_the_trailing_Optional_is_present()
    {
        var wireResult = DamlRecord.Create(
            new DamlField("_1", new DamlContractId("first-ticket")),
            new DamlField("_2", DamlOptional.Some(new DamlContractId("second-ephemeral"))));
        using var client = ClientCommitting(PairExercised(wireResult));

        var outcome = await Desk.TryPairAsync(client, new TicketDesk.Pair(true), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<PairTuple>.One>().Subject.Result;
        result._1.Value.Should().Be("first-ticket");
        result._2.GetValueOrThrow().Value.Should().Be("second-ephemeral");
    }

    [Fact]
    public async Task TryPairAsync_returns_CommittedUndecodable_when_the_non_optional_first_component_is_omitted()
    {
        using var client = ClientCommitting(PairExercised(DamlRecord.Create()));

        var outcome = await Desk.TryPairAsync(client, new TicketDesk.Pair(false), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<PairTuple>.CommittedUndecodable>()
            .Which.UpdateId.Should().Be("upd-pair");
    }
}
