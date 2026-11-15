// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace Daml.Runtime.Data;

/// <summary>
/// The equality comparer that generated bindings and the <c>Daml.Runtime.Stdlib</c> types compare
/// and hash map keys, set elements and record members through. Equality is
/// <see cref="EqualityComparer{T}.Default"/>'s, except for <c>IReadOnlyList&lt;E&gt;</c> and
/// <c>IReadOnlyDictionary&lt;K,V&gt;</c>, which compare by content and recursively: a list element
/// by element in order, a dictionary entry by entry independently of insertion order, each
/// element, key and value through <see cref="DamlEqualityComparer{T}"/> in turn. The hash code
/// reads the same content, and differs from the default's for <see cref="long"/>,
/// <see cref="decimal"/> and <see cref="DateTimeOffset"/>, bare or nullable, which it computes
/// from the whole value with the per-process randomised <see cref="HashCode"/>.
/// </summary>
/// <remarks>
/// The framework's own hash codes for those three types fold the value into 32 bits by a fixed
/// rule: <c>long.GetHashCode()</c> is the exclusive-or of the two halves, so every multiple of
/// <c>4294967297</c> hashes to <c>0</c>. A counterparty who fills a <c>GenMap</c> or <c>Set</c>
/// with such keys would make every decode into a <see cref="Dictionary{TKey,TValue}"/> or
/// <see cref="HashSet{T}"/> under the default comparer cost quadratic time.
/// </remarks>
/// <typeparam name="T">The type of the values compared.</typeparam>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Mirrors EqualityComparer<T>.Default: generated code names the comparer for a concrete key type as DamlEqualityComparer<TKey>.Default.")]
public sealed class DamlEqualityComparer<T> : IEqualityComparer<T>
{
    private static readonly IEqualityComparer<T>? StructuralComparer = StructuralCollectionComparers.For<T>();

    private DamlEqualityComparer()
    {
    }

    /// <summary>The comparer for <typeparamref name="T"/>.</summary>
    public static DamlEqualityComparer<T> Default { get; } = new();

    /// <inheritdoc />
    public bool Equals(T? x, T? y) =>
        StructuralComparer is { } structural
            ? structural.Equals(x, y)
            : EqualityComparer<T>.Default.Equals(x, y);

    /// <inheritdoc />
    public int GetHashCode([DisallowNull] T obj) => StructuralComparer is { } structural
        ? structural.GetHashCode(obj)
        : ScalarHashCode(obj);

    private static int ScalarHashCode(T obj) => obj switch
    {
        long value => DamlHash.Int64(value),
        decimal value => DamlHash.Decimal(value),
        DateTimeOffset value => DamlHash.Int64(value.UtcTicks),
        _ => EqualityComparer<T>.Default.GetHashCode(obj!),
    };
}
