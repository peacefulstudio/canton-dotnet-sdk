// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Result of a submitted transaction, preserving the parent/child hierarchy
/// of its events. A tree-aware sibling of <see cref="TransactionResult"/>,
/// which flattens the same information into separate created/archived/exercised
/// lists. Use <see cref="TransactionTreeExtensions.ToTransactionResult"/> to
/// project a <see cref="TransactionTree"/> to that flattened shape.
/// </summary>
/// <param name="UpdateId">Ledger-assigned update identifier.</param>
/// <param name="CompletionOffset">Offset at which the transaction was committed.</param>
/// <param name="RootEvents">The transaction's top-level events, in transaction
/// order. Events caused by an exercise (its sub-creates and sub-exercises) are
/// not repeated here — they nest under that exercise's
/// <see cref="TreeEvent.Exercised.ChildEvents"/>.</param>
public sealed record TransactionTree(
    string UpdateId,
    LedgerOffset CompletionOffset,
    EquatableArray<TreeEvent> RootEvents);

/// <summary>
/// A single node in a <see cref="TransactionTree"/>: either a contract
/// creation (<see cref="Created"/>) or a choice exercise (<see cref="Exercised"/>).
/// Exercise nodes carry the events they directly caused, preserving the
/// ledger's causal hierarchy.
/// </summary>
public abstract record TreeEvent
{
    private TreeEvent()
    {
    }

    /// <summary>
    /// Enumerates every event nested under this one, in depth-first pre-order.
    /// Safe on arbitrarily deep trees — traversal does not use call-stack
    /// recursion, so it cannot overflow the stack. Empty for
    /// <see cref="Created"/> events and for <see cref="Exercised"/> events
    /// with no <see cref="Exercised.ChildEvents"/>.
    /// </summary>
    public IEnumerable<TreeEvent> DescendantEvents()
    {
        if (this is not Exercised exercised)
        {
            yield break;
        }

        var stack = new Stack<TreeEvent>();
        PushChildrenInPreOrder(stack, exercised.ChildEvents);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (current is Exercised currentExercised && currentExercised.ChildEvents.Count > 0)
            {
                PushChildrenInPreOrder(stack, currentExercised.ChildEvents);
            }
        }
    }

    private static void PushChildrenInPreOrder(Stack<TreeEvent> stack, EquatableArray<TreeEvent> children)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            stack.Push(children[i]);
        }
    }

    /// <summary>
    /// A contract creation node in a <see cref="TransactionTree"/>.
    /// </summary>
    /// <param name="EventId">The ledger-assigned event identifier.</param>
    /// <param name="ContractId">The on-ledger contract ID of the created contract.</param>
    /// <param name="TemplateId">The template identifier (package + module + entity).</param>
    /// <param name="CreateArguments">Wire-level create-argument payload.</param>
    /// <param name="WitnessParties">Parties notified of this event.</param>
    /// <param name="Signatories">Parties that authorized the contract's creation.</param>
    /// <param name="Observers">Parties with read access to the contract.</param>
    /// <param name="ContractKey">The contract's key, when its template declares one;
    /// <c>null</c> otherwise. Mirrors <see cref="CreatedEvent.ContractKey"/>.</param>
    /// <param name="CreatedAt">Ledger-effective time at which the contract was created;
    /// <c>null</c> when the transport does not supply it. Mirrors
    /// <see cref="CreatedEvent.CreatedAt"/>.</param>
    public sealed record Created(
        string EventId,
        string ContractId,
        Identifier TemplateId,
        DamlRecord CreateArguments,
        EquatableArray<Party> WitnessParties,
        EquatableArray<Party> Signatories,
        EquatableArray<Party> Observers,
        ContractKey? ContractKey = null,
        DateTimeOffset? CreatedAt = null) : TreeEvent
    {
        /// <summary>
        /// Interface ids the participant computed for this created event
        /// (Canton gRPC <c>CreatedEvent.interface_views[].interface_id</c>).
        /// Defaults to empty — populated by ledger-client transport
        /// implementations for interface-only consumption, where a contract is
        /// known only as an interface and must be dispatched at runtime. Flattened
        /// through to <see cref="CreatedContract.InterfaceIds"/> by
        /// <see cref="TransactionTreeExtensions.ToTransactionResult"/>.
        /// </summary>
        public EquatableArray<Identifier> InterfaceIds { get; init; }

        /// <summary>
        /// The create arguments as the Daml-LF JSON the participant sent, when the transport could not decode
        /// them because the process holds no generated type for <see cref="TemplateId"/> (or holds more than
        /// one) — <see langword="null"/> when <see cref="CreateArguments"/> was decoded. In that case
        /// <see cref="CreateArguments"/> is an empty record. Flattened through to
        /// <see cref="CreatedContract.UndecodedPayload"/> by <see cref="TransactionTreeExtensions.ToTransactionResult"/>.
        /// </summary>
        public DamlUndecodedJson? UndecodedCreateArguments { get; init; }
    }

    /// <summary>
    /// A choice-exercise node in a <see cref="TransactionTree"/>. Carries the
    /// wire-level <see cref="ChoiceArgument"/> and <see cref="ExerciseResult"/>,
    /// consistent with <see cref="ExercisedEvent"/>, plus the events this
    /// exercise directly caused as <see cref="ChildEvents"/>.
    /// </summary>
    /// <param name="EventId">The ledger-assigned event identifier.</param>
    /// <param name="ContractId">The on-ledger contract ID the choice was exercised on.</param>
    /// <param name="TemplateId">The template that defines the exercised choice. The package
    /// id may differ from the target contract's package id when the contract has been
    /// upgraded or downgraded.</param>
    /// <param name="InterfaceId">When the choice is inherited from an interface, the
    /// interface identifier; <c>null</c> for choices defined directly on the template.</param>
    /// <param name="ChoiceName">The choice that was exercised on the target contract.</param>
    /// <param name="ChoiceArgument">The argument value passed to the choice. Wire-level
    /// <see cref="DamlValue"/>; codegen-emitted wrappers deserialize to the typed argument.</param>
    /// <param name="ExerciseResult">The result returned by the choice. Wire-level
    /// <see cref="DamlValue"/>; codegen-emitted wrappers deserialize to the typed return.</param>
    /// <param name="Consuming">Whether the exercise consumed (archived) the target contract.</param>
    /// <param name="ActingParties">Parties that exercised the choice.</param>
    /// <param name="WitnessParties">Parties notified of this event.</param>
    /// <param name="ChildEvents">The events this exercise directly caused — its
    /// sub-creates and sub-exercises — in transaction order.</param>
    public sealed record Exercised(
        string EventId,
        string ContractId,
        Identifier TemplateId,
        Identifier? InterfaceId,
        ChoiceName ChoiceName,
        DamlValue ChoiceArgument,
        DamlValue ExerciseResult,
        bool Consuming,
        EquatableArray<Party> ActingParties,
        EquatableArray<Party> WitnessParties,
        EquatableArray<TreeEvent> ChildEvents) : TreeEvent
    {
        /// <summary>
        /// Hashes the declared fields, folding in the number of <see cref="ChildEvents"/> rather
        /// than the children themselves. Equal nodes still hash alike, since equal trees have
        /// equal child counts, but hashing a node costs the node rather than its subtree, so a
        /// deep tree cannot overflow the stack here any more than it can in
        /// <see cref="DescendantEvents"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The synthesized hash this replaces also folded in the record's
        /// <c>EqualityContract</c>; this one does not. Both <see cref="Created"/> and
        /// <see cref="Exercised"/> are sealed, so cross-type hash collisions are bounded to those
        /// two concrete types, and their disjoint field sets make an actual collision essentially
        /// impossible. Equality, not the hash, decides.
        /// </para>
        /// <para>
        /// Only the hash is bounded. Equality is the record's own and compares
        /// <see cref="ChildEvents"/> by content, so comparing two separately built, structurally
        /// equal trees recurses to their depth — including a set or dictionary lookup that finds
        /// an equal-hashing entry. Comparing a node with itself short-circuits.
        /// </para>
        /// </remarks>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(EventId);
            hash.Add(ContractId);
            hash.Add(TemplateId);
            hash.Add(InterfaceId);
            hash.Add(ChoiceName);
            hash.Add(ChoiceArgument);
            hash.Add(ExerciseResult);
            hash.Add(Consuming);
            hash.Add(ActingParties);
            hash.Add(WitnessParties);
            hash.Add(ChildEvents.Count);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Renders the declared fields, printing the number of <see cref="ChildEvents"/> rather
        /// than the children themselves, so rendering a node costs the node rather than its
        /// subtree and a deep tree cannot overflow the stack here any more than it can in
        /// <see cref="DescendantEvents"/>.
        /// </summary>
        protected override bool PrintMembers(StringBuilder builder)
        {
            builder.Append("EventId = ").Append(EventId)
                .Append(", ContractId = ").Append(ContractId)
                .Append(", TemplateId = ").Append(TemplateId)
                .Append(", InterfaceId = ").Append(InterfaceId)
                .Append(", ChoiceName = ").Append(ChoiceName)
                .Append(", ChoiceArgument = ").Append(ChoiceArgument)
                .Append(", ExerciseResult = ").Append(ExerciseResult)
                .Append(", Consuming = ").Append(Consuming)
                .Append(", ActingParties = ").Append(ActingParties)
                .Append(", WitnessParties = ").Append(WitnessParties)
                .Append(", ChildEvents.Count = ").Append(ChildEvents.Count);
            return true;
        }
    }
}
