// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ExerciseResultProjectionTests
{
    private sealed class OracleTemplate : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(new Identifier("pkg-v1", "Test.Oracle", "Oracle"), DamlTypeKind.Template, "oracle");
    }

    private sealed class QuotableInterface : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(new Identifier("pkg-v1", "Test.Oracle", "Quotable"), DamlTypeKind.Interface, "oracle");
    }

    private static Choice<TOwner, DamlUnit, TResult> ChoiceNamed<TOwner, TResult>(
        string name,
        Func<DamlValue, TResult> resultDecoder)
        where TOwner : IDamlType =>
        new()
        {
            Name = new ChoiceName(name),
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = resultDecoder,
            ArgumentJsonReader = (_, _) => throw new NotSupportedException(),
            ResultJsonReader = (_, _) => throw new NotSupportedException(),
        };

    private static Choice<OracleTemplate, DamlUnit, long> GetCount { get; } =
        ChoiceNamed<OracleTemplate, long>("GetCount", value => value.As<DamlInt64>().Value);

    private static ExercisedEvent Exercised(
        string contractId,
        string choiceName,
        DamlValue result,
        Identifier? templateId = null,
        Identifier? interfaceId = null) =>
        new(
            ContractId: contractId,
            TemplateId: templateId ?? new Identifier("pkg-v1", "Test.Oracle", "Oracle"),
            InterfaceId: interfaceId,
            ChoiceName: new ChoiceName(choiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: result,
            Consuming: false,
            ActingParties: [],
            WitnessParties: []);

    private static TransactionResult TransactionWith(params ExercisedEvent[] events) =>
        TransactionWithUpdateId("update-7", events);

    private static TransactionResult TransactionWithUpdateId(string updateId, params ExercisedEvent[] events) =>
        new(updateId, LedgerOffset.At(1), [], [], default)
        {
            ExercisedEvents = [.. events],
        };

    [Fact]
    public void ProjectChoiceResult_returns_One_decoded_from_the_exercise_on_the_target_contract()
    {
        var tx = TransactionWith(Exercised("contract-1", "GetCount", new DamlInt64(42)));

        var outcome = tx.ProjectChoiceResult(GetCount, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(42L);
    }

    private static Choice<OracleTemplate, DamlUnit, long> GetCountReadingJson { get; } = new()
    {
        Name = new ChoiceName("GetCount"),
        Consuming = false,
        ArgumentEncoder = _ => DamlUnit.Instance,
        ArgumentDecoder = _ => DamlUnit.Instance,
        ResultDecoder = value => value.As<DamlInt64>().Value,
        ArgumentJsonReader = (_, _) => throw new NotSupportedException(),
        ResultJsonReader = DamlLfJsonDecoders.ReadInt64,
    };

    [Fact]
    public void ProjectChoiceResult_decodes_a_carried_result_through_the_choice_json_reader()
    {
        var tx = TransactionWith(Exercised("contract-1", "GetCount", new DamlUndecodedJson("\"42\"")));

        var outcome = tx.ProjectChoiceResult(GetCountReadingJson, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(42L);
    }

    [Fact]
    public void ProjectChoiceResult_reports_CommittedUndecodable_when_a_carried_result_does_not_fit_the_choice_json_reader()
    {
        var tx = TransactionWith(Exercised("contract-1", "GetCount", new DamlUndecodedJson("\"forty-two\"")));

        var outcome = tx.ProjectChoiceResult(GetCountReadingJson, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>();
    }

    [Fact]
    public void ProjectChoiceResult_ignores_the_same_choice_exercised_on_another_contract()
    {
        var tx = TransactionWith(
            Exercised("other-contract", "GetCount", new DamlInt64(99)),
            Exercised("contract-1", "GetCount", new DamlInt64(42)));

        var outcome = tx.ProjectChoiceResult(GetCount, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(42L);
    }

    [Fact]
    public void ProjectChoiceResult_ignores_another_choice_exercised_on_the_target_contract()
    {
        var tx = TransactionWith(
            Exercised("contract-1", "Reset", new DamlInt64(99)),
            Exercised("contract-1", "GetCount", new DamlInt64(42)));

        var outcome = tx.ProjectChoiceResult(GetCount, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(42L);
    }

    [Fact]
    public void ProjectChoiceResult_matches_a_template_owner_through_package_id_drift()
    {
        var upgraded = new Identifier("pkg-v2", "Test.Oracle", "Oracle");
        var tx = TransactionWith(Exercised("contract-1", "GetCount", new DamlInt64(42), templateId: upgraded));

        var outcome = tx.ProjectChoiceResult(GetCount, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(42L);
    }

    [Fact]
    public void ProjectChoiceResult_ignores_an_exercise_on_a_template_with_another_entity_name()
    {
        var other = new Identifier("pkg-v1", "Test.Oracle", "Gauge");
        var tx = TransactionWith(Exercised("contract-1", "GetCount", new DamlInt64(99), templateId: other));

        var act = () => tx.ProjectChoiceResult(GetCount, "contract-1");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProjectChoiceResult_ignores_an_exercise_on_a_template_with_another_module_name()
    {
        var other = new Identifier("pkg-v1", "Other.Oracle", "Oracle");
        var tx = TransactionWith(Exercised("contract-1", "GetCount", new DamlInt64(99), templateId: other));

        var act = () => tx.ProjectChoiceResult(GetCount, "contract-1");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProjectChoiceResult_matches_an_interface_owner_on_the_exercised_interface_id()
    {
        var choice = ChoiceNamed<QuotableInterface, long>("Quote", value => value.As<DamlInt64>().Value);
        var viaInterface = new Identifier("pkg-v2", "Test.Oracle", "Quotable");
        var tx = TransactionWith(
            Exercised("contract-1", "Quote", new DamlInt64(1)),
            Exercised("contract-1", "Quote", new DamlInt64(42), interfaceId: viaInterface));

        var outcome = tx.ProjectChoiceResult(choice, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(42L);
    }

    [Fact]
    public void ProjectChoiceResult_ignores_a_template_id_only_exercise_for_an_interface_owner()
    {
        var choice = ChoiceNamed<QuotableInterface, long>("Quote", value => value.As<DamlInt64>().Value);
        var sameNameOnTheTemplate = new Identifier("pkg-v1", "Test.Oracle", "Quotable");
        var tx = TransactionWith(Exercised("contract-1", "Quote", new DamlInt64(99), templateId: sameNameOnTheTemplate));

        var act = () => tx.ProjectChoiceResult(choice, "contract-1");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProjectChoiceResult_ignores_an_exercise_through_another_interface_for_an_interface_owner()
    {
        var choice = ChoiceNamed<QuotableInterface, long>("Quote", value => value.As<DamlInt64>().Value);
        var otherInterface = new Identifier("pkg-v1", "Test.Oracle", "Gaugeable");
        var tx = TransactionWith(Exercised("contract-1", "Quote", new DamlInt64(99), interfaceId: otherInterface));

        var act = () => tx.ProjectChoiceResult(choice, "contract-1");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProjectChoiceResult_reports_CommittedUndecodable_when_the_result_does_not_decode()
    {
        var tx = TransactionWith(Exercised("contract-1", "GetCount", new DamlText("not a number")));

        var outcome = tx.ProjectChoiceResult(GetCount, "contract-1");

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("update-7");
        undecodable.SourceException.Should().BeOfType<InvalidCastException>();
        undecodable.Message.Should().Be(undecodable.SourceException!.Message);
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_null_UpdateId_when_the_transaction_declares_an_empty_one()
    {
        var tx = TransactionWithUpdateId("", Exercised("contract-1", "GetCount", new DamlText("not a number")));

        var outcome = tx.ProjectChoiceResult(GetCount, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Which.UpdateId.Should().BeNull();
    }

    [Fact]
    public void ProjectChoiceResult_lets_an_OperationCanceledException_from_the_decoder_propagate()
    {
        var cancelling = ChoiceNamed<OracleTemplate, long>(
            "GetCount",
            _ => throw new OperationCanceledException("stop"));
        var tx = TransactionWith(Exercised("contract-1", "GetCount", new DamlInt64(42)));

        var act = () => tx.ProjectChoiceResult(cancelling, "contract-1");

        act.Should().Throw<OperationCanceledException>().WithMessage("stop");
    }

    [Fact]
    public void ProjectChoiceResult_reports_One_null_for_an_Optional_None_result()
    {
        var optionalLabel = ChoiceNamed<OracleTemplate, string?>(
            "GetLabel",
            value => value.As<DamlOptional>().Value is { } label ? label.As<DamlText>().Value : null);
        var tx = TransactionWith(Exercised("contract-1", "GetLabel", new DamlOptional(null)));

        var outcome = tx.ProjectChoiceResult(optionalLabel, "contract-1");

        outcome.Should().BeOfType<ExerciseOutcome<string?>.One>().Which.Result.Should().BeNull();
    }

    [Fact]
    public void ProjectChoiceResult_throws_the_pinned_diagnostic_when_the_transaction_has_no_exercised_event()
    {
        var tx = TransactionWith();

        var act = () => tx.ProjectChoiceResult(GetCount, "contract-1");

        act.Should().Throw<InvalidOperationException>().WithMessage(
            "Submission succeeded but no 'GetCount' exercise on contract 'contract-1' was recorded on transaction update-7. " +
            "The transaction returned for this submission carries no exercised event for it. " +
            "Either a custom ILedgerWriter did not project the transaction's exercised events into TransactionResult.ExercisedEvents, " +
            "or the transaction was requested in a shape without exercised events (ACS_DELTA); " +
            "request the LEDGER_EFFECTS shape with verbose events.");
    }

    [Fact]
    public void ProjectChoiceResult_throws_the_pinned_diagnostic_without_an_update_id_when_the_transaction_declares_an_empty_one()
    {
        var tx = TransactionWithUpdateId("");

        var act = () => tx.ProjectChoiceResult(GetCount, "contract-1");

        act.Should().Throw<InvalidOperationException>().WithMessage(
            "Submission succeeded but no 'GetCount' exercise on contract 'contract-1' was recorded on a transaction that declares no update id. " +
            "The transaction returned for this submission carries no exercised event for it. " +
            "Either a custom ILedgerWriter did not project the transaction's exercised events into TransactionResult.ExercisedEvents, " +
            "or the transaction was requested in a shape without exercised events (ACS_DELTA); " +
            "request the LEDGER_EFFECTS shape with verbose events.");
    }

    [Fact]
    public void ProjectChoiceResult_throws_when_only_other_contracts_were_exercised()
    {
        var tx = TransactionWith(Exercised("other-contract", "GetCount", new DamlInt64(99)));

        var act = () => tx.ProjectChoiceResult(GetCount, "contract-1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Submission succeeded but no 'GetCount' exercise on contract 'contract-1' was recorded on transaction update-7. *");
    }

    [Fact]
    public void ProjectChoiceResult_rejects_a_null_transaction()
    {
        var act = () => ExerciseOutcomeProjection.ProjectChoiceResult(null!, GetCount, "contract-1");

        act.Should().Throw<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public void ProjectChoiceResult_rejects_a_null_choice()
    {
        var act = () => TransactionWith().ProjectChoiceResult<OracleTemplate, DamlUnit, long>(null!, "contract-1");

        act.Should().Throw<ArgumentNullException>().WithParameterName("choice");
    }

    [Fact]
    public void ProjectChoiceResult_rejects_a_null_contract_id()
    {
        var act = () => TransactionWith().ProjectChoiceResult(GetCount, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("contractId");
    }
}
