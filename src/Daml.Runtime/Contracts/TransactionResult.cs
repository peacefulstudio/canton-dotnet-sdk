// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Outcome of a fire-and-wait submission: the effective <see cref="CommandId"/> the
/// participant recorded for the submission, together with the resulting transaction's
/// <see cref="UpdateId"/> and <see cref="CompletionOffset"/>. Returned by
/// <c>ILedgerClient.SubmitAndWaitAsync</c> so callers can correlate the completion with
/// the command id used for deduplication — even when the id was assigned by the client
/// rather than supplied by the caller.
/// </summary>
/// <param name="CommandId">The effective command id the participant recorded for the
/// submission, used for deduplication.</param>
/// <param name="UpdateId">Ledger-assigned update identifier of the resulting transaction.</param>
/// <param name="CompletionOffset">Offset at which the transaction was committed.</param>
public sealed record SubmitAndWaitResult(
    CommandId CommandId,
    string UpdateId,
    LedgerOffset CompletionOffset);

/// <summary>
/// Result of a submitted transaction.
/// </summary>
/// <param name="UpdateId">Ledger-assigned update identifier.</param>
/// <param name="CompletionOffset">Offset at which the transaction was committed.</param>
/// <param name="CreatedContracts">Contracts created by the transaction. Project to
/// typed <see cref="ContractId{T}"/> values via <see cref="TransactionResultExtensions"/>.</param>
/// <param name="ArchivedContractIds">Raw contract IDs archived by the transaction.</param>
/// <param name="CommandId">The effective command id the participant recorded for the
/// submission that produced this transaction, used for deduplication; <c>null</c> when
/// the participant reported none. The Ledger API omits the command id on transactions
/// this participant did not submit, so callers must treat it as optional rather than
/// assume every transaction carries one.</param>
public sealed record TransactionResult(
    string UpdateId,
    LedgerOffset CompletionOffset,
    EquatableArray<CreatedContract> CreatedContracts,
    EquatableArray<string> ArchivedContractIds,
    CommandId? CommandId)
{
    /// <summary>
    /// Choice-exercise events observed in the transaction, in transaction order.
    /// Defaults to empty — populated by ledger-client transport
    /// implementations when the transaction was requested with
    /// ledger-effects shape. Codegen-emitted choice wrappers deserialize each
    /// <see cref="ExercisedEvent.ExerciseResult"/> through the appropriate typed
    /// projector to surface a typed <c>ExerciseOutcome&lt;TResult&gt;</c> for choices
    /// whose return type is not a contract id (e.g. <c>choice C : Decimal</c>). Empty is
    /// legitimate for a result obtained by other means, but a successful result from a
    /// ledger writer's primary submit-and-wait method must populate this, including
    /// nonconsuming exercises — see that method's documentation.
    /// </summary>
    public EquatableArray<ExercisedEvent> ExercisedEvents { get; init; }
}

/// <summary>
/// Information about a contract created by a transaction. A field-for-field mirror of
/// <see cref="TreeEvent.Created"/>, so
/// <see cref="TransactionTreeExtensions.ToTransactionResult"/> flattens a create node
/// losslessly.
/// </summary>
/// <param name="EventId">The ledger-assigned event identifier.</param>
/// <param name="ContractId">The on-ledger contract ID.</param>
/// <param name="TemplateId">The template identifier (package + module + entity).</param>
/// <param name="Payload">The create-arguments record — <see cref="TreeEvent.Created.CreateArguments"/>
/// under the name the snapshot-side shapes use for the same slot (compare
/// <c>AcsSnapshotEntry.Created.Payload</c>); the mirror is field-for-field, not name-for-name
/// here. Non-nullable, so a transport
/// projecting a created event the participant sent without create arguments — the
/// interface-only case, where the contract is known only as an interface — passes an
/// empty record, which a consumer cannot distinguish from a template whose payload
/// genuinely has no fields. The same empty record stands in when the JSON Ledger API client held no
/// generated type to decode the arguments with; <see cref="UndecodedPayload"/> then holds them.</param>
/// <param name="WitnessParties">Parties notified of this event. Required rather than
/// defaulted: an empty list must mean the event named no witnesses, not that a producer
/// never populated the slot. That is what separates these three from the two defaulted
/// list members — <see cref="InterfaceIds"/> and
/// <see cref="TransactionResult.ExercisedEvents"/>. Witnesses, signatories and observers
/// are always on the wire, so a silently-unpopulated one is a producer bug rather than a fact
/// about the contract — an empty list is itself meaningful, and a template that names no
/// observer clause reports empty observers on every create; interface views and exercise
/// events are populated only when the read requested that shape, so empty is both common and
/// correct for them outside a ledger writer's primary submit-and-wait result, which must
/// populate exercise events on success.</param>
/// <param name="Signatories">Parties that authorized the contract's creation. Required
/// for the same reason as <paramref name="WitnessParties"/>.</param>
/// <param name="Observers">Parties the template names as observers. Required for the
/// same reason as <paramref name="WitnessParties"/>.</param>
/// <param name="ContractKey">The contract's key, when its template declares one;
/// <c>null</c> otherwise. Trailing and defaulted to mirror
/// <see cref="TreeEvent.Created.ContractKey"/> exactly. Contrast
/// <see cref="CreatedEvent.ContractKey"/>, which is mandatory: that shape is the single
/// entry point feeding the downstream key slots, whereas this one is built beside it and
/// keeps its mirror's shape.</param>
/// <param name="CreatedAt">Ledger-effective time at which the contract was created;
/// <c>null</c> when the transport does not supply it. Mirrors
/// <see cref="TreeEvent.Created.CreatedAt"/>.</param>
public sealed record CreatedContract(
    string EventId,
    string ContractId,
    Identifier TemplateId,
    DamlRecord Payload,
    EquatableArray<Party> WitnessParties,
    EquatableArray<Party> Signatories,
    EquatableArray<Party> Observers,
    ContractKey? ContractKey = null,
    DateTimeOffset? CreatedAt = null)
{
    /// <summary>
    /// Interface ids the participant computed for this created event
    /// (Canton gRPC <c>CreatedEvent.interface_views[].interface_id</c>).
    /// Defaults to empty — populated by ledger-client transport
    /// implementations for interface-only consumption, where a contract is
    /// known only as an interface and must be dispatched at runtime.
    /// </summary>
    public EquatableArray<Identifier> InterfaceIds { get; init; }

    /// <summary>
    /// The create arguments as the Daml-LF JSON the participant sent, when the transport could not decode them
    /// because the process holds no generated type for <see cref="TemplateId"/> (or holds more than one) —
    /// <see langword="null"/> when <see cref="Payload"/> was decoded. In that case <see cref="Payload"/> is an
    /// empty record, so check this property to tell it from a template whose payload genuinely has no fields.
    /// Only the JSON Ledger API client produces it: gRPC values describe themselves, so a gRPC create
    /// always decodes.
    /// </summary>
    public DamlUndecodedJson? UndecodedPayload { get; init; }
}
