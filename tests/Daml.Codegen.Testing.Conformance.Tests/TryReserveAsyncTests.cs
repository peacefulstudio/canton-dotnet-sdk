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

    [Fact]
    public async Task TryReserveAsync_returns_the_contract_id_the_Reserve_exercise_returned_when_no_created_contract_is_visible()
    {
        using var client = ClientCommitting([], ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.One>()
            .Which.Result.Ticket.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_returns_the_contract_id_the_Reserve_exercise_returned_over_a_different_visible_ticket()
    {
        using var client = ClientCommitting(
            [TicketCreated("unrelated-ticket")],
            ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.One>()
            .Which.Result.Ticket.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_through_SubmitterInfo_returns_the_contract_id_the_Reserve_exercise_returned()
    {
        using var client = ClientCommitting([], ReserveExercised("desk-cid", new DamlContractId("reserved-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), new SubmitterInfo(Patron),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.One>()
            .Which.Result.Ticket.Value.Should().Be("reserved-ticket");
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

        outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.One>()
            .Which.Result.Ticket.Value.Should().Be("reserved-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_keeps_reporting_Many_when_two_tickets_are_visible()
    {
        using var client = ClientCommitting(
            [TicketCreated("ticket-a"), TicketCreated("ticket-b")],
            ReserveExercised("desk-cid", new DamlContractId("ticket-b")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.Many>()
            .Which.ContractIds.Should().Equal("ticket-a", "ticket-b");
    }

    [Fact]
    public async Task TryReserveAsync_ignores_a_Reserve_exercise_on_another_desk_and_projects_the_visible_ticket()
    {
        using var client = ClientCommitting(
            [TicketCreated("visible-ticket")],
            ReserveExercised("other-desk-cid", new DamlContractId("other-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.One>()
            .Which.Result.Ticket.Value.Should().Be("visible-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_ignores_another_choice_exercised_on_the_same_desk_and_projects_the_visible_ticket()
    {
        using var client = ClientCommitting(
            [TicketCreated("visible-ticket")],
            DeskExercised("desk-cid", "Issue", new DamlContractId("issued-ticket")));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.One>()
            .Which.Result.Ticket.Value.Should().Be("visible-ticket");
    }

    [Fact]
    public async Task TryReserveAsync_returns_None_when_neither_an_exercise_nor_a_ticket_is_visible()
    {
        using var client = ClientCommitting([]);

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.None>();
    }

    [Fact]
    public async Task TryReserveAsync_returns_CommittedUndecodable_when_the_Reserve_exercise_result_is_no_contract_id()
    {
        using var client = ClientCommitting([], ReserveExercised("desk-cid", new DamlInt64(42)));

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<ReserveResult>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-reserve");
        undecodable.Message.Should().Be("Cannot cast DamlInt64 to DamlContractId");
        undecodable.SourceException.Should().BeOfType<InvalidCastException>();
    }
}
