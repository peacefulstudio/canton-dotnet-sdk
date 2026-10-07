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
/// One entry in an active-contract-set snapshot filtered to the Daml interface
/// <typeparamref name="TInterface"/>. The interface-family counterpart of
/// <see cref="AcsSnapshotEntry{T}"/>: identical variants and fields, but the payload is
/// the interface's server-computed view record <typeparamref name="TView"/> rather than
/// the implementing template's own record, which an interface subscription never sees.
/// The snapshot yields <see cref="Created"/> rows and an <see cref="Unclassified"/> row
/// for anything the projector cannot classify, then ends with a single terminal
/// <see cref="Checkpoint"/> — emitted even when the snapshot is empty — or, when the
/// transport faults mid-snapshot, a terminal <see cref="StreamError"/> in place of that
/// <see cref="Checkpoint"/>.
/// </summary>
/// <typeparam name="TInterface">The Daml interface marker the snapshot is filtered to,
/// matched by interface id.</typeparam>
/// <typeparam name="TView">The interface's view record type, carried as the payload.</typeparam>
/// <remarks>
/// Call sites do not name the pair: they pass the marker's generated
/// <see cref="ViewDescriptor{TInterface, TView}"/> witness to
/// <c>ILedgerStreamer.SubscribeActiveAsync</c> and both type parameters are inferred from
/// it. A row that arrives without an interface view is surfaced as
/// <see cref="Unclassified"/> with
/// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/>.
/// <para>
/// Through <see cref="System.Text.Json"/> it travels as its own concrete arm's object with a
/// <c>"$case"</c> discriminator, mirroring <see cref="AcsSnapshotEntry{T}"/>'s shape (a
/// CLR round-trip contract, not the Daml-LF wire encoding). It names
/// <see cref="InterfaceAcsSnapshotEntryJsonConverterFactory"/> in a
/// <see cref="JsonConverterAttribute"/>, so it converts on bare <see cref="JsonSerializerOptions"/>
/// with no registration.
/// </para>
/// </remarks>
[JsonConverter(typeof(InterfaceAcsSnapshotEntryJsonConverterFactory))]
public abstract record InterfaceAcsSnapshotEntry<TInterface, TView>
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord<TView>
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected InterfaceAcsSnapshotEntry() { }

    /// <summary>
    /// An active contract implementing <typeparamref name="TInterface"/> in the snapshot.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID, interface-typed.</param>
    /// <param name="Payload">The interface view, decoded into <typeparamref name="TView"/>.</param>
    /// <param name="Key">The contract key read off the created event, or <c>null</c> when the
    /// event carried none. Stays wire-level even though <paramref name="Payload"/> is decoded:
    /// the key's type is the implementing template's key type, not
    /// <typeparamref name="TView"/>, so decoding it is the consumer's
    /// <c>TKey.FromRecord(Key.Value.As&lt;DamlRecord&gt;())</c> hop.</param>
    /// <param name="Offset">The ledger offset at which the contract was created — a
    /// per-contract fact, not the snapshot's position. It is not a resume point: a consumer
    /// persisting resume state must take it from the terminal <see cref="Checkpoint"/>'s
    /// <see cref="StakeholderResume"/> ticket, never from a per-row offset.</param>
    /// <param name="SynchronizerId">The synchronizer the contract is active on.</param>
    /// <param name="WitnessParties">Parties that witnessed the create event.</param>
    public sealed record Created(
        ContractId<TInterface> ContractId,
        TView Payload,
        ContractKey? Key,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        EquatableArray<Party> WitnessParties) : InterfaceAcsSnapshotEntry<TInterface, TView>
    {
        /// <summary>
        /// The contract, ready to attach to another party's submission as an explicit
        /// disclosure: its contract ID, the implementing template ID in package-ID form, and the
        /// participant's <c>created_event_blob</c>. <c>null</c> unless the snapshot was opened
        /// with <c>includeDisclosure: true</c> and the participant returned a blob.
        /// </summary>
        public DisclosedContract? Disclosure { get; init; }
    }

    /// <summary>
    /// A snapshot row the projector could not classify; surfaced, never dropped. Carries the
    /// same discriminator pair as
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Unclassified"/>, so a consumer
    /// handling both the snapshot and the live stream switches on one
    /// <see cref="UnclassifiedKind"/> vocabulary.
    /// </summary>
    /// <param name="Offset">The ledger offset at which the unrecognized row occurred, or
    /// <c>null</c> when the row could not be placed on the ledger at all. A consumer
    /// persisting resume state must not checkpoint a <c>null</c> offset:
    /// <see cref="LedgerOffset"/> has no absent value and its <c>default</c> is
    /// <see cref="LedgerOffset.Begin"/>, a genuine ledger position, so substituting one
    /// resumes from the beginning of the ledger and re-reads the whole stream. Skip the row
    /// and keep the last offset that was real.</param>
    /// <param name="Kind">Why the row could not be classified, as a strongly-typed
    /// discriminator consumers <c>switch</c> on.</param>
    /// <param name="RawKind">The transport's raw descriptor for the unrecognized row —
    /// non-<c>null</c> exactly when <paramref name="Kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/>, and <c>null</c> for every enumerated reason.</param>
    /// <exception cref="ArgumentException"><paramref name="Kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/> with a <c>null</c> <paramref name="RawKind"/>, or
    /// an enumerated <paramref name="Kind"/> with a non-<c>null</c> <paramref name="RawKind"/>.</exception>
    public sealed record Unclassified(
        LedgerOffset? Offset,
        UnclassifiedKind Kind,
        string? RawKind = null) : InterfaceAcsSnapshotEntry<TInterface, TView>
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
    /// The single terminal marker that always ends the snapshot stream — emitted even when
    /// the snapshot is empty — carrying the snapshot's effective offset as a
    /// <see cref="StakeholderResume"/> ticket.
    /// </summary>
    /// <param name="Resume">The resume ticket for the snapshot's effective offset — pass it to
    /// <c>ILedgerStreamer.SubscribeAsync</c> for a gapless, duplicate-free handover; that
    /// subscription's lower bound is exclusive, so the event at this offset is not re-delivered.
    /// The raw offset is reachable via <see cref="StakeholderResume.Offset"/>.</param>
    public sealed record Checkpoint(StakeholderResume Resume) : InterfaceAcsSnapshotEntry<TInterface, TView>;

    /// <summary>
    /// The transport stream failed mid-snapshot. Surfaced in-band rather than thrown so a
    /// caller draining the snapshot with <c>await foreach</c> can decide policy.
    /// </summary>
    /// <remarks>
    /// Terminal, and mutually exclusive with <see cref="Checkpoint"/>: a faulted snapshot ends
    /// with this entry instead of the <see cref="Checkpoint"/> a successful snapshot ends
    /// with, so no snapshot offset is available to hand over to a live subscription and the
    /// caller must treat the snapshot as incomplete.
    /// </remarks>
    /// <param name="Status">What the transport reported for the failed call: a gRPC status, an
    /// HTTP status, no response at all, or a response whose body could not be decoded.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    /// <param name="Category">Classification of the fault, whether the transport read it off
    /// the participant's structured Canton error or determined it without one; <c>null</c>
    /// when the failure was not classified.</param>
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
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.StreamError"/>: a read restores it as
    /// <see langword="null"/>.
    /// A diagnostic only: excluded from <see cref="Equals(StreamError)"/> and <see cref="GetHashCode"/>.</param>
    public sealed record StreamError(
        TransportStatus Status,
        string Message,
        DamlErrorCategory? Category = null,
        string? ErrorId = null,
        [property: JsonIgnore] Exception? SourceException = null) : InterfaceAcsSnapshotEntry<TInterface, TView>
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
/// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}"/>, including its four arms. Without it the
/// declared-abstract type writes an empty object for every arm and refuses to read any of them back
/// — see <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}"/>'s remarks.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CanConvert"/> also matches the arm types directly, so that a caller whose variable is
/// statically typed as a concrete arm — e.g.
/// <c>InterfaceAcsSnapshotEntry&lt;TInterface, TView&gt;.Checkpoint</c>, not
/// <c>InterfaceAcsSnapshotEntry&lt;TInterface, TView&gt;</c> — still gets the discriminated shape once
/// this factory is registered, e.g. via <see cref="DamlJsonConverters.AddDamlConverters"/>. That
/// registration is required for the arm case specifically:
/// <see cref="JsonConverterAttribute"/> is not inherited by <see cref="System.Text.Json"/>'s
/// converter resolution, so the <see cref="JsonConverterAttribute"/> on
/// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}"/> alone leaves an arm-typed lookup on the
/// default reflection-based contract — the same limitation
/// <see cref="DamlJsonConverters.AddDamlConverters"/>'s remarks describe for a hand-written
/// <see cref="Daml.Runtime.Contracts.ContractId{T}"/> derivation. Putting the attribute on the arm
/// types too would not lift that requirement: see
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
[RequiresUnreferencedCode("InterfaceAcsSnapshotEntryJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("InterfaceAcsSnapshotEntryJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
internal sealed class InterfaceAcsSnapshotEntryJsonConverterFactory : JsonConverterFactory, IDiscriminatedUnionJsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        IsClosedInterfaceAcsSnapshotEntry(typeToConvert) || IsArmOfClosedInterfaceAcsSnapshotEntry(typeToConvert);

    private static bool IsClosedInterfaceAcsSnapshotEntry(Type type) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == typeof(InterfaceAcsSnapshotEntry<,>);

    private static bool IsArmOfClosedInterfaceAcsSnapshotEntry(Type type) =>
        type.BaseType is { } baseType && IsClosedInterfaceAcsSnapshotEntry(baseType);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var closedEntryType = IsClosedInterfaceAcsSnapshotEntry(typeToConvert) ? typeToConvert : typeToConvert.BaseType!;
        var typeArguments = closedEntryType.GetGenericArguments();
        return (JsonConverter)Activator.CreateInstance(
            typeof(InterfaceAcsSnapshotEntryJsonConverter<,>).MakeGenericType(typeArguments[0], typeArguments[1]))!;
    }
}

internal sealed class InterfaceAcsSnapshotEntryJsonConverter<TInterface, TView> : JsonConverter<InterfaceAcsSnapshotEntry<TInterface, TView>>
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord<TView>
{
    private static readonly string TypeName =
        $"{nameof(InterfaceAcsSnapshotEntry<TInterface, TView>)}<{DiscriminatedUnionJson.Describe(typeof(TInterface))}, {DiscriminatedUnionJson.Describe(typeof(TView))}>";

    private static readonly IReadOnlyDictionary<string, Type> Cases = new Dictionary<string, Type>
    {
        [nameof(InterfaceAcsSnapshotEntry<TInterface, TView>.Created)] = typeof(InterfaceAcsSnapshotEntry<TInterface, TView>.Created),
        [nameof(InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint)] = typeof(InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint),
        [nameof(InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError)] = typeof(InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError),
        [nameof(InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified)] = typeof(InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified),
    };

    public override InterfaceAcsSnapshotEntry<TInterface, TView> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Read<InterfaceAcsSnapshotEntry<TInterface, TView>>(ref reader, options, Cases, TypeName);

    public override void Write(Utf8JsonWriter writer, InterfaceAcsSnapshotEntry<TInterface, TView> value, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Write(writer, value, options, TypeName);
}
