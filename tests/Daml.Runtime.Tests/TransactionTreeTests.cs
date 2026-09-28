// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

public class TransactionTreeTests
{
    private static readonly RuntimeIdentifier FooTemplateId = new("test-pkg", "Acme.Foo", "FooBar");

    [Fact]
    public void TransactionTree_root_events_preserve_transaction_order()
    {
        var first = MakeCreated("00first");
        var second = MakeCreated("00second");

        var tree = new TransactionTree("u1", LedgerOffset.At(1),[first, second]);

        tree.RootEvents.Should().HaveCount(2);
        tree.RootEvents[0].Should().BeSameAs(first);
        tree.RootEvents[1].Should().BeSameAs(second);
    }

    [Fact]
    public void TransactionTree_child_events_are_reachable_from_exercised_node()
    {
        var child = MakeCreated("00child");
        var exercise = MakeExercised("00parent", children: [child]);

        exercise.ChildEvents.Should().ContainSingle().Which.Should().BeSameAs(child);
    }

    [Fact]
    public void TreeEventCreated_defaults_InterfaceIds_to_empty()
    {
        MakeCreated("00solo").InterfaceIds.Should().BeEmpty();
    }

    [Fact]
    public void DescendantEvents_is_empty_for_created_event()
    {
        var created = MakeCreated("00solo");

        created.DescendantEvents().Should().BeEmpty();
    }

    [Fact]
    public void DescendantEvents_is_empty_for_exercised_event_without_children()
    {
        var exercise = MakeExercised("00leaf", children: []);

        exercise.DescendantEvents().Should().BeEmpty();
    }

    [Fact]
    public void DescendantEvents_enumerates_nested_children_depth_first()
    {
        var grandchild = MakeCreated("00grandchild");
        var innerExercise = MakeExercised("00inner", children: [grandchild]);
        var outerCreated = MakeCreated("00sibling");
        var outerExercise = MakeExercised("00outer", children: [innerExercise, outerCreated]);

        var descendants = outerExercise.DescendantEvents().ToList();

        descendants.Should().HaveCount(3);
        descendants[0].Should().BeSameAs(innerExercise);
        descendants[1].Should().BeSameAs(grandchild);
        descendants[2].Should().BeSameAs(outerCreated);
    }

    [Fact]
    public void DescendantEvents_handles_deeply_nested_trees_without_stack_overflow()
    {
        const int depth = 5000;
        var leaf = MakeCreated("leaf");
        var deepTree = ChainAbove(leaf, depth);

        var descendants = deepTree.DescendantEvents().ToList();

        descendants.Should().HaveCount(depth);
        descendants.Should().OnlyHaveUniqueItems();
        descendants[^1].Should().BeSameAs(leaf);
    }

    [Fact]
    public void TreeEventExercised_hashes_a_deep_tree_without_recursing_into_its_subtree()
    {
        var first = ChainAbove(MakeCreated("leaf"), depth: 5000);
        var second = ChainAbove(MakeCreated("leaf"), depth: 5000);

        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void TreeEventExercised_compares_child_events_by_content()
    {
        var first = MakeExercised("00outer", children: [MakeExercised("00inner", children: [MakeCreated("00leaf")])]);
        var second = MakeExercised("00outer", children: [MakeExercised("00inner", children: [MakeCreated("00leaf")])]);
        var otherLeaf = MakeExercised("00outer", children: [MakeExercised("00inner", children: [MakeCreated("00other")])]);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Should().NotBe(otherLeaf);
    }

    [Fact]
    public void TransactionTree_compares_root_events_by_content()
    {
        var first = new TransactionTree("u1", LedgerOffset.At(1), [MakeCreated("00alice")]);
        var second = new TransactionTree("u1", LedgerOffset.At(1), [MakeCreated("00alice")]);
        var other = new TransactionTree("u1", LedgerOffset.At(1), [MakeCreated("00bob")]);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Should().NotBe(other);
    }

    [Fact]
    public void TreeEventExercised_hash_distinguishes_nodes_differing_in_choice_name()
    {
        var node = MakeExercised("00node", children: []);

        node.GetHashCode().Should().NotBe((node with { ChoiceName = new ChoiceName("Other") }).GetHashCode());
    }

    [Fact]
    public void TreeEventExercised_hash_distinguishes_nodes_differing_in_acting_parties()
    {
        var node = MakeExercised("00node", children: []);

        node.GetHashCode().Should().NotBe(
            (node with { ActingParties = [new Party("mallory")] }).GetHashCode());
    }

    [Fact]
    public void TreeEventExercised_hash_distinguishes_nodes_differing_in_witness_parties()
    {
        var node = MakeExercised("00node", children: []);

        node.GetHashCode().Should().NotBe(
            (node with { WitnessParties = [new Party("mallory")] }).GetHashCode());
    }

    [Fact]
    public void TreeEventExercised_hash_distinguishes_nodes_differing_in_child_count()
    {
        var node = MakeExercised("00node", children: []);

        node.GetHashCode().Should().NotBe(
            (node with { ChildEvents = [MakeCreated("00child")] }).GetHashCode());
    }

    [Fact]
    public void TreeEventExercised_ToString_renders_the_child_events_as_a_count()
    {
        var node = MakeExercised("00parent", children: [MakeCreated("00child"), MakeCreated("00sibling")]);

        node.ToString().Should().Be(
            "Exercised { EventId = evt-00parent, ContractId = 00parent, "
            + $"TemplateId = {FooTemplateId}, InterfaceId = , ChoiceName = DoThing, "
            + $"ChoiceArgument = {DamlUnit.Instance}, ExerciseResult = {DamlUnit.Instance}, "
            + "Consuming = False, ActingParties = [alice], WitnessParties = [alice], "
            + "ChildEvents.Count = 2 }");
    }

    [Fact]
    public void TreeEventExercised_ToString_bounds_a_deep_tree_to_the_node()
    {
        MakeExercised("00guard", children: [MakeCreated("00child")]).ToString().Should().Contain(
            "ChildEvents.Count = 1",
            "rendering the children instead overflows the stack on the deep tree below, and a "
            + "stack overflow kills the whole test process rather than failing this test");

        var deepTree = ChainAbove(MakeCreated("00leaf"), depth: 5000);
        string? rendered = null;
        Exception? failure = null;
        var render = new Thread(
            () =>
            {
                try
                {
                    rendered = deepTree.ToString();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            },
            maxStackSize: 1024 * 1024);

        render.Start();
        render.Join();

        failure.Should().BeNull();
        rendered.Should().Contain("ChildEvents.Count = 1").And.NotContain("00leaf");
        rendered!.Length.Should().BeLessThan(500, "the render costs the node, not its subtree");
    }

    private static TreeEvent ChainAbove(TreeEvent leaf, int depth)
    {
        var tree = leaf;
        for (var level = 0; level < depth; level++)
        {
            tree = MakeExercised($"level-{level:D5}", children: [tree]);
        }

        return tree;
    }

    [Fact]
    public void DescendantEvents_enumerates_many_siblings_in_declared_order()
    {
        var children = Enumerable.Range(0, 50)
            .Select(i => (TreeEvent)MakeCreated($"00child-{i:D2}"))
            .ToList();
        var exercise = MakeExercised("00parent", children: [.. children]);

        var descendants = exercise.DescendantEvents().ToList();

        descendants.Should().Equal(children);
    }

    [Fact]
    public void DescendantEvents_preserves_pre_order_across_branching_and_depth()
    {
        const int chainLength = 200;
        TreeEvent tree = MakeCreated("leaf");

        for (int i = 0; i < chainLength; i++)
        {
            var sibling = MakeCreated($"sibling-{i:D3}");
            tree = MakeExercised($"level-{i:D3}", children: [tree, sibling]);
        }

        var descendants = tree.DescendantEvents().ToList();

        descendants.Should().Equal(ExpectedPreOrder(((TreeEvent.Exercised)tree).ChildEvents));
    }

    private static List<TreeEvent> ExpectedPreOrder(IReadOnlyList<TreeEvent> events)
    {
        var result = new List<TreeEvent>();

        foreach (var treeEvent in events)
        {
            result.Add(treeEvent);

            if (treeEvent is TreeEvent.Exercised exercised)
            {
                result.AddRange(ExpectedPreOrder(exercised.ChildEvents));
            }
        }

        return result;
    }

    [Fact]
    public void AllEvents_enumerates_roots_and_descendants_in_pre_order()
    {
        var child = MakeCreated("00child");
        var rootExercise = MakeExercised("00root-exercise", children: [child]);
        var rootCreated = MakeCreated("00root-created");
        var tree = new TransactionTree("u1", LedgerOffset.At(1),[rootExercise, rootCreated]);

        var all = tree.AllEvents().ToList();

        all.Should().HaveCount(3);
        all[0].Should().BeSameAs(rootExercise);
        all[1].Should().BeSameAs(child);
        all[2].Should().BeSameAs(rootCreated);
    }

    [Fact]
    public void AllEvents_throws_when_tree_is_null()
    {
        TransactionTree tree = null!;

        Action act = () => tree.AllEvents();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToTransactionResult_flattens_created_events_with_the_create_arguments_as_payload()
    {
        var created = MakeCreated("00alice");
        var tree = new TransactionTree("u1", LedgerOffset.At(5), [created]);

        var result = tree.ToTransactionResult();

        result.UpdateId.Should().Be("u1");
        result.CompletionOffset.Should().Be(LedgerOffset.At(5));
        result.CreatedContracts.Should().ContainSingle();
        result.CreatedContracts[0].ContractId.Should().Be("00alice");
        result.CreatedContracts[0].TemplateId.Should().Be(FooTemplateId);
        result.CreatedContracts[0].Payload.Should().BeSameAs(created.CreateArguments);
    }

    [Fact]
    public void ToTransactionResult_leaves_command_id_absent_since_tree_carries_none()
    {
        var tree = new TransactionTree("u1", LedgerOffset.At(1),[MakeCreated("00alice")]);

        var result = tree.ToTransactionResult();

        result.CommandId.Should().BeNull();
    }

    [Fact]
    public void ToTransactionResult_preserves_interface_ids_on_created_contracts()
    {
        var interfaceId = new RuntimeIdentifier("test-pkg", "Acme.Foo", "IAsset");
        var created = MakeCreated("00iface") with { InterfaceIds = [interfaceId] };
        var tree = new TransactionTree("u1", LedgerOffset.At(1),[created]);

        var result = tree.ToTransactionResult();

        result.CreatedContracts.Should().ContainSingle()
            .Which.InterfaceIds.Should().ContainSingle().Which.Should().Be(interfaceId);
    }

    [Fact]
    public void ToTransactionResult_forwards_every_created_field_to_the_flattened_contract()
    {
        var key = new ContractKey(DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice"))), FooTemplateId);
        var createdAt = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero);
        var created = MakeCreated("00alice") with { ContractKey = key, CreatedAt = createdAt };
        var tree = new TransactionTree("u1", LedgerOffset.At(5), [created]);

        var result = tree.ToTransactionResult();

        var flattened = result.CreatedContracts.Should().ContainSingle().Which;
        flattened.EventId.Should().Be("evt-00alice");
        flattened.WitnessParties.Should().Equal(new Party("alice"), new Party("bob"), new Party("carol"));
        flattened.Signatories.Should().Equal(new Party("alice"));
        flattened.Observers.Should().Equal(new Party("bob"));
        flattened.ContractKey.Should().Be(key);
        flattened.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void ToTransactionResult_flattens_nested_exercised_events_in_pre_order()
    {
        var childCreate = MakeCreated("00child");
        var innerExercise = MakeExercised("00inner", children: [childCreate], choiceName: "Inner");
        var tree = new TransactionTree("u1", LedgerOffset.At(1),[innerExercise]);

        var result = tree.ToTransactionResult();

        result.ExercisedEvents.Should().ContainSingle().Which.ChoiceName.Should().Be(new ChoiceName("Inner"));
        result.CreatedContracts.Should().ContainSingle().Which.ContractId.Should().Be("00child");
    }

    [Fact]
    public void ToTransactionResult_collects_archived_contract_ids_from_consuming_exercises()
    {
        var consuming = MakeExercised("00consumed", children: [], consuming: true);
        var nonConsuming = MakeExercised("00untouched", children: [], consuming: false);
        var tree = new TransactionTree("u1", LedgerOffset.At(1),[consuming, nonConsuming]);

        var result = tree.ToTransactionResult();

        result.ArchivedContractIds.Should().ContainSingle().Which.Should().Be("00consumed");
    }

    [Fact]
    public void ToTransactionResult_throws_when_tree_is_null()
    {
        TransactionTree tree = null!;

        Action act = () => tree.ToTransactionResult();

        act.Should().Throw<ArgumentNullException>();
    }

    private static TreeEvent.Created MakeCreated(string contractId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: FooTemplateId,
            CreateArguments: DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice"))),
            WitnessParties: [new Party("alice"), new Party("bob"), new Party("carol")],
            Signatories: [new Party("alice")],
            Observers: [new Party("bob")]);

    private static TreeEvent.Exercised MakeExercised(
        string contractId,
        EquatableArray<TreeEvent> children,
        string choiceName = "DoThing",
        bool consuming = false) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: FooTemplateId,
            InterfaceId: null,
            ChoiceName: new ChoiceName(choiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: consuming,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")],
            ChildEvents: children);
}
