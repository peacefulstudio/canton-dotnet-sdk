// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The Canton participant client surface: everything on <see cref="ILedgerClient"/>
/// plus the operations that are specific to a Canton node and absent from the upstream
/// abstraction — fire-and-forget submission, the command completion stream,
/// connected-synchronizer and Ledger API version discovery, offset/id point reads,
/// tree-shaped submission, and traffic-cost estimation.
/// This is the type registered in dependency injection, alongside <see cref="ILedgerClient"/> and the
/// narrower reader, writer and streamer service types, all served by one adapter registration — under
/// a singleton transport they resolve to the same instance, under a transient one to an adapter each.
/// So consumers of the flagship fire path reach these operations through the injected abstraction
/// without downcasting to the concrete <c>LedgerClient</c> — keeping the client mockable and decoratable.
/// </summary>
public interface ICantonLedgerClient : ILedgerClient
{
    /// <summary>
    /// Fire-and-forget submission: hands the commands to the participant and
    /// returns once they are accepted for processing, yielding the
    /// <c>command_id</c> for correlating the eventual completion. The verdict
    /// on the transaction itself is not awaited here — observe it on
    /// <see cref="CompletionStreamAsync"/>.
    /// </summary>
    /// <remarks>
    /// A true fire path with no client-side pending-set: the consumer
    /// correlates completions by <c>command_id</c>/<c>submission_id</c> and owns
    /// its own offset. The returned <see cref="RuntimeCommands.CommandId"/> is the
    /// effective id the participant recorded — minted here when the submission
    /// omits one — so a caller retrying after a transport failure (by hand or via
    /// a resilience policy such as Polly) must resubmit with this same id for
    /// ledger-side deduplication. Re-invoking with a fresh, command_id-less
    /// submission instead mints a new id and double-submits, because the
    /// participant may have accepted the first attempt before the failure
    /// surfaced.
    /// </remarks>
    /// <param name="submission">The commands to hand to the participant.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RuntimeCommands.CommandId> SubmitAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fire-and-forget reassignment submission: submits an <see cref="UnassignCommand"/> or
    /// <see cref="AssignCommand"/> through <c>CommandSubmissionService.SubmitReassignment</c> and
    /// returns once the participant accepts it, yielding the <c>command_id</c> for correlating the
    /// eventual completion. The resulting <c>Unassigned</c>/<c>Assigned</c> event is observed on
    /// <c>SubscribeAsync</c> or <see cref="CompletionStreamAsync"/>, not awaited here.
    /// Source and target synchronizer ids are required on the command.
    /// </summary>
    /// <remarks>
    /// The two-step unassign→assign dance is the consumer's: capture the <c>reassignment_id</c> from
    /// the resulting <c>UnassignedEvent</c> and pass it to a follow-up <see cref="AssignCommand"/>.
    /// The returned <see cref="RuntimeCommands.CommandId"/> is the effective id the participant
    /// recorded — minted here when omitted — so a retry after a transport failure must resubmit with
    /// this same id for ledger-side deduplication.
    /// </remarks>
    /// <param name="submission">The reassignment submission to hand to the participant.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RuntimeCommands.CommandId> SubmitReassignmentAsync(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits an <see cref="UnassignCommand"/> or <see cref="AssignCommand"/> through
    /// <c>CommandService.SubmitAndWaitForReassignment</c> and awaits the resulting reassignment,
    /// projecting it into the typed read-side <see cref="ContractStreamEvent{T}.Unassigned"/> /
    /// <see cref="ContractStreamEvent{T}.Assigned"/> variant wrapped in the shared
    /// <see cref="ExerciseOutcome{T}"/> — one typed representation whether a reassignment
    /// is observed or caused. Source and target synchronizer ids are required on the command.
    /// </summary>
    /// <typeparam name="T">The template or interface marker the resulting contract is projected as.</typeparam>
    /// <param name="submission">The reassignment submission to submit and await.</param>
    /// <param name="timeout">
    /// Per-call deadline for the submit-and-wait RPC. Takes precedence over
    /// <c>LedgerClientOptions.Timeout</c>; null uses the configured default. Overrunning it
    /// surfaces as an <see cref="ExerciseOutcome{T}.InfraError"/>, never as caller cancellation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The projected <c>Unassigned</c>/<c>Assigned</c> variants now carry the <c>reassignment_id</c>
    /// and <c>reassignment_counter</c> the participant reported, so a consumer driving the
    /// unassign→assign dance reads the id straight off the returned event.
    /// </remarks>
    Task<ExerciseOutcome<ContractStreamEvent<T>>> TrySubmitAndWaitForReassignmentAsync<T>(
        ReassignmentSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>;

    /// <summary>
    /// Submits commands, waits for the resulting transaction, and returns it with its parent/child
    /// hierarchy intact — which exercise caused which sub-creates and sub-exercises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree-shaped counterpart to
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync(RuntimeCommands.CommandsSubmission, RuntimeCommands.SubmitterInfo, TimeSpan?, CancellationToken)"/>,
    /// which returns the same transaction flattened into separate created/archived/exercised lists.
    /// Neither shape is a superset of the other on the wire: the flat overload takes the participant's
    /// ACS-delta view (creates and archives), while hierarchy is only meaningful over the ledger-effects
    /// view (creates and exercises), which this method always requests. Callers that want the flattened
    /// shape as well project the tree with <see cref="TransactionTreeExtensions.ToTransactionResult"/>
    /// rather than submitting twice.
    /// </para>
    /// <para>
    /// Only events the submitting parties are entitled to see are returned. An event whose parent
    /// exercise the participant filtered out attaches to the nearest enclosing exercise those parties
    /// can still see, or surfaces as a root when none remains.
    /// </para>
    /// <para>
    /// A committed transaction whose events cannot describe a tree yields
    /// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/> carrying the reason, never a silently wrong tree.
    /// </para>
    /// <para>
    /// A committed transaction that cannot be decoded, for example because a payload has no loaded
    /// generated type, is returned as <see cref="ExerciseOutcome{T}.CommittedUndecodable"/>, never as a
    /// failure: do not resubmit, and read the transaction by its
    /// <see cref="ExerciseOutcome{T}.CommittedUndecodable.UpdateId"/> when it carries one.
    /// </para>
    /// </remarks>
    /// <param name="submission">The commands to submit.</param>
    /// <param name="submitter">The parties to submit as, and to read the resulting events as.</param>
    /// <param name="timeout">Overrides the client's configured request timeout when supplied.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>
    /// <see cref="ExerciseOutcome{T}.One"/> carrying the committed transaction as a tree,
    /// <see cref="ExerciseOutcome{T}.DamlError"/> when the participant rejected the commands, or
    /// <see cref="ExerciseOutcome{T}.InfraError"/> on a transport failure or a per-call timeout, or
    /// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/> when the committed transaction cannot be
    /// decoded or cannot describe a tree.
    /// </returns>
    Task<ExerciseOutcome<TransactionTree>> TrySubmitAndWaitForTransactionTreeAsync(
        RuntimeCommands.CommandsSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializes the active-contract-set snapshot for a Daml interface, decoding each row's
    /// participant-computed interface view into <typeparamref name="TView"/>. The gRPC counterpart
    /// of <see cref="IPqsClient.QueryAsync{TInterface, TView}(CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drains <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> over
    /// <typeparamref name="TInterface"/> — which already asks the participant for an
    /// <c>InterfaceFilter</c> with the view included, and already projects the view record
    /// (not the implementing template's create-arguments) onto each snapshot row — and decodes
    /// that record through the generated view type's <c>FromRecord</c> factory. Both type
    /// arguments are explicit (<c>QueryActiveAsync&lt;IHolding, HoldingView&gt;(party)</c>)
    /// because the queried interface and its view are distinct types; the
    /// <see cref="IHasView{TView}"/> constraint ties them.
    /// </para>
    /// <para>
    /// Each returned element carries the contract's last-update offset and synchronizer id. The
    /// last-update offset identifies the update that most recently created or assigned that
    /// contract; it may point at a pruned update and is not a resume offset. To resume after the
    /// snapshot, use <see cref="ILedgerStreamer.SubscribeActiveAsync{TInterface, TView}"/> and retain
    /// its terminal checkpoint, which this materializing convenience consumes and discards.
    /// </para>
    /// <para>
    /// This is a materializing convenience, not a streaming read: it cannot hand a fault back
    /// in-band, so a snapshot that faults, carries a row the projector could not classify, or ends
    /// without its terminal checkpoint throws <see cref="LedgerOperationException"/> rather than
    /// returning a short list that looks complete. The in-band terminal-<c>StreamError</c>
    /// contract binds the <c>await foreach</c> streaming surfaces; stay on
    /// <see cref="ILedgerStreamer.SubscribeActiveAsync{TInterface, TView}"/> for value-shaped
    /// fault handling.
    /// </para>
    /// <para>
    /// Both shipped transports project the participant-computed view, so both serve this method.
    /// A snapshot row whose view the participant did not compute — an absent view, or one carrying
    /// a <c>viewStatus</c> other than <c>OK</c> — and a row whose view did not decode into
    /// <typeparamref name="TView"/> both reach the drain as an unclassified row and throw
    /// <see cref="LedgerOperationException"/> rather than yielding an empty view record.
    /// </para>
    /// </remarks>
    /// <typeparam name="TInterface">The generated Daml interface marker (e.g. <c>IHolding</c>).</typeparam>
    /// <typeparam name="TView">The interface's view record (e.g. <c>HoldingView</c>).</typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <see langword="null"/> means the current ledger end.</param>
    /// <param name="includeDisclosure">
    /// <see langword="true"/> asks the participant for each contract's <c>created_event_blob</c>, so every
    /// returned <see cref="ActiveContract{TContract}"/> carries a <see cref="ActiveContract{TContract}.Disclosure"/>
    /// naming the implementing template. <see langword="false"/>, the default, leaves it <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the underlying snapshot stream cleanly.</param>
    /// <returns>
    /// The active interface contracts, each wrapped with its last-update offset and synchronizer id.
    /// </returns>
    /// <exception cref="LedgerOperationException">
    /// The snapshot faulted, carried an unclassified row, or ended without its terminal checkpoint.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<ActiveContract<InterfaceContract<TInterface, TView>>>> QueryActiveAsync<TInterface, TView>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        InterfaceViewSnapshot.DrainAsync(
            SubscribeActiveAsync(
                new ViewDescriptor<TInterface, TView>(), submitter, activeAtOffset, includeDisclosure, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Streams command completions for the submitter's parties as they arrive,
    /// surfacing each response as a <see cref="CompletionStreamEvent"/>:
    /// <see cref="CompletionStreamEvent.CommandAccepted"/> carries the neutral
    /// <see cref="Completion"/> payload and the resulting update id for an accepted
    /// command, <see cref="CompletionStreamEvent.CommandRejected"/> carries the neutral
    /// <see cref="Completion"/> and the rejection <see cref="CompletionStatus"/>,
    /// <see cref="CompletionStreamEvent.Checkpoint"/> carries the participant's offset
    /// checkpoints so the resume offset keeps advancing during quiet periods, and a
    /// mid-stream transport fault or a completion whose payload cannot be decoded is
    /// surfaced in-band as a terminal
    /// <see cref="CompletionStreamEvent.StreamError"/> rather than thrown,
    /// at parity with the update stream. The consumer correlates each completion by
    /// <see cref="Completion.CommandId"/> and persists its own offset; the client holds
    /// no correlation or recovery state. To catch the completion of a command you are
    /// about to <see cref="SubmitAsync"/>, capture the offset before submitting and pass
    /// it as <paramref name="beginExclusiveOffset"/> — a completion can be emitted before
    /// the stream is opened. A caller cancelling via <paramref name="cancellationToken"/>
    /// gets an <see cref="OperationCanceledException"/>, not a
    /// <see cref="CompletionStreamEvent.StreamError"/>.
    /// </summary>
    /// <remarks>
    /// Enumeration may end at any time, possibly having yielded nothing. A caller that wants
    /// to keep following reopens from the highest offset it has observed — which may be the
    /// offset it passed in as <paramref name="beginExclusiveOffset"/> — and supplies its own
    /// backoff, because the call may return immediately. An ordinary ending carries no closing
    /// entry: <see cref="CompletionStreamEvent.Checkpoint"/> is emitted on the participant's own
    /// cadence rather than as a terminator, so a caller that observed nothing resumes from the
    /// offset it started from, and an immediate reopen is the right answer.
    /// <para>
    /// An enumeration that ends with a terminal <see cref="CompletionStreamEvent.StreamError"/>
    /// invites a reopen only when its <see cref="CompletionStreamEvent.StreamError.ErrorId"/> says
    /// the condition is self-clearing: <c>STALE_STREAM_AUTHORIZATION</c> resolves on a fresh stream,
    /// or fails with an explicit authentication or permission denial, while a fault the participant
    /// will report again — a window the participant answered with <c>413</c>, say — reproduces
    /// itself on the next call. The client retries none of them; reading the code is how a caller
    /// tells the two apart.
    /// </para>
    /// </remarks>
    /// <param name="submitter">The parties whose completions the stream carries.</param>
    /// <param name="beginExclusiveOffset">
    /// Exclusive lower bound: the stream resumes strictly after this offset, so a persisted
    /// <see cref="Completion.Offset"/> or <see cref="CompletionStreamEvent.Checkpoint.Offset"/>
    /// never re-delivers the entry already seen at that offset. <c>null</c> means
    /// <see cref="LedgerOffset.Begin"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<CompletionStreamEvent> CompletionStreamAsync(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? beginExclusiveOffset = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the synchronizers the participant is currently connected to.
    /// </summary>
    /// <param name="party">
    /// Optional party whose connection permissions scope the result. Null returns
    /// every synchronizer the participant is connected to, with an unspecified
    /// permission on each entry.
    /// </param>
    /// <param name="participantId">
    /// Optional participant id, for a participant querying another participant's
    /// mapping. Null defaults to the host participant.
    /// </param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ConnectedSynchronizer>> GetConnectedSynchronizersAsync(
        Party? party = null,
        string? participantId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Ledger API version reported by the participant.
    /// </summary>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> GetLedgerApiVersionAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single update by its absolute offset, projected the same way as
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/>'s success case. The
    /// <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c> parties scope
    /// visibility, with no template/interface restriction — every event those
    /// parties witness on the update is returned.
    /// </summary>
    /// <param name="offset">The absolute offset of the update to look up. Must be past
    /// <see cref="LedgerOffset.Begin"/>.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="offset"/> is <see cref="LedgerOffset.Begin"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The update at <paramref name="offset"/> is a reassignment or topology
    /// transaction rather than a ledger transaction, or the transaction payload
    /// is malformed (a required field is unset, or a value cannot be decoded) and
    /// cannot be projected.
    /// </exception>
    Task<TransactionResult> GetUpdateByOffsetAsync(
        LedgerOffset offset,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single update by its update id, projected the same way as
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/>'s success case. The
    /// <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c> parties scope
    /// visibility, with no template/interface restriction — every event those
    /// parties witness on the update is returned.
    /// </summary>
    /// <param name="updateId">The id of the update to look up.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The update with <paramref name="updateId"/> is a reassignment or topology
    /// transaction rather than a ledger transaction, or the transaction payload
    /// is malformed (a required field is unset, or a value cannot be decoded) and
    /// cannot be projected.
    /// </exception>
    Task<TransactionResult> GetUpdateByIdAsync(
        string updateId,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single update by its absolute offset and returns it with its parent/child hierarchy
    /// intact — which exercise caused which sub-creates and sub-exercises. The tree-shaped counterpart
    /// to <see cref="GetUpdateByOffsetAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Always reads the participant's ledger-effects view, because hierarchy is only meaningful over
    /// creates and exercises. <see cref="GetUpdateByOffsetAsync"/> asks for that same view over the same
    /// RPC and differs only in projection: it flattens the response into created/archived/exercised
    /// lists, while this method rebuilds the parent/child structure from the node ids already carried on
    /// those events. A caller wanting both shapes reads once and flattens the tree with
    /// <see cref="TransactionTreeExtensions.ToTransactionResult"/> rather than reading twice.
    /// </para>
    /// <para>
    /// The <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c> parties scope visibility, with
    /// no template or interface restriction. An event whose parent exercise the participant filtered
    /// out attaches to the nearest enclosing exercise those parties can still see, or surfaces as a
    /// root when none remains, so node-id gaps are normal and tolerated.
    /// </para>
    /// </remarks>
    /// <param name="offset">The absolute offset of the update to look up. Must be past
    /// <see cref="LedgerOffset.Begin"/>.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="offset"/> is <see cref="LedgerOffset.Begin"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The update at <paramref name="offset"/> is a reassignment or topology transaction rather than a
    /// ledger transaction, its payload is malformed, or its node ids cannot describe a tree — the last
    /// carried as a <c>MalformedTransactionTreeException</c> in
    /// <see cref="Exception.InnerException"/>, so catch this base type rather than the derived one.
    /// A tree that cannot be rebuilt fails loudly instead of coming back silently wrong.
    /// </exception>
    Task<TransactionTree> GetUpdateTreeByOffsetAsync(
        LedgerOffset offset,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the participant what <paramref name="submission"/> would cost in synchronizer traffic,
    /// without submitting it and without changing ledger state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The estimate comes from the participant's interactive-submission prepare step, so the commands
    /// are fully interpreted to produce it: invalid commands fail here exactly as they would on
    /// submission, and the call costs about what a submission costs. Authorization is looser than
    /// submitting, though — the caller's token needs only <em>read</em> rights for the parties in
    /// <see cref="RuntimeCommands.CommandsSubmission.ActAs"/>, not act rights, because nothing is
    /// executed. The prepared transaction is discarded; this call prices a submission rather than
    /// beginning an external-signing flow.
    /// </para>
    /// <para>
    /// The workflow id on <paramref name="submission"/> is not carried — the prepare step has no field
    /// for it.
    /// </para>
    /// </remarks>
    /// <param name="submission">The commands to price, exactly as they would be submitted.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The participant's estimate, or <see langword="null"/> when it returned none — a participant may
    /// omit the estimation, and one with traffic control disabled does. An estimation that is present
    /// but reports zero is a zero-cost estimate, not an absent one.
    /// </returns>
    Task<TrafficCostEstimate?> EstimateTrafficCostAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one contract by id through <c>ContractService.GetContract</c>, decoded into
    /// <typeparamref name="T"/>. The <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c>
    /// parties are the querying parties whose visibility scopes the lookup. The result carries no
    /// offset, because the participant never populates the created event's offset on this call.
    /// </summary>
    /// <param name="contractId">The id of the contract to read.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CreatedContract<T>> GetContractAsync<T>(
        ContractId<T> contractId,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>;

    /// <summary>
    /// Reads the creation and archival of one contract through
    /// <c>EventQueryService.GetEventsByContractId</c>, decoded into <typeparamref name="T"/>. The
    /// <paramref name="submitter"/>'s combined <c>ActAs ∪ ReadAs</c> parties scope visibility.
    /// </summary>
    /// <param name="contractId">The id of the contract whose events to read.</param>
    /// <param name="submitter">The parties whose visibility scopes the lookup.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ContractLifecycle<T>> GetEventsByContractIdAsync<T>(
        ContractId<T> contractId,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>;

    /// <summary>
    /// Reads one page of the active-contract snapshot for <typeparamref name="T"/> through
    /// <c>StateService.GetActiveContractsPage</c>. The first call omits <paramref name="pageToken"/>;
    /// each following call passes the previous page's <see cref="AcsPage{T}.NextPageToken"/> together with
    /// its <see cref="AcsPage{T}.ActiveAtOffset"/> as <paramref name="activeAtOffset"/>.
    /// </summary>
    /// <param name="submitter">The parties whose visibility scopes the snapshot.</param>
    /// <param name="activeAtOffset">
    /// The offset to compute the snapshot at; the current ledger end when omitted.
    /// </param>
    /// <param name="maxPageSize">The most entries a page may hold; the participant's default when omitted.</param>
    /// <param name="pageToken">The token of the page to read; the first page when omitted.</param>
    /// <param name="includeDisclosure">
    /// When set, each created entry carries the disclosure needed to use the contract in an explicit-disclosure submission.
    /// </param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AcsPage<T>> GetActiveContractsPageAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        int? maxPageSize = null,
        LedgerPageToken? pageToken = null,
        bool includeDisclosure = false,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>;

    /// <summary>
    /// Reads the offsets the participant has pruned up to through
    /// <c>StateService.GetLatestPrunedOffsets</c>.
    /// </summary>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PrunedOffsets> GetLatestPrunedOffsetsAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one page of transactions through <c>UpdateService.GetUpdatesPage</c>, projected the same
    /// way as <see cref="GetUpdateByIdAsync"/>. The <paramref name="submitter"/>'s combined
    /// <c>ActAs ∪ ReadAs</c> parties scope visibility.
    /// </summary>
    /// <param name="submitter">The parties whose visibility scopes the page.</param>
    /// <param name="beginExclusive">The exclusive lower offset bound; ledger begin when omitted.</param>
    /// <param name="endInclusive">The inclusive upper offset bound; the ledger end when omitted.</param>
    /// <param name="maxPageSize">The most updates a page may hold; the participant's default when omitted.</param>
    /// <param name="descendingOrder">Whether to return the newest updates first.</param>
    /// <param name="pageToken">The token of the page to read; the first page when omitted.</param>
    /// <param name="timeout">
    /// Per-call deadline overriding the client's configured request timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// A returned update is a reassignment or topology transaction rather than a ledger transaction, or its
    /// payload is malformed and cannot be projected.
    /// </exception>
    Task<UpdatesPage> GetUpdatesPageAsync(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? beginExclusive = null,
        LedgerOffset? endInclusive = null,
        int? maxPageSize = null,
        bool descendingOrder = false,
        LedgerPageToken? pageToken = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the completions of <paramref name="parties"/> through
    /// <c>CommandCompletionService.GetCompletions</c>, which reads across every user's submissions
    /// where <see cref="CompletionStreamAsync"/> reads the configured user's. The fault contract is
    /// that of <see cref="CompletionStreamAsync"/>. Over REST it reads <c>POST /v2/commands/command-completions</c>
    /// through the same window loop as <see cref="CompletionStreamAsync"/>.
    /// </summary>
    /// <param name="parties">
    /// The parties whose completions to stream. Only a user with the <c>CanReadAsAnyParty</c> right may pass none.
    /// </param>
    /// <param name="beginExclusiveOffset">The exclusive offset to start from; ledger begin when omitted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<CompletionStreamEvent> GetCompletionsAsync(
        IEnumerable<Party> parties,
        LedgerOffset? beginExclusiveOffset = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the participant to interpret and hash <paramref name="submission"/> without executing it,
    /// the first step of the external-signing flow: sign <see cref="PreparedSubmission.Hash"/>, then
    /// execute with <see cref="ExecuteSubmissionAsync"/>, <see cref="ExecuteSubmissionAndWaitAsync"/> or
    /// <see cref="ExecuteSubmissionAndWaitForTransactionAsync"/>.
    /// </summary>
    /// <remarks>
    /// The workflow id on <paramref name="submission"/> is not carried — the prepare step has no field
    /// for it. The caller's token needs only read rights for the acting parties.
    /// </remarks>
    /// <param name="submission">The commands to prepare, exactly as they would be submitted.</param>
    /// <param name="timeout">Per-call deadline overriding the client's configured request timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<PreparedSubmission> PrepareSubmissionAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a signed prepared submission and returns once the participant has accepted it, without
    /// waiting for it to commit; follow the completion stream for the outcome. The call is not retried
    /// by the opt-in retry pipeline, because replaying an executed submission is not idempotent.
    /// </summary>
    /// <param name="submission">The prepared submission and its signatures.</param>
    /// <param name="timeout">Per-call deadline overriding the client's configured request timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task ExecuteSubmissionAsync(
        SignedSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a signed prepared submission and waits for it to commit. The call is not retried by
    /// the opt-in retry pipeline, because replaying an executed submission is not idempotent.
    /// </summary>
    /// <param name="submission">The prepared submission and its signatures.</param>
    /// <param name="timeout">Per-call deadline overriding the client's configured request timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The update id and completion offset of the committed transaction.</returns>
    Task<ExecutedSubmission> ExecuteSubmissionAndWaitAsync(
        SignedSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a signed prepared submission, waits for it to commit and returns the transaction,
    /// projected the same way as <see cref="GetUpdateByIdAsync"/>. The call is not retried by the opt-in
    /// retry pipeline, because replaying an executed submission is not idempotent.
    /// </summary>
    /// <param name="submission">The prepared submission and its signatures.</param>
    /// <param name="submitter">The parties whose visibility scopes the returned transaction.</param>
    /// <param name="timeout">Per-call deadline overriding the client's configured request timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<TransactionResult> ExecuteSubmissionAndWaitForTransactionAsync(
        SignedSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the preferred package for each of <paramref name="requirements"/>: the highest version
    /// that every participant hosting the requirement's parties has vetted.
    /// </summary>
    /// <param name="requirements">The package names to resolve, each with the parties that must have vetted it.</param>
    /// <param name="synchronizerId">
    /// The synchronizer whose topology state the vetting is resolved against, or <see langword="null"/>
    /// to resolve against every synchronizer the participant is connected to.
    /// </param>
    /// <param name="vettingValidAt">
    /// The time to compute vetting validity at, or <see langword="null"/> for the participant's current time.
    /// </param>
    /// <param name="timeout">Per-call deadline overriding the client's configured request timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<PreferredPackages> GetPreferredPackagesAsync(
        IEnumerable<PackageVettingRequirement> requirements,
        SynchronizerId? synchronizerId = null,
        DateTimeOffset? vettingValidAt = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the preferred version of one package: the highest version that every participant
    /// hosting <paramref name="parties"/> has vetted. Canton deprecates the underlying route for
    /// removal in 3.6; prefer <see cref="GetPreferredPackagesAsync"/>.
    /// </summary>
    /// <param name="parties">The parties whose hosting participants must have vetted the package.</param>
    /// <param name="packageName">The package name to resolve a preferred version for.</param>
    /// <param name="synchronizerId">
    /// The synchronizer whose topology state the vetting is resolved against, or <see langword="null"/>
    /// to resolve against every synchronizer the participant is connected to.
    /// </param>
    /// <param name="vettingValidAt">
    /// The time to compute vetting validity at, or <see langword="null"/> for the participant's current time.
    /// </param>
    /// <param name="timeout">Per-call deadline overriding the client's configured request timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The preference, or <see langword="null"/> when no package satisfies the requirements.</returns>
    Task<PackagePreference?> GetPreferredPackageVersionAsync(
        IEnumerable<Party> parties,
        string packageName,
        SynchronizerId? synchronizerId = null,
        DateTimeOffset? vettingValidAt = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
