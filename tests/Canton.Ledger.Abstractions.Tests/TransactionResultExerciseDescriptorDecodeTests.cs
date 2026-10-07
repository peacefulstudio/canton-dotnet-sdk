// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Abstractions.Tests;

public class TransactionResultExerciseDescriptorDecodeTests
{
    private static readonly RuntimeIdentifier DescriptorDecodedId = new("accessor-pkg", "Accessor", "DescriptorDecoded");

    static TransactionResultExerciseDescriptorDecodeTests()
    {
        GeneratedTypeReaders.ForChoices<DescriptorDecodedTemplate>();
    }

    [Fact]
    public void ExerciseResult_decodes_a_variant_through_the_generated_choice_descriptor()
    {
        var result = MakeResult(OnGenericResults(
            "ReturnOutcome",
            DamlVariant.Create(
                "Win",
                DamlRecord.Create(DamlField.Create("prize", new DamlNumeric(12.5m)), DamlField.Create("tier", new DamlText("gold"))))));

        result.ExerciseResult<Outcome>("ReturnOutcome").Should().Be(new Outcome.Win(new Outcome_Win(12.5m, "gold")));
    }

    [Fact]
    public void ExerciseResult_decodes_an_enum_through_the_generated_choice_descriptor()
    {
        var result = MakeResult(OnGenericResults("ReturnSuit", DamlEnum.Create("Hearts")));

        result.ExerciseResult<Suit>("ReturnSuit").Should().Be(Suit.Hearts);
    }

    [Fact]
    public void ExerciseResult_decodes_a_collection_through_the_generated_choice_descriptor()
    {
        var result = MakeResult(OnGenericResults(
            "ReturnProfiles",
            new DamlList(
            [
                DamlRecord.Create(DamlField.Create("nickname", new DamlText("alice")), DamlField.Create("level", new DamlInt64(3))),
                DamlRecord.Create(DamlField.Create("nickname", new DamlText("bob")), DamlField.Create("level", new DamlInt64(4))),
            ])));

        result.ExerciseResult<IReadOnlyList<Profile>>("ReturnProfiles")
            .Should().Equal(new Profile("alice", 3), new Profile("bob", 4));
    }

    [Fact]
    public void AllExerciseResults_decodes_every_matching_variant_through_the_generated_choice_descriptor()
    {
        var result = MakeResult(
            OnGenericResults("ReturnOutcome", DamlVariant.Create("Pending", DamlUnit.Instance)),
            OnGenericResults("ReturnSuit", DamlEnum.Create("Spades")),
            OnGenericResults(
                "ReturnOutcome",
                DamlVariant.Create(
                    "Win",
                    DamlRecord.Create(DamlField.Create("prize", new DamlNumeric(3m)), DamlField.Create("tier", new DamlText("silver"))))));

        result.AllExerciseResults<Outcome>("ReturnOutcome").Should().Equal(
            new Outcome.Pending(), new Outcome.Win(new Outcome_Win(3m, "silver")));
    }

    [Fact]
    public void ExerciseResult_decodes_an_interface_choice_through_the_interface_id_of_the_event()
    {
        var result = MakeResult(Exercised(
            Asset.TemplateId, IHolding.InterfaceId, "Grade", DamlEnum.Create("Diamonds")));

        result.ExerciseResult<Suit>("Grade").Should().Be(Suit.Diamonds);
    }

    [Fact]
    public void ExerciseResult_returns_the_raw_value_for_a_DamlValue_target_though_a_descriptor_would_decode_it()
    {
        var result = MakeResult(Exercised(DescriptorDecodedId, null, "Pick", new DamlText("raw")));

        result.ExerciseResult<DamlValue>("Pick").Should().Be(new DamlText("raw"));
    }

    [Fact]
    public void AllExerciseResults_returns_the_raw_values_for_a_DamlValue_target()
    {
        var result = MakeResult(
            Exercised(DescriptorDecodedId, null, "Pick", new DamlText("first")),
            Exercised(DescriptorDecodedId, null, "Pick", new DamlText("second")));

        result.AllExerciseResults<DamlValue>("Pick").Should().Equal(new DamlText("first"), new DamlText("second"));
    }

    [Fact]
    public void ExerciseResult_falls_back_to_FromDamlValue_when_the_descriptor_result_type_is_not_assignable_to_the_target()
    {
        var result = MakeResult(OnGenericResults("ReturnOptionalText", DamlOptional.Some(new DamlText("ink"))));

        result.ExerciseResult<Optional<string>>("ReturnOptionalText").Should().Be(new Optional<string>.Some("ink"));
    }

    [Fact]
    public void ExerciseResult_falls_back_to_FromDamlValue_when_no_binding_is_registered_for_the_template()
    {
        var tails = new OptionalTails((Party)"party::alice", null, new TrailingNote("inner", null), null);
        var result = MakeResult(Exercised(
            new RuntimeIdentifier("accessor-pkg", "Accessor", "Unregistered"), null, "Echo", tails.ToRecord()));

        result.ExerciseResult<OptionalTails>("Echo").Should().Be(tails);
    }

    [Fact]
    public void AllExerciseResults_falls_back_to_FromDamlValue_when_no_binding_is_registered_for_the_template()
    {
        var tails = new OptionalTails((Party)"party::alice", null, new TrailingNote("inner", null), null);
        var result = MakeResult(Exercised(
            new RuntimeIdentifier("accessor-pkg", "Accessor", "Unregistered"), null, "Echo", tails.ToRecord()));

        result.AllExerciseResults<OptionalTails>("Echo").Should().Equal(tails);
    }

    [Fact]
    public void ExerciseResult_names_the_choice_when_neither_a_descriptor_nor_FromDamlValue_decodes_the_result()
    {
        var result = MakeResult(Exercised(
            new RuntimeIdentifier("accessor-pkg", "Accessor", "Unregistered"), null, "Echo", DamlEnum.Create("Hearts")));

        var act = () => result.ExerciseResult<Suit>("Echo");

        act.Should().Throw<NotSupportedException>().Which.Message.Should().StartWith(
            "Choice 'Echo' of 'accessor-pkg:Accessor:Unregistered' did not resolve to a generated result decoder: "
            + "no generated binding for it is registered. Decoding its result as "
            + "Daml.Codegen.Testing.Conformance.RichTypes.Suit through FromDamlValue failed: ");
    }

    private static ExercisedEvent OnGenericResults(string choice, DamlValue result) =>
        Exercised(GenericResults.TemplateId, null, choice, result);

    private static ExercisedEvent Exercised(
        RuntimeIdentifier templateId, RuntimeIdentifier? interfaceId, string choice, DamlValue result) =>
        new(
            ContractId: "00cid",
            TemplateId: templateId,
            InterfaceId: interfaceId,
            ChoiceName: new ChoiceName(choice),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: result,
            Consuming: false,
            ActingParties: [(Party)"alice"],
            WitnessParties: [(Party)"alice"]);

    private static TransactionResult MakeResult(params ExercisedEvent[] exercised) =>
        new(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: default)
        {
            ExercisedEvents = EquatableArray.Create(exercised),
        };

    public sealed class DescriptorDecodedTemplate : IDamlType, IHasChoices<DescriptorDecodedTemplate>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(DescriptorDecodedId, DamlTypeKind.Template, "accessor");

        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            new Choice<DescriptorDecodedTemplate, DamlUnit, DamlText>
            {
                Name = new ChoiceName("Pick"),
                Consuming = false,
                ArgumentEncoder = _ => DamlUnit.Instance,
                ArgumentDecoder = _ => DamlUnit.Instance,
                ResultDecoder = _ => new DamlText("from-descriptor"),
                ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
                ResultJsonReader = DamlLfJsonDecoders.ReadText,
            },
        ];
    }
}
