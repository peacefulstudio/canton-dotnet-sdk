// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Streams;

/// <summary>
/// A typed event observed on a subscription stream over <typeparamref name="T"/>.
/// Discriminated union: callers <c>switch</c> on the concrete subtype rather than
/// catching exceptions for stream errors. Transport-agnostic — lives in
/// <c>Daml.Runtime</c> so any ledger client (gRPC, JSON, in-memory) can yield
/// these without dragging the consumer into a specific transport dependency.
/// </summary>
/// <typeparam name="T">
/// The Daml template the stream is filtered to, matched by <c>TemplateId</c>. A
/// subscription filtered to a Daml interface marker yields
/// <see cref="InterfaceStreamEvent{TInterface, TView}"/> instead, whose payload is the
/// interface's view record.
/// </typeparam>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Created"/> — a contract of type <typeparamref name="T"/>
///   was created on the ledger; full payload is available.</item>
///   <item><see cref="Archived"/> — a contract of type <typeparamref name="T"/>
///   was archived; payload is not available (Canton does not re-emit it).
///   Emitted only on ACS-delta-shaped streams (the shape the live
///   <c>ILedgerStreamer.SubscribeAsync</c> stream uses); the ledger-effects
///   subscription (<c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c>) never
///   yields this variant — an archive there arrives as a consuming
///   <see cref="Exercised"/> (<see cref="Exercised.Consuming"/> is <c>true</c>).</item>
///   <item><see cref="Exercised"/> — a choice was exercised on a contract of
///   type <typeparamref name="T"/>; choice argument and result are available.
///   Emitted only on the ledger-effects shape (the shape the live
///   <c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c> stream uses); a
///   consuming exercise (<see cref="Exercised.Consuming"/> is <c>true</c>) is
///   that shape's archival signal.</item>
///   <item><see cref="Assigned"/>/<see cref="Unassigned"/> — a contract of
///   type <typeparamref name="T"/> was reassigned across synchronizers.</item>
///   <item><see cref="Checkpoint"/> — an offset checkpoint carrying no
///   contract payload: a participant-emitted marker on a live subscription that
///   consumers persist via <see cref="Checkpoint.Offset"/> to advance the
///   resume offset during quiet periods (no template-matching transactions
///   arriving), avoiding the re-process-from-stale-offset failure mode after
///   a crash. Not every variant's offset is safe to persist: an
///   <see cref="Unclassified"/> event carries a nullable
///   <see cref="Unclassified.Offset"/>, and <c>null</c> there means the event
///   has no ledger position at all. Skip it and keep the last offset that was
///   real — substituting <see cref="LedgerOffset.Begin"/> checkpoints at the
///   beginning of the ledger and re-reads the entire stream.
///   Active-contract-set snapshots stream
///   <see cref="AcsSnapshotEntry{T}"/> instead of this type; their terminal
///   marker is <see cref="AcsSnapshotEntry{T}.Checkpoint"/>.</item>
///   <item><see cref="StreamError"/> — the transport stream failed mid-flight.
///   Surfaced as a value rather than thrown so the consuming
///   <c>await foreach</c> loop can decide whether to retry, log, or stop.</item>
///   <item><see cref="Unclassified"/> — an event the transport delivered but
///   this layer could not map to any other variant; surfaced as a value so
///   consumers can implement a no-silent-drop policy for themselves.</item>
/// </list>
/// <para>
/// Which variants a stream yields is a property of its transaction shape, not
/// of this type: the union structurally admits every variant, but a given
/// stream emits only the subset its shape produces.
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Stream</term>
///     <description>Variants emitted</description>
///   </listheader>
///   <item>
///     <term>Ledger-effects live update stream
///     (<c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c>)</term>
///     <description><see cref="Created"/>, <see cref="Exercised"/> (a consuming
///     exercise is the archival signal), <see cref="Assigned"/>,
///     <see cref="Unassigned"/>, <see cref="Checkpoint"/>,
///     <see cref="StreamError"/>, <see cref="Unclassified"/>. Never
///     <see cref="Archived"/>.</description>
///   </item>
///   <item>
///     <term>ACS-delta live update stream
///     (<c>ILedgerStreamer.SubscribeAsync</c>)</term>
///     <description><see cref="Created"/>, <see cref="Archived"/>,
///     <see cref="Assigned"/>, <see cref="Unassigned"/>,
///     <see cref="Checkpoint"/>, <see cref="StreamError"/>,
///     <see cref="Unclassified"/>. Never <see cref="Exercised"/>.</description>
///   </item>
///   <item>
///     <term>Active-contract-set snapshot
///     (<c>ILedgerStreamer.SubscribeActiveAsync</c>)</term>
///     <description><see cref="Created"/> and <see cref="Unclassified"/>
///     entries followed by a single terminal <see cref="Checkpoint"/>.
///     In-flight reassignments surface as <see cref="Created"/>, not
///     <see cref="Assigned"/>/<see cref="Unassigned"/>.</description>
///   </item>
/// </list>
/// <para>
/// Through <see cref="System.Text.Json"/> it travels as its own concrete arm's object with a
/// <c>"$case"</c> discriminator, e.g. <c>{"$case":"Created","ContractId":"c1",...}</c>, rather
/// than being left to the default reflection-based writer, which — because this is a
/// declared-abstract type — would write only this base's (empty) member set and refuse to read
/// any arm back at all (a CLR round-trip contract, not the Daml-LF wire encoding). It
/// names <see cref="ContractStreamEventJsonConverterFactory"/> in a
/// <see cref="JsonConverterAttribute"/>, so it converts on bare <see cref="JsonSerializerOptions"/>
/// with no registration.
/// </para>
/// </remarks>
[JsonConverter(typeof(ContractStreamEventJsonConverterFactory))]
public abstract record ContractStreamEvent<T>
    where T : ITemplate, IDamlRecord<T>
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected ContractStreamEvent() { }

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was created.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Payload">The create-arguments, decoded into <typeparamref name="T"/>.</param>
    /// <param name="Key">The contract key read off the created event, or <c>null</c> when the
    /// event carried none. Separate from <paramref name="Payload"/> because the key is its own
    /// wire field, not a projection of the create-arguments, and its type is the template's key
    /// type rather than <typeparamref name="T"/>.</param>
    /// <param name="Offset">The ledger offset at which the contract was
    /// created. Strictly increasing per synchronizer; suitable for use as
    /// the resume offset on a subsequent subscription (exclusive).</param>
    /// <param name="SynchronizerId">The synchronizer the contract was created on.</param>
    /// <param name="WitnessParties">Parties that witnessed the create event.</param>
    public sealed record Created(
        ContractId<T> ContractId,
        T Payload,
        ContractKey? Key,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        EquatableArray<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was archived. Emitted only on
    /// ACS-delta-shaped streams (the shape the live
    /// <c>ILedgerStreamer.SubscribeAsync</c> stream uses); the ledger-effects
    /// subscription (<c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c>) never
    /// yields this variant — an archive there arrives as a consuming
    /// <see cref="Exercised"/> (<see cref="Exercised.Consuming"/> is <c>true</c>).
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Offset">The ledger offset at which the contract was archived.</param>
    /// <param name="SynchronizerId">The synchronizer the contract was archived on.</param>
    /// <param name="WitnessParties">Parties that witnessed the archive event.</param>
    public sealed record Archived(
        ContractId<T> ContractId,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        EquatableArray<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A choice was exercised on a contract of type <typeparamref name="T"/>.
    /// Only emitted when the stream is opened with ledger-effects shape (the
    /// shape the live <c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c> update
    /// stream uses); ACS-delta streams emit only <see cref="Created"/> and
    /// <see cref="Archived"/>. On the ledger-effects shape a consuming exercise
    /// (<see cref="Consuming"/> is <c>true</c>) is the contract's archival
    /// signal — there is no separate <see cref="Archived"/> event on that shape.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID the choice was exercised on.</param>
    /// <param name="ChoiceName">The choice name.</param>
    /// <param name="ChoiceArgument">The argument value passed to the choice.</param>
    /// <param name="ExerciseResult">The result returned by the choice.</param>
    /// <param name="Consuming">Whether the exercise consumed (archived) the contract.</param>
    /// <param name="Offset">The ledger offset of the exercise.</param>
    /// <param name="SynchronizerId">The synchronizer the exercise occurred on.</param>
    /// <param name="WitnessParties">Parties that witnessed the exercise event.</param>
    public sealed record Exercised(
        ContractId<T> ContractId,
        ChoiceName ChoiceName,
        DamlValue ChoiceArgument,
        DamlValue ExerciseResult,
        bool Consuming,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        EquatableArray<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was assigned to a
    /// synchronizer (typically completing a reassignment from another
    /// synchronizer). The contract becomes active on the target synchronizer
    /// at this offset; the create-arguments are re-emitted so consumers
    /// rebuilding state from a single stream stay correct.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Payload">The contract's create-arguments, re-emitted on assignment and
    /// decoded into <typeparamref name="T"/>.</param>
    /// <param name="Key">The contract key read off the assigned event's created contract, or
    /// <c>null</c> when it carried none. Canton's assignment wraps the whole created contract,
    /// so the key is on the wire here exactly as it is on a create; a consumer rebuilding
    /// state from one stream would otherwise lose the key at every reassignment.</param>
    /// <param name="Offset">The ledger offset of the assignment.</param>
    /// <param name="Source">The synchronizer the contract was reassigned from.</param>
    /// <param name="Target">The synchronizer the contract was reassigned to.</param>
    /// <param name="ReassignmentId">The reassignment's unique id — the same value on the
    /// paired unassignment and assignment, and the input to the completing assign command.</param>
    /// <param name="ReassignmentCounter">The reassignment counter shared by the paired
    /// unassignment and assignment; consumers pair the two events (and dedup replays) by
    /// matching this value.</param>
    /// <param name="WitnessParties">Parties that witnessed the assignment.</param>
    public sealed record Assigned(
        ContractId<T> ContractId,
        T Payload,
        ContractKey? Key,
        LedgerOffset Offset,
        SynchronizerId Source,
        SynchronizerId Target,
        string ReassignmentId,
        long ReassignmentCounter,
        EquatableArray<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was unassigned from a
    /// synchronizer (the start of a reassignment). The contract is no longer
    /// active on the source synchronizer at this offset.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Offset">The ledger offset of the unassignment.</param>
    /// <param name="Source">The synchronizer the contract is leaving.</param>
    /// <param name="Target">The synchronizer the contract is moving to.</param>
    /// <param name="ReassignmentId">The reassignment's unique id — the same value on the
    /// paired assignment, and the input to the assign command that completes the move.</param>
    /// <param name="ReassignmentCounter">The reassignment counter shared by the paired
    /// assignment; consumers pair the two events (and dedup replays) by matching this value.</param>
    /// <param name="WitnessParties">Parties that witnessed the unassignment.</param>
    public sealed record Unassigned(
        ContractId<T> ContractId,
        LedgerOffset Offset,
        SynchronizerId Source,
        SynchronizerId Target,
        string ReassignmentId,
        long ReassignmentCounter,
        EquatableArray<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// An offset checkpoint with no contract payload: on a live update
    /// subscription, a participant-emitted marker with no template-matching
    /// activity to surface — Canton emits these on a participant-configured
    /// cadence (<c>max_offset_checkpoint_emission_delay</c>) regardless of
    /// the active filter, so consumers can advance their persisted resume
    /// offset during quiet periods. Active-contract-set snapshots stream
    /// <see cref="AcsSnapshotEntry{T}"/> and carry their own terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/>.
    /// </summary>
    /// <remarks>
    /// Without the quiet-period signal a low-traffic subscription that
    /// crashes during a quiet period would resume from a stale
    /// <c>Created</c>/<c>Exercised</c> offset and re-process
    /// every transaction the participant has retained between then and now.
    /// </remarks>
    /// <param name="Offset">The participant's current ledger offset; persist it
    /// as the resume offset for a subsequent subscription. That subscription
    /// treats its lower bound as exclusive, so resuming from this offset does
    /// not re-deliver any event already seen up to it.</param>
    public sealed record Checkpoint(LedgerOffset Offset) : ContractStreamEvent<T>;

    /// <summary>
    /// The transport stream failed mid-flight. Surfaced in-band rather than
    /// thrown so callers can decide policy — log and continue with a fresh
    /// stream from the last good offset, terminate, etc.
    /// </summary>
    /// <remarks>
    /// Emitted only by the live update subscriptions
    /// (<c>ILedgerStreamer.SubscribeAsync</c> and
    /// <c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c>). Active-contract-set
    /// snapshots stream <see cref="AcsSnapshotEntry{T}"/> and surface a
    /// mid-snapshot transport fault in-band as their own terminal
    /// <see cref="AcsSnapshotEntry{T}.StreamError"/> variant instead.
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
    /// <param name="SourceException">Transport exception that caused the stream failure, when
    /// available. Carries <see cref="JsonIgnoreAttribute"/> and is excluded from the
    /// <see cref="System.Text.Json"/> round trip: the reflection-based serializer writes an
    /// arbitrary <see cref="Exception"/> by walking its public members, and
    /// <see cref="System.Reflection.MethodBase"/> — reachable through
    /// <see cref="Exception.TargetSite"/> — throws <see cref="NotSupportedException"/> the moment
    /// a real caught exception (which always has a <see cref="Exception.TargetSite"/>) is
    /// serialized this way. A read restores this member as <see langword="null"/> rather than
    /// reconstructing the original exception, which the CLR type offers no JSON-constructible
    /// shape for in general anyway.
    /// A diagnostic only: excluded from <see cref="Equals(StreamError)"/> and <see cref="GetHashCode"/>.</param>
    public sealed record StreamError(
        TransportStatus Status,
        string Message,
        DamlErrorCategory? Category = null,
        string? ErrorId = null,
        [property: JsonIgnore] Exception? SourceException = null) : ContractStreamEvent<T>
    {
        /// <summary>
        /// Compares two stream errors by <see cref="Status"/>, <see cref="Message"/>, <see cref="Category"/> and <see cref="ErrorId"/>, ignoring <see cref="SourceException"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="SourceException"/> is a diagnostic attachment, not part of the value's identity:
        /// it does not travel through <see cref="System.Text.Json"/>, so a value read back from JSON
        /// must equal the value that was written, and two failures with the same content are the same
        /// failure whichever exception each one caught. It is excluded from <see cref="GetHashCode"/> likewise.
        /// </remarks>
        /// <param name="other">The value to compare against.</param>
        /// <returns><c>true</c> when every member other than <see cref="SourceException"/> is equal.</returns>
        public bool Equals(StreamError? other) =>
            other is not null
            && EqualityComparer<TransportStatus>.Default.Equals(Status, other.Status)
            && Message == other.Message
            && Category == other.Category
            && ErrorId == other.ErrorId;

        /// <summary>
        /// Hashes the value by every member other than <see cref="SourceException"/>, consistently with
        /// <see cref="Equals(StreamError)"/>.
        /// </summary>
        /// <returns>A hash code over <see cref="Status"/>, <see cref="Message"/>, <see cref="Category"/> and <see cref="ErrorId"/>.</returns>
        public override int GetHashCode() => HashCode.Combine(Status, Message, Category, ErrorId);
    }

    /// <summary>
    /// An event the transport delivered but this layer could not map to any
    /// of the other variants. Surfaced rather than silently dropped so
    /// consumers can honour a no-silent-drop invariant — this is the
    /// transport-agnostic <c>Daml.Runtime</c> layer, so no raw wire bytes
    /// are available to attach here.
    /// </summary>
    /// <param name="Offset">The ledger offset at which the unrecognized event occurred, or
    /// <c>null</c> when the event could not be placed on the ledger at all — typically because
    /// the wire offset itself was absent or unparseable. A consumer persisting resume state
    /// must not checkpoint a <c>null</c> offset: <see cref="LedgerOffset"/> has no absent value
    /// and its <c>default</c> is <see cref="LedgerOffset.Begin"/>, a genuine ledger position,
    /// so substituting one resumes from the beginning of the ledger and re-reads the whole
    /// stream. Skip the event and keep the last offset that was real.</param>
    /// <param name="Kind">Why the event could not be mapped to a typed variant, as a
    /// strongly-typed discriminator consumers <c>switch</c> on. <see cref="UnclassifiedKind.Unknown"/>
    /// means the transport delivered a variant this layer does not recognise; the raw
    /// descriptor is then on <paramref name="RawKind"/>.</param>
    /// <param name="RawKind">The transport's raw descriptor for the unrecognized event. The
    /// constructor guarantees the invariant that <paramref name="RawKind"/> is non-<c>null</c>
    /// exactly when <paramref name="Kind"/> is <see cref="UnclassifiedKind.Unknown"/>, and
    /// <c>null</c> for every enumerated reason — so a consumer never sees a stale descriptor
    /// attached to a named kind. Preserves forward-compatibility with server event variants
    /// added after <see cref="UnclassifiedKind"/> was published, so such an event is surfaced
    /// as data rather than dropped.</param>
    /// <exception cref="ArgumentException"><paramref name="Kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/> with a <c>null</c> <paramref name="RawKind"/>, or an
    /// enumerated <paramref name="Kind"/> with a non-<c>null</c> <paramref name="RawKind"/>.</exception>
    public sealed record Unclassified(
        LedgerOffset? Offset,
        UnclassifiedKind Kind,
        string? RawKind = null) : ContractStreamEvent<T>
    {
        /// <summary>
        /// Why the event could not be mapped to a typed variant, as a strongly-typed
        /// discriminator consumers <c>switch</c> on. Get-only, so a <c>with</c> expression
        /// cannot reassign it independently of <see cref="RawKind"/>.
        /// </summary>
        public UnclassifiedKind Kind { get; } = Kind;

        /// <summary>
        /// The transport's raw descriptor for the unrecognized event — non-<c>null</c> exactly
        /// when <see cref="Kind"/> is <see cref="UnclassifiedKind.Unknown"/>, and <c>null</c>
        /// otherwise. Get-only, so the invariant validated at construction cannot be bypassed by
        /// a <c>with</c> expression.
        /// </summary>
        public string? RawKind { get; } = UnclassifiedRawKind.Validated(
            Kind, RawKind, UnclassifiedRawKind.EventSubject, nameof(RawKind));
    }
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed
/// <see cref="ContractStreamEvent{T}"/>, including its eight arms. Without it the declared-abstract
/// type writes an empty object for every arm and refuses to read any of them back — see
/// <see cref="ContractStreamEvent{T}"/>'s remarks.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CanConvert"/> also matches the arm types directly, so that a caller whose variable is
/// statically typed as a concrete arm — e.g. <c>ContractStreamEvent&lt;T&gt;.Checkpoint</c>, not
/// <c>ContractStreamEvent&lt;T&gt;</c> — still gets the discriminated shape once this factory is
/// registered, e.g. via <see cref="DamlJsonConverters.AddDamlConverters"/>. That registration is
/// required for the arm case specifically: <see cref="JsonConverterAttribute"/> is not inherited by
/// <see cref="System.Text.Json"/>'s converter resolution, so the <see cref="JsonConverterAttribute"/>
/// on <see cref="ContractStreamEvent{T}"/> alone leaves an arm-typed lookup on the default
/// reflection-based contract — the same limitation <see cref="DamlJsonConverters.AddDamlConverters"/>'s
/// remarks describe for a hand-written <see cref="Daml.Runtime.Contracts.ContractId{T}"/>
/// derivation. Putting the attribute on the arm types too would not lift that requirement: see
/// <see cref="DiscriminatedUnionJson.Write{TUnion}"/>'s remarks for why an arm can carry this
/// converter only through <see cref="JsonSerializerOptions.Converters"/>, never its own attribute.
/// </para>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime — the same cost
/// <see cref="Daml.Runtime.Stdlib.SetJsonConverterFactory"/> already carries, accepted so the
/// attribute reaches a consumer who never registers the converters.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("ContractStreamEventJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("ContractStreamEventJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
internal sealed class ContractStreamEventJsonConverterFactory : JsonConverterFactory, IDiscriminatedUnionJsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        IsClosedContractStreamEvent(typeToConvert) || IsArmOfClosedContractStreamEvent(typeToConvert);

    private static bool IsClosedContractStreamEvent(Type type) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == typeof(ContractStreamEvent<>);

    private static bool IsArmOfClosedContractStreamEvent(Type type) =>
        type.BaseType is { } baseType && IsClosedContractStreamEvent(baseType);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var closedEventType = IsClosedContractStreamEvent(typeToConvert) ? typeToConvert : typeToConvert.BaseType!;
        return (JsonConverter)Activator.CreateInstance(
            typeof(ContractStreamEventJsonConverter<>).MakeGenericType(closedEventType.GetGenericArguments()[0]))!;
    }
}

internal sealed class ContractStreamEventJsonConverter<T> : JsonConverter<ContractStreamEvent<T>>
    where T : ITemplate, IDamlRecord<T>
{
    private static readonly string TypeName =
        $"{nameof(ContractStreamEvent<T>)}<{DiscriminatedUnionJson.Describe(typeof(T))}>";

    private static readonly IReadOnlyDictionary<string, Type> Cases = new Dictionary<string, Type>
    {
        [nameof(ContractStreamEvent<T>.Created)] = typeof(ContractStreamEvent<T>.Created),
        [nameof(ContractStreamEvent<T>.Archived)] = typeof(ContractStreamEvent<T>.Archived),
        [nameof(ContractStreamEvent<T>.Exercised)] = typeof(ContractStreamEvent<T>.Exercised),
        [nameof(ContractStreamEvent<T>.Assigned)] = typeof(ContractStreamEvent<T>.Assigned),
        [nameof(ContractStreamEvent<T>.Unassigned)] = typeof(ContractStreamEvent<T>.Unassigned),
        [nameof(ContractStreamEvent<T>.Checkpoint)] = typeof(ContractStreamEvent<T>.Checkpoint),
        [nameof(ContractStreamEvent<T>.StreamError)] = typeof(ContractStreamEvent<T>.StreamError),
        [nameof(ContractStreamEvent<T>.Unclassified)] = typeof(ContractStreamEvent<T>.Unclassified),
    };

    public override ContractStreamEvent<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Read<ContractStreamEvent<T>>(ref reader, options, Cases, TypeName);

    public override void Write(Utf8JsonWriter writer, ContractStreamEvent<T> value, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Write(writer, value, options, TypeName);
}
