// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Which event shape a transaction's event list carries. Each shape carries its own identity and
/// offset fields, which is what <see cref="ContractStreamProjectorParityTests"/> pins.
/// </summary>
public enum TransactionEventShape
{
    /// <summary>A created event, classifying as <c>Created</c> when it matches the marker.</summary>
    Created,

    /// <summary>An archived event, classifying as <c>Archived</c> when it matches the marker.</summary>
    Archived,

    /// <summary>An exercised event, classifying as <c>Exercised</c> when it matches the marker.</summary>
    Exercised,

    /// <summary>An event with no case set at all, which no marker can classify.</summary>
    Empty,
}

/// <summary>
/// A transport-neutral description of one transaction and the events it carries. Each transport's
/// parity subclass renders it into its own wire shape — protobuf messages for gRPC, a JSON body for
/// HTTP — so the shared assertions compare classification outcomes rather than encodings.
/// </summary>
public sealed record TransactionEventScenario
{
    /// <summary>The offset the transaction itself carries.</summary>
    public const long TransactionOffset = 70L;

    /// <summary>The offset the scenario's event carries.</summary>
    public const long EventOffset = 71L;

    /// <summary>The offset the trailing well-formed created event carries.</summary>
    public const long TrailingEventOffset = 72L;

    /// <summary>The choice name an exercised event reports.</summary>
    public const string ChoiceName = "Transfer";

    /// <summary>The argument an exercised event reports, distinct from <see cref="ExerciseResultValue"/>.</summary>
    public const string ChoiceArgumentValue = "choice-argument-value";

    /// <summary>The result an exercised event reports, distinct from <see cref="ChoiceArgumentValue"/>.</summary>
    public const string ExerciseResultValue = "exercise-result-value";

    /// <summary>Which event shape to render.</summary>
    public required TransactionEventShape Event { get; init; }

    /// <summary>The event's entity name — the lever that decides whether the marker matches.</summary>
    public string EntityName { get; init; } = ActiveContractScenario.MatchingEntityName;

    /// <summary>The synchronizer the transaction carries, or <c>null</c> to omit it entirely.</summary>
    public string? Synchronizer { get; init; } = ActiveContractScenario.SynchronizerId;

    /// <summary>Renders the event with no template id, which every transport's decoder rejects.</summary>
    public bool OmitTemplateId { get; init; }

    /// <summary>
    /// Renders the event with no offset of its own — the unset field every transport encodes as
    /// zero — so a resume offset read off the event rather than off the transaction is visible as
    /// the beginning of the ledger.
    /// </summary>
    public bool OmitEventOffset { get; init; }

    /// <summary>
    /// Appends a well-formed matching created event after the scenario's own event. It carries the
    /// same <see cref="InterfaceView"/> as the scenario's own created event, so it matches an
    /// interface marker exactly when the scenario asks for a view.
    /// </summary>
    public bool FollowedByMatchingCreated { get; init; }

    /// <summary>
    /// The interface view a created event carries — the lever the interface-marker lane turns on
    /// <see cref="TransactionEventShape.Created"/>.
    /// </summary>
    public InterfaceViewRendering InterfaceView { get; init; } = InterfaceViewRendering.None;

    /// <summary>
    /// Renders an archived or exercised event as implementing the subscribed interface — the lever
    /// the interface-marker lane turns on <see cref="TransactionEventShape.Archived"/> and
    /// <see cref="TransactionEventShape.Exercised"/>, which carry no view of their own.
    /// </summary>
    public bool ImplementsSubscribedInterface { get; init; }

    /// <summary>Renders a created event with no create arguments, whatever view it carries.</summary>
    public bool OmitCreateArguments { get; init; }
}
