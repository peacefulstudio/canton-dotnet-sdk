// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Serialization;

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
/// <para>
/// Through <see cref="System.Text.Json"/> it travels as its own concrete arm's object with a
/// <c>"$case"</c> discriminator, e.g. <c>{"$case":"One","Result":42}</c>, mirroring
/// <see cref="Streams.ContractStreamEvent{T}"/>'s shape (a CLR round-trip contract, not
/// the Daml-LF wire encoding). It names <see cref="ExerciseOutcomeJsonConverterFactory"/> in a
/// <see cref="JsonConverterAttribute"/>, so it converts on bare <see cref="JsonSerializerOptions"/>
/// with no registration. <typeparamref name="T"/> itself must round-trip through
/// <see cref="System.Text.Json"/> for <see cref="One"/> to.
/// </para>
/// </remarks>
[JsonConverter(typeof(ExerciseOutcomeJsonConverterFactory))]
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
    /// <param name="SourceException">Transport exception that caused the infrastructure failure,
    /// when available. Carries <see cref="JsonIgnoreAttribute"/> and is excluded from the
    /// <see cref="System.Text.Json"/> round trip, as on
    /// <see cref="Streams.ContractStreamEvent{T}.StreamError"/>: a read restores it as
    /// <see langword="null"/>.
    /// A diagnostic only: excluded from <see cref="Equals(InfraError)"/> and <see cref="GetHashCode"/>.</param>
    public sealed record InfraError(
        TransportStatus Status,
        string Message,
        DamlErrorCategory? Category = null,
        [property: JsonIgnore] Exception? SourceException = null) : ExerciseOutcome<T>
    {
        /// <summary>
        /// Compares two infrastructure failures by <see cref="Status"/>, <see cref="Message"/> and <see cref="Category"/>, ignoring <see cref="SourceException"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="SourceException"/> is a diagnostic attachment, not part of the value's identity:
        /// it does not travel through <see cref="System.Text.Json"/>, so a value read back from JSON
        /// must equal the value that was written, and two failures with the same content are the same
        /// failure whichever exception each one caught. It is excluded from <see cref="GetHashCode"/> likewise.
        /// </remarks>
        /// <param name="other">The value to compare against.</param>
        /// <returns><c>true</c> when every member other than <see cref="SourceException"/> is equal.</returns>
        public bool Equals(InfraError? other) =>
            other is not null
            && EqualityComparer<TransportStatus>.Default.Equals(Status, other.Status)
            && Message == other.Message
            && Category == other.Category;

        /// <summary>
        /// Hashes the value by every member other than <see cref="SourceException"/>, consistently with
        /// <see cref="Equals(InfraError)"/>.
        /// </summary>
        /// <returns>A hash code over <see cref="Status"/>, <see cref="Message"/> and <see cref="Category"/>.</returns>
        public override int GetHashCode() => HashCode.Combine(Status, Message, Category);
    }

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
    /// <param name="SourceException">The exception the decode failure raised, when available.
    /// Carries <see cref="JsonIgnoreAttribute"/> and is excluded from the
    /// <see cref="System.Text.Json"/> round trip, as on
    /// <see cref="Streams.ContractStreamEvent{T}.StreamError"/>: a read restores it as
    /// <see langword="null"/>.
    /// A diagnostic only: excluded from <see cref="Equals(CommittedUndecodable)"/> and <see cref="GetHashCode"/>.</param>
    public sealed record CommittedUndecodable(
        string? UpdateId,
        string Message,
        [property: JsonIgnore] Exception? SourceException = null) : ExerciseOutcome<T>
    {
        /// <summary>
        /// Compares two committed-but-undecodable outcomes by <see cref="UpdateId"/> and <see cref="Message"/>, ignoring <see cref="SourceException"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="SourceException"/> is a diagnostic attachment, not part of the value's identity:
        /// it does not travel through <see cref="System.Text.Json"/>, so a value read back from JSON
        /// must equal the value that was written, and two failures with the same content are the same
        /// failure whichever exception each one caught. It is excluded from <see cref="GetHashCode"/> likewise.
        /// </remarks>
        /// <param name="other">The value to compare against.</param>
        /// <returns><c>true</c> when every member other than <see cref="SourceException"/> is equal.</returns>
        public bool Equals(CommittedUndecodable? other) =>
            other is not null
            && UpdateId == other.UpdateId
            && Message == other.Message;

        /// <summary>
        /// Hashes the value by every member other than <see cref="SourceException"/>, consistently with
        /// <see cref="Equals(CommittedUndecodable)"/>.
        /// </summary>
        /// <returns>A hash code over <see cref="UpdateId"/> and <see cref="Message"/>.</returns>
        public override int GetHashCode() => HashCode.Combine(UpdateId, Message);
    }
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed
/// <see cref="ExerciseOutcome{T}"/>, including its six arms. Without it the declared-abstract
/// type writes an empty object for every arm and refuses to read any of them back — see
/// <see cref="ExerciseOutcome{T}"/>'s remarks.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CanConvert"/> also matches the arm types directly, so that a caller whose variable is
/// statically typed as a concrete arm — e.g. <c>ExerciseOutcome&lt;T&gt;.One</c>, not
/// <c>ExerciseOutcome&lt;T&gt;</c> — still gets the discriminated shape once this factory is
/// registered, e.g. via <see cref="DamlJsonConverters.AddDamlConverters"/>. That registration is
/// required for the arm case specifically: <see cref="JsonConverterAttribute"/> is not inherited by
/// <see cref="System.Text.Json"/>'s converter resolution, so the <see cref="JsonConverterAttribute"/>
/// on <see cref="ExerciseOutcome{T}"/> alone leaves an arm-typed lookup on the default
/// reflection-based contract. See <see cref="DiscriminatedUnionJson.Write{TUnion}"/>'s remarks for
/// why an arm can carry this converter only through <see cref="JsonSerializerOptions.Converters"/>,
/// never its own attribute.
/// </para>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime — the same cost
/// <see cref="Daml.Runtime.Stdlib.SetJsonConverterFactory"/> already carries, accepted so the
/// attribute reaches a consumer who never registers the converters.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("ExerciseOutcomeJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("ExerciseOutcomeJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
internal sealed class ExerciseOutcomeJsonConverterFactory : JsonConverterFactory, IDiscriminatedUnionJsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        IsClosedExerciseOutcome(typeToConvert) || IsArmOfClosedExerciseOutcome(typeToConvert);

    private static bool IsClosedExerciseOutcome(Type type) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == typeof(ExerciseOutcome<>);

    private static bool IsArmOfClosedExerciseOutcome(Type type) =>
        type.BaseType is { } baseType && IsClosedExerciseOutcome(baseType);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var closedOutcomeType = IsClosedExerciseOutcome(typeToConvert) ? typeToConvert : typeToConvert.BaseType!;
        return (JsonConverter)Activator.CreateInstance(
            typeof(ExerciseOutcomeJsonConverter<>).MakeGenericType(closedOutcomeType.GetGenericArguments()[0]))!;
    }
}

internal sealed class ExerciseOutcomeJsonConverter<T> : JsonConverter<ExerciseOutcome<T>>
{
    private static readonly string TypeName =
        $"{nameof(ExerciseOutcome<T>)}<{DiscriminatedUnionJson.Describe(typeof(T))}>";

    private static readonly IReadOnlyDictionary<string, Type> Cases = new Dictionary<string, Type>
    {
        [nameof(ExerciseOutcome<T>.One)] = typeof(ExerciseOutcome<T>.One),
        [nameof(ExerciseOutcome<T>.None)] = typeof(ExerciseOutcome<T>.None),
        [nameof(ExerciseOutcome<T>.Many)] = typeof(ExerciseOutcome<T>.Many),
        [nameof(ExerciseOutcome<T>.DamlError)] = typeof(ExerciseOutcome<T>.DamlError),
        [nameof(ExerciseOutcome<T>.InfraError)] = typeof(ExerciseOutcome<T>.InfraError),
        [nameof(ExerciseOutcome<T>.CommittedUndecodable)] = typeof(ExerciseOutcome<T>.CommittedUndecodable),
    };

    public override ExerciseOutcome<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Read<ExerciseOutcome<T>>(ref reader, options, Cases, TypeName);

    public override void Write(Utf8JsonWriter writer, ExerciseOutcome<T> value, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Write(writer, value, options, TypeName);
}
