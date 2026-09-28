// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Tree-walking and compatibility helpers for <see cref="TransactionTree"/>.
/// </summary>
public static class TransactionTreeExtensions
{
    /// <summary>
    /// Enumerates every event in the tree — <see cref="TransactionTree.RootEvents"/>
    /// followed by each root's <see cref="TreeEvent.DescendantEvents"/> — in
    /// depth-first pre-order.
    /// </summary>
    public static IEnumerable<TreeEvent> AllEvents(this TransactionTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return AllEventsCore(tree);
    }

    private static IEnumerable<TreeEvent> AllEventsCore(TransactionTree tree)
    {
        foreach (var root in tree.RootEvents)
        {
            yield return root;
            foreach (var descendant in root.DescendantEvents())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Projects this <see cref="TransactionTree"/> to the flattened
    /// <see cref="TransactionResult"/> shape, for callers that don't need
    /// hierarchy. <see cref="TreeEvent.Created"/> nodes become
    /// <see cref="CreatedContract"/> entries carrying
    /// <see cref="TreeEvent.Created.CreateArguments"/> as their payload;
    /// <see cref="TreeEvent.Exercised"/> nodes become
    /// <see cref="TransactionResult.ExercisedEvents"/> entries, and consuming
    /// exercises additionally contribute their target contract id to
    /// <see cref="TransactionResult.ArchivedContractIds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TreeEvent.Created"/> nodes project losslessly:
    /// <see cref="CreatedContract"/> mirrors that node field for field.
    /// </para>
    /// <para>
    /// Exercise nodes do not. <see cref="ExercisedEvent"/> has no slot for
    /// <see cref="TreeEvent.Exercised.EventId"/> or <see cref="TreeEvent.Exercised.ChildEvents"/>;
    /// a caller that needs either walks
    /// <see cref="TransactionTree.RootEvents"/> directly instead.
    /// </para>
    /// <para>
    /// Hierarchy is flattened either way: the parent/child structure of the tree is not
    /// recoverable from the projected lists.
    /// </para>
    /// <para>
    /// <see cref="TransactionResult.CommandId"/> is <c>null</c> here because
    /// <see cref="TransactionTree"/> carries no command id to project.
    /// </para>
    /// </remarks>
    public static TransactionResult ToTransactionResult(this TransactionTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var createdContracts = new List<CreatedContract>();
        var archivedContractIds = new List<string>();
        var exercisedEvents = new List<ExercisedEvent>();

        foreach (var treeEvent in tree.AllEvents())
        {
            switch (treeEvent)
            {
                case TreeEvent.Created created:
                    createdContracts.Add(new CreatedContract(
                        created.EventId,
                        created.ContractId,
                        created.TemplateId,
                        created.CreateArguments,
                        created.WitnessParties,
                        created.Signatories,
                        created.Observers,
                        created.ContractKey,
                        created.CreatedAt)
                    {
                        InterfaceIds = created.InterfaceIds,
                    });
                    break;
                case TreeEvent.Exercised exercised:
                    exercisedEvents.Add(new ExercisedEvent(
                        exercised.ContractId,
                        exercised.TemplateId,
                        exercised.InterfaceId,
                        exercised.ChoiceName,
                        exercised.ChoiceArgument,
                        exercised.ExerciseResult,
                        exercised.Consuming,
                        exercised.ActingParties,
                        exercised.WitnessParties));
                    if (exercised.Consuming)
                    {
                        archivedContractIds.Add(exercised.ContractId);
                    }
                    break;
                default:
                    throw new UnreachableException(
                        $"Unhandled {nameof(TreeEvent)} case: {treeEvent.GetType().Name}");
            }
        }

        return new TransactionResult(
            tree.UpdateId,
            tree.CompletionOffset,
            EquatableArray.Create(createdContracts),
            EquatableArray.Create(archivedContractIds),
            null)
        {
            ExercisedEvents = EquatableArray.Create(exercisedEvents),
        };
    }
}
