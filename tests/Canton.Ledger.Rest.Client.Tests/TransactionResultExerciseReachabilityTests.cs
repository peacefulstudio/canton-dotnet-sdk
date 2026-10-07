// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// This suite references <c>Canton.Ledger.Rest.Client</c> and no other transport package, so it
/// stands in for a REST-only consumer: the typed choice-result accessors compiling here is the
/// assertion that reading a choice return off a <see cref="TransactionResult"/> no longer obliges
/// a dependency on the gRPC transport.
/// </summary>
public class TransactionResultExerciseReachabilityTests
{
    private static readonly Identifier TemplateId = new("pkg", "Token.Holding", "Holding");

    [Fact]
    public void ExerciseResult_reads_a_typed_choice_return_for_a_consumer_that_takes_only_the_REST_client()
    {
        var result = Result(Exercised("GetTrailingTwap", new DamlNumeric(42.5m)));

        result.ExerciseResult<decimal>("GetTrailingTwap").Should().Be(42.5m);
    }

    [Fact]
    public void ExerciseResult_reads_a_record_choice_return_as_the_generated_record_type()
    {
        var result = Result(Exercised("Quote", DamlRecord.Create(new DamlField("bid", new DamlNumeric(1.5m)))));

        result.ExerciseResult<TwapQuote>("Quote").Should().Be(new TwapQuote(1.5m));
    }

    [Fact]
    public void ExerciseResult_reads_an_Optional_choice_return_as_a_nullable_value()
    {
        var result = Result(Exercised("MaybeTwap", DamlOptional.Some(new DamlNumeric(3m))));

        result.ExerciseResult<decimal?>("MaybeTwap").Should().Be(3m);
    }

    [Fact]
    public void AllExerciseResults_reads_every_typed_choice_return_for_that_same_consumer()
    {
        var result = Result(
            Exercised("GetTrailingTwap", new DamlNumeric(1m)),
            Exercised("GetTrailingTwap", new DamlNumeric(2m)));

        result.AllExerciseResults<decimal>("GetTrailingTwap").Should().Equal(1m, 2m);
    }

    [Fact]
    public void The_accessors_do_not_live_in_the_gRPC_transport_assembly()
    {
        typeof(TransactionResultExerciseExtensions).Assembly.GetName().Name
            .Should().Be("Canton.Ledger.Abstractions");
    }

    [Fact]
    public void ProjectChoiceResult_reads_the_choice_return_for_a_consumer_that_takes_only_the_REST_client()
    {
        var outcome = RestTransactionResultProjector.ProjectChoiceResult<decimal>(
            new ExerciseOutcome<TransactionResult>.One(Result(Exercised("GetTrailingTwap", new DamlNumeric(7m)))),
            new ExerciseCommand(TemplateId, new ContractId<TokenHolding>("00aa"), new ChoiceName("GetTrailingTwap"), DamlUnit.Instance));

        outcome.Should().BeOfType<ExerciseOutcome<decimal>.One>().Which.Result.Should().Be(7m);
    }

    private sealed record TwapQuote(decimal Bid) : IDamlRecord<TwapQuote>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("bid", new DamlNumeric(Bid)));

        public static TwapQuote FromRecord(DamlRecord record) => new(record.GetRequiredField("bid").As<DamlNumeric>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private sealed class TokenHolding : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(new Identifier("pkg", "Token.Holding", "Holding"), DamlTypeKind.Template, "token");
    }

    private static ExercisedEvent Exercised(string choiceName, DamlValue exerciseResult) =>
        new("00aa", TemplateId, null, new ChoiceName(choiceName), DamlUnit.Instance, exerciseResult, false, [], []);

    private static TransactionResult Result(params ExercisedEvent[] exercised) =>
        new(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: default)
        {
            ExercisedEvents = EquatableArray.Create(exercised),
        };
}
