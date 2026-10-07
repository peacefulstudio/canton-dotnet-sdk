// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over the hand-built choice-result projection, run against every
/// transport's transaction-result projector through one shared set of test bodies. It pins the
/// decisions both transports must agree on: that a decoded return is reported as
/// <see cref="ExerciseOutcome{T}.One"/> (a return that decodes to <c>null</c>, such as an
/// <c>Optional</c> <c>None</c>, is <c>One(null)</c>), that the exercise is found by contract id, owner
/// and choice, that a failed submission keeps its error arm, that a committed transaction whose
/// exercised result cannot be decoded is reported as
/// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/> carrying its update id, and that a committed
/// transaction without the exercise throws. Every row that projects a committed transaction is also
/// pinned against <c>ExerciseOutcomeProjection.ProjectChoiceResult</c> run on the same transaction,
/// so the hand-built and generated paths cannot drift apart; the rows that pass a failed outcome
/// through have no committed transaction to project. The wire decoding that produces the
/// <see cref="TransactionResult"/> stays per-transport.
/// </summary>
public abstract class ChoiceResultProjectionParityTests
{
    /// <summary>A choice whose return decodes to <c>null</c> for the projected result type.</summary>
    protected static readonly ChoiceName NullDecodingChoice = new("Acknowledge");

    /// <summary>A choice whose return decodes to a value for the projected result type.</summary>
    protected static readonly ChoiceName PartyReturningChoice = new("GetOwner");

    /// <summary>The party a successful <see cref="PartyReturningChoice"/> exercise returns.</summary>
    protected const string ReturnedParty = "alice::ns1";

    private const string TargetContractId = "00holding";

    private static readonly RuntimeIdentifier HoldingTemplateId = new("tmpl-pkg", "Sample.Token", "Holding");

    private static readonly RuntimeIdentifier HoldingInterfaceId = new("iface-pkg", "Sample.Token", "Holdable");

    private static readonly ChoiceName RecordReturningChoice = new("Split");

    private static readonly ChoiceName OptionalReturningChoice = new("GetLimit");

    private static readonly ChoiceName CountReturningChoice = new("GetCount");

    private const string WireContractId = "00wire";

    private static readonly RuntimeIdentifier GenericResultsId = GenericResults.TemplateId;

    static ChoiceResultProjectionParityTests()
    {
        ChoiceResultParityBindings.RegisterAll();
    }

    private const string NoExercisedEventMessage =
        "Submission succeeded but no 'GetOwner' exercise on contract '00holding' was recorded on transaction upd-1.";

    /// <summary>
    /// Projects <paramref name="outcome"/> into this transport's typed choice-result outcome for the
    /// choice <paramref name="command"/> exercised.
    /// </summary>
    protected abstract ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, ExerciseCommand command);

    [Fact]
    public void ProjectChoiceResult_reports_a_choice_return_that_decodes_to_null_as_One_null()
    {
        var transaction = Exercised(NullDecodingChoice, DamlUnit.Instance);

        var projected = ProjectChoiceResult<DamlRecord>(transaction, Command(NullDecodingChoice));

        projected.Should().BeOfType<ExerciseOutcome<DamlRecord>.One>().Subject.Result.Should().BeNull();
        projected.Should().Be(Generated<DamlRecord>(transaction, NullDecodingChoice, _ => null!));
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_choice_return_that_decodes_to_a_value_as_One()
    {
        var transaction = Exercised(PartyReturningChoice, new DamlParty(ReturnedParty));

        var projected = ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        projected.Should().BeOfType<ExerciseOutcome<Party>.One>()
            .Subject.Result.Should().Be((Party)ReturnedParty);
        projected.Should().Be(Generated(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_passes_a_DamlError_outcome_through()
    {
        var projected = ProjectChoiceResult<DamlRecord>(
            new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
                "SOME_ERROR",
                "boom",
                new Dictionary<string, string>()),
            Command(NullDecodingChoice));

        projected.Should().BeOfType<ExerciseOutcome<DamlRecord>.DamlError>()
            .Subject.ErrorId.Should().Be("SOME_ERROR");
    }

    [Fact]
    public void ProjectChoiceResult_passes_an_InfraError_outcome_through()
    {
        var projected = ProjectChoiceResult<DamlRecord>(
            new ExerciseOutcome<TransactionResult>.InfraError(
                new TransportStatus.Http(HttpStatusCode.ServiceUnavailable), "unavailable"),
            Command(NullDecodingChoice));

        projected.Should().BeOfType<ExerciseOutcome<DamlRecord>.InfraError>()
            .Subject.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.ServiceUnavailable));
    }

    [Fact]
    public void ProjectChoiceResult_passes_a_CommittedUndecodable_outcome_through()
    {
        var sourceException = new FormatException("Cannot parse wire Int64 value 'x' as a 64-bit integer.");

        var projected = ProjectChoiceResult<DamlRecord>(
            new ExerciseOutcome<TransactionResult>.CommittedUndecodable("upd-1", "cannot decode", sourceException),
            Command(NullDecodingChoice));

        var undecodable = projected.Should().BeOfType<ExerciseOutcome<DamlRecord>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.Message.Should().Be("cannot decode");
        undecodable.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void ProjectChoiceResult_matches_the_exercise_on_the_target_contract_when_the_same_choice_ran_on_another_contract()
    {
        var transaction = Exercised(
            Event(PartyReturningChoice, new DamlParty("other::ns1"), contractId: "00other"),
            Event(PartyReturningChoice, new DamlParty(ReturnedParty)));

        var handBuilt = ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<Party>.One>().Subject.Result.Should().Be((Party)ReturnedParty);
        handBuilt.Should().Be(Generated(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_does_not_match_the_same_choice_exercised_only_on_another_contract()
    {
        var transaction = Exercised(Event(PartyReturningChoice, new DamlParty(ReturnedParty), contractId: "00other"));

        var act = () => ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(NoExercisedEventMessage);
        ShouldThrowNoExercisedEvent(() => Generated(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_throws_when_the_transaction_carries_no_exercised_event()
    {
        var transaction = Exercised();

        var act = () => ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(NoExercisedEventMessage);
        ShouldThrowNoExercisedEvent(() => Generated(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_throws_when_only_another_choice_was_exercised_on_the_target_contract()
    {
        var transaction = Exercised(NullDecodingChoice, DamlUnit.Instance);

        var act = () => ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(NoExercisedEventMessage);
        ShouldThrowNoExercisedEvent(() => Generated(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_throws_when_the_target_contract_was_exercised_on_another_template()
    {
        var transaction = Exercised(Event(
            PartyReturningChoice,
            new DamlParty(ReturnedParty),
            template: new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "Other")));

        var act = () => ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(NoExercisedEventMessage);
        ShouldThrowNoExercisedEvent(() => Generated(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_reads_the_first_exercise_when_the_choice_ran_twice_on_the_target_contract()
    {
        var transaction = Exercised(
            Event(PartyReturningChoice, new DamlParty(ReturnedParty)),
            Event(PartyReturningChoice, new DamlParty("second::ns1")));

        var handBuilt = ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<Party>.One>().Subject.Result.Should().Be((Party)ReturnedParty);
        handBuilt.Should().Be(Generated(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_matches_an_interface_choice_by_the_interface_id_the_command_names()
    {
        var transaction = Exercised(Event(
            PartyReturningChoice,
            new DamlParty(ReturnedParty),
            template: new RuntimeIdentifier("impl-pkg", "Sample.Impl", "Holding"),
            interfaceId: HoldingInterfaceId));

        var handBuilt = ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice, HoldingInterfaceId));

        handBuilt.Should().BeOfType<ExerciseOutcome<Party>.One>().Subject.Result.Should().Be((Party)ReturnedParty);
        handBuilt.Should().Be(GeneratedOn<HoldingInterface, Party>(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_ignores_the_package_id_when_matching_the_exercised_template()
    {
        var transaction = Exercised(Event(
            PartyReturningChoice,
            new DamlParty(ReturnedParty),
            template: new RuntimeIdentifier("tmpl-pkg-v2", "Sample.Token", "Holding")));

        var handBuilt = ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<Party>.One>().Subject.Result.Should().Be((Party)ReturnedParty);
        handBuilt.Should().Be(Generated(transaction, PartyReturningChoice, PartyDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_decodes_a_record_result_into_the_generated_record_type()
    {
        var transaction = Exercised(RecordReturningChoice, new HoldingSplit(7, "seven").ToRecord());

        var handBuilt = ProjectChoiceResult<HoldingSplit>(transaction, Command(RecordReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<HoldingSplit>.One>()
            .Subject.Result.Should().Be(new HoldingSplit(7, "seven"));
        handBuilt.Should().Be(Generated(
            transaction, RecordReturningChoice, value => HoldingSplit.FromRecord(value.As<DamlRecord>())));
    }

    [Fact]
    public void ProjectChoiceResult_decodes_an_Optional_None_result_as_One_null()
    {
        var transaction = Exercised(OptionalReturningChoice, DamlOptional.None);

        var handBuilt = ProjectChoiceResult<long?>(transaction, Command(OptionalReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<long?>.One>().Subject.Result.Should().BeNull();
        handBuilt.Should().Be(Generated(transaction, OptionalReturningChoice, OptionalInt64Decoder));
    }

    [Fact]
    public void ProjectChoiceResult_decodes_an_Optional_Some_result_as_One_of_the_carried_value()
    {
        var transaction = Exercised(OptionalReturningChoice, DamlOptional.Some(new DamlInt64(5)));

        var handBuilt = ProjectChoiceResult<long?>(transaction, Command(OptionalReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<long?>.One>().Subject.Result.Should().Be(5L);
        handBuilt.Should().Be(Generated(transaction, OptionalReturningChoice, OptionalInt64Decoder));
    }

    [Fact]
    public void ProjectChoiceResult_decodes_an_empty_Optional_result_into_an_Optional_target_as_Optional_None()
    {
        var transaction = Exercised(OptionalReturningChoice, DamlOptional.None);

        var handBuilt = ProjectChoiceResult<Optional<long>>(transaction, Command(OptionalReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<Optional<long>>.One>()
            .Subject.Result.Should().Be(new Optional<long>.None());
        handBuilt.Should().Be(Generated(transaction, OptionalReturningChoice, OptionalInt64ChainDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_decodes_a_present_Optional_result_into_an_Optional_target_as_Optional_Some()
    {
        var transaction = Exercised(OptionalReturningChoice, DamlOptional.Some(new DamlInt64(5)));

        var handBuilt = ProjectChoiceResult<Optional<long>>(transaction, Command(OptionalReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<Optional<long>>.One>()
            .Subject.Result.Should().Be(new Optional<long>.Some(5));
        handBuilt.Should().Be(Generated(transaction, OptionalReturningChoice, OptionalInt64ChainDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_decodes_an_empty_outer_Optional_into_a_nested_Optional_None()
    {
        var transaction = Exercised(OptionalReturningChoice, DamlOptional.None);

        var handBuilt = ProjectChoiceResult<Optional<Optional<string>>>(transaction, Command(OptionalReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<Optional<Optional<string>>>.One>()
            .Subject.Result.Should().Be(new Optional<Optional<string>>.None());
        handBuilt.Should().Be(Generated(transaction, OptionalReturningChoice, NestedOptionalTextDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_decodes_a_Some_of_None_into_a_nested_Optional_that_keeps_both_levels()
    {
        var transaction = Exercised(OptionalReturningChoice, DamlOptionalChain.Some(DamlOptionalChain.None));

        var handBuilt = ProjectChoiceResult<Optional<Optional<string>>>(transaction, Command(OptionalReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<Optional<Optional<string>>>.One>()
            .Subject.Result.Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.None()));
        handBuilt.Should().Be(Generated(transaction, OptionalReturningChoice, NestedOptionalTextDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_decodes_a_Some_of_Some_into_a_nested_Optional_that_keeps_both_levels()
    {
        var transaction = Exercised(
            OptionalReturningChoice, DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("x"))));

        var handBuilt = ProjectChoiceResult<Optional<Optional<string>>>(transaction, Command(OptionalReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<Optional<Optional<string>>>.One>()
            .Subject.Result.Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.Some("x")));
        handBuilt.Should().Be(Generated(transaction, OptionalReturningChoice, NestedOptionalTextDecoder));
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_value_of_the_wrong_Daml_type_as_CommittedUndecodable()
    {
        var transaction = Exercised(CountReturningChoice, new DamlText("not-a-number"));

        var handBuilt = ProjectChoiceResult<long>(transaction, Command(CountReturningChoice));

        var undecodable = handBuilt.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.SourceException.Should().BeOfType<NotSupportedException>();
        Generated(transaction, CountReturningChoice, value => value.As<DamlInt64>().Value)
            .Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>();
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_null_update_id_when_the_transaction_declares_an_empty_one_and_the_result_does_not_decode()
    {
        var transaction = WithoutUpdateId(Exercised(CountReturningChoice, new DamlText("not-a-number")));

        var handBuilt = ProjectChoiceResult<long>(transaction, Command(CountReturningChoice));

        handBuilt.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Which.UpdateId.Should().BeNull();
        Generated(transaction, CountReturningChoice, value => value.As<DamlInt64>().Value)
            .Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Which.UpdateId.Should().BeNull();
    }

    [Fact]
    public void ProjectChoiceResult_names_no_update_id_in_the_diagnostic_when_the_transaction_declares_an_empty_one()
    {
        var transaction = WithoutUpdateId(Exercised());

        var act = () => ProjectChoiceResult<Party>(transaction, Command(PartyReturningChoice));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(
            "Submission succeeded but no 'GetOwner' exercise on contract '00holding' was recorded on a transaction that declares no update id.");
        ((Action)(() => Generated(transaction, PartyReturningChoice, PartyDecoder)))
            .Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(
                "Submission succeeded but no 'GetOwner' exercise on contract '00holding' was recorded on a transaction that declares no update id.");
    }

    [Fact]
    public void ProjectChoiceResult_reports_a_result_type_without_a_Daml_mapping_as_CommittedUndecodable()
    {
        var transaction = Exercised(PartyReturningChoice, new DamlParty(ReturnedParty));

        var projected = ProjectChoiceResult<ResultTypeWithoutDamlMapping>(transaction, Command(PartyReturningChoice));

        var undecodable = projected
            .Should().BeOfType<ExerciseOutcome<ResultTypeWithoutDamlMapping>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.SourceException.Should().BeOfType<NotSupportedException>();
        Generated<ResultTypeWithoutDamlMapping>(
                transaction, PartyReturningChoice, value => value.FromDamlValue<ResultTypeWithoutDamlMapping>()!)
            .Should().BeOfType<ExerciseOutcome<ResultTypeWithoutDamlMapping>.CommittedUndecodable>();
    }

    /// <summary>
    /// Exercises the choice <paramref name="exercise"/> names through this transport's hand-built
    /// <c>TryExerciseAsync</c>, answering <see cref="WireExercise.Result"/> in this transport's own
    /// wire format.
    /// </summary>
    protected abstract Task<ExerciseOutcome<TResult>> ExerciseOnWireAsync<TResult>(WireExercise exercise);

    /// <summary>One committed exercise as a transport receives it, with its result in both wire forms.</summary>
    /// <param name="TemplateId">The template the exercised contract belongs to.</param>
    /// <param name="InterfaceId">The interface the choice was exercised through, if any.</param>
    /// <param name="Choice">The exercised choice name.</param>
    /// <param name="ArgumentLfJson">The choice argument as Daml-LF JSON.</param>
    /// <param name="Result">The choice result as the ledger's value messages carry it.</param>
    /// <param name="ResultLfJson">The choice result as Daml-LF JSON.</param>
    protected sealed record WireExercise(
        RuntimeIdentifier TemplateId,
        RuntimeIdentifier? InterfaceId,
        string Choice,
        string ArgumentLfJson,
        DamlValue Result,
        string ResultLfJson)
    {
        /// <summary>The identifier the exercise command names: the interface when there is one.</summary>
        public RuntimeIdentifier CommandTarget => InterfaceId ?? TemplateId;

        /// <summary>The contract the exercise ran on.</summary>
        public string ContractId => WireContractId;
    }

    /// <summary>An exercise of <paramref name="choice"/> on the shared <c>GenericResults</c> template.</summary>
    protected static WireExercise OnGenericResults(
        string choice, string argumentLfJson, DamlValue result, string resultLfJson) =>
        new(GenericResultsId, null, choice, argumentLfJson, result, resultLfJson);

    /// <summary>An exercise of <see cref="ChoiceResultParityBindings.Pick"/> on a registered synthetic binding.</summary>
    protected static WireExercise PickOn(
        RuntimeIdentifier templateId, RuntimeIdentifier? interfaceId, DamlValue result, string resultLfJson) =>
        new(templateId, interfaceId, ChoiceResultParityBindings.Pick.Value, "{}", result, resultLfJson);

    /// <summary>An exercise of <see cref="ChoiceResultParityBindings.Pick"/> answering <see cref="WireTails"/>.</summary>
    protected static WireExercise PickRecordOn(RuntimeIdentifier templateId) =>
        PickOn(templateId, null, WireTails.ToRecord(), WireTailsLfJson);

    /// <summary>The record the wire-level rows answer with.</summary>
    protected static readonly OptionalTails WireTails =
        new((Party)"party::alice", null, new TrailingNote("inner", null), null);

    /// <summary>Daml-LF JSON of <see cref="WireTails"/>.</summary>
    protected const string WireTailsLfJson =
        """{"owner": "party::alice", "midNote": null, "inner": {"text": "inner", "remark": null}, "tailNote": null}""";

    private static async Task<TResult> OneResultOfAsync<TResult>(
        Func<Task<ExerciseOutcome<TResult>>> exercise) =>
        (await exercise()).Should().BeOfType<ExerciseOutcome<TResult>.One>().Subject.Result;

    private async Task<TResult> OneResultMatchingGeneratedAsync<TOwner, TArg, TResult>(
        WireExercise exercise, Choice<TOwner, TArg, TResult> generated)
        where TOwner : IDamlType
    {
        var handBuilt = await OneResultOfAsync(() => ExerciseOnWireAsync<TResult>(exercise));
        var generatedPath = Exercised(Event(
                new ChoiceName(exercise.Choice), exercise.Result, exercise.ContractId, exercise.TemplateId, exercise.InterfaceId))
            .Result.ProjectChoiceResult(generated, exercise.ContractId);

        generatedPath.Should().BeOfType<ExerciseOutcome<TResult>.One>().Subject.Result.Should().BeEquivalentTo(handBuilt, options => options.ComparingRecordsByValue());
        return handBuilt;
    }

    [Fact]
    public async Task Row1_a_record_result_decodes_into_the_generated_record()
    {
        var result = await OneResultMatchingGeneratedAsync(
            new WireExercise(
                OptionalTails.TemplateId, null, "EchoOptionalTails", "{}", WireTails.ToRecord(), WireTailsLfJson),
            OptionalTails.ChoiceEchoOptionalTails);

        result.Should().Be(new OptionalTails((Party)"party::alice", null, new TrailingNote("inner", null), null));
    }

    [Fact]
    public async Task Row2_an_Optional_Text_None_result_is_One_null()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnOptionalText", """{"wantSome": false}""", DamlOptional.None, "null"),
            GenericResults.ChoiceReturnOptionalText);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Row2_an_Optional_Text_Some_result_is_One_of_the_text()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnOptionalText", """{"wantSome": true}""", DamlOptional.Some(new DamlText("x")), "\"x\""),
            GenericResults.ChoiceReturnOptionalText);

        result.Should().Be("x");
    }

    [Fact]
    public async Task Row3_a_nested_Optional_Some_of_None_keeps_both_levels()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnNestedOptional",
                """{"outer": true, "inner": false}""",
                DamlOptionalChain.Some(DamlOptionalChain.None),
                "[[]]"),
            GenericResults.ChoiceReturnNestedOptional);

        result.Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.None()));
    }

    [Fact]
    public async Task Row3_a_nested_Optional_Some_of_Some_read_as_a_nullable_string_is_the_carried_text()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<string?>(
            OnGenericResults(
                "ReturnNestedOptional",
                """{"outer": true, "inner": true}""",
                DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("x"))),
                """[["x"]]""")));

        result.Should().Be("x");
    }

    [Fact]
    public async Task Row3_a_nested_Optional_Some_of_None_read_as_a_nullable_string_is_null()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<string?>(
            OnGenericResults(
                "ReturnNestedOptional",
                """{"outer": true, "inner": false}""",
                DamlOptionalChain.Some(DamlOptionalChain.None),
                "[[]]")));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Row3_a_nested_Optional_None_read_as_a_nullable_string_is_null()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<string?>(
            OnGenericResults(
                "ReturnNestedOptional",
                """{"outer": false, "inner": false}""",
                DamlOptionalChain.None,
                "[]")));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Row4_an_Either_result_decodes_to_the_chosen_arm()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnEither",
                """{"wantRight": true}""",
                DamlVariant.Create("Right", new DamlInt64(1)),
                """{"tag": "Right", "value": "1"}"""),
            GenericResults.ChoiceReturnEither);

        result.Should().Be(new Either<string, long>.Right(1));
    }

    [Fact]
    public async Task Row4_a_Tuple2_result_decodes_to_both_components()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnTuple",
                "{}",
                DamlRecord.Create(DamlField.Create("_1", new DamlText("gold")), DamlField.Create("_2", new DamlInt64(42))),
                """{"_1": "gold", "_2": "42"}"""),
            GenericResults.ChoiceReturnTuple);

        result.Should().Be(new Tuple2<string, long>("gold", 42));
    }

    [Fact]
    public async Task Row4_a_Map_result_decodes_to_its_entries()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnGenMap",
                "{}",
                new DamlGenMap([(new DamlText("gold"), new DamlInt64(1)), (new DamlText("silver"), new DamlInt64(2))]),
                """[["gold", "1"], ["silver", "2"]]"""),
            GenericResults.ChoiceReturnGenMap);

        result.Should().BeEquivalentTo(new Dictionary<string, long> { ["gold"] = 1, ["silver"] = 2 });
    }

    [Fact]
    public async Task Row4_a_TextMap_result_decodes_to_its_entries()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnTextMap",
                "{}",
                new DamlTextMap(new Dictionary<string, DamlValue> { ["gold"] = new DamlInt64(1), ["silver"] = new DamlInt64(2) }),
                """{"gold": "1", "silver": "2"}"""),
            GenericResults.ChoiceReturnTextMap);

        result.Should().BeEquivalentTo(new Dictionary<string, long> { ["gold"] = 1, ["silver"] = 2 });
    }

    [Fact]
    public async Task Row4_a_Set_result_decodes_to_its_elements()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnSet",
                "{}",
                DamlRecord.Create(DamlField.Create(
                    "map", new DamlGenMap([(new DamlInt64(1), DamlUnit.Instance), (new DamlInt64(2), DamlUnit.Instance)]))),
                """{"map": [["1", {}], ["2", {}]]}"""),
            GenericResults.ChoiceReturnSet);

        result.Should().Be(new Set<long>([1, 2]));
    }

    [Fact]
    public async Task Row4_a_NonEmpty_result_decodes_to_its_head_and_tail()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnNonEmpty",
                "{}",
                DamlRecord.Create(
                    DamlField.Create("hd", new DamlInt64(1)),
                    DamlField.Create("tl", new DamlList([new DamlInt64(2), new DamlInt64(3)]))),
                """{"hd": "1", "tl": ["2", "3"]}"""),
            GenericResults.ChoiceReturnNonEmpty);

        result.Should().Be(new NonEmpty<long>(1, [2, 3]));
    }

    [Fact]
    public async Task Row4_a_list_of_contract_ids_decodes_in_order()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                    "ReturnContractIds",
                    "{}",
                    new DamlList([new DamlContractId("00a"), new DamlContractId("00b")]),
                    """["00a", "00b"]"""),
            GenericResults.ChoiceReturnContractIds);

        result.Should().Equal(new ContractId<GenericResults>("00a"), new ContractId<GenericResults>("00b"));
    }

    [Fact]
    public async Task Row5_a_variant_result_decodes_the_arm_with_a_payload()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnOutcome",
                """{"wantWin": true}""",
                DamlVariant.Create(
                    "Win",
                    DamlRecord.Create(DamlField.Create("prize", new DamlNumeric(12.5m)), DamlField.Create("tier", new DamlText("gold")))),
                """{"tag": "Win", "value": {"prize": "12.5", "tier": "gold"}}"""),
            GenericResults.ChoiceReturnOutcome);

        result.Should().Be(new Outcome.Win(new Outcome_Win(12.5m, "gold")));
    }

    [Fact]
    public async Task Row5_a_variant_result_decodes_the_arm_without_a_payload()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnOutcome",
                """{"wantWin": false}""",
                DamlVariant.Create("Pending", DamlUnit.Instance),
                """{"tag": "Pending", "value": {}}"""),
            GenericResults.ChoiceReturnOutcome);

        result.Should().Be(new Outcome.Pending());
    }

    [Fact]
    public async Task Row6_an_enum_result_decodes_to_the_enum_member()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnSuit", "{}", DamlEnum.Create("Hearts"), "\"Hearts\""),
            GenericResults.ChoiceReturnSuit);

        result.Should().Be(Suit.Hearts);
    }

    [Fact]
    public async Task Row6_an_Optional_enum_result_decodes_to_the_member_when_present()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnOptionalSuit", "{}", DamlOptional.Some(DamlEnum.Create("Hearts")), "\"Hearts\""),
            GenericResults.ChoiceReturnOptionalSuit);

        result.Should().Be(Suit.Hearts);
    }

    [Fact]
    public async Task Row6_an_Optional_enum_result_decodes_to_null_when_absent()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnOptionalSuit", "{}", DamlOptional.None, "null"),
            GenericResults.ChoiceReturnOptionalSuit);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Row7_a_generic_variant_result_decodes_with_its_type_argument()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnSlot",
                "{}",
                DamlVariant.Create("Filled", new DamlInt64(7)),
                """{"tag": "Filled", "value": "7"}"""),
            GenericResults.ChoiceReturnSlot);

        result.Should().Be(new Slot<long>.Filled(7));
    }

    [Fact]
    public async Task Row7_a_generic_record_result_decodes_with_its_type_argument()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnBox",
                "{}",
                DamlRecord.Create(DamlField.Create("item", new DamlInt64(5))),
                """{"item": "5"}"""),
            GenericResults.ChoiceReturnBox);

        result.Should().Be(new Box<long>(5));
    }

    [Fact]
    public async Task Row7_a_list_of_records_decodes_in_order()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnProfiles",
                "{}",
                new DamlList(
                [
                    DamlRecord.Create(DamlField.Create("nickname", new DamlText("alice")), DamlField.Create("level", new DamlInt64(3))),
                    DamlRecord.Create(DamlField.Create("nickname", new DamlText("bob")), DamlField.Create("level", new DamlInt64(4))),
                ]),
                """[{"nickname": "alice", "level": "3"}, {"nickname": "bob", "level": "4"}]"""),
            GenericResults.ChoiceReturnProfiles);

        result.Should().Equal(new Profile("alice", 3), new Profile("bob", 4));
    }

    [Fact]
    public async Task Row7_a_map_of_variants_decodes_to_its_entries()
    {
        var result = await OneResultMatchingGeneratedAsync(
            OnGenericResults(
                "ReturnOutcomes",
                "{}",
                new DamlGenMap(
                [
                    (new DamlText("won"), DamlVariant.Create(
                        "Win",
                        DamlRecord.Create(DamlField.Create("prize", new DamlNumeric(12.5m)), DamlField.Create("tier", new DamlText("gold"))))),
                    (new DamlText("waiting"), DamlVariant.Create("Pending", DamlUnit.Instance)),
                ]),
                """[["won", {"tag": "Win", "value": {"prize": "12.5", "tier": "gold"}}], ["waiting", {"tag": "Pending", "value": {}}]]"""),
            GenericResults.ChoiceReturnOutcomes);

        result.Should().BeEquivalentTo(new Dictionary<string, Outcome>
        {
            ["won"] = new Outcome.Win(new Outcome_Win(12.5m, "gold")),
            ["waiting"] = new Outcome.Pending(),
        });
    }

    [Fact]
    public async Task Row8_an_interface_choice_result_decodes_through_the_interface_id()
    {
        var result = await OneResultMatchingGeneratedAsync(
            new WireExercise(
            Asset.TemplateId, IHolding.InterfaceId, "Grade", "{}", DamlEnum.Create("Hearts"), "\"Hearts\""),
            IHolding.ChoiceGrade);

        result.Should().Be(Suit.Hearts);
    }

    [Fact]
    public async Task Row8_an_interface_choice_also_declared_by_the_template_decodes_through_the_interface()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<string>(PickOn(
            ChoiceResultParityBindings.OrderedTemplate,
            ChoiceResultParityBindings.OrderedInterface,
            new DamlText("x"),
            "\"x\"")));

        result.Should().Be("via-interface");
    }

    [Fact]
    public async Task Row9_a_DamlValue_result_is_the_raw_value_though_a_descriptor_would_decode_it()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<DamlValue>(PickOn(
            ChoiceResultParityBindings.DescriptorDecoded, null, new DamlText("raw"), "\"raw\"")));

        result.Should().Be(new DamlText("raw"));
    }

    [Fact]
    public async Task Row9_a_DamlValue_result_is_the_raw_value_for_a_generated_choice()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<DamlValue>(OnGenericResults(
            "ReturnSuit", "{}", DamlEnum.Create("Hearts"), "\"Hearts\"")));

        result.Should().Be(DamlEnum.Create("Hearts"));
    }

    [Fact]
    public async Task Row10_a_result_type_the_descriptor_does_not_produce_decodes_through_FromDamlValue()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<Optional<string>>(OnGenericResults(
            "ReturnOptionalText", """{"wantSome": true}""", DamlOptional.Some(new DamlText("ink")), "\"ink\"")));

        result.Should().Be(new Optional<string>.Some("ink"));
    }

    [Fact]
    public async Task Row11_a_result_no_FromDamlValue_reading_decodes_is_CommittedUndecodable_naming_the_unresolved_choice()
    {
        var outcome = await ExerciseOnWireAsync<Either<string, long>>(PickOn(
            ChoiceResultParityBindings.Unbound,
            null,
            DamlVariant.Create("Right", new DamlInt64(1)),
            """{"tag": "Right", "value": "1"}"""));

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<Either<string, long>>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.SourceException.Should().BeOfType<NotSupportedException>();
        undecodable.Message.Should().StartWith(
            "Choice 'Pick' of 'parity-pkg-unbound:Parity:Unbound' did not resolve to a generated result decoder: "
            + "no generated binding for it is registered. Decoding its result as "
            + "Daml.Runtime.Stdlib.Either`2[System.String,System.Int64] through FromDamlValue failed: ");
    }

    [Fact]
    public async Task Row12_a_record_result_without_a_registered_binding_decodes_through_FromDamlValue()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<OptionalTails>(
            PickRecordOn(ChoiceResultParityBindings.Unbound)));

        result.Should().Be(WireTails);
    }

    [Fact]
    public async Task Row13_two_registered_versions_that_the_event_package_matches_neither_decode_through_FromDamlValue()
    {
        var unmatched = new RuntimeIdentifier(
            ChoiceResultParityBindings.UnregisteredVersionPackageId,
            ChoiceResultParityBindings.VersionOne.ModuleName,
            ChoiceResultParityBindings.VersionOne.EntityName);

        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<OptionalTails>(PickRecordOn(unmatched)));

        result.Should().Be(WireTails);
    }

    [Fact]
    public async Task Row15_one_package_id_registered_by_two_declaring_types_decodes_through_FromDamlValue()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<OptionalTails>(
            PickRecordOn(ChoiceResultParityBindings.Duplicated)));

        result.Should().Be(WireTails);
    }

    [Fact]
    public async Task Row19_a_value_of_the_wrong_Daml_type_for_a_resolved_descriptor_is_CommittedUndecodable()
    {
        var outcome = await ExerciseOnWireAsync<long>(PickOn(
            ChoiceResultParityBindings.Int64Decoded, null, new DamlText("not-a-number"), "\"not-a-number\""));

        outcome.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Subject.UpdateId.Should().Be("upd-1");
    }

    [Fact]
    public async Task Row14_two_registered_versions_decode_through_the_one_whose_package_id_the_event_names()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<OptionalTails>(
            PickRecordOn(ChoiceResultParityBindings.VersionTwo)));

        result.Owner.Should().Be((Party)"via-v2");
    }

    [Fact]
    public async Task Row14_the_first_registered_version_decodes_through_its_own_descriptor()
    {
        var result = await OneResultOfAsync(() => ExerciseOnWireAsync<OptionalTails>(
            PickRecordOn(ChoiceResultParityBindings.VersionOne)));

        result.Owner.Should().Be((Party)"via-v1");
    }

    private static Party PartyDecoder(DamlValue value) => Party.FromDamlValue(value.As<DamlParty>());

    private static long? OptionalInt64Decoder(DamlValue value) =>
        value.As<DamlOptional>().Value is { } carried ? carried.As<DamlInt64>().Value : null;

    private static Optional<long> OptionalInt64ChainDecoder(DamlValue value) =>
        Optional<long>.FromChainValue(value, carried => carried.As<DamlInt64>().Value);

    private static Optional<Optional<string>> NestedOptionalTextDecoder(DamlValue value) =>
        Optional<Optional<string>>.FromChainValue(
            value,
            inner => Optional<string>.FromChainValue(inner, carried => carried.As<DamlText>().Value));

    private static void ShouldThrowNoExercisedEvent(Action generatedPath) =>
        generatedPath.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(NoExercisedEventMessage);

    private static ExerciseCommand Command(ChoiceName choice, RuntimeIdentifier? exercisedOn = null) =>
        new(exercisedOn ?? HoldingTemplateId, new ContractId<HoldingOwner>(TargetContractId), choice, DamlUnit.Instance);

    private static ExerciseOutcome<TResult> Generated<TResult>(
        ExerciseOutcome<TransactionResult>.One transaction, ChoiceName choice, Func<DamlValue, TResult> resultDecoder) =>
        GeneratedOn<HoldingOwner, TResult>(transaction, choice, resultDecoder);

    private static ExerciseOutcome<TResult> GeneratedOn<TOwner, TResult>(
        ExerciseOutcome<TransactionResult>.One transaction, ChoiceName choice, Func<DamlValue, TResult> resultDecoder)
        where TOwner : IDamlType
    {
        var descriptor = new Choice<TOwner, DamlUnit, TResult>
        {
            Name = choice,
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = resultDecoder,
            ArgumentJsonReader = (_, _) => throw new NotSupportedException(),
            ResultJsonReader = (_, _) => throw new NotSupportedException(),
        };
        return transaction.Result.ProjectChoiceResult(descriptor, TargetContractId);
    }

    private static ExercisedEvent Event(
        ChoiceName choice,
        DamlValue exerciseResult,
        string contractId = TargetContractId,
        RuntimeIdentifier? template = null,
        RuntimeIdentifier? interfaceId = null) =>
        new(
            contractId,
            template ?? HoldingTemplateId,
            interfaceId,
            choice,
            DamlUnit.Instance,
            exerciseResult,
            false,
            [(Party)ReturnedParty],
            [(Party)ReturnedParty]);

    private static ExerciseOutcome<TransactionResult>.One Exercised(ChoiceName choice, DamlValue exerciseResult) =>
        Exercised(Event(choice, exerciseResult));

    private static ExerciseOutcome<TransactionResult>.One WithoutUpdateId(ExerciseOutcome<TransactionResult>.One transaction) =>
        new(transaction.Result with { UpdateId = "" });

    private static ExerciseOutcome<TransactionResult>.One Exercised(params ExercisedEvent[] exercised) =>
        new(new TransactionResult(
            "upd-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))
        {
            ExercisedEvents = EquatableArray.Create(exercised),
        });

    /// <summary>A result type <c>FromDamlValue</c> has no mapping for.</summary>
    public sealed class ResultTypeWithoutDamlMapping;

    /// <summary>The template the exercised choices belong to.</summary>
    public sealed class HoldingOwner : IDamlType
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(new RuntimeIdentifier("tmpl-pkg", "Sample.Token", "Holding"), DamlTypeKind.Template, "sample");
    }

    /// <summary>The interface the interface-exercised choices belong to.</summary>
    public sealed class HoldingInterface : IDamlType
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(new RuntimeIdentifier("iface-pkg", "Sample.Token", "Holdable"), DamlTypeKind.Interface, "sample");
    }

    /// <summary>A record-shaped choice result with the surface codegen emits for a record.</summary>
    public sealed record HoldingSplit(long Count, string Label) : IDamlRecord<HoldingSplit>
    {
        /// <inheritdoc />
        public DamlRecord ToRecord() =>
            DamlRecord.Create(new DamlField("count", new DamlInt64(Count)), new DamlField("label", new DamlText(Label)));

        /// <inheritdoc />
        public static HoldingSplit FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("count").As<DamlInt64>().Value, record.GetRequiredField("label").As<DamlText>().Value);

        /// <inheritdoc />
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }
}
