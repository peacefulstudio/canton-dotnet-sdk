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
/// Daml stdlib type <c>DA.Map.Types.Map k v</c> / <c>DA.Internal.Map.Map k v</c> —
/// an ordered associative map.
/// </summary>
/// <remarks>
/// <para>
/// In Daml stdlib, <c>DA.Map.Types.Map k v</c> is a record wrapper over the
/// <c>GenMap</c> primitive — roughly <c>data Map k v = Map { map :: GenMap k v }</c> —
/// so the Ledger API wire shape is a record with a single field named
/// <c>map</c> carrying a <see cref="DamlGenMap"/>. This stub mirrors that
/// shape exactly. It is rarely reached: the codegen routes most uses of
/// <c>GenMap k v</c> directly to <see cref="IReadOnlyDictionary{TKey, TValue}"/>
/// backed by <see cref="DamlGenMap"/>, and only falls through to this wrapper
/// when a DAR references the type by its <c>DA.Map.Types</c> or
/// <c>DA.Internal.Map</c> module path explicitly.
/// </para>
/// <para>
/// The wrapper carries an ordered list of key/value pairs, matching the
/// order-preserving <c>GenMap</c> wire shape. No CLR <c>Dictionary</c>-backed
/// view is exposed because <c>GenMap</c> tolerates structurally-equal duplicate
/// keys that <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/>
/// would reject.
/// </para>
/// <para>
/// Through <see cref="System.Text.Json"/> it travels as the object its members derive —
/// <c>{"Entries":[{"Key":...,"Value":...}],"Count":...}</c> — the same shape a bare
/// <see cref="System.Text.Json"/> round trip produced before this type named
/// <see cref="MapJsonConverterFactory"/>. What the converter adds is refusal: a null key, or a
/// null value of a reference <typeparamref name="TValue"/>, surfaces from
/// <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/> as a
/// <see cref="JsonException"/> naming the entry's index, on bare <see cref="JsonSerializerOptions"/>
/// as well as under <see cref="Serialization.DamlJsonConverters.AddDamlConverters"/>, instead of the
/// <see cref="ArgumentException"/> the shared null guard throws reaching the caller unwrapped.
/// </para>
/// </remarks>
/// <typeparam name="TKey">Key type.</typeparam>
/// <typeparam name="TValue">Value type.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as Map<...>.FromRecord, mirroring the Daml constructor it decodes.")]
[JsonConverter(typeof(MapJsonConverterFactory))]
public sealed record Map<TKey, TValue>(IReadOnlyList<KeyValuePair<TKey, TValue>> Entries)
    where TKey : notnull
    where TValue : notnull
{
    private readonly IReadOnlyList<KeyValuePair<TKey, TValue>> _entries =
        EventCollections.CopyRejectingNullKeysOrValues(Entries, nameof(Entries), nameof(Map<TKey, TValue>));

    /// <summary>
    /// The map's entries, in wire order. Copied at construction and on <c>init</c>, so a
    /// producer that retains the list it supplied cannot change this value's equality or
    /// hash code afterwards.
    /// </summary>
    public IReadOnlyList<KeyValuePair<TKey, TValue>> Entries
    {
        get => _entries;
        init => _entries = EventCollections.CopyRejectingNullKeysOrValues(
            value, nameof(Entries), nameof(Map<TKey, TValue>));
    }

    /// <summary>The number of entries in the map.</summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Converts this map to its Ledger API record representation. The wrapping
    /// record has a single field named <c>map</c> carrying a
    /// <see cref="DamlGenMap"/>, matching the stdlib definition of
    /// <c>DA.Map.Types.Map</c> (and mirroring the shape used by
    /// <see cref="Set{T}"/>).
    /// </summary>
    public DamlRecord ToRecord(
        Func<TKey, DamlValue> convertKey,
        Func<TValue, DamlValue> convertValue)
    {
        ArgumentNullException.ThrowIfNull(convertKey);
        ArgumentNullException.ThrowIfNull(convertValue);
        var entries = Entries
            .Select(kv => ((DamlValue)convertKey(kv.Key), (DamlValue)convertValue(kv.Value)))
            .ToList();
        return DamlRecord.Create(
            DamlField.Create("map", new DamlGenMap(entries)));
    }

    /// <summary>
    /// Reconstructs a map from its Ledger API record representation.
    /// </summary>
    public static Map<TKey, TValue> FromRecord(
        DamlRecord record,
        Func<DamlValue, TKey> convertKey,
        Func<DamlValue, TValue> convertValue)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(convertKey);
        ArgumentNullException.ThrowIfNull(convertValue);
        var map = record.GetRequiredField("map", 0).As<DamlGenMap>();
        var entries = map.Entries
            .Select(entry => new KeyValuePair<TKey, TValue>(
                convertKey(entry.Key),
                convertValue(entry.Value)))
            .ToList();
        return new Map<TKey, TValue>(entries);
    }

    /// <summary>
    /// Compares two maps entry by entry, in entry order. The record-synthesized equality
    /// compares the backing <see cref="IReadOnlyList{T}"/> by reference — a footgun for a
    /// value type — so we override it with structural comparison.
    /// </summary>
    public bool Equals(Map<TKey, TValue>? other) =>
        other is not null && Entries.SequenceEqual(other.Entries);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Entries)
        {
            hash.Add(entry);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed <see cref="Map{TKey, TValue}"/>.
/// <see cref="System.Text.Json"/>'s own reflection-based constructor binding could read and write the
/// type without this converter — <see cref="Map{TKey, TValue}"/>'s constructor parameter
/// <c>Entries</c> binds directly to the property of the same name — but it would leave the
/// <see cref="ArgumentException"/> a null key or value trips reaching
/// <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/> unwrapped, on
/// bare <see cref="JsonSerializerOptions"/> and under
/// <see cref="Serialization.DamlJsonConverters.AddDamlConverters"/> alike, with no converter Read to
/// add a translating catch to. <see cref="MapJsonConverter{TKey, TValue}"/> supplies that translation
/// while keeping the object shape byte-identical to what the reflection-based binding already wrote.
/// </summary>
/// <remarks>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime. These reflection-based calls are not compatible
/// with Native AOT compilation or aggressive IL trimming and will produce
/// <see cref="NotSupportedException"/> in those environments — the cost
/// <see cref="Serialization.EquatableArrayJsonConverterFactory"/> and
/// <see cref="SetJsonConverterFactory"/> already carry, accepted so that the attribute reaches a
/// consumer who never registers the converters.
/// </para>
/// <para>
/// A source-generated <see cref="JsonSerializerContext"/> does not route around this. Because
/// <see cref="Map{TKey, TValue}"/> carries the factory as an attribute, the generator emits the
/// factory for a member of that type rather than collection metadata, and the consumer build reports
/// <c>IL2026</c> and <c>IL3050</c>. The converter reads and writes keys and values through the
/// reflection-based <see cref="JsonSerializer"/> overloads, which need a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo"/> for <c>TKey</c> and <c>TValue</c>
/// the context no longer registers.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("MapJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("MapJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
internal sealed class MapJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => IsClosedMap(typeToConvert);

    private static bool IsClosedMap(Type type) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == typeof(Map<,>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(MapJsonConverter<,>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
}

internal sealed class MapJsonConverter<TKey, TValue> : JsonConverter<Map<TKey, TValue>>
    where TKey : notnull
    where TValue : notnull
{
    private static readonly string TypeName =
        $"{nameof(Map<object, object>)}<{DiscriminatedUnionJson.Describe(typeof(TKey))}, {DiscriminatedUnionJson.Describe(typeof(TValue))}>";

    /// <remarks>
    /// The posture <see cref="SetJsonConverter{T}"/> already holds: a null token is read as the
    /// absent value the declared type already permits, and refused by
    /// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> where it does not — which only
    /// <see cref="Serialization.DamlJsonConverters.AddDamlConverters"/> sets, so on bare options a
    /// null in a non-nullable slot binds as null rather than being refused.
    /// </remarks>
    public override bool HandleNull => false;

    public override Map<TKey, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected object token for {TypeName}, got {reader.TokenType}.");
        }

        var entriesName = PropertyName(nameof(Map<TKey, TValue>.Entries), options);
        var countName = PropertyName(nameof(Map<TKey, TValue>.Count), options);
        var comparison = options.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        IReadOnlyList<KeyValuePair<TKey, TValue>>? entries = null;
        while (true)
        {
            if (!reader.Read())
            {
                throw new JsonException($"Unexpected end of JSON while reading {TypeName}.");
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, entriesName, comparison))
            {
                entries = ReadEntries(ref reader, options);
            }
            else if (string.Equals(propertyName, countName, comparison))
            {
                reader.Skip();
            }
            else if (options.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
            {
                throw new JsonException(
                    $"The JSON property '{propertyName}' could not be mapped to any .NET member contained in type '{TypeName}'.");
            }
            else
            {
                reader.Skip();
            }
        }

        try
        {
            return new Map<TKey, TValue>(entries!);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid entry in {TypeName}: {ex.Message}", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Map<TKey, TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(PropertyName(nameof(Map<TKey, TValue>.Entries), options));
        JsonSerializer.Serialize(writer, value.Entries, options);
        if (!options.IgnoreReadOnlyProperties && !IsIgnoredDefault(value.Count, options))
        {
            writer.WritePropertyName(PropertyName(nameof(Map<TKey, TValue>.Count), options));
            JsonSerializer.Serialize(writer, value.Count, options);
        }

        writer.WriteEndObject();
    }

    private static IReadOnlyList<KeyValuePair<TKey, TValue>>? ReadEntries(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<KeyValuePair<TKey, TValue>>>(ref reader, options);
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException($"Cannot read {TypeName}.Entries: {ex.Message}", ex);
        }
    }

    private static string PropertyName(string clrName, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(clrName) ?? clrName;

    private static bool IsIgnoredDefault<TProperty>(TProperty value, JsonSerializerOptions options) =>
        options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingDefault
        && EqualityComparer<TProperty>.Default.Equals(value, default!);
}
