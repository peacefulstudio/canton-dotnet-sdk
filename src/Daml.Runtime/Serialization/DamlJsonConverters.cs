// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Daml.Runtime.Streams;

namespace Daml.Runtime.Serialization;

/// <summary>
/// The <see cref="System.Text.Json"/> converters for the scalar types generated records and the
/// event, stream, command and submission-result shapes carry as fields — <see cref="Party"/>,
/// <see cref="ContractId{T}"/>, <see cref="SynchronizerId"/>, <see cref="CommandId"/>,
/// <see cref="ChoiceName"/> and <see cref="WorkflowId"/>, each of which serializes as a bare JSON
/// string rather than as an object — the same spelling PQS rows and JSON Ledger API payloads give
/// them — and <see cref="LedgerOffset"/>, which serializes as a bare JSON number, together with the
/// <see cref="EquatableArray{T}"/> every list member of the event and stream records carries,
/// which the built-in collection converter can write but not read,
/// <see cref="SubmitterInfo"/>, whose two get-only party sets no built-in reader can assign
/// through, <see cref="Set{T}"/>, whose constructor parameter binds to no property so that no
/// built-in reader can construct one at all, <see cref="Map{TKey, TValue}"/> and
/// <see cref="NonEmpty{T}"/>, whose own built-in reader constructs one but lets the
/// <see cref="ArgumentException"/> a null key, value or element trips escape
/// <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/> unwrapped
/// rather than as the <see cref="JsonException"/> naming the offending index that <see cref="Set{T}"/>
/// reports, and the declared-abstract discriminated unions
/// <see cref="Optional{T}"/>, <see cref="Either{TL, TR}"/>,
/// <see cref="ContractStreamEvent{T}"/>, <see cref="InterfaceStreamEvent{TInterface, TView}"/> and
/// <see cref="TransportStatus"/>,
/// none of which the reflection-based serializer can write or read past their own base members —
/// together with <see cref="DamlUnit"/>, whose private constructor leaves the
/// reflection-based serializer nothing to call.
/// </summary>
/// <remarks>
/// Every one of these types carries its converter as a
/// <see cref="JsonConverterAttribute"/>, <see cref="EquatableArray{T}"/> included, so plain
/// <c>JsonSerializer.Deserialize&lt;MyTemplate&gt;(json)</c> converts all of them with no
/// registration.
/// <para>
/// What that buys is a <em>CLR round-trip</em>: a value serialized by
/// <see cref="System.Text.Json"/> reads back as itself. It is not a decoder for a payload Canton
/// produced. Above these scalars the shapes diverge from the Daml-LF JSON encoding a participant
/// and PQS speak — a <c>Map</c> and a <c>NonEmpty</c> take the object contract
/// <see cref="System.Text.Json"/> derives from their members, and a generated record is read under
/// its C# member names rather than the <c>DamlFieldAttribute</c> labels the wire carries. Read a
/// ledger or PQS payload with <see cref="DamlLfJsonReader"/>, which is given the schema and
/// produces the right node for each slot.
/// </para>
/// What an attribute cannot reach is the rest of the posture, and that is what
/// <see cref="AddDamlConverters"/> is for: it sets
/// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> and marks required the
/// constructor parameters an absent member would bind to a value the payload never stated —
/// <see cref="EquatableArray{T}"/>, whose default is the empty list,
/// <see cref="LedgerOffset"/>, whose default is <see cref="LedgerOffset.Begin"/>, and the Daml
/// stdlib collections <see cref="Set{T}"/>, <see cref="Map{TKey, TValue}"/> and
/// <see cref="NonEmpty{T}"/> together with <see cref="TransportStatus"/>, whose default is a
/// null their own slot forbids — so a payload
/// that omits one is refused rather than read as that value, a property the payload never
/// mentions never reaching a converter at all. <see cref="AddDamlConverters"/> is also how a
/// host that builds its own <see cref="JsonSerializerOptions"/> — a ledger or PQS client's
/// default options, say — should register these conversions, rather than inheriting them from
/// attributes or copying <see cref="All"/> on its own: a converter on
/// <see cref="JsonSerializerOptions.Converters"/> takes precedence over the one an attribute
/// names on the type, and only <see cref="AddDamlConverters"/> carries the full posture above.
/// </remarks>
public static class DamlJsonConverters
{
    /// <summary>
    /// The converters, in the order <see cref="AddDamlConverters"/> appends them.
    /// Each instance is stateless and safe to share across
    /// <see cref="JsonSerializerOptions"/>.
    /// </summary>
    public static IReadOnlyList<JsonConverter> All { get; } =
    [
        new PartyJsonConverter(),
        new ContractIdJsonConverterFactory(),
        new SynchronizerIdJsonConverter(),
        new CommandIdJsonConverter(),
        new ChoiceNameJsonConverter(),
        new WorkflowIdJsonConverter(),
        new LedgerOffsetJsonConverter(),
        new SubmitterInfoJsonConverter(),
        new EquatableArrayJsonConverterFactory(),
        new SetJsonConverterFactory(),
        new MapJsonConverterFactory(),
        new NonEmptyJsonConverterFactory(),
        new OptionalJsonConverterFactory(),
        new EitherJsonConverterFactory(),
        new ContractStreamEventJsonConverterFactory(),
        new InterfaceStreamEventJsonConverterFactory(),
        new AcsSnapshotEntryJsonConverterFactory(),
        new InterfaceAcsSnapshotEntryJsonConverterFactory(),
        new ExerciseOutcomeJsonConverterFactory(),
        new DeduplicationPeriodJsonConverterFactory(),
        new TransportStatusJsonConverterFactory(),
        new DamlUnitJsonConverter(),
        new DamlVariantJsonConverterFactory(),
    ];

    /// <summary>
    /// Appends every converter in <see cref="All"/> to <paramref name="options"/>, sets
    /// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/>, requires the
    /// non-nullable <see cref="EquatableArray{T}"/>, <see cref="LedgerOffset"/>,
    /// <see cref="Set{T}"/>, <see cref="Map{TKey, TValue}"/> and <see cref="NonEmpty{T}"/>
    /// constructor parameters of the nullable-annotated types <paramref name="options"/>
    /// serializes, and returns
    /// <paramref name="options"/> so the call chains off an options initializer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scalar types share one posture on null: a JSON null is rejected wherever the
    /// declared type forbids one, and read as absent wherever it permits one. Every one of them
    /// but <see cref="ContractId{T}"/> is a struct, and their converters enforce that themselves;
    /// <see cref="ContractId{T}"/> is a reference type, and <c>ContractId&lt;T&gt;?</c> — the C#
    /// rendering of <c>Optional (ContractId T)</c> — is indistinguishable from it at runtime, so
    /// only the nullability annotation separates a required contract id from an optional one.
    /// Enabling <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> is therefore not a
    /// preference but the condition for the family agreeing at all; it applies to every type
    /// <paramref name="options"/> serializes, not only the Daml ones.
    /// </para>
    /// <para>
    /// The other half of that posture is the one no converter can supply: a property the
    /// payload never mentions never reaches a converter at all, so a parameter it omits is bound
    /// in silence to the CLR default for its type. For a struct whose default is itself a value
    /// the payload could have stated, that default is a plausible lie: a missing
    /// <c>EquatableArray&lt;T&gt;</c> would otherwise arrive as the empty array —
    /// indistinguishable from an event that genuinely named no witnesses, which is the
    /// distinction <see cref="CreatedContract.WitnessParties"/> and its siblings exist to carry
    /// — and a missing <see cref="LedgerOffset"/> as <see cref="LedgerOffset.Begin"/>,
    /// indistinguishable from a participant that genuinely reported the start of the stream and
    /// enough to restart a resumed stream from the beginning of the ledger. For the Daml stdlib
    /// collections <see cref="Set{T}"/>, <see cref="Map{TKey, TValue}"/> and
    /// <see cref="NonEmpty{T}"/> it is a null the slot's own declared type forbids, which
    /// surfaces as a <see cref="NullReferenceException"/> at the first use rather than at the
    /// parse that produced it. A
    /// <see cref="JsonTypeInfo"/> modifier supplies it instead, marking required exactly those
    /// constructor parameters, so an absent one is a <see cref="JsonException"/> naming it and
    /// nothing else on the type changes requiredness. A member declared
    /// <c>EquatableArray&lt;T&gt;?</c>, <c>LedgerOffset?</c>, <c>Set&lt;T&gt;?</c>,
    /// <c>Map&lt;TKey, TValue&gt;?</c> or <c>NonEmpty&lt;T&gt;?</c> stays optional, which is the
    /// escape hatch for a producer that really does omit the slot — the shape
    /// <c>ContractStreamEvent&lt;T&gt;.Snapshot</c> and its siblings already carry for an offset
    /// a participant need not report, and the shape codegen emits for a Daml <c>Optional</c> of
    /// one of the collections. The list
    /// members that are <em>meant</em> to default — <c>InterfaceIds</c>,
    /// <c>ExercisedEvents</c> — are init-only properties rather than constructor parameters, and
    /// stay absent-means-empty.
    /// </para>
    /// <para>
    /// The identity structs stop short of this rule. <c>default(CommandId)</c> and its siblings
    /// are documented as never a valid representation of an absent id and throw on every access
    /// to <c>Value</c>, so an omitted id is already loud rather than silently plausible; and
    /// widening the rule to reach them would reach <see cref="Party"/> too, which codegen emits
    /// as a positional parameter of every generated template record.
    /// </para>
    /// <para>
    /// The modifier composes onto whatever <see cref="JsonSerializerOptions.TypeInfoResolver"/>
    /// <paramref name="options"/> already carries, so a host that has installed a
    /// source-generated <see cref="JsonSerializerContext"/> or a resolver of its own keeps it.
    /// Unlike <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> above, this reaches
    /// only parameters typed <see cref="EquatableArray{T}"/>, <see cref="LedgerOffset"/>,
    /// <see cref="Set{T}"/>, <see cref="Map{TKey, TValue}"/> or <see cref="NonEmpty{T}"/>, a
    /// host's own included; a parameter of any other type keeps the requiredness it had, so an
    /// absent non-nullable <c>string</c> — or any other reference-typed parameter — still binds
    /// to null. A host
    /// that wants one of its own such parameters to stay optional gives it a C# default or
    /// declares it nullable, either of which the modifier leaves alone.
    /// </para>
    /// <para>
    /// The requiredness it adds is read off the declaring type's nullable annotations, so it
    /// reaches a nullable-annotated declaration only. A nullable-oblivious one carries no such
    /// annotation — a type compiled under <c>#nullable disable</c>, or generated with nullable
    /// reference types turned off (<c>DamlNullable=false</c>, <c>--nullable false</c>, which omits
    /// the <c>#nullable enable</c> the emitter otherwise writes) — and every parameter on it reads
    /// as nullable, so none is marked required and an absent member binds to <c>null</c> as it did
    /// before. This limit is the modifier's alone;
    /// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> above is bounded the same way
    /// for the same reason, and the struct-typed converters, which enforce their own posture on a
    /// stated null, are not.
    /// </para>
    /// <para>
    /// Each parameter it marks required is also pinned to serialize, so that
    /// <see cref="JsonSerializerOptions.DefaultIgnoreCondition"/> cannot drop an empty list, an
    /// empty collection or a <see cref="LedgerOffset.Begin"/> offset on write and leave the same
    /// options unable to read their own output.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="options"/> is already read-only — that is, serialization has begun.
    /// </exception>
    public static JsonSerializerOptions AddDamlConverters(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var converter in All)
        {
            options.Converters.Add(converter);
        }
        options.RespectNullableAnnotations = true;
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(RequireParametersWhoseAbsenceBindsAnUnstatedValue);
        return options;
    }

    private static void RequireParametersWhoseAbsenceBindsAnUnstatedValue(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.AssociatedParameter is { HasDefaultValue: false, IsNullable: false }
                && AbsenceBindsAnUnstatedValue(property.PropertyType))
            {
                property.IsRequired = true;
                property.ShouldSerialize = static (_, _) => true;
            }
        }
    }

    private static bool AbsenceBindsAnUnstatedValue(Type type) =>
        EquatableArrayJsonConverterFactory.IsClosedEquatableArray(type)
        || type == typeof(LedgerOffset)
        || type == typeof(TransportStatus)
        || IsClosedGeneric(type, typeof(Set<>))
        || IsClosedGeneric(type, typeof(Map<,>))
        || IsClosedGeneric(type, typeof(NonEmpty<>));

    private static bool IsClosedGeneric(Type type, Type definition) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == definition;
}
