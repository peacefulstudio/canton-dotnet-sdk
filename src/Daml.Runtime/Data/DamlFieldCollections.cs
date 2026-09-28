// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Daml.Runtime.Data;

/// <summary>
/// Value semantics for the collection-typed fields of a generated Daml record: the defensive
/// copy taken at every entry point, the content comparison, and the hash code that reads the
/// same content. Generated records call into these rather than carrying the loops inline, so a
/// list field compares element by element and a map field compares key by key independently of
/// insertion order — the semantics <see cref="DamlList"/> and <see cref="DamlTextMap"/>
/// already settled on for the same shapes.
/// </summary>
/// <remarks>
/// A read-only interface is a view, not an immutable collection: a producer that keeps the
/// collection it handed over can mutate it afterwards, and on a record whose equality and hash
/// code read the contents that silently invalidates an already-computed hash and makes the value
/// unfindable in a set or dictionary that already holds it. Copying at the primary constructor
/// and at each <c>init</c> accessor covers <c>with</c> expressions too.
/// </remarks>
public static class DamlFieldCollections
{
    /// <summary>Materializes <paramref name="values"/> so a later change to the caller's collection cannot reach the record.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The elements to copy, or <see langword="null"/>.</param>
    /// <returns>An independent read-only view of <paramref name="values"/>, or <see langword="null"/> when it is null.</returns>
    [return: NotNullIfNotNull(nameof(values))]
    public static IReadOnlyList<T>? Copy<T>(IReadOnlyList<T>? values) => values switch
    {
        null => null,
        { Count: 0 } => Array.Empty<T>(),
        _ => [.. values],
    };

    /// <summary>Materializes <paramref name="entries"/> so a later change to the caller's dictionary cannot reach the record.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="entries">The entries to copy, or <see langword="null"/>.</param>
    /// <returns>An independent read-only view of <paramref name="entries"/>, or <see langword="null"/> when it is null.</returns>
    [return: NotNullIfNotNull(nameof(entries))]
    public static IReadOnlyDictionary<TKey, TValue>? Copy<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? entries)
        where TKey : notnull => entries switch
    {
        null => null,
        { Count: 0 } => ReadOnlyDictionary<TKey, TValue>.Empty,
        _ => entries.ToDictionary(),
    };

    /// <summary>Compares two lists element by element, in order.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="left">The first list, or <see langword="null"/>.</param>
    /// <param name="right">The second list, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when both are null or hold equal elements in the same order.</returns>
    public static bool Equal<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }
        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Compares two dictionaries key by key, independently of insertion order.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="left">The first dictionary, or <see langword="null"/>.</param>
    /// <param name="right">The second dictionary, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when both are null or hold the same key-value pairs.</returns>
    public static bool Equal<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? left,
        IReadOnlyDictionary<TKey, TValue>? right)
        where TKey : notnull
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }
        var comparer = EqualityComparer<TValue>.Default;
        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue) || !comparer.Equals(value, otherValue))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Hashes a list from its elements, in order, matching <see cref="Equal{T}(IReadOnlyList{T}, IReadOnlyList{T})"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The list to hash, or <see langword="null"/>.</param>
    /// <returns>The content hash code; <c>0</c> when <paramref name="values"/> is null.</returns>
    public static int Hash<T>(IReadOnlyList<T>? values)
    {
        if (values is null)
        {
            return 0;
        }
        var hash = new HashCode();
        foreach (var value in values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    /// <summary>Hashes a dictionary from its entries, independently of insertion order, matching <see cref="Equal{TKey,TValue}(IReadOnlyDictionary{TKey,TValue}, IReadOnlyDictionary{TKey,TValue})"/>.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="entries">The dictionary to hash, or <see langword="null"/>.</param>
    /// <returns>The content hash code; <c>0</c> when <paramref name="entries"/> is null or empty.</returns>
    public static int Hash<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? entries)
        where TKey : notnull
    {
        if (entries is null)
        {
            return 0;
        }
        var hash = 0;
        foreach (var (key, value) in entries)
        {
            hash ^= HashCode.Combine(key, value);
        }
        return hash;
    }
}
