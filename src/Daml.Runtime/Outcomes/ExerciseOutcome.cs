// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;

namespace Daml.Runtime.Outcomes;

/// <summary>
/// Outcome of an exercise/create whose success carries a value of type <typeparamref name="T"/>.
/// Discriminated union: callers <c>switch</c> on the concrete subtype instead of catching
/// exceptions. Transport-agnostic — lives in <c>Daml.Runtime</c> so any ledger client
/// (gRPC, JSON, in-memory) can yield these without dragging consumers into a specific
/// transport dependency.
/// </summary>
/// <typeparam name="T">
/// The success payload type. Common shapes:
/// <list type="bullet">
///   <item><see cref="TransactionResult"/> — the raw transaction (use
///   <see cref="TransactionResultExtensions.Single{T}"/> et al. to project).</item>
///   <item><see cref="ContractId{T}"/> — the typed contract ID of a single created template.</item>
///   <item>A choice result record — composite Daml choice results.</item>
///   <item>Any record / scalar — choice results that aren't template-typed.</item>
/// </list>
/// No constraint is imposed on <typeparamref name="T"/>: the outcome describes
/// success/failure, not the shape of the success.
/// </typeparam>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="One"/> — the operation succeeded and produced a <typeparamref name="T"/>.</item>
///   <item><see cref="None"/> — the operation committed, but the expected single result was
///   absent (at the writer level, no transaction).</item>
///   <item><see cref="Many"/> — the operation committed, but more than one candidate filled a
///   slot that expected exactly one (at the writer level, more than one transaction);
///   <see cref="Many.ContractIds"/> holds the candidates' raw contract ids, not
///   <typeparamref name="T"/> values.</item>
///   <item><see cref="DamlError"/> — structured Canton/Daml error decoded from a transport-level trailer
///   (gRPC <c>grpc-status-details-bin</c>, JSON error body, etc.).</item>
///   <item><see cref="InfraError"/> — transport-level failure with no structured Canton error attached.</item>
///   <item><see cref="CommittedUndecodable"/> — the command committed, but the participant's
///   response could not be decoded into <typeparamref name="T"/>; do not resubmit, and read the
///   transaction by <see cref="CommittedUndecodable.UpdateId"/> instead.</item>
/// </list>
/// </remarks>
public abstract record ExerciseOutcome<T>
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected ExerciseOutcome() { }

    /// <summary>The operation succeeded and produced a <typeparamref name="T"/>.</summary>
    public sealed record One(T Result) : ExerciseOutcome<T>;

    /// <summary>
    /// The operation committed, but the expected single result was absent — no created contract
    /// filled the slot where exactly one was expected. At the writer level (<typeparamref name="T"/>
    /// is <see cref="TransactionResult"/>), this means the submission produced no transaction.
    /// </summary>
    public sealed record None : ExerciseOutcome<T>;

    /// <summary>
    /// The operation committed, but more than one candidate filled a slot that expected exactly
    /// one. At the writer level (<typeparamref name="T"/> is <see cref="TransactionResult"/>),
    /// this means the submission produced more than one transaction.
    /// </summary>
    /// <param name="ContractIds">Raw contract ids of the competing candidates — at least two of
    /// them, since fewer is what <see cref="None"/> and <see cref="One"/> are for. Not
    /// <typeparamref name="T"/> values — the created contracts' template is deliberately not part
    /// of <typeparamref name="T"/>, so the ids are carried untyped to survive re-wrapping across
    /// generic instantiations. Carried as an
    /// <see cref="EquatableArray{T}">EquatableArray&lt;string&gt;</see>: the outcome owns
    /// the ids and compares them by content, so two <see cref="Many"/> over the same ids are
    /// equal and a producer that keeps its own list cannot change one after handover.</param>
    /// <exception cref="ArgumentException"><paramref name="ContractIds"/> holds fewer than two
    /// ids, at construction or through a <c>with</c> expression.</exception>
    public sealed record Many(EquatableArray<string> ContractIds) : ExerciseOutcome<T>
    {
        private readonly EquatableArray<string> _contractIds =
            CompetingCandidates(ContractIds, nameof(ContractIds));

        /// <summary>
        /// Raw contract ids of the competing candidates; never fewer than two. Not
        /// <typeparamref name="T"/> values — the created contracts' template is deliberately not
        /// part of <typeparamref name="T"/>, so the ids are carried untyped to survive re-wrapping
        /// across generic instantiations. The outcome owns the ids and compares them by content, so
        /// two <see cref="Many"/> over the same ids are equal and a producer that keeps its own
        /// list cannot change one after handover.
        /// </summary>
        public EquatableArray<string> ContractIds
        {
            get => _contractIds;
            init => _contractIds = CompetingCandidates(value, nameof(ContractIds));
        }

        /// <summary>The number of competing candidates; never fewer than two.</summary>
        public int Count => ContractIds.Count;

        private static EquatableArray<string> CompetingCandidates(
            EquatableArray<string> contractIds,
            string parameterName) =>
            contractIds.Count >= 2
                ? contractIds
                : throw new ArgumentException(
                    $"Many needs at least two contract ids and got {contractIds.Count}: it means more "
                    + "than one candidate filled a slot that expected exactly one. None is the arm "
                    + "for no candidate, One for a single one.",
                    parameterName);
    }

    /// <summary>
    /// Structured Canton/Daml error returned by the participant
    /// (e.g. <c>CONTRACT_NOT_FOUND</c>, <c>INCONSISTENT</c>, or a Daml-defined
    /// <c>failWithStatus</c> error ID).
    /// </summary>
    /// <param name="Category">Canton error category — closed set; falls back to
    /// <see cref="DamlErrorCategory.Unknown"/> when the transport trailer is missing or unparseable.</param>
    /// <param name="ErrorId">Open string — Canton built-in or Daml-defined.</param>
    /// <param name="Message">Status message from the participant.</param>
    /// <param name="Metadata">Structured detail from <c>ErrorInfo.metadata</c>. Copied at
    /// construction and on <c>init</c>, so a producer that retains the dictionary it supplied
    /// cannot change this outcome's contents, equality or hash code afterwards. The copy uses
    /// the default comparer, so a source dictionary's custom one does not carry over.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Metadata"/> is <c>null</c>.</exception>
    public sealed record DamlError(
        DamlErrorCategory Category,
        string ErrorId,
        string Message,
        IReadOnlyDictionary<string, string> Metadata) : ExerciseOutcome<T>
    {
        private readonly IReadOnlyDictionary<string, string> _metadata =
            EventCollections.Copy(Metadata, nameof(Metadata));

        /// <summary>
        /// Structured detail from <c>ErrorInfo.metadata</c>. Copied at construction and on
        /// <c>init</c>, so a producer that retains the dictionary it supplied cannot change
        /// this outcome's contents, equality or hash code afterwards.
        /// </summary>
        /// <exception cref="ArgumentNullException">The supplied dictionary is <c>null</c>.</exception>
        public IReadOnlyDictionary<string, string> Metadata
        {
            get => _metadata;
            init => _metadata = EventCollections.Copy(value, nameof(Metadata));
        }

        /// <summary>
        /// Compares two structured errors by content, comparing <see cref="Metadata"/> key by
        /// key and independently of insertion order. The record-synthesized equality compares
        /// the backing <see cref="IReadOnlyDictionary{TKey,TValue}"/> by reference — a footgun
        /// for a value type — so we override it, as <see cref="Data.DamlTextMap"/>
        /// already does for the same shape.
        /// </summary>
        /// <param name="other">The structured error to compare against.</param>
        /// <returns><c>true</c> when both describe the same participant error.</returns>
        public bool Equals(DamlError? other)
        {
            if (other is null
                || Category != other.Category
                || ErrorId != other.ErrorId
                || Message != other.Message
                || Metadata.Count != other.Metadata.Count)
            {
                return false;
            }
            foreach (var (key, value) in Metadata)
            {
                if (!other.Metadata.TryGetValue(key, out var otherValue) || value != otherValue)
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = HashCode.Combine(Category, ErrorId, Message, Metadata.Count);
            foreach (var (key, value) in Metadata)
            {
                hash ^= HashCode.Combine(key, value);
            }
            return hash;
        }
    }

    /// <summary>
    /// Infrastructure-level failure (no structured Canton error attached).
    /// Must not represent cancellation of the caller's own <c>CancellationToken</c> —
    /// a <c>Try*</c> method must let <see cref="OperationCanceledException"/> (or a
    /// subtype, e.g. <c>TaskCanceledException</c>) propagate for that case instead of
    /// mapping it to this outcome, even where the underlying transport reports caller
    /// cancellation via the same channel as a genuine infrastructure failure (e.g. a
    /// gRPC <c>Cancelled</c> status).
    /// </summary>
    /// <param name="Status">What the transport reported for the failed call: a gRPC status, an
    /// HTTP status, no response at all, or a response whose body could not be decoded.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    /// <param name="Category">Classification of the transport failure when the transport could determine
    /// one without a structured Canton error attached; <c>null</c> when the failure was not classified.</param>
    /// <param name="SourceException">Transport exception that caused the infrastructure failure, when available.</param>
    public sealed record InfraError(
        TransportStatus Status,
        string Message,
        DamlErrorCategory? Category = null,
        Exception? SourceException = null) : ExerciseOutcome<T>;

    /// <summary>
    /// The command committed, but the participant's response could not be decoded into
    /// <typeparamref name="T"/>. Unlike every other failure arm, the command must not be
    /// resubmitted — resubmitting a command that already committed risks executing it twice.
    /// The caller should instead read the resulting transaction by <see cref="UpdateId"/>.
    /// </summary>
    /// <param name="UpdateId">The committed transaction's update id, when the response was
    /// decoded far enough to read one before decoding failed; <c>null</c> when the decode
    /// failure happened before the id was read.</param>
    /// <param name="Message">Description of the decode failure.</param>
    /// <param name="SourceException">The exception the decode failure raised.</param>
    public sealed record CommittedUndecodable(
        string? UpdateId,
        string Message,
        Exception SourceException) : ExerciseOutcome<T>;
}
