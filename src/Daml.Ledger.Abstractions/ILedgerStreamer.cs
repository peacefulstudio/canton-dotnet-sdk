// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Threading;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions;

/// <summary>The streaming capability: subscribe to contract events and ACS snapshots.</summary>
public interface ILedgerStreamer
{
    /// <summary>
    /// Streams active-contract-set delta events for <typeparamref name="T"/> over the
    /// half-open offset window <c>(fromOffset, toOffset]</c> — lower bound exclusive,
    /// upper bound inclusive.
    /// </summary>
    /// <remarks>
    /// Uses ACS-delta transaction shape with stakeholder-based visibility — the same
    /// visibility basis as the <see cref="SubscribeActiveAsync{T}"/> snapshot, so the
    /// live stream and the snapshot reconstruct the same contract set: a snapshot
    /// followed by a resume from its offset rebuilds exactly the contracts the
    /// snapshot held, plus every subsequent delta, with no divergence. The stream
    /// emits <see cref="ContractStreamEvent{T}.Created"/> and
    /// <see cref="ContractStreamEvent{T}.Archived"/> events — an archival is a
    /// first-class <see cref="ContractStreamEvent{T}.Archived"/> event, not a
    /// consuming exercise — plus
    /// <see cref="ContractStreamEvent{T}.Assigned"/>/<see cref="ContractStreamEvent{T}.Unassigned"/>
    /// for cross-synchronizer reassignments,
    /// <see cref="ContractStreamEvent{T}.Checkpoint"/>,
    /// <see cref="ContractStreamEvent{T}.StreamError"/>, and
    /// <see cref="ContractStreamEvent{T}.Unclassified"/>. It never emits
    /// <see cref="ContractStreamEvent{T}.Exercised"/>; that variant appears only on
    /// the ledger-effects shape exposed by <see cref="SubscribeLedgerEffectsAsync{T}"/>.
    /// A consumer maintaining a contract cache evicts on
    /// <see cref="ContractStreamEvent{T}.Archived"/> and checkpoints on the last
    /// observed offset — the documented cache/checkpoint pattern is sound on this
    /// shape. For an event boundary, <c>break</c> out of the enumeration; for an
    /// offset boundary, pass <paramref name="toOffset"/>.
    /// </remarks>
    /// <typeparam name="T">The Daml template the stream is filtered to. Subscribing to a
    /// Daml interface goes through the witness overload of the same name, which yields the
    /// interface's view record as the payload.</typeparam>
    /// <param name="submitter">
    /// The submitter authorization whose combined parties scope visibility. A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party
    /// subscription passes the party itself here.
    /// </param>
    /// <param name="fromOffset">
    /// Exclusive lower bound: the stream resumes strictly after this offset, so the
    /// event at <paramref name="fromOffset"/> is never delivered. <c>null</c> means
    /// <see cref="LedgerOffset.Begin"/>. Because the bound is exclusive, resuming
    /// from a previously returned offset — a persisted
    /// <see cref="ContractStreamEvent{T}.Checkpoint"/> offset or a completion
    /// offset — never re-delivers the event already seen at that offset.
    /// See <see href="https://docs.canton.network/reference/json-api-reference/post-v2updates">Canton
    /// Ledger API — update stream offsets (begin exclusive, end inclusive)</see>.
    /// </param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>;

    /// <summary>
    /// Resumes the stakeholder-based live stream from a <see cref="SubscribeActiveAsync{T}"/>
    /// snapshot's terminal checkpoint — the typed counterpart of
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>
    /// that guards the snapshot↔stream pairing at compile time.
    /// </summary>
    /// <remarks>
    /// Default implementation forwards to
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>
    /// via <see cref="StakeholderResume.Offset"/>; implementations may override for a more
    /// direct path.
    /// </remarks>
    /// <typeparam name="T">The Daml template the stream is filtered to. Subscribing to a
    /// Daml interface goes through the witness overload of the same name, which yields the
    /// interface's view record as the payload.</typeparam>
    /// <param name="submitter">
    /// The submitter authorization whose combined parties scope visibility. A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party
    /// subscription passes the party itself here.
    /// </param>
    /// <param name="resumeFrom">The resume ticket from a <see cref="SubscribeActiveAsync{T}"/>
    /// snapshot's terminal <see cref="AcsSnapshotEntry{T}.Checkpoint"/>.</param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        StakeholderResume resumeFrom,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        SubscribeAsync<T>(submitter, resumeFrom.Offset, toOffset, cancellationToken);

    /// <summary>Streams the active-contract-set snapshot for <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// A successful snapshot stream terminates with a single terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/> carrying the snapshot's
    /// effective offset as a <see cref="StakeholderResume"/> ticket — emitted even
    /// when the snapshot is empty. Resume a live
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, StakeholderResume, LedgerOffset?, CancellationToken)"/>
    /// from that ticket for a gapless, duplicate-free handover: because that overload
    /// treats its lower bound as exclusive, the event at the snapshot offset is not
    /// delivered twice across the handover. A mid-snapshot transport fault surfaces
    /// in-band as a terminal <see cref="AcsSnapshotEntry{T}.StreamError"/> in place of
    /// that <see cref="AcsSnapshotEntry{T}.Checkpoint"/>, so a caller draining the
    /// snapshot handles faults as values rather than exceptions — the same fault
    /// contract as <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>.
    /// An active contract arrives as an <see cref="AcsSnapshotEntry{T}.Created"/> row
    /// carrying its create-arguments already decoded into <typeparamref name="T"/>.
    /// See <see href="https://docs.canton.network/reference/json-api-asyncapi-reference/operations/v2-state-active-contracts/details">Canton
    /// Ledger API — active contracts snapshot and its active-at offset</see> for the snapshot→stream
    /// handover this mirrors.
    /// </remarks>
    /// <typeparam name="T">The Daml template the stream is filtered to. Subscribing to a
    /// Daml interface goes through the witness overload of the same name, which yields the
    /// interface's view record as the payload.</typeparam>
    /// <param name="submitter">
    /// The submitter authorization whose combined parties scope visibility. A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party
    /// subscription passes the party itself here.
    /// </param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="includeDisclosure">
    /// <c>true</c> asks the participant for each contract's <c>created_event_blob</c>, so every
    /// <c>Created</c> row carries a <c>Disclosure</c> another party can attach to its submission.
    /// <c>false</c>, the default, leaves the blob out and <c>Disclosure</c> <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>;

    /// <summary>
    /// Streams ledger-effects events for <typeparamref name="T"/> over the half-open
    /// offset window <c>(fromOffset, toOffset]</c> — lower bound exclusive, upper
    /// bound inclusive.
    /// </summary>
    /// <remarks>
    /// Uses ledger-effects transaction shape with witness-based visibility. The
    /// stream emits <see cref="ContractStreamEvent{T}.Created"/> and
    /// <see cref="ContractStreamEvent{T}.Exercised"/> events — a consuming exercise
    /// (<see cref="ContractStreamEvent{T}.Exercised.Consuming"/> is <c>true</c>) is a
    /// contract's archival on this shape, so there is no separate
    /// <see cref="ContractStreamEvent{T}.Archived"/> event — plus
    /// <see cref="ContractStreamEvent{T}.Assigned"/>/<see cref="ContractStreamEvent{T}.Unassigned"/>
    /// for cross-synchronizer reassignments,
    /// <see cref="ContractStreamEvent{T}.Checkpoint"/>,
    /// <see cref="ContractStreamEvent{T}.StreamError"/>, and
    /// <see cref="ContractStreamEvent{T}.Unclassified"/>. It never emits
    /// <see cref="ContractStreamEvent{T}.Archived"/>; that variant appears only on
    /// the ACS-delta shape exposed by
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>.
    /// A consumer maintaining a contract cache must therefore evict on the consuming
    /// <see cref="ContractStreamEvent{T}.Exercised"/>, not on
    /// <see cref="ContractStreamEvent{T}.Archived"/> — waiting on the latter here
    /// leaves archived contracts cached forever.
    /// <para>
    /// This shape matches on witnesses, whereas <see cref="SubscribeActiveAsync{T}"/>
    /// matches on stakeholders, so a snapshot's <see cref="StakeholderResume"/> ticket
    /// only resumes <see cref="SubscribeAsync{T}(SubmitterInfo, StakeholderResume, LedgerOffset?, CancellationToken)"/>
    /// — there is no overload of this method accepting it; use
    /// <see cref="StakeholderResume.Offset"/> if a cross-basis resume is deliberate.
    /// </para>
    /// For an event boundary (e.g. the target contract's consuming
    /// <see cref="ContractStreamEvent{T}.Exercised"/> event), <c>break</c> out of the
    /// enumeration; for an offset boundary, pass <paramref name="toOffset"/>.
    /// </remarks>
    /// <typeparam name="T">The Daml template the stream is filtered to. Subscribing to a
    /// Daml interface goes through the witness overload of the same name, which yields the
    /// interface's view record as the payload.</typeparam>
    /// <param name="submitter">
    /// The submitter authorization whose combined parties scope visibility. A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party
    /// subscription passes the party itself here.
    /// </param>
    /// <param name="fromOffset">
    /// Exclusive lower bound: the stream resumes strictly after this offset, so the
    /// event at <paramref name="fromOffset"/> is never delivered. <c>null</c> means
    /// <see cref="LedgerOffset.Begin"/>. Because the bound is exclusive, resuming
    /// from a previously returned offset — a persisted
    /// <see cref="ContractStreamEvent{T}.Checkpoint"/> offset or a completion
    /// offset — never re-delivers the event already seen at that offset.
    /// </param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>;

    /// <summary>
    /// Streams active-contract-set delta events for the Daml interface
    /// <typeparamref name="TInterface"/> over the half-open offset window
    /// <c>(fromOffset, toOffset]</c> — lower bound exclusive, upper bound inclusive.
    /// </summary>
    /// <remarks>
    /// The interface-family counterpart of
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>,
    /// with the same shape contract: it emits
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Created"/> and
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Archived"/> and never
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Exercised"/>. The payload is the
    /// participant-computed interface view rather than the implementing template's record,
    /// which an interface subscription never sees; a matching event delivered without a view
    /// is surfaced as
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Unclassified"/> with
    /// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/>.
    /// </remarks>
    /// <typeparam name="TInterface">The Daml interface marker the stream is filtered to.</typeparam>
    /// <typeparam name="TView">The interface's view record type.</typeparam>
    /// <param name="view">The marker's generated <c>View</c> witness — e.g.
    /// <c>SubscribeAsync(IHolding.View, parties)</c>. C# performs no partial type-argument
    /// inference, so the pair travels as one argument and both type parameters are inferred
    /// from it.</param>
    /// <param name="submitter">
    /// The submitter authorization whose combined parties scope visibility. A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party
    /// subscription passes the party itself here.
    /// </param>
    /// <param name="fromOffset">
    /// Exclusive lower bound: the stream resumes strictly after this offset, so the
    /// event at <paramref name="fromOffset"/> is never delivered. <c>null</c> means
    /// <see cref="LedgerOffset.Begin"/>.
    /// </param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>;

    /// <summary>
    /// Resumes the stakeholder-based live interface stream from a
    /// <see cref="SubscribeActiveAsync{TInterface, TView}"/> snapshot's terminal checkpoint —
    /// the interface-family counterpart of
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, StakeholderResume, LedgerOffset?, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// Default implementation forwards to
    /// <see cref="SubscribeAsync{TInterface, TView}(ViewDescriptor{TInterface, TView}, SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>
    /// via <see cref="StakeholderResume.Offset"/>; implementations may override for a more
    /// direct path.
    /// </remarks>
    /// <typeparam name="TInterface">The Daml interface marker the stream is filtered to.</typeparam>
    /// <typeparam name="TView">The interface's view record type.</typeparam>
    /// <param name="view">The marker's generated <c>View</c> witness.</param>
    /// <param name="submitter">
    /// The submitter authorization whose combined parties scope visibility. A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party
    /// subscription passes the party itself here.
    /// </param>
    /// <param name="resumeFrom">The resume ticket from a
    /// <see cref="SubscribeActiveAsync{TInterface, TView}"/> snapshot's terminal
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Checkpoint"/>.</param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        StakeholderResume resumeFrom,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        SubscribeAsync(view, submitter, resumeFrom.Offset, toOffset, cancellationToken);

    /// <summary>
    /// Streams the active-contract-set snapshot for the Daml interface
    /// <typeparamref name="TInterface"/>.
    /// </summary>
    /// <remarks>
    /// The interface-family counterpart of <see cref="SubscribeActiveAsync{T}"/>, with the
    /// same terminal contract: a successful snapshot ends with a single
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Checkpoint"/> carrying a
    /// <see cref="StakeholderResume"/> ticket — emitted even when the snapshot is empty — and
    /// a mid-snapshot transport fault ends it with a terminal
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.StreamError"/> instead. Each
    /// active contract arrives as an
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Created"/> row carrying the
    /// participant-computed interface view decoded into <typeparamref name="TView"/>; a row
    /// delivered without a view is surfaced as
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Unclassified"/> with
    /// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/>.
    /// </remarks>
    /// <typeparam name="TInterface">The Daml interface marker the snapshot is filtered to.</typeparam>
    /// <typeparam name="TView">The interface's view record type.</typeparam>
    /// <param name="view">The marker's generated <c>View</c> witness — e.g.
    /// <c>SubscribeActiveAsync(IHolding.View, parties)</c>.</param>
    /// <param name="submitter">
    /// The submitter authorization whose combined parties scope visibility. A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party
    /// subscription passes the party itself here.
    /// </param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="includeDisclosure">
    /// <c>true</c> asks the participant for each contract's <c>created_event_blob</c>, so every
    /// <c>Created</c> row carries a <c>Disclosure</c> another party can attach to its submission.
    /// <c>false</c>, the default, leaves the blob out and <c>Disclosure</c> <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>;

    /// <summary>
    /// Streams ledger-effects events for the Daml interface <typeparamref name="TInterface"/>
    /// over the half-open offset window <c>(fromOffset, toOffset]</c> — lower bound
    /// exclusive, upper bound inclusive.
    /// </summary>
    /// <remarks>
    /// The interface-family counterpart of <see cref="SubscribeLedgerEffectsAsync{T}"/>, with
    /// the same shape contract: a consuming
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Exercised"/> is a contract's
    /// archival on this shape, so it never emits
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Archived"/>. As on every interface
    /// subscription, the payload is the participant-computed view and a matching event
    /// delivered without one is surfaced as
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Unclassified"/> with
    /// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/>.
    /// </remarks>
    /// <typeparam name="TInterface">The Daml interface marker the stream is filtered to.</typeparam>
    /// <typeparam name="TView">The interface's view record type.</typeparam>
    /// <param name="view">The marker's generated <c>View</c> witness — e.g.
    /// <c>SubscribeLedgerEffectsAsync(IHolding.View, parties)</c>.</param>
    /// <param name="submitter">
    /// The submitter authorization whose combined parties scope visibility. A single
    /// <see cref="Daml.Runtime.Data.Party"/> converts implicitly, so a one-party
    /// subscription passes the party itself here.
    /// </param>
    /// <param name="fromOffset">
    /// Exclusive lower bound: the stream resumes strictly after this offset, so the
    /// event at <paramref name="fromOffset"/> is never delivered. <c>null</c> means
    /// <see cref="LedgerOffset.Begin"/>.
    /// </param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>;
}
