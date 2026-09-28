// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Daml.Runtime.Streams;

/// <summary>
/// One entry in an active-contract-set snapshot. The snapshot yields
/// <see cref="Created"/> rows and an <see cref="Unclassified"/> row for anything the
/// projector cannot classify, then ends with a single terminal
/// <see cref="Checkpoint"/> — emitted even when the snapshot is empty — or, when the
/// transport faults mid-snapshot, a terminal <see cref="StreamError"/> in place of that
/// <see cref="Checkpoint"/>.
/// </summary>
/// <typeparam name="T">
/// The Daml template the snapshot is filtered to, matched by <c>TemplateId</c>. A
/// snapshot filtered to a Daml interface marker yields
/// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}"/> instead, whose payload is
/// the interface's view record.
/// </typeparam>
public abstract record AcsSnapshotEntry<T>
    where T : ITemplate, IDamlRecord<T>
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected AcsSnapshotEntry() { }

    /// <summary>
    /// An active contract in the snapshot.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Payload">The create-arguments, decoded into <typeparamref name="T"/>.</param>
    /// <param name="Key">The contract key read off the created event, or <c>null</c> when the
    /// event carried none. Stays wire-level even though <paramref name="Payload"/> is decoded:
    /// the key's type is the template's key type, not <typeparamref name="T"/>, so decoding it
    /// is the consumer's <c>TKey.FromRecord(Key.Value.As&lt;DamlRecord&gt;())</c> hop.</param>
    /// <param name="Offset">The ledger offset at which the contract was created — a
    /// per-contract fact, not the snapshot's position. It is not a resume point: a consumer
    /// persisting resume state must take it from the terminal <see cref="Checkpoint"/>'s
    /// <see cref="StakeholderResume"/> ticket, never from a per-row offset.</param>
    /// <param name="SynchronizerId">The synchronizer the contract is active on.</param>
    /// <param name="WitnessParties">Parties that witnessed the create event.</param>
    public sealed record Created(
        ContractId<T> ContractId,
        T Payload,
        ContractKey? Key,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        EquatableArray<Party> WitnessParties) : AcsSnapshotEntry<T>;

    /// <summary>
    /// A snapshot row the projector could not classify; surfaced, never dropped.
    /// Carries the same discriminator pair as
    /// <see cref="ContractStreamEvent{T}.Unclassified"/>, so a consumer handling both the
    /// snapshot and the live stream switches on one <see cref="UnclassifiedKind"/>
    /// vocabulary rather than on magic strings here and an enum there.
    /// </summary>
    /// <param name="Offset">The ledger offset at which the unrecognized row occurred, or
    /// <c>null</c> when the row could not be placed on the ledger at all — typically because
    /// the wire offset itself was absent or unparseable. A consumer persisting resume state
    /// must not checkpoint a <c>null</c> offset: <see cref="LedgerOffset"/> has no absent value
    /// and its <c>default</c> is <see cref="LedgerOffset.Begin"/>, a genuine ledger position,
    /// so substituting one resumes from the beginning of the ledger and re-reads the whole
    /// stream. Skip the row and keep the last offset that was real.</param>
    /// <param name="Kind">Why the row could not be classified, as a strongly-typed
    /// discriminator consumers <c>switch</c> on. <see cref="UnclassifiedKind.Unknown"/> means
    /// the transport delivered a row this layer does not recognise; the raw descriptor is
    /// then on <paramref name="RawKind"/>.</param>
    /// <param name="RawKind">The transport's raw descriptor for the unrecognized row.
    /// Non-<c>null</c> exactly when <paramref name="Kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/>, and <c>null</c> for every enumerated reason, so
    /// a consumer never sees a stale descriptor attached to a named kind.</param>
    /// <exception cref="ArgumentException"><paramref name="Kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/> with a <c>null</c> <paramref name="RawKind"/>, or
    /// an enumerated <paramref name="Kind"/> with a non-<c>null</c> <paramref name="RawKind"/>.</exception>
    public sealed record Unclassified(
        LedgerOffset? Offset,
        UnclassifiedKind Kind,
        string? RawKind = null) : AcsSnapshotEntry<T>
    {
        /// <summary>
        /// Why the row could not be classified, as a strongly-typed discriminator consumers
        /// <c>switch</c> on. Get-only, so a <c>with</c> expression cannot reassign it
        /// independently of <see cref="RawKind"/>.
        /// </summary>
        public UnclassifiedKind Kind { get; } = Kind;

        /// <summary>
        /// The transport's raw descriptor for the unrecognized row — non-<c>null</c> exactly
        /// when <see cref="Kind"/> is <see cref="UnclassifiedKind.Unknown"/>, and <c>null</c>
        /// otherwise. Get-only, so the invariant validated at construction cannot be bypassed
        /// by a <c>with</c> expression.
        /// </summary>
        public string? RawKind { get; } = UnclassifiedRawKind.Validated(
            Kind, RawKind, UnclassifiedRawKind.SnapshotRowSubject, nameof(RawKind));
    }

    /// <summary>
    /// The single terminal marker that always ends the snapshot stream — emitted
    /// even when the snapshot is empty — carrying the snapshot's effective offset
    /// as a <see cref="StakeholderResume"/> ticket.
    /// </summary>
    /// <param name="Resume">The resume ticket for the snapshot's effective offset — pass it to
    /// <c>ILedgerStreamer.SubscribeAsync</c> for a gapless, duplicate-free handover; that
    /// subscription's lower bound is exclusive, so the event at this offset is not re-delivered.
    /// The raw offset is reachable via <see cref="StakeholderResume.Offset"/>.</param>
    public sealed record Checkpoint(StakeholderResume Resume) : AcsSnapshotEntry<T>;

    /// <summary>
    /// The transport stream failed mid-snapshot. Surfaced in-band rather than
    /// thrown so a caller draining the snapshot with <c>await foreach</c> can
    /// decide policy — retry from a fresh snapshot, log, or stop — with the same
    /// value-not-exception handling it uses for
    /// <see cref="ContractStreamEvent{T}.StreamError"/> on the live subscription.
    /// </summary>
    /// <remarks>
    /// Terminal, and mutually exclusive with <see cref="Checkpoint"/>: a faulted
    /// snapshot ends with this entry instead of the <see cref="Checkpoint"/> a
    /// successful snapshot ends with, so no snapshot offset is available to hand
    /// over to a live subscription and the caller must treat the snapshot as
    /// incomplete.
    /// </remarks>
    /// <param name="Status">What the transport reported for the failed call: a gRPC status, an
    /// HTTP status, no response at all, or a response whose body could not be decoded.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    /// <param name="Category">Classification of the fault, whether the transport read it off the
    /// participant's structured Canton error or determined it without one; <c>null</c> when the
    /// failure was not classified.</param>
    /// <param name="ErrorId">Canton built-in or Daml-defined error identifier the transport
    /// decoded from the participant's structured error, under the name
    /// <see cref="ExerciseOutcome{T}.DamlError.ErrorId"/> carries on the write path — nullable
    /// here, where the write path's is not, because this one arm covers both the structured and
    /// the unstructured fault. <c>null</c> when the fault carried no structured error to decode;
    /// a transport that parsed none leaves it <c>null</c> rather than inventing a sentinel. Read
    /// it as an identity rather than parsing it: <see cref="Category"/> and
    /// <see cref="Status"/> are both too coarse to separate two faults that need opposite
    /// handling, and <see cref="Message"/> is participant prose rather than an API.</param>
    /// <param name="SourceException">Transport exception that caused the stream failure, when available.</param>
    public sealed record StreamError(
        TransportStatus Status,
        string Message,
        DamlErrorCategory? Category = null,
        string? ErrorId = null,
        Exception? SourceException = null) : AcsSnapshotEntry<T>;
}
