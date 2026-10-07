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

namespace Daml.Codegen.Testing.Conformance.Tests;

public class TryReserveAsyncTests
{
    private static readonly ContractId<TicketDesk> Desk = new("desk-cid");
    private static readonly Party Patron = new("patron");

    private static ExercisedEvent ReserveExercised(string contractId, DamlValue result) =>
        DeskExercised(contractId, "Reserve", result);

    private static ExercisedEvent DeskExercised(string contractId, string choiceName, DamlValue result) =>
        new(
            ContractId: contractId,
            TemplateId: new Identifier("any-package", "SubmitShapes", "TicketDesk"),
            InterfaceId: null,
            ChoiceName: new ChoiceName(choiceName),
            ChoiceArgument: DamlRecord.Create(),
            ExerciseResult: result,
            Consuming: false,
            ActingParties: [Patron],
            WitnessParties: [Patron]);

    private static CreatedContract TicketCreated(string contractId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: new Identifier("any-package", "SubmitShapes", "Ticket"),
            Payload: DamlRecord.Create(),
            WitnessParties: [Patron],
            Signatories: [new Party("issuer")],
            Observers: [Patron]);

    private static FakeLedgerClient ClientCommitting(
        IReadOnlyList<CreatedContract> created, params ExercisedEvent[] exercised) =>
        new(_ => new ExerciseOutcome<TransactionResult>.One(
            new TransactionResult(
                UpdateId: "upd-reserve",
                CompletionOffset: LedgerOffset.At(1),
                CreatedContracts: [.. created],
                ArchivedContractIds: [],
                CommandId: default)
            {
                ExercisedEvents = [.. exercised],
            }));

    private const string NoReserveExerciseMessage =
        "Submission succeeded but no 'Reserve' exercise on contract 'desk-cid' was recorded on transaction upd-reserve. " +
        "The transaction returned for this submission carries no exercised event for it. " +
        "Either a custom ILedgerWriter did not project the transaction's exercised events into TransactionResult.ExercisedEvents, " +
        "or the transaction was requested in a shape without exercised events (ACS_DELTA); " +
        "request the LEDGER_EFFECTS shape with verbose events.";

    [Fact]
    public async Task TryReserveAsync_returns_the_contract_id_the_Reserve_exercise_returned_when_no_ticket_was_created()
    {
        using var client = ClientCommitting([], ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>()
            .Which.Result.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_returns_the_returned_contract_id_when_it_is_the_only_ticket_created()
    {
        using var client = ClientCommitting(
            [TicketCreated("reserved-ticket")],
            ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>()
            .Which.Result.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_returns_the_returned_contract_id_over_a_different_created_ticket()
    {
        using var client = ClientCommitting(
            [TicketCreated("unrelated-ticket")],
            ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>()
            .Which.Result.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_through_SubmitterInfo_returns_the_contract_id_the_Reserve_exercise_returned()
    {
        using var client = ClientCommitting([], ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), new SubmitterInfo(Patron),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>()
            .Which.Result.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_matches_the_Reserve_exercise_across_a_package_id_upgrade()
    {
        var exercised = ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")) with
        {
            TemplateId = new Identifier("upgraded-package", "SubmitShapes", "TicketDesk"),
        };
        using var client = ClientCommitting([], exercised);

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>()
            .Which.Result.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_reports_One_of_the_returned_ticket_when_two_tickets_are_created()
    {
        using var client = ClientCommitting(
            [TicketCreated("ticket-a"), TicketCreated("ticket-b")],
            ReserveExercised("desk-cid", new DamlContractId("ticket-b")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>()
            .Which.Result.Value.Should().Be("ticket-b");
    }

    [Fact]
    public async Task TryReserveAsync_ignores_a_Reserve_exercise_on_another_desk()
    {
        using var client = ClientCommitting(
            [],
            ReserveExercised("other-desk-cid", new DamlContractId("other-ticket")),
            ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>()
            .Which.Result.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_throws_when_only_another_desk_exercised_Reserve()
    {
        using var client = ClientCommitting(
            [TicketCreated("visible-ticket")],
            ReserveExercised("other-desk-cid", new DamlContractId("other-ticket")));

        var act = () => Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(NoReserveExerciseMessage);
    }

    [Fact]
    public async Task TryReserveAsync_throws_when_only_another_choice_was_exercised_on_the_same_desk()
    {
        using var client = ClientCommitting(
            [TicketCreated("visible-ticket")],
            DeskExercised("desk-cid", "Issue", new DamlContractId("issued-ticket")));

        var act = () => Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(NoReserveExerciseMessage);
    }

    [Fact]
    public async Task TryReserveAsync_throws_when_no_exercise_is_recorded_even_though_a_ticket_was_created()
    {
        using var client = ClientCommitting([TicketCreated("visible-ticket")]);

        var act = () => Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(NoReserveExerciseMessage);
    }

    [Fact]
    public async Task TryReserveAsync_throws_when_neither_an_exercise_nor_a_ticket_is_recorded()
    {
        using var client = ClientCommitting([]);

        var act = () => Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(NoReserveExerciseMessage);
    }

    [Fact]
    public async Task TryReserveAsync_returns_CommittedUndecodable_when_the_Reserve_exercise_result_is_no_contract_id()
    {
        using var client = ClientCommitting([], ReserveExercised("desk-cid", new DamlInt64(42)));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-reserve");
        undecodable.Message.Should().Be("Cannot cast DamlInt64 to DamlContractId");
        undecodable.SourceException.Should().BeOfType<InvalidCastException>();
    }
}
