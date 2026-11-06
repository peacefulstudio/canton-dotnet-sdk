// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ByKeyExercisePlannerTests
{
    private static readonly DamlPrimitiveType PartyType = new(DamlPrimitive.Party);

    private static ChoiceExercisePlanner Planner() => new(new PartyAnalysis());

    private static DamlPartyAnalysis StaticParties(params string[] fieldNames) =>
        DamlPartyAnalysis.Static(fieldNames.Select(n => (DamlPartyReference)new DamlPartyPayloadField(n)).ToList());

    private static DamlFieldDefinition[] PartyFields(params string[] names) =>
        [.. names.Select(name => new DamlFieldDefinition(name, PartyType))];

    private static DamlChoice Choice(string name, DamlType returnType, DamlPartyAnalysis? controllers = null) =>
        new()
        {
            Name = name,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Consuming = false,
            Controllers = controllers ?? DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlTemplate Template(params DamlChoice[] choices) =>
        new()
        {
            Name = "Vault",
            Choices = choices,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static ByKeyExercisePlan PlanSingleChoice(DamlChoice choice, IReadOnlyList<DamlFieldDefinition> fields) =>
        Planner().PlanByKey(Template(choice), fields).Single();

    [Fact]
    public void PlanByKey_names_the_controller_of_a_static_controller_choice()
    {
        var plan = PlanSingleChoice(
            Choice("Credit", new DamlPrimitiveType(DamlPrimitive.Int64), StaticParties("custodian")),
            PartyFields("custodian"));

        plan.ControllerParameters.Should().Equal("custodian");
        plan.OffersNamedControllers.Should().BeTrue();
    }

    [Fact]
    public void PlanByKey_names_controllers_in_declaration_order_without_duplicates()
    {
        var plan = PlanSingleChoice(
            Choice("Credit", new DamlPrimitiveType(DamlPrimitive.Int64), StaticParties("owner", "agent", "owner")),
            PartyFields("agent", "owner"));

        plan.ControllerParameters.Should().Equal("owner", "agent");
    }

    [Fact]
    public void PlanByKey_offers_no_named_controllers_for_a_dynamic_controller_choice()
    {
        var plan = PlanSingleChoice(Choice("Credit", new DamlPrimitiveType(DamlPrimitive.Int64)), PartyFields("custodian"));

        plan.ControllerParameters.Should().BeEmpty();
        plan.OffersNamedControllers.Should().BeFalse();
    }

    [Fact]
    public void PlanByKey_offers_no_named_controllers_when_a_controller_is_not_a_party_field()
    {
        var plan = PlanSingleChoice(
            Choice("Credit", new DamlPrimitiveType(DamlPrimitive.Int64), StaticParties("custodian")),
            [new DamlFieldDefinition("custodian", new DamlPrimitiveType(DamlPrimitive.Text))]);

        plan.OffersNamedControllers.Should().BeFalse();
    }

    [Fact]
    public void PlanByKey_offers_no_named_controllers_for_an_empty_static_controller_set()
    {
        var plan = PlanSingleChoice(
            Choice("Credit", new DamlPrimitiveType(DamlPrimitive.Int64), StaticParties()),
            PartyFields("custodian"));

        plan.OffersNamedControllers.Should().BeFalse();
    }

    [Theory]
    [InlineData("key")]
    [InlineData("client")]
    [InlineData("argument")]
    [InlineData("submitter")]
    [InlineData("workflowId")]
    [InlineData("commandId")]
    [InlineData("timeout")]
    [InlineData("configure")]
    [InlineData("cancellationToken")]
    public void PlanByKey_offers_no_named_controllers_when_a_controller_would_take_a_parameter_name_of_the_exerciser(
        string fieldName)
    {
        var plan = PlanSingleChoice(
            Choice("Credit", new DamlPrimitiveType(DamlPrimitive.Int64), StaticParties(fieldName)),
            PartyFields(fieldName));

        plan.OffersNamedControllers.Should().BeFalse();
    }

    [Fact]
    public void PlanByKey_plans_every_choice_of_the_template_in_declaration_order()
    {
        var template = Template(
            Choice("Credit", new DamlPrimitiveType(DamlPrimitive.Int64), StaticParties("custodian")),
            Choice("Peek", new DamlPrimitiveType(DamlPrimitive.Unit)));

        var plans = Planner().PlanByKey(template, PartyFields("custodian"));

        plans.Select(plan => plan.Choice.Name).Should().Equal("Credit", "Peek");
    }
}
