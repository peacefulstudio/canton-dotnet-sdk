// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class ContractIdAccessorsTests
{
    [Fact]
    public void ContractIdAs_on_a_created_contract_returns_the_id_for_its_own_template()
    {
        var created = CreatedGadget("00gadget");

        var id = created.ContractIdAs<Gadget>();

        id.Should().Be(new ContractId<Gadget>("00gadget"));
    }

    [Fact]
    public void ContractIdAs_on_a_created_contract_rejects_an_unrelated_template_naming_both_ids()
    {
        var created = CreatedGadget("00gadget");

        var act = () => created.ContractIdAs<Widget>();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("gadgets-pkg-id-v1:Acme.Gadgets:Gadget", StringComparison.Ordinal)
                && e.Message.Contains("widgets-pkg-id-v1:Acme.Widgets:Widget", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIdAs_on_a_created_contract_accepts_an_upgraded_package_id_of_the_same_package_name()
    {
        var created = CreatedGadget("00gadget") with
        {
            TemplateId = new Identifier("gadgets-pkg-id-v2", "Acme.Gadgets", "Gadget"),
        };

        var id = created.ContractIdAs<Gadget>();

        id.Value.Should().Be("00gadget");
    }

    [Fact]
    public void ContractIdAs_on_a_created_contract_rejects_a_same_named_template_of_another_package()
    {
        var created = CreatedGadget("00gadget") with { PackageName = "someone-elses-gadgets" };

        var act = () => created.ContractIdAs<Gadget>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ContractIdAs_on_a_created_contract_accepts_an_interface_the_contract_implements()
    {
        var created = CreatedGadget("00gadget") with { InterfaceIds = [IGlow.InterfaceId] };

        var id = created.ContractIdAs<IGlow>();

        id.Should().Be(new ContractId<IGlow>("00gadget"));
    }

    [Fact]
    public void ContractIdAs_on_a_created_contract_rejects_an_interface_the_contract_does_not_implement_naming_both_ids()
    {
        var created = CreatedGadget("00gadget") with { InterfaceIds = [IGlow.InterfaceId] };

        var act = () => created.ContractIdAs<IHum>();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("gadgets-pkg-id-v1:Acme.Gadgets:Gadget", StringComparison.Ordinal)
                && e.Message.Contains("hum-pkg-id:Acme.Hum:IHum", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIdAs_on_a_created_contract_rejects_an_interface_whose_package_id_differs()
    {
        var created = CreatedGadget("00gadget") with
        {
            InterfaceIds = [new Identifier("glow-pkg-id-other", "Acme.Glow", "IGlow")],
        };

        var act = () => created.ContractIdAs<IGlow>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ContractIdAs_on_a_created_tree_event_returns_the_id_for_its_own_template()
    {
        var created = CreatedGadgetNode("00gadget");

        created.ContractIdAs<Gadget>().Should().Be(new ContractId<Gadget>("00gadget"));
    }

    [Fact]
    public void ContractIdAs_on_a_created_tree_event_accepts_an_upgraded_package_id_of_the_same_package_name()
    {
        var created = CreatedGadgetNode("00gadget") with
        {
            TemplateId = new Identifier("gadgets-pkg-id-v2", "Acme.Gadgets", "Gadget"),
        };

        created.ContractIdAs<Gadget>().Value.Should().Be("00gadget");
    }

    [Fact]
    public void ContractIdAs_on_a_created_tree_event_accepts_an_interface_the_contract_implements()
    {
        var created = CreatedGadgetNode("00gadget") with { InterfaceIds = [IGlow.InterfaceId] };

        created.ContractIdAs<IGlow>().Should().Be(new ContractId<IGlow>("00gadget"));
    }

    [Fact]
    public void ContractIdAs_on_a_created_tree_event_rejects_an_unrelated_template_naming_both_ids()
    {
        var created = CreatedGadgetNode("00gadget");

        var act = () => created.ContractIdAs<Widget>();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("gadgets-pkg-id-v1:Acme.Gadgets:Gadget", StringComparison.Ordinal)
                && e.Message.Contains("widgets-pkg-id-v1:Acme.Widgets:Widget", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIdAs_on_a_created_tree_event_rejects_an_interface_the_contract_does_not_implement_naming_both_ids()
    {
        var created = CreatedGadgetNode("00gadget") with { InterfaceIds = [IGlow.InterfaceId] };

        var act = () => created.ContractIdAs<IHum>();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("gadgets-pkg-id-v1:Acme.Gadgets:Gadget", StringComparison.Ordinal)
                && e.Message.Contains("hum-pkg-id:Acme.Hum:IHum", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_tree_event_returns_the_id_for_its_own_template()
    {
        var exercised = ExercisedGadgetNode("00gadget");

        exercised.ContractIdAs<Gadget>().Should().Be(new ContractId<Gadget>("00gadget"));
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_tree_event_accepts_an_upgraded_package_id_of_the_same_package_name()
    {
        var exercised = ExercisedGadgetNode("00gadget") with
        {
            TemplateId = new Identifier("gadgets-pkg-id-v2", "Acme.Gadgets", "Gadget"),
        };

        exercised.ContractIdAs<Gadget>().Value.Should().Be("00gadget");
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_tree_event_accepts_the_interface_the_choice_was_exercised_through()
    {
        var exercised = ExercisedGadgetNode("00gadget") with { InterfaceId = IGlow.InterfaceId };

        exercised.ContractIdAs<IGlow>().Should().Be(new ContractId<IGlow>("00gadget"));
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_tree_event_rejects_an_unrelated_template_naming_both_ids()
    {
        var exercised = ExercisedGadgetNode("00gadget");

        var act = () => exercised.ContractIdAs<Widget>();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("gadgets-pkg-id-v1:Acme.Gadgets:Gadget", StringComparison.Ordinal)
                && e.Message.Contains("widgets-pkg-id-v1:Acme.Widgets:Widget", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_tree_event_rejects_an_interface_it_was_not_exercised_through_naming_both_ids()
    {
        var exercised = ExercisedGadgetNode("00gadget") with { InterfaceId = IGlow.InterfaceId };

        var act = () => exercised.ContractIdAs<IHum>();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("gadgets-pkg-id-v1:Acme.Gadgets:Gadget", StringComparison.Ordinal)
                && e.Message.Contains("hum-pkg-id:Acme.Hum:IHum", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_event_returns_the_id_for_its_own_template()
    {
        var exercised = ExercisedGadget("00gadget");

        exercised.ContractIdAs<Gadget>().Should().Be(new ContractId<Gadget>("00gadget"));
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_event_accepts_an_upgraded_package_id_of_the_same_package_name()
    {
        var exercised = ExercisedGadget("00gadget") with
        {
            TemplateId = new Identifier("gadgets-pkg-id-v2", "Acme.Gadgets", "Gadget"),
        };

        exercised.ContractIdAs<Gadget>().Value.Should().Be("00gadget");
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_event_accepts_the_interface_the_choice_was_exercised_through()
    {
        var exercised = ExercisedGadget("00gadget") with { InterfaceId = IGlow.InterfaceId };

        exercised.ContractIdAs<IGlow>().Should().Be(new ContractId<IGlow>("00gadget"));
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_event_rejects_an_unrelated_template_naming_both_ids()
    {
        var exercised = ExercisedGadget("00gadget");

        var act = () => exercised.ContractIdAs<Widget>();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("gadgets-pkg-id-v1:Acme.Gadgets:Gadget", StringComparison.Ordinal)
                && e.Message.Contains("widgets-pkg-id-v1:Acme.Widgets:Widget", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_event_rejects_an_interface_it_was_not_exercised_through_naming_both_ids()
    {
        var exercised = ExercisedGadget("00gadget") with { InterfaceId = IGlow.InterfaceId };

        var act = () => exercised.ContractIdAs<IHum>();

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("gadgets-pkg-id-v1:Acme.Gadgets:Gadget", StringComparison.Ordinal)
                && e.Message.Contains("hum-pkg-id:Acme.Hum:IHum", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIdAs_on_a_created_tree_event_rejects_a_same_named_template_of_another_package()
    {
        var created = CreatedGadgetNode("00gadget") with { PackageName = "someone-elses-gadgets" };

        var act = () => created.ContractIdAs<Gadget>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_tree_event_rejects_a_same_named_template_of_another_package()
    {
        var exercised = ExercisedGadgetNode("00gadget") with { PackageName = "someone-elses-gadgets" };

        var act = () => exercised.ContractIdAs<Gadget>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ContractIdAs_on_an_exercised_event_rejects_a_same_named_template_of_another_package()
    {
        var exercised = ExercisedGadget("00gadget") with { PackageName = "someone-elses-gadgets" };

        var act = () => exercised.ContractIdAs<Gadget>();

        act.Should().Throw<InvalidOperationException>();
    }

    private static TreeEvent.Created CreatedGadgetNode(string contractId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: new Identifier("gadgets-pkg-id-v1", "Acme.Gadgets", "Gadget"),
            CreateArguments: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: [])
        {
            PackageName = "gadgets",
        };

    private static TreeEvent.Exercised ExercisedGadgetNode(string contractId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: new Identifier("gadgets-pkg-id-v1", "Acme.Gadgets", "Gadget"),
            InterfaceId: null,
            ChoiceName: new ChoiceName("Spin"),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: false,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")],
            ChildEvents: [])
        {
            PackageName = "gadgets",
        };

    private static ExercisedEvent ExercisedGadget(string contractId) =>
        new(
            ContractId: contractId,
            TemplateId: new Identifier("gadgets-pkg-id-v1", "Acme.Gadgets", "Gadget"),
            InterfaceId: null,
            ChoiceName: new ChoiceName("Spin"),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: false,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")])
        {
            PackageName = "gadgets",
        };

    private static CreatedContract CreatedGadget(string contractId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: new Identifier("gadgets-pkg-id-v1", "Acme.Gadgets", "Gadget"),
            Payload: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: [])
        {
            PackageName = "gadgets",
        };

    private sealed record Gadget : ITemplate
    {
        public static Identifier TemplateId { get; } = new("gadgets-pkg-id-v1", "Acme.Gadgets", "Gadget");
        public static string PackageId => "gadgets-pkg-id-v1";
        public static string PackageName => "gadgets";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record Widget : ITemplate
    {
        public static Identifier TemplateId { get; } = new("widgets-pkg-id-v1", "Acme.Widgets", "Widget");
        public static string PackageId => "widgets-pkg-id-v1";
        public static string PackageName => "widgets";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record IGlow : IDamlInterface
    {
        public static Identifier InterfaceId { get; } = new("glow-pkg-id", "Acme.Glow", "IGlow");
        public static string PackageId => "glow-pkg-id";
        public static string PackageName => "glow";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record IHum : IDamlInterface
    {
        public static Identifier InterfaceId { get; } = new("hum-pkg-id", "Acme.Hum", "IHum");
        public static string PackageId => "hum-pkg-id";
        public static string PackageName => "hum";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }
}
