// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// Thrown by a ledger call that did not succeed — the one failure kind of a throwing call on
/// either transport, carrying the transport <see cref="Status"/>, the error <see cref="Category"/>
/// and the <see cref="CommitState"/> — by the throwing convenience wrappers in
/// <see cref="Extensions.ThrowingExercise"/>
/// when the underlying <c>Try*</c> method yields a non-success outcome, and by
/// the active-contract-set snapshot reads (<see cref="Extensions.StreamerSnapshot"/> and
/// <c>ICantonLedgerClient.QueryActiveAsync</c>, which share one drain) when a snapshot cannot be
/// completed. Carries the
/// structured data of a <see cref="ExerciseOutcome{T}.DamlError"/> /
/// <see cref="ExerciseOutcome{T}.InfraError"/> / <see cref="ExerciseOutcome{T}.CommittedUndecodable"/>
/// outcome so catch sites keep access to the detail the structured API exposes; a classified
/// infrastructure failure carries its <see cref="Category"/> alongside its
/// <see cref="Status"/>, and a committed-but-undecodable failure carries its
/// <see cref="UpdateId"/> so a catch site can read the transaction without resubmitting.
/// Derives from <see cref="InvalidOperationException"/> because the convenience wrappers
/// previously threw <see cref="InvalidOperationException"/> directly; the base
/// type preserves that catch contract.
/// </summary>
public sealed class LedgerOperationException : InvalidOperationException
{
    /// <summary>
    /// Canton error category when the failed outcome was a
    /// <see cref="ExerciseOutcome{T}.DamlError"/>, when it was an
    /// <see cref="ExerciseOutcome{T}.InfraError"/> the transport classified without a
    /// structured Canton error attached, or when a faulted stream carried one on the
    /// <c>StreamError</c> entry this exception was raised from, whether the transport read
    /// that one off the participant's structured Canton error or determined it without one;
    /// <c>null</c> when none applies.
    /// </summary>
    public DamlErrorCategory? Category { get; }

    /// <summary>
    /// Canton built-in or Daml-defined error identifier when the failed outcome was a
    /// <see cref="ExerciseOutcome{T}.DamlError"/>, or when a faulted stream carried one on the
    /// <c>StreamError</c> entry this exception was raised from; otherwise <c>null</c>. A stream
    /// fault supplies it without any <see cref="Metadata"/>, so a non-null identifier no longer
    /// implies a non-null <see cref="Metadata"/> the way it did before the stream path filled it.
    /// </summary>
    public string? ErrorId { get; }

    /// <summary>
    /// Structured detail from <c>ErrorInfo.metadata</c> when the failed outcome was a
    /// <see cref="ExerciseOutcome{T}.DamlError"/>; otherwise <c>null</c>, including when
    /// <see cref="ErrorId"/> came from a faulted stream.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// What the transport reported when the failed outcome was an
    /// <see cref="ExerciseOutcome{T}.InfraError"/> or a faulted stream; otherwise <c>null</c>.
    /// </summary>
    public TransportStatus? Status { get; }

    /// <summary>
    /// The committed transaction's update id when the failed outcome was a
    /// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/> whose response was decoded far
    /// enough to read one before decoding failed; <c>null</c> when the failed outcome was not a
    /// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/>, or was one whose decode failure
    /// happened before the id was read. The command already committed — do not resubmit it;
    /// read the transaction by this id instead.
    /// </summary>
    public string? UpdateId { get; }

    /// <summary>
    /// Whether the failed call's command committed to the ledger, decided by the kind of call.
    /// A call that only reads — a query, a point read, a prepare, an active-contract-set snapshot,
    /// an admin read — commits nothing, so every failure is <see cref="CommitState.NotCommitted"/>.
    /// A call that writes reports from what the transport and the participant said: no answer at all
    /// is <see cref="CommitState.Unknown"/>, and so is a participant error whose
    /// <see cref="Category"/> is <see cref="DamlErrorCategory.DeadlineExceededRequestStateUnknown"/>
    /// or <see cref="DamlErrorCategory.Unknown"/> (the participant could not tell either); any other
    /// structured participant error is <see cref="CommitState.NotCommitted"/>, with two exceptions
    /// keyed on <see cref="ErrorId"/> before the category is consulted. <c>DUPLICATE_COMMAND</c> is
    /// <see cref="CommitState.Committed"/>, because the ledger already accepted a command with that
    /// command id, unless its <c>accepted</c> metadata is <c>"false"</c>, which is
    /// <see cref="CommitState.Unknown"/>; the original submission's offset stays in
    /// <see cref="Metadata"/> as <c>completion_offset</c>, and <see cref="UpdateId"/> stays <c>null</c>.
    /// A command id reused for a different command reports the earlier change as
    /// <see cref="CommitState.Committed"/>, since the participant deduplicates on user, command id
    /// and submitters without comparing the payload. <c>SUBMISSION_ALREADY_IN_FLIGHT</c> is
    /// <see cref="CommitState.Unknown"/>, since the original submission may still commit.
    /// <c>DUPLICATE_CONTRACT_KEY</c>, which shares the category of <c>DUPLICATE_COMMAND</c>, is a
    /// rejection and stays <see cref="CommitState.NotCommitted"/>. An exception built
    /// from an <see cref="ExerciseOutcome{T}"/> follows the outcome instead —
    /// <see cref="CommitState.Committed"/> for <see cref="ExerciseOutcome{T}.None"/>,
    /// <see cref="ExerciseOutcome{T}.Many"/>, and <see cref="ExerciseOutcome{T}.CommittedUndecodable"/>,
    /// and <see cref="CommitState.Unknown"/> for an <see cref="ExerciseOutcome{T}.InfraError"/>.
    /// A catch site must read this, not a null <see cref="UpdateId"/>, to decide whether resubmitting
    /// the command is safe — and must resubmit with the same command id when this is
    /// <see cref="CommitState.Unknown"/>, so command-id deduplication resolves the duplicate if the
    /// original request did commit.
    /// </summary>
    public CommitState CommitState { get; }

    /// <summary>
    /// Creates an exception for a non-committed or unexpected outcome with no structured
    /// error payload.
    /// </summary>
    public LedgerOperationException(string message)
        : base(message)
    {
        CommitState = CommitState.NotCommitted;
    }

    /// <summary>
    /// Creates an exception with no structured error payload that wraps the
    /// exception that caused the failure.
    /// </summary>
    public LedgerOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
        CommitState = CommitState.NotCommitted;
    }

    /// <summary>
    /// Creates an exception carrying a <see cref="ExerciseOutcome{T}.CommittedUndecodable"/>
    /// outcome. The command already committed, so <paramref name="updateId"/> — when the
    /// response was decoded far enough to read one — is how a catch site reads the transaction
    /// instead of resubmitting.
    /// </summary>
    public LedgerOperationException(string message, string? updateId, Exception? innerException)
        : base(message, innerException)
    {
        UpdateId = updateId;
        CommitState = CommitState.Committed;
    }

    /// <summary>
    /// Creates an exception carrying a <see cref="ExerciseOutcome{T}.DamlError"/> outcome.
    /// </summary>
    public LedgerOperationException(
        string message,
        DamlErrorCategory category,
        string errorId,
        IReadOnlyDictionary<string, string> metadata)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Category = category;
        ErrorId = errorId;
        Metadata = metadata;
        CommitState = ParticipantRejectionCommitState.Of(category, errorId, metadata);
    }

    /// <summary>
    /// Creates an exception carrying an <see cref="ExerciseOutcome{T}.InfraError"/> outcome,
    /// its classification when the transport determined one, and the transport exception that
    /// caused it when available, and the error identifier when the failure came from a stream
    /// fault that carried one. The ledger may already have committed the command before the
    /// transport failure, so <see cref="CommitState"/> is always <see cref="CommitState.Unknown"/>.
    /// A faulted active-contract-set snapshot read, template or interface, uses
    /// <see cref="FromStreamFault"/> through the shared <c>SnapshotDrain</c> instead, since it
    /// submits no command for the ledger to have committed.
    /// </summary>
    public LedgerOperationException(
        string message,
        TransportStatus status,
        DamlErrorCategory? category = null,
        Exception? innerException = null,
        string? errorId = null)
        : this(message, status, CommitState.Unknown, category, errorId, metadata: null, innerException)
    {
    }

    /// <summary>
    /// Creates an exception for a failed call carrying a transport <paramref name="status"/> and an
    /// explicit <paramref name="commitState"/>, with the other detail the ledger clients populate.
    /// Use it to stage a failure in a test — for example a read that got no answer, with
    /// <see cref="TransportStatus.NoResponse"/> and <see cref="CommitState.NotCommitted"/> — or to
    /// forward a failure you already classified. The ledger clients derive <see cref="CommitState"/>
    /// from the kind of call they made, so a failure staged here should carry the commit state the
    /// call under test would report: <see cref="CommitState.NotCommitted"/> for a call that only
    /// reads, and for a call that writes <see cref="CommitState.Unknown"/> when it got no answer.
    /// </summary>
    /// <param name="message">The reason the call failed.</param>
    /// <param name="status">What the transport reported.</param>
    /// <param name="commitState">Whether the failed call's command committed to the ledger.</param>
    /// <param name="category">The Canton error category, when one applies.</param>
    /// <param name="errorId">The Canton error identifier, when one applies.</param>
    /// <param name="metadata">Structured detail from <c>ErrorInfo.metadata</c>, when the participant sent any.</param>
    /// <param name="innerException">The transport exception that caused the failure, when available.</param>
    public LedgerOperationException(
        string message,
        TransportStatus status,
        CommitState commitState,
        DamlErrorCategory? category = null,
        string? errorId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
        Category = category;
        ErrorId = errorId;
        Metadata = metadata;
        CommitState = commitState;
    }

    private LedgerOperationException(string message, CommitState commitState)
        : base(message)
    {
        CommitState = commitState;
    }

    internal static LedgerOperationException FromFailedCall(
        string message,
        TransportStatus status,
        DamlErrorCategory? category,
        string? errorId,
        IReadOnlyDictionary<string, string>? metadata,
        Exception? innerException,
        CommitState commitState) =>
        new(message, status, commitState, category, errorId, metadata, innerException);

    /// <summary>
    /// Creates an exception for a committed <see cref="ExerciseOutcome{T}.None"/> or
    /// <see cref="ExerciseOutcome{T}.Many"/> outcome, which carries no structured payload
    /// of its own but did commit. The exception has <see cref="CommitState.Committed"/>
    /// and no <see cref="UpdateId"/> or inner exception; do not resubmit the command.
    /// </summary>
    /// <param name="message">The reason the committed outcome could not satisfy the caller.</param>
    /// <returns>An exception preserving the committed state without synthetic error detail.</returns>
    public static LedgerOperationException CommittedWithoutDetail(string message) =>
        new(message, CommitState.Committed);

    /// <summary>
    /// Creates an exception for a faulted active-contract-set snapshot read, raised by the shared
    /// <see cref="SnapshotDrain"/> for the template and the interface family alike — an
    /// <see cref="ExerciseOutcome{T}.InfraError"/>-shaped failure that submitted no command, so
    /// <see cref="CommitState"/> is <see cref="CommitState.NotCommitted"/> rather than
    /// <see cref="CommitState.Unknown"/>: there is no command to resubmit, and retrying the read
    /// is always safe.
    /// </summary>
    internal static LedgerOperationException FromStreamFault(
        string message,
        TransportStatus status,
        DamlErrorCategory? category,
        Exception? innerException,
        string? errorId) =>
        new(message, status, CommitState.NotCommitted, category, errorId, metadata: null, innerException);
}
