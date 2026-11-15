// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcProjectionCoreMalformedValueTests
{
    public enum MalformedSlot
    {
        ChoiceArgument,
        ExerciseResult,
    }

    [Theory]
    [InlineData(MalformedSlot.ChoiceArgument)]
    [InlineData(MalformedSlot.ExerciseResult)]
    public void ProjectTransactionEvents_surfaces_an_exercised_event_with_an_unset_value_as_Unclassified_DecodeFailure_logged_as_a_malformed_response(
        MalformedSlot slot)
    {
        var logs = new CapturingLoggerFactory();
        var transaction = new Transaction { Offset = 70L, SynchronizerId = ActiveContractScenario.SynchronizerId };
        transaction.Events.Add(new Event { Exercised = ExercisedEventWithAnUnsetValueIn(slot) });

        var projected = GrpcContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, logs.CreateLogger("parity")).ToList();

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(70L));
        logs.Records.Should().ContainSingle().Which.Exception.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Be(
                "Malformed response from ledger: Received a proto Value with no value set (SumCase.None). "
                + "This typically indicates a malformed response from the ledger.");
    }

    private static ExercisedEvent ExercisedEventWithAnUnsetValueIn(MalformedSlot slot) => new()
    {
        ContractId = ActiveContractScenario.ContractId,
        TemplateId = new Identifier
        {
            PackageId = "tmpl-pkg",
            ModuleName = ActiveContractScenario.ModuleName,
            EntityName = ActiveContractScenario.MatchingEntityName,
        },
        PackageName = ActiveContractScenario.PackageName,
        Choice = TransactionEventScenario.ChoiceName,
        ChoiceArgument = slot == MalformedSlot.ChoiceArgument ? new Value() : new Value { Text = "argument" },
        ExerciseResult = slot == MalformedSlot.ExerciseResult ? new Value() : new Value { Text = "result" },
        Consuming = true,
        Offset = 71L,
    };
}
