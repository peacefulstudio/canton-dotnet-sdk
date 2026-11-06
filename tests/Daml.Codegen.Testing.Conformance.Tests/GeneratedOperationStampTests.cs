// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Codegen.Testing.Conformance.SubmitShapes;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class GeneratedOperationStampTests
{
    private static readonly ContractId<TicketDesk> Desk = new("desk-cid");
    private static readonly Party Patron = new("patron");

    private static FakeLedgerClient ClientRejectingEverything() =>
        new(
            transaction: _ => new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND",
                "desk-cid is not active",
                new Dictionary<string, string>()),
            create: _ => new ExerciseOutcome<object>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND",
                "marker is not active",
                new Dictionary<string, string>()));

    [Fact]
    public async Task A_generated_choice_helper_stamps_the_template_and_choice_on_a_failed_outcome()
    {
        using var client = ClientRejectingEverything();

        var outcome = await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.DamlError>();
        var operation = outcome.Operation.Should().BeOfType<LedgerOperation.Command>().Subject;
        operation.Name.Should().Be("Reserve");
        operation.TemplateId.ModuleName.Should().Be("SubmitShapes");
        operation.TemplateId.EntityName.Should().Be("TicketDesk");
        operation.Label.Should().Be("TicketDesk.Reserve");
    }

    [Fact]
    public async Task OneOrThrow_after_a_generated_choice_helper_names_the_operation_without_a_literal()
    {
        using var client = ClientRejectingEverything();

        var act = async () => await Desk.TryReserveAsync(client, new TicketDesk.Reserve(), Patron,
            cancellationToken: TestContext.Current.CancellationToken).OneOrThrowAsync();

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Operation.Should().Be("TicketDesk.Reserve");
        thrown.Message.Should().Be(
            "TicketDesk.Reserve: Daml error [InvalidGivenCurrentSystemStateResourceMissing/CONTRACT_NOT_FOUND]: desk-cid is not active");
    }

    [Fact]
    public async Task A_generated_create_helper_stamps_the_template_and_Create_on_a_failed_outcome()
    {
        using var client = ClientRejectingEverything();

        var outcome = await client.TryCreateAsync(
            new Marker(new Party("alice")), cancellationToken: TestContext.Current.CancellationToken);

        var operation = outcome.Operation.Should().BeOfType<LedgerOperation.Command>().Subject;
        operation.Name.Should().Be("Create");
        operation.TemplateId.ModuleName.Should().Be("RichTypes");
        operation.TemplateId.EntityName.Should().Be("Marker");
        operation.Label.Should().Be("Marker.Create");
    }

    [Fact]
    public async Task A_generated_create_helper_stamps_the_template_and_Create_on_a_successful_outcome()
    {
        using var client = new FakeLedgerClient(create: _ => new ExerciseOutcome<object>.One("marker-cid"));

        var outcome = await client.TryCreateAsync(
            new Marker(new Party("alice")), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>();
        outcome.Operation!.Label.Should().Be("Marker.Create");
    }
}
