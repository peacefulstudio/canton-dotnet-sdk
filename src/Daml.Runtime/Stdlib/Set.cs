// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib type <c>DA.Set.Types.Set k</c> — a set of values.
/// </summary>
/// <remarks>
/// <para>
/// On the wire the type is a record with a single field <c>map : Map k Unit</c>:
/// a set is internally a Daml-LF generic map whose values are <see cref="DamlUnit"/>.
/// We expose <see cref="IReadOnlyCollection{T}"/> semantics so the consumer-facing
/// surface is the obvious one (iterate elements, count, contains-by-equality).
/// </para>
/// <para>
/// The C# codegen emits the type with a concrete CLR generic argument
/// (e.g. <c>Set&lt;Party&gt;</c>) which is not in general <see cref="IDamlRecord"/>.
/// Round-tripping therefore goes through caller-supplied converters that bridge
/// the generic CLR type to <see cref="DamlValue"/>; the codegen knows the
/// concrete element type at the call site and inlines the appropriate conversion
/// lambdas.
/// </para>
/// <para>
/// Through <see cref="System.Text.Json"/> it travels as a plain JSON array of its elements —
/// <c>["alice", "bob"]</c>, and <c>[]</c> when empty — rather than as the record the Daml-LF
/// encoding uses, because that path is a CLR round-trip contract rather than a wire one: a
/// <see cref="Set{T}"/> reads back the value it wrote and owes the ledger encoding nothing. It
/// names <see cref="SetJsonConverterFactory"/> in a <see cref="JsonConverterAttribute"/>, so it
/// converts on bare <see cref="JsonSerializerOptions"/> with no registration. Elements are written
/// in the order the set enumerates them, which for a set built from a sequence is the order of
/// first occurrence in that sequence.
/// </para>
/// </remarks>
/// <typeparam name="T">Element type of the set.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Matches the Daml stdlib type name DA.Set.Types.Set so the codegen-emitted type name resolves directly. Renaming would force the codegen to learn an additional translation.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as Set<...>.FromRecord, mirroring the Daml constructor it decodes.")]
[JsonConverter(typeof(SetJsonConverterFactory))]
public sealed record Set<T>
    where T : notnull
{
    /// <summary>
    /// The set's elements, deduplicated under default CLR equality. Materialized at
    /// construction, so a producer that retains the collection it supplied cannot change
    /// this value's equality or hash code afterwards.
    /// </summary>
    public IReadOnlySet<T> Elements { get; }

    /// <summary>
    /// Builds a set from an arbitrary input sequence. Duplicates under
    /// <see cref="EqualityComparer{T}.Default"/> are removed so the wire
    /// representation produced by <see cref="ToRecord"/> is always a valid
    /// <c>Map k Unit</c> with unique keys.
    /// </summary>
    public Set(IEnumerable<T> elements)
    {
        Elements = EventCollections.CopyRejectingNullElements(elements, nameof(elements), nameof(Set<T>));
    }

    /// <summary>The number of elements in the set.</summary>
    public int Count => Elements.Count;

    /// <summary>
    /// Returns true if the set contains <paramref name="element"/> under the
    /// default CLR equality for <typeparamref name="T"/>. Daml's set uses
    /// structural equality on the wire; for primitive element types and
    /// <see cref="Daml.Runtime.Data.Party"/> the two coincide, but for custom
    /// record element types consumers should rely on the record's own equality
    /// implementation.
    /// </summary>
    public bool Contains(T element) => Elements.Contains(element);

    /// <summary>
    /// Converts this set to its Ledger API record representation. The supplied
    /// <paramref name="convertElement"/> encodes each element to a <see cref="DamlValue"/>;
    /// elements are paired with <see cref="DamlUnit.Instance"/> in the inner map to match
    /// the Daml-LF wire shape <c>Set { map : Map k () }</c>.
    /// </summary>
    public DamlRecord ToRecord(Func<T, DamlValue> convertElement)
    {
        ArgumentNullException.ThrowIfNull(convertElement);
        var entries = Elements
            .Select(element => ((DamlValue)convertElement(element), (DamlValue)DamlUnit.Instance))
            .ToList();
        return DamlRecord.Create(
            DamlField.Create("map", new DamlGenMap(entries)));
    }

    /// <summary>
    /// Reconstructs a set from its Ledger API record representation. The supplied
    /// <paramref name="convertElement"/> decodes each element from its <see cref="DamlValue"/>
    /// form. Map values (which are always <see cref="DamlUnit"/>) are discarded;
    /// any structurally-equal duplicate keys on the wire are collapsed.
    /// </summary>
    public static Set<T> FromRecord(DamlRecord record, Func<DamlValue, T> convertElement)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(convertElement);
        var map = record.GetRequiredField("map", 0).As<DamlGenMap>();
        var elements = map.Entries.Select(entry => convertElement(entry.Key));
        return new Set<T>(elements);
    }

    /// <summary>
    /// Compares two sets by element content, independent of iteration order. The
    /// record-synthesized equality compares the backing <see cref="IReadOnlySet{T}"/>
    /// by reference — a footgun for a value type — so we override it with structural
    /// comparison.
    /// </summary>
    public bool Equals(Set<T>? other) =>
        other is not null && Elements.SetEquals(other.Elements);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var element in Elements)
        {
            hash ^= DamlEqualityComparer<T>.Default.GetHashCode(element);
        }
        return hash;
    }
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed <see cref="Set{T}"/>.
/// Without it the type is readable as nothing and writable only as its properties: the
/// <c>Set(IEnumerable&lt;T&gt; elements)</c> parameter binds to no property — the
/// <see cref="Set{T}.Elements"/> beside it is an <see cref="IReadOnlySet{T}"/>, so the names match
/// and the types do not — and <see cref="System.Text.Json"/> refuses the type with
/// <see cref="InvalidOperationException"/> before reading any payload, not even the object it had
/// itself written.
/// </summary>
/// <remarks>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime. These reflection-based calls are not compatible
/// with Native AOT compilation or aggressive IL trimming and will produce
/// <see cref="NotSupportedException"/> in those environments — the cost
/// <see cref="Serialization.EquatableArrayJsonConverterFactory"/> already carries, accepted so
/// that the attribute reaches a consumer who never registers the converters.
/// </para>
/// <para>
/// A source-generated <see cref="JsonSerializerContext"/> does not route around this. Because
/// <see cref="Set{T}"/> carries the factory as an attribute, the generator emits the factory for a
/// member of that type rather than collection metadata, and the consumer build reports
/// <c>IL2026</c> and <c>IL3050</c>. The converter reads and writes its elements through the
/// reflection-based <see cref="JsonSerializer"/> overloads, which need a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo"/> for the element type the
/// context no longer registers: writing throws a <see cref="JsonException"/> naming that element
/// type until the context declares <c>[JsonSerializable(typeof(T))]</c> for it.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("SetJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("SetJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
internal sealed class SetJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => IsClosedSet(typeToConvert);

    private static bool IsClosedSet(Type type) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == typeof(Set<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(SetJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class SetJsonConverter<T> : JsonConverter<Set<T>>
    where T : notnull
{
    private static readonly string TypeName = $"{nameof(Set<object>)}<{DiscriminatedUnionJson.Describe(typeof(T))}>";

    /// <remarks>
    /// The posture <see cref="Serialization.ContractIdJsonConverterFactory"/> already holds: a null
    /// token is read as the absent value the declared type already permits, and refused by
    /// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> where it does not — which
    /// only <see cref="Serialization.DamlJsonConverters.AddDamlConverters"/> sets, so on bare
    /// options a null in a non-nullable slot binds as null rather than being refused.
    /// </remarks>
    public override bool HandleNull => false;

    public override Set<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected array token for {TypeName}, got {reader.TokenType}.");
        }

        var elements = new List<T>();
        while (true)
        {
            if (!reader.Read())
            {
                throw new JsonException($"Unexpected end of JSON while reading {TypeName}.");
            }

            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            elements.Add(ReadElement(ref reader, elements.Count, options));
        }

        try
        {
            return new Set<T>(elements);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid element in {TypeName}: {ex.Message}", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Set<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        var index = 0;
        foreach (var element in value.Elements)
        {
            WriteElement(writer, element, index++, options);
        }
        writer.WriteEndArray();
    }

    private static T ReadElement(ref Utf8JsonReader reader, int index, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(ref reader, options)!;
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot read element {index} of {TypeName}: {ex.Message}", ex);
        }
    }

    private static void WriteElement(Utf8JsonWriter writer, T element, int index, JsonSerializerOptions options)
    {
        try
        {
            JsonSerializer.Serialize(writer, element, options);
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot write element {index} of {TypeName}: {ex.Message}", ex);
        }
    }
}
