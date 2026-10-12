// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceExercisePlannerTests
{
    private static readonly DamlPrimitiveType Unit = new(DamlPrimitive.Unit);

    private static readonly DamlPrimitiveType PartyType = new(DamlPrimitive.Party);

    private static readonly DamlTypeApp ContractIdResult =
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef("pkg-id", "Main", "Token")]);

    private static ChoiceExercisePlanner Planner() => new(new PartyAnalysis());

    private static DamlPartyAnalysis StaticParties(params string[] fieldNames) =>
        DamlPartyAnalysis.Static(fieldNames.Select(n => (DamlPartyReference)new DamlPartyPayloadField(n)).ToList());

    private static DamlFieldDefinition[] PartyFields(params string[] names) =>
        [.. names.Select(name => new DamlFieldDefinition(name, PartyType))];

    private static DamlChoice Choice(
        string name,
        DamlType returnType,
        DamlPartyAnalysis? controllers = null,
        DamlPartyAnalysis? observers = null) =>
        new()
        {
            Name = name,
            ArgumentType = Unit,
            ReturnType = returnType,
            Consuming = false,
            Controllers = controllers ?? DamlPartyAnalysis.Dynamic,
            Observers = observers ?? DamlPartyAnalysis.Dynamic,
        };

    private static DamlTemplate Template(DamlPartyAnalysis observers, params DamlChoice[] choices) =>
        new()
        {
            Name = "Vault",
            Choices = choices,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = observers,
        };

    private static ChoiceExercisePlan PlanSingleChoice(
        DamlChoice choice,
        IReadOnlyList<DamlFieldDefinition> fields,
        DamlPartyAnalysis? templateObservers = null) =>
        Planner().PlanTemplate(Template(templateObservers ?? DamlPartyAnalysis.Dynamic, choice), fields).Single();

    private static ExerciseOverload[] Overloads(ChoiceExercisePlan plan) => [.. plan.Overloads];

    [Fact]
    public void PlanTemplate_gives_a_static_controller_contract_id_choice_four_overloads()
    {
        var plan = PlanSingleChoice(
            Choice("Spawn", ContractIdResult, controllers: StaticParties("owner")),
            PartyFields("owner"));

        plan.Class.Should().Be(ExerciserClass.TemplateExtensions);
        plan.Overloads.Should().HaveCount(4);
        plan.Overloads[0].Receiver.Should().Be(ExerciseReceiver.ContractId);
        plan.Overloads[0].Submitter.Should().BeEquivalentTo(new NamedControllersSubmitter(["owner"], []));
        plan.Overloads[1].Receiver.Should().Be(ExerciseReceiver.ContractId);
        plan.Overloads[1].Submitter.Should().BeOfType<ExplicitSubmitter>();
        plan.Overloads[2].Receiver.Should().Be(ExerciseReceiver.FetchedContract);
        plan.Overloads[2].Submitter.Should().BeEquivalentTo(new PayloadPartiesSubmitter(["owner"], []));
        plan.Overloads[3].Receiver.Should().Be(ExerciseReceiver.FetchedContract);
        plan.Overloads[3].Submitter.Should().BeOfType<ExplicitSubmitter>();
    }

    [Fact]
    public void PlanTemplate_orders_controllers_in_declaration_order_and_keeps_only_observer_only_parties_for_read_as()
    {
        var plan = PlanSingleChoice(
            Choice(
                "Spawn",
                ContractIdResult,
                controllers: StaticParties("owner", "agent"),
                observers: StaticParties("auditor", "owner")),
            PartyFields("owner", "agent", "auditor", "regulator"),
            templateObservers: StaticParties("regulator"));

        plan.Overloads[0].Submitter.Should().BeEquivalentTo(
            new NamedControllersSubmitter(["owner", "agent"], ["regulator", "auditor"]));
        plan.Overloads[2].Submitter.Should().BeEquivalentTo(
            new PayloadPartiesSubmitter(["owner", "agent"], ["regulator", "auditor"]));
    }

    [Fact]
    public void PlanTemplate_gives_a_dynamic_controller_contract_id_choice_a_single_explicit_submitter_overload()
    {
        var plan = PlanSingleChoice(Choice("Spawn", ContractIdResult), PartyFields("owner"));

        plan.Class.Should().Be(ExerciserClass.TemplateExtensions);
        plan.Overloads.Should().ContainSingle();
        plan.Overloads[0].Receiver.Should().Be(ExerciseReceiver.ContractId);
        plan.Overloads[0].Submitter.Should().BeOfType<ExplicitSubmitter>();
    }

    [Fact]
    public void PlanTemplate_demotes_a_controller_that_is_not_a_party_field_to_a_single_explicit_submitter_overload()
    {
        var plan = PlanSingleChoice(
            Choice("Spawn", ContractIdResult, controllers: StaticParties("owner")),
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Text))]);

        plan.Overloads.Should().ContainSingle();
        plan.Overloads[0].Submitter.Should().BeOfType<ExplicitSubmitter>();
    }

    [Fact]
    public void PlanTemplate_gives_an_empty_static_controller_set_a_single_explicit_submitter_overload()
    {
        var plan = PlanSingleChoice(
            Choice("Spawn", ContractIdResult, controllers: StaticParties()),
            PartyFields("owner"));

        plan.Overloads.Should().ContainSingle();
        plan.Overloads[0].Submitter.Should().BeOfType<ExplicitSubmitter>();
    }

    [Fact]
    public void PlanTemplate_drops_read_as_parties_when_an_observer_clause_is_dynamic()
    {
        var plan = PlanSingleChoice(
            Choice("Spawn", ContractIdResult, controllers: StaticParties("owner")),
            PartyFields("owner", "auditor"),
            templateObservers: StaticParties("auditor"));

        plan.Overloads[0].Submitter.Should().BeEquivalentTo(new NamedControllersSubmitter(["owner"], []));
        plan.Overloads[2].Submitter.Should().BeEquivalentTo(new PayloadPartiesSubmitter(["owner"], []));
    }

    [Fact]
    public void PlanTemplate_places_a_value_returning_choice_in_the_non_contract_class_with_one_explicit_overload_whatever_its_controllers()
    {
        var plan = PlanSingleChoice(
            Choice("Total", new DamlPrimitiveType(DamlPrimitive.Numeric), controllers: StaticParties("owner")),
            PartyFields("owner"));

        plan.Class.Should().Be(ExerciserClass.TemplateNonContractExtensions);
        plan.Overloads.Should().ContainSingle();
        plan.Overloads[0].Receiver.Should().Be(ExerciseReceiver.ContractId);
        plan.Overloads[0].Submitter.Should().BeOfType<ExplicitSubmitter>();
    }

    [Fact]
    public void PlanTemplate_places_a_unit_returning_choice_in_the_non_contract_class()
    {
        var plan = PlanSingleChoice(Choice("Close", Unit), []);

        plan.Class.Should().Be(ExerciserClass.TemplateNonContractExtensions);
    }

    [Fact]
    public void PlanTemplate_plans_every_choice_in_declaration_order()
    {
        var plans = Planner().PlanTemplate(
            Template(
                DamlPartyAnalysis.Dynamic,
                Choice("Spawn", ContractIdResult),
                Choice("Close", Unit),
                Choice("Total", new DamlPrimitiveType(DamlPrimitive.Int64))),
            []);

        plans.Select(plan => plan.Choice.Name).Should().Equal("Spawn", "Close", "Total");
        plans.Select(plan => plan.Class).Should().Equal(
            ExerciserClass.TemplateExtensions,
            ExerciserClass.TemplateNonContractExtensions,
            ExerciserClass.TemplateNonContractExtensions);
    }

    [Fact]
    public void PlanTemplate_places_a_choice_returning_an_optional_contract_id_in_the_contract_id_class()
    {
        var plan = PlanSingleChoice(Choice("MaybeSpawn", new DamlOptionalType(ContractIdResult)), []);

        plan.Class.Should().Be(ExerciserClass.TemplateExtensions);
    }

    [Fact]
    public void PlanNonContractChoices_keeps_value_and_unit_choices_with_one_explicit_overload_each_in_declaration_order()
    {
        var plans = ChoiceExercisePlanner.PlanNonContractChoices(
            Template(
                DamlPartyAnalysis.Dynamic,
                Choice("Total", new DamlPrimitiveType(DamlPrimitive.Int64), controllers: StaticParties("owner")),
                Choice("Spawn", ContractIdResult),
                Choice("Close", Unit)));

        plans.Select(plan => plan.Choice.Name).Should().Equal("Total", "Close");
        plans.Should().OnlyContain(plan => plan.Class == ExerciserClass.TemplateNonContractExtensions);
        plans.Should().OnlyContain(plan => plan.Overloads.Count == 1 && plan.Overloads[0].SubmitsDirectly);
    }

    [Fact]
    public void PlanTemplate_plans_nothing_for_a_template_without_choices()
    {
        Planner().PlanTemplate(Template(DamlPartyAnalysis.Dynamic), []).Should().BeEmpty();
    }

    [Fact]
    public void PlanInterfaceChoice_gives_one_explicit_submitter_overload_on_a_contract_id_receiver()
    {
        var plan = ChoiceExercisePlanner.PlanInterfaceChoice(
            Choice("Transfer", ContractIdResult, controllers: StaticParties("owner")));

        plan.Class.Should().Be(ExerciserClass.InterfaceExtensions);
        plan.Overloads.Should().ContainSingle();
        plan.Overloads[0].Receiver.Should().Be(ExerciseReceiver.ContractId);
        plan.Overloads[0].Submitter.Should().BeOfType<ExplicitSubmitter>();
    }

    [Fact]
    public void ExerciseOverload_dispatches_only_for_an_explicit_submitter_on_a_contract_id_receiver()
    {
        var plan = PlanSingleChoice(
            Choice("Spawn", ContractIdResult, controllers: StaticParties("owner")),
            PartyFields("owner"));

        Overloads(plan).Select(overload => overload.SubmitsDirectly).Should().Equal(false, true, false, false);
    }

    [Fact]
    public void ChoiceExercisePlan_offers_named_controllers_only_when_an_overload_names_them()
    {
        var staticPlan = PlanSingleChoice(
            Choice("Spawn", ContractIdResult, controllers: StaticParties("owner")),
            PartyFields("owner"));
        var dynamicPlan = PlanSingleChoice(Choice("Spawn", ContractIdResult), PartyFields("owner"));

        staticPlan.OffersNamedControllers.Should().BeTrue();
        dynamicPlan.OffersNamedControllers.Should().BeFalse();
    }

    [Theory]
    [InlineData("Spawn", "TrySpawnAsync", "SpawnCommand", "ProjectSpawnResult", "SpawnByKeyCommand")]
    [InlineData("class", "Try@classAsync", "@classCommand", "Project@classResult", "@classByKeyCommand")]
    public void ExerciseMemberNames_spell_each_member_once_per_choice(
        string damlName,
        string exerciser,
        string command,
        string projector,
        string byKeyCommand)
    {
        var choice = Choice(damlName, Unit);

        ExerciseMemberNames.Exerciser(choice).Should().Be(exerciser);
        ExerciseMemberNames.Command(choice).Should().Be(command);
        ExerciseMemberNames.Projector(choice).Should().Be(projector);
        ExerciseMemberNames.ByKeyCommand(choice).Should().Be(byKeyCommand);
    }
}
