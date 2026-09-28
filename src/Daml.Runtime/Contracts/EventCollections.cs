// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Null guards and defensive copies for the collection-typed members that still take a caller's
/// <see cref="IReadOnlyList{T}"/>, <see cref="IEnumerable{T}"/> or
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/>:
/// <see cref="Outcomes.ExerciseOutcome{T}.DamlError.Metadata"/>, and
/// the emitter-facing command and value shapes — <c>Commands.CommandsSubmission</c>,
/// <see cref="Data.DamlRecord"/> and the <c>Daml.Runtime.Stdlib</c> collection types — that
/// generated code is compiled against. The event records' list members carry
/// <see cref="EquatableArray{T}"/> instead, which copies at construction and has no <c>null</c>
/// state, so they need nothing from here.
/// </summary>
/// <remarks>
/// A read-only interface is a view, not an immutable collection: a producer that keeps its
/// backing collection can mutate it after handing it over, and on a record whose equality and
/// hash code read the contents that would silently change an already-computed hash and make the
/// value unfindable in a set or dictionary that already holds it. Every caller here therefore
/// copies at each entry point — every constructor and <c>init</c> accessor, so <c>with</c>
/// expressions are covered too.
/// </remarks>
internal static class EventCollections
{
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return values.Count == 0 ? Array.Empty<T>() : [.. values];
    }

    internal static IReadOnlySet<T> Copy<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return values.TryGetNonEnumeratedCount(out var count) && count == 0
            ? ReadOnlySet<T>.Empty
            : values.ToHashSet();
    }

    internal static IReadOnlyDictionary<TKey, TValue> Copy<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        string parameterName)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return values.Count == 0 ? ReadOnlyDictionary<TKey, TValue>.Empty : values.ToDictionary();
    }

    /// <summary>
    /// Returns <paramref name="value"/> unchanged, refusing it if it is <see langword="null"/> and
    /// <typeparamref name="T"/> is a reference type. Guards a scalar member that carries nothing to
    /// copy but shares the same no-null contract as its sibling
    /// <see cref="CopyRejectingNullElements{T}(IReadOnlyList{T}, string, string)"/> — namely
    /// <see cref="Stdlib.NonEmpty{T}.Hd"/>, the head of a non-empty list, whose element type is not
    /// itself guaranteed non-null at runtime.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    internal static T RejectNull<T>(T value, string parameterName, string containerName)
    {
        if (!typeof(T).IsValueType && value is null)
        {
            throw new ArgumentException(
                $"{parameterName} is null; a {containerName}<{typeof(T).Name}> holds no null elements.",
                parameterName);
        }

        return value;
    }

    /// <summary>
    /// Copies <paramref name="values"/> as <see cref="Copy{T}(IReadOnlyList{T}, string)"/> does,
    /// additionally refusing a null element of a reference type. Used by the
    /// <c>Daml.Runtime.Stdlib</c> collection types (<see cref="Stdlib.Set{T}"/>,
    /// <see cref="Stdlib.NonEmpty{T}"/>) whose element type is not itself guaranteed non-null at
    /// runtime, unlike <see cref="EquatableArray{T}"/>'s own construction-time check.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An element at a given index is <see langword="null"/>.
    /// </exception>
    internal static IReadOnlyList<T> CopyRejectingNullElements<T>(
        IReadOnlyList<T> values, string parameterName, string containerName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count == 0)
        {
            return Array.Empty<T>();
        }

        var copy = new T[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var item = values[index];
            if (!typeof(T).IsValueType && item is null)
            {
                throw new ArgumentException(
                    $"Element {index} is null; a {containerName}<{typeof(T).Name}> holds no null elements.",
                    parameterName);
            }

            copy[index] = item;
        }

        return copy;
    }

    /// <summary>
    /// Copies <paramref name="values"/> as <see cref="Copy{T}(IEnumerable{T}, string)"/> does,
    /// additionally refusing a null element of a reference type. Used by <see cref="Stdlib.Set{T}"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An element at a given enumeration index is <see langword="null"/>.
    /// </exception>
    internal static IReadOnlySet<T> CopyRejectingNullElements<T>(
        IEnumerable<T> values, string parameterName, string containerName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var set = new HashSet<T>();
        var index = 0;
        foreach (var item in values)
        {
            if (!typeof(T).IsValueType && item is null)
            {
                throw new ArgumentException(
                    $"Element {index} is null; a {containerName}<{typeof(T).Name}> holds no null elements.",
                    parameterName);
            }

            set.Add(item);
            index++;
        }

        return set.Count == 0 ? ReadOnlySet<T>.Empty : set;
    }

    /// <summary>
    /// Copies <paramref name="values"/> as <see cref="Copy{T}(IReadOnlyList{T}, string)"/> does,
    /// additionally refusing a null key, and a null value of a reference type. Used by
    /// <see cref="Stdlib.Map{TKey, TValue}"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An entry at a given index has a <see langword="null"/> key or value.
    /// </exception>
    internal static IReadOnlyList<KeyValuePair<TKey, TValue>> CopyRejectingNullKeysOrValues<TKey, TValue>(
        IReadOnlyList<KeyValuePair<TKey, TValue>> values, string parameterName, string containerName)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count == 0)
        {
            return Array.Empty<KeyValuePair<TKey, TValue>>();
        }

        var copy = new KeyValuePair<TKey, TValue>[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var entry = values[index];
            if (entry.Key is null)
            {
                throw new ArgumentException(
                    $"Entry {index} has a null key; a {containerName}<{typeof(TKey).Name}, {typeof(TValue).Name}> holds no null keys.",
                    parameterName);
            }

            if (!typeof(TValue).IsValueType && entry.Value is null)
            {
                throw new ArgumentException(
                    $"Entry {index} has a null value; a {containerName}<{typeof(TKey).Name}, {typeof(TValue).Name}> holds no null values.",
                    parameterName);
            }

            copy[index] = entry;
        }

        return copy;
    }
}
