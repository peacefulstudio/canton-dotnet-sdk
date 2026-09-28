// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Represents a contract creation event from the ledger.
/// </summary>
/// <param name="EventId">The event's identifier within its transaction.</param>
/// <param name="ContractId">The on-ledger contract ID of the created contract.</param>
/// <param name="TemplateId">The template the contract instantiates.</param>
/// <param name="CreateArguments">The create-arguments record.</param>
/// <param name="WitnessParties">Parties notified of this event.</param>
/// <param name="Signatories">Parties that authorized the create.</param>
/// <param name="Observers">Parties the template names as observers.</param>
/// <param name="ContractKey">The contract key the event carried, or <c>null</c> when the
/// template declares none. Mandatory rather than defaulted: this is the single entry point
/// feeding every downstream key slot, so a transport must state the absence rather than
/// inherit it and populate every consumer's key with <c>null</c> while compiling clean.</param>
/// <param name="CreatedAt">The ledger-effective time of the create, when the event
/// reported one.</param>
public sealed record CreatedEvent(
    string EventId,
    string ContractId,
    Identifier TemplateId,
    DamlRecord CreateArguments,
    EquatableArray<Party> WitnessParties,
    EquatableArray<Party> Signatories,
    EquatableArray<Party> Observers,
    ContractKey? ContractKey,
    DateTimeOffset? CreatedAt = null);

/// <summary>
/// Represents a contract key.
/// </summary>
/// <param name="Value">The key value, wire-level. Decode it with the same hop generated
/// code performs — <c>TKey.FromRecord(Value.As&lt;DamlRecord&gt;())</c> — or read it
/// directly when the template's key is a bare scalar.</param>
/// <param name="TemplateId">The template that declares the key, when the event named one;
/// <c>null</c> otherwise.</param>
public sealed record ContractKey(DamlValue Value, Identifier? TemplateId = null)
{
    /// <summary>
    /// The ledger's hash of this key — the value Canton indexes keyed contracts by. It
    /// travels beside the key on both wire formats (<c>contractKeyHash</c> on the JSON
    /// encoding, <c>contract_key_hash</c> on gRPC) and is held here as the base64 text the
    /// JSON encoding uses. A <c>null</c> there is a stated absence: the created event
    /// carried no hash, or the key was constructed by a caller rather than read off the
    /// wire. It is never computed here — the hash is the ledger's, and a transport that
    /// reads one off a created event populates it.
    /// </summary>
    /// <remarks>
    /// Deliberately excluded from <see cref="Equals(ContractKey)"/> and
    /// <see cref="GetHashCode"/>: two keys are the same key when they name the same value
    /// of the same template, and a key read off the wire must therefore equal the same key
    /// a caller constructed to exercise by it — which carries no hash. Including it would
    /// make by-key matching fail quietly depending on where the key came from.
    /// </remarks>
    public string? KeyHash { get; init; }

    /// <summary>
    /// Compares two keys by the key they name — <see cref="Value"/> and
    /// <see cref="TemplateId"/> — ignoring <see cref="KeyHash"/>.
    /// </summary>
    /// <param name="other">The key to compare against.</param>
    /// <returns><c>true</c> when both name the same key.</returns>
    public bool Equals(ContractKey? other) =>
        other is not null
        && EqualityComparer<DamlValue>.Default.Equals(Value, other.Value)
        && EqualityComparer<Identifier?>.Default.Equals(TemplateId, other.TemplateId);

    /// <summary>
    /// Hashes the key by the key it names, consistently with
    /// <see cref="Equals(ContractKey)"/>.
    /// </summary>
    /// <returns>A hash code over <see cref="Value"/> and <see cref="TemplateId"/>.</returns>
    public override int GetHashCode() => HashCode.Combine(Value, TemplateId);
}

/// <summary>
/// Represents an archived (consumed) contract event.
/// </summary>
/// <param name="EventId">The ledger-assigned event identifier.</param>
/// <param name="ContractId">The on-ledger contract ID of the archived contract.</param>
/// <param name="TemplateId">The template identifier (package + module + entity).</param>
/// <param name="WitnessParties">Parties notified of this event.</param>
public sealed record ArchivedEvent(
    string EventId,
    string ContractId,
    Identifier TemplateId,
    EquatableArray<Party> WitnessParties);

/// <summary>
/// Represents a choice-exercise event observed in a transaction. Carries the
/// wire-level <see cref="ExerciseResult"/> so codegen-emitted choice wrappers
/// can deserialize the choice's typed return value (e.g. project a
/// <c>choice GetTrailingTwap : Decimal</c> result to <c>ExerciseOutcome&lt;decimal&gt;</c>).
/// </summary>
/// <param name="ContractId">The on-ledger contract ID the choice was exercised on.</param>
/// <param name="TemplateId">The template that defines the executed choice. The package
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
public sealed record ExercisedEvent(
    string ContractId,
    Identifier TemplateId,
    Identifier? InterfaceId,
    ChoiceName ChoiceName,
    DamlValue ChoiceArgument,
    DamlValue ExerciseResult,
    bool Consuming,
    EquatableArray<Party> ActingParties,
    EquatableArray<Party> WitnessParties);
