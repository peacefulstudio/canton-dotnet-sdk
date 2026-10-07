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

public class TryReturnContractIdsAsyncTests
{
    private static readonly ContractId<GenericResults> Target = new("generic-cid");
    private static readonly Party Owner = new("owner");

    private static FakeLedgerClient ClientCommitting(DamlValue exerciseResult) =>
        new(_ => new ExerciseOutcome<TransactionResult>.One(
            new TransactionResult(
                UpdateId: "upd-return",
                CompletionOffset: LedgerOffset.At(1),
                CreatedContracts: [],
                ArchivedContractIds: [],
                CommandId: default)
            {
                ExercisedEvents =
                [
                    new ExercisedEvent(
                        ContractId: "generic-cid",
                        TemplateId: new Identifier("any-package", "RichTypes", "GenericResults"),
                        InterfaceId: null,
                        ChoiceName: new ChoiceName("ReturnContractIds"),
                        ChoiceArgument: DamlRecord.Create(),
                        ExerciseResult: exerciseResult,
                        Consuming: false,
                        ActingParties: [Owner],
                        WitnessParties: [Owner]),
                ],
            }));

    [Fact]
    public async Task TryReturnContractIdsAsync_returns_every_contract_id_the_exercise_result_lists_in_order()
    {
        using var client = ClientCommitting(
            new DamlList([new DamlContractId("returned-b"), new DamlContractId("returned-a")]));

        var outcome = await Target.TryReturnContractIdsAsync(client, new GenericResults.ReturnContractIds(), Owner,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<IReadOnlyList<ContractId<GenericResults>>>.One>()
            .Which.Result.Select(c => c.Value).Should().Equal("returned-b", "returned-a");
    }

    [Fact]
    public async Task TryReturnContractIdsAsync_returns_an_empty_list_when_the_exercise_result_lists_none()
    {
        using var client = ClientCommitting(new DamlList([]));

        var outcome = await Target.TryReturnContractIdsAsync(client, new GenericResults.ReturnContractIds(), Owner,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<IReadOnlyList<ContractId<GenericResults>>>.One>()
            .Which.Result.Should().BeEmpty();
    }
}
