// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Disclosure;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class TryInspectAsyncTests
{
    private static readonly ContractId<Offer> Target = new("offer-cid");
    private static readonly Offer.Inspect Argument = new(new Party("bob"));
    private static readonly Party Alice = new("alice");

    private static ExercisedEvent InspectExercised(string contractId, DamlValue result) =>
        new(
            ContractId: contractId,
            TemplateId: new Identifier("any-package", "Disclosure", "Offer"),
            InterfaceId: null,
            ChoiceName: new ChoiceName("Inspect"),
            ChoiceArgument: DamlRecord.Create(),
            ExerciseResult: result,
            Consuming: false,
            ActingParties: [Alice],
            WitnessParties: [Alice]);

    private static FakeLedgerClient ClientCommitting(params ExercisedEvent[] exercised) =>
        new(_ => new ExerciseOutcome<TransactionResult>.One(
            new TransactionResult(
                UpdateId: "upd-inspect",
                CompletionOffset: LedgerOffset.At(1),
                CreatedContracts: [],
                ArchivedContractIds: [],
                CommandId: default)
            {
                ExercisedEvents = [.. exercised],
            }));

    [Fact]
    public async Task TryInspectAsync_decodes_the_exercise_result_of_the_target_contract()
    {
        using var client = ClientCommitting(InspectExercised("offer-cid", new DamlInt64(42)));

        var outcome = await Target.TryInspectAsync(client, Argument, Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(42L);
    }

    [Fact]
    public async Task TryInspectAsync_ignores_the_same_choice_exercised_on_another_contract()
    {
        using var client = ClientCommitting(
            InspectExercised("nested-offer-cid", new DamlInt64(7)),
            InspectExercised("offer-cid", new DamlInt64(42)));

        var outcome = await Target.TryInspectAsync(client, Argument, Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(42L);
    }

    [Fact]
    public async Task TryInspectAsync_reports_CommittedUndecodable_when_the_exercise_result_does_not_decode()
    {
        using var client = ClientCommitting(InspectExercised("offer-cid", new DamlText("not a number")));

        var outcome = await Target.TryInspectAsync(client, Argument, Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Which.UpdateId.Should().Be("upd-inspect");
    }

    [Fact]
    public async Task TryInspectAsync_throws_when_the_transaction_carries_no_exercised_event()
    {
        using var client = ClientCommitting();

        var act = () => Target.TryInspectAsync(client, Argument, Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Submission succeeded but no 'Inspect' exercise on contract 'offer-cid' was recorded on transaction upd-inspect. " +
                "The transaction returned for this submission carries no exercised event for it. " +
                "Either a custom ILedgerWriter did not project the transaction's exercised events into TransactionResult.ExercisedEvents, " +
                "or the transaction was requested in a shape without exercised events (ACS_DELTA); " +
                "request the LEDGER_EFFECTS shape with verbose events.");
    }
}
