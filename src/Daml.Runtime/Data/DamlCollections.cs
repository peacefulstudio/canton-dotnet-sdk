// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml Optional value.
/// </summary>
public sealed record DamlOptional(DamlValue? Value) : DamlValue
{
    /// <summary>True when this optional is Some; false when it is None.</summary>
    public bool HasValue => Value is not null;

    /// <summary>The empty optional, mirroring Daml's <c>None</c>.</summary>
    public static DamlOptional None => new(default(DamlValue));

    /// <summary>Wraps a value as a present optional, mirroring Daml's <c>Some</c>.</summary>
    public static DamlOptional Some(DamlValue value) => new(value);

    /// <summary>The carried value as <typeparamref name="T"/>, or null when None or of a different type.</summary>
    public T? GetValueOrDefault<T>() where T : DamlValue =>
        Value as T;

    /// <summary>The carried value; throws <see cref="InvalidOperationException"/> when None.</summary>
    public DamlValue GetValueOrThrow() =>
        Value ?? throw new InvalidOperationException("Optional value is None.");
}

/// <summary>
/// One level of a Daml Optional chain nested two or more levels deep —
/// <c>Optional (Optional a)</c> and beyond. Distinct from <see cref="DamlOptional"/> because
/// the two carry different wire encodings for the same shape: a flat optional is JSON
/// <c>null</c> or its bare value, while every level of a nested chain is the array form,
/// <c>[]</c> when absent and <c>[v]</c> when present. A participant rejects the flat encoding
/// in a nested position.
/// </summary>
/// <param name="Value">The carried value, or <see langword="null"/> when absent.</param>
public sealed record DamlOptionalChain(DamlValue? Value) : DamlValue
{
    /// <summary>The absent level, mirroring Daml's <c>None</c>.</summary>
    public static DamlOptionalChain None => new(default(DamlValue));

    /// <summary>Wraps a value as a present level, mirroring Daml's <c>Some</c>.</summary>
    /// <param name="value">The value to carry.</param>
    /// <returns>A present level carrying <paramref name="value"/>.</returns>
    public static DamlOptionalChain Some(DamlValue value) => new(value);
}

/// <summary>
/// Represents a Daml List value.
/// </summary>
public sealed record DamlList : DamlValue
{
    /// <summary>Creates a list from the supplied values, materializing them defensively.</summary>
    public DamlList(IReadOnlyList<DamlValue> values)
    {
        Values = values.ToList();
    }

    /// <summary>The list elements, materialized at construction time.</summary>
    public IReadOnlyList<DamlValue> Values { get; }

    /// <summary>The number of elements in the list.</summary>
    public int Count => Values.Count;

    /// <summary>The element at the given zero-based position.</summary>
    public DamlValue this[int index] => Values[index];

    /// <summary>Builds a list from the given elements, preserving order.</summary>
    public static DamlList Create(params DamlValue[] values) => new(values);

    /// <summary>Builds a list from a sequence, materializing it so later mutation of the source has no effect.</summary>
    public static DamlList Create(IEnumerable<DamlValue> values) => new(values.ToList());

    /// <summary>The elements downcast to <typeparamref name="T"/>; throws on first mismatched element when enumerated.</summary>
    public IEnumerable<T> AsEnumerable<T>() where T : DamlValue =>
        Values.Cast<T>();

    /// <summary>
    /// Compares two lists element by element. The record-synthesized equality
    /// compares the backing <see cref="IReadOnlyList{T}"/> by reference — a
    /// footgun for a value type — so we override it with structural comparison.
    /// </summary>
    public bool Equals(DamlList? other) =>
        other is not null && Values.SequenceEqual(other.Values);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents a Daml TextMap value (map with string keys).
/// </summary>
public sealed record DamlTextMap : DamlValue
{
    /// <summary>Creates a text map from the supplied entries, materializing them defensively.</summary>
    public DamlTextMap(IReadOnlyDictionary<string, DamlValue> values)
    {
        Values = values.ToDictionary();
    }

    /// <summary>The map entries, materialized at construction time.</summary>
    public IReadOnlyDictionary<string, DamlValue> Values { get; }

    /// <summary>The number of entries in the map.</summary>
    public int Count => Values.Count;

    /// <summary>The value stored under the given key; throws <see cref="KeyNotFoundException"/> when absent.</summary>
    public DamlValue this[string key] => Values[key];

    /// <summary>Builds a text map from key/value pairs; duplicate keys throw.</summary>
    public static DamlTextMap Create(params (string Key, DamlValue Value)[] entries) =>
        new(entries.ToDictionary(e => e.Key, e => e.Value));

    /// <summary>Looks up a key without throwing; returns false and a null value when absent.</summary>
    public bool TryGetValue(string key, [NotNullWhen(true)] out DamlValue? value) =>
        Values.TryGetValue(key, out value);

    /// <summary>
    /// Compares two maps by key-value content, independent of insertion order.
    /// The record-synthesized equality compares the backing
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/> by reference — a footgun
    /// for a value type — so we override it with structural comparison.
    /// </summary>
    public bool Equals(DamlTextMap? other)
    {
        if (other is null || Values.Count != other.Values.Count)
        {
            return false;
        }
        foreach (var (key, value) in Values)
        {
            if (!other.Values.TryGetValue(key, out var otherValue) || !value.Equals(otherValue))
            {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (key, value) in Values)
        {
            hash ^= HashCode.Combine(key, value);
        }
        return hash;
    }
}

/// <summary>
/// Represents a Daml GenMap value (map with arbitrary key types).
/// </summary>
/// <remarks>
/// Declared as itself, serializes through <see cref="System.Text.Json"/> as
/// <c>{"Entries":[{"Key":…,"Value":…}]}</c>, each key and value a <see cref="DamlValue"/> with its
/// <c>"$case"</c>, and reads back as the same entries in the same order.
/// </remarks>
[JsonConverter(typeof(DamlGenMapJsonConverter))]
public sealed record DamlGenMap : DamlValue
{
    /// <summary>Creates a GenMap from the supplied entries, materializing them defensively.</summary>
    /// <exception cref="ArgumentException">Thrown when the entries contain a duplicate key.</exception>
    public DamlGenMap(IReadOnlyList<(DamlValue Key, DamlValue Value)> entries)
    {
        var seenKeys = new HashSet<DamlValue>();
        foreach (var (key, _) in entries)
        {
            if (!seenKeys.Add(key))
            {
                throw new ArgumentException($"GenMap has a duplicate key '{key}'.", nameof(entries));
            }
        }
        Entries = entries.ToList();
    }

    /// <summary>The map entries, materialized at construction time.</summary>
    public IReadOnlyList<(DamlValue Key, DamlValue Value)> Entries { get; }

    /// <summary>The number of entries in the map.</summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Builds a generic map from key/value pairs, preserving entry order; keys that are
    /// structurally equal throw <see cref="ArgumentException"/>, mirroring
    /// <see cref="DamlTextMap.Create"/>.
    /// </summary>
    public static DamlGenMap Create(params (DamlValue Key, DamlValue Value)[] entries) => new(entries);

    /// <summary>
    /// Compares two maps entry by entry, in entry order (GenMap entries are
    /// ordered on the ledger). The record-synthesized equality compares the
    /// backing <see cref="IReadOnlyList{T}"/> by reference — a footgun for a
    /// value type — so we override it with structural comparison.
    /// </summary>
    public bool Equals(DamlGenMap? other) =>
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
