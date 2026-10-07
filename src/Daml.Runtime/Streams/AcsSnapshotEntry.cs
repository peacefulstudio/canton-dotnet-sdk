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
/// <remarks>
/// Through <see cref="System.Text.Json"/> it travels as its own concrete arm's object with a
/// <c>"$case"</c> discriminator, e.g. <c>{"$case":"Checkpoint","Resume":{"Offset":6}}</c>,
/// mirroring <see cref="ContractStreamEvent{T}"/>'s shape (a CLR round-trip contract,
/// not the Daml-LF wire encoding). It names <see cref="AcsSnapshotEntryJsonConverterFactory"/> in a
/// <see cref="JsonConverterAttribute"/>, so it converts on bare <see cref="JsonSerializerOptions"/>
/// with no registration.
/// </remarks>
[JsonConverter(typeof(AcsSnapshotEntryJsonConverterFactory))]
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
        EquatableArray<Party> WitnessParties) : AcsSnapshotEntry<T>
    {
        /// <summary>
        /// The contract, ready to attach to another party's submission as an explicit
        /// disclosure: its contract ID, the contract's template ID in package-ID form, and the
        /// participant's <c>created_event_blob</c>. <c>null</c> unless the snapshot was opened
        /// with <c>includeDisclosure: true</c> and the participant returned a blob.
        /// </summary>
        public DisclosedContract? Disclosure { get; init; }
    }

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
    /// <param name="SourceException">Transport exception that caused the stream failure, when
    /// available. Carries <see cref="JsonIgnoreAttribute"/> and is excluded from the
    /// <see cref="System.Text.Json"/> round trip, as on
    /// <see cref="ContractStreamEvent{T}.StreamError"/>: a read restores it as
    /// <see langword="null"/>.
    /// A diagnostic only: excluded from <see cref="Equals(StreamError)"/> and <see cref="GetHashCode"/>.</param>
    public sealed record StreamError(
        TransportStatus Status,
        string Message,
        DamlErrorCategory? Category = null,
        string? ErrorId = null,
        [property: JsonIgnore] Exception? SourceException = null) : AcsSnapshotEntry<T>
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
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed
/// <see cref="AcsSnapshotEntry{T}"/>, including its four arms. Without it the declared-abstract
/// type writes an empty object for every arm and refuses to read any of them back — see
/// <see cref="AcsSnapshotEntry{T}"/>'s remarks.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CanConvert"/> also matches the arm types directly, so that a caller whose variable is
/// statically typed as a concrete arm — e.g. <c>AcsSnapshotEntry&lt;T&gt;.Checkpoint</c>, not
/// <c>AcsSnapshotEntry&lt;T&gt;</c> — still gets the discriminated shape once this factory is
/// registered, e.g. via <see cref="DamlJsonConverters.AddDamlConverters"/>. That registration is
/// required for the arm case specifically: <see cref="JsonConverterAttribute"/> is not inherited by
/// <see cref="System.Text.Json"/>'s converter resolution, so the <see cref="JsonConverterAttribute"/>
/// on <see cref="AcsSnapshotEntry{T}"/> alone leaves an arm-typed lookup on the default
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
[RequiresUnreferencedCode("AcsSnapshotEntryJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("AcsSnapshotEntryJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
internal sealed class AcsSnapshotEntryJsonConverterFactory : JsonConverterFactory, IDiscriminatedUnionJsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        IsClosedAcsSnapshotEntry(typeToConvert) || IsArmOfClosedAcsSnapshotEntry(typeToConvert);

    private static bool IsClosedAcsSnapshotEntry(Type type) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == typeof(AcsSnapshotEntry<>);

    private static bool IsArmOfClosedAcsSnapshotEntry(Type type) =>
        type.BaseType is { } baseType && IsClosedAcsSnapshotEntry(baseType);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var closedEntryType = IsClosedAcsSnapshotEntry(typeToConvert) ? typeToConvert : typeToConvert.BaseType!;
        return (JsonConverter)Activator.CreateInstance(
            typeof(AcsSnapshotEntryJsonConverter<>).MakeGenericType(closedEntryType.GetGenericArguments()[0]))!;
    }
}

internal sealed class AcsSnapshotEntryJsonConverter<T> : JsonConverter<AcsSnapshotEntry<T>>
    where T : ITemplate, IDamlRecord<T>
{
    private static readonly string TypeName =
        $"{nameof(AcsSnapshotEntry<T>)}<{DiscriminatedUnionJson.Describe(typeof(T))}>";

    private static readonly IReadOnlyDictionary<string, Type> Cases = new Dictionary<string, Type>
    {
        [nameof(AcsSnapshotEntry<T>.Created)] = typeof(AcsSnapshotEntry<T>.Created),
        [nameof(AcsSnapshotEntry<T>.Checkpoint)] = typeof(AcsSnapshotEntry<T>.Checkpoint),
        [nameof(AcsSnapshotEntry<T>.StreamError)] = typeof(AcsSnapshotEntry<T>.StreamError),
        [nameof(AcsSnapshotEntry<T>.Unclassified)] = typeof(AcsSnapshotEntry<T>.Unclassified),
    };

    public override AcsSnapshotEntry<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Read<AcsSnapshotEntry<T>>(ref reader, options, Cases, TypeName);

    public override void Write(Utf8JsonWriter writer, AcsSnapshotEntry<T> value, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Write(writer, value, options, TypeName);
}
