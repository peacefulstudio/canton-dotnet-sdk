// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// Thrown by the throwing convenience wrappers in <see cref="Extensions.ThrowingExercise"/>
/// when the underlying <c>Try*</c> method yields a non-success outcome, and by
/// <see cref="Extensions.StreamerSnapshot"/> when a snapshot cannot be completed. Carries the
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
    /// Whether the failed outcome's command committed to the ledger —
    /// <see cref="CommitState.Committed"/> for <see cref="ExerciseOutcome{T}.None"/>,
    /// <see cref="ExerciseOutcome{T}.Many"/>, and <see cref="ExerciseOutcome{T}.CommittedUndecodable"/>;
    /// <see cref="CommitState.Unknown"/> for a write-path <see cref="ExerciseOutcome{T}.InfraError"/>,
    /// or for a <see cref="ExerciseOutcome{T}.DamlError"/> whose <see cref="Category"/> is
    /// <see cref="DamlErrorCategory.DeadlineExceededRequestStateUnknown"/> or
    /// <see cref="DamlErrorCategory.Unknown"/> (the transport could not classify it, so it might be
    /// either of those); otherwise <see cref="CommitState.NotCommitted"/> — including for a faulted
    /// <see cref="Extensions.StreamerSnapshot"/> read, which submits no command and so has nothing to
    /// have committed. A catch site must read this, not a null <see cref="UpdateId"/>, to decide
    /// whether resubmitting the command is safe — and must resubmit with the same command id when
    /// this is <see cref="CommitState.Unknown"/>, so command-id deduplication resolves the duplicate
    /// if the original request did commit.
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
    public LedgerOperationException(string message, string? updateId, Exception innerException)
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
        CommitState = category is DamlErrorCategory.DeadlineExceededRequestStateUnknown or DamlErrorCategory.Unknown
            ? CommitState.Unknown
            : CommitState.NotCommitted;
    }

    /// <summary>
    /// Creates an exception carrying an <see cref="ExerciseOutcome{T}.InfraError"/> outcome,
    /// its classification when the transport determined one, and the transport exception that
    /// caused it when available, and the error identifier when the failure came from a stream
    /// fault that carried one. The ledger may already have committed the command before the
    /// transport failure, so <see cref="CommitState"/> is always <see cref="CommitState.Unknown"/>.
    /// A faulted <see cref="Extensions.StreamerSnapshot"/> read uses
    /// <see cref="FromStreamFault"/> instead, since it submits no command for the ledger to have
    /// committed.
    /// </summary>
    public LedgerOperationException(
        string message,
        TransportStatus status,
        DamlErrorCategory? category = null,
        Exception? innerException = null,
        string? errorId = null)
        : this(message, status, category, innerException, errorId, CommitState.Unknown)
    {
    }

    private LedgerOperationException(
        string message,
        TransportStatus status,
        DamlErrorCategory? category,
        Exception? innerException,
        string? errorId,
        CommitState commitState)
        : base(message, innerException)
    {
        Status = status;
        Category = category;
        ErrorId = errorId;
        CommitState = commitState;
    }

    private LedgerOperationException(string message, CommitState commitState)
        : base(message)
    {
        CommitState = commitState;
    }

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
    /// Creates an exception for a faulted <see cref="Extensions.StreamerSnapshot"/> read — an
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
        new(message, status, category, innerException, errorId, CommitState.NotCommitted);
}
