// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Builds <see cref="EquatableArray{T}"/> values. Also the collection builder behind
/// collection expressions targeting that type, so <c>[alice, bob]</c>, <c>[]</c> and
/// <c>[.. list]</c> all construct one.
/// </summary>
public static class EquatableArray
{
    /// <summary>
    /// Copies <paramref name="items"/> into a new array, so a producer that keeps the
    /// buffer it supplied cannot change the value afterwards. An empty span yields
    /// <see cref="EquatableArray{T}.Empty"/> without allocating.
    /// </summary>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is a reference type and
    /// an element is <c>null</c>.</exception>
    public static EquatableArray<T> Create<T>(ReadOnlySpan<T> items) =>
        items.IsEmpty ? default : Own(items.ToArray(), nameof(items));

    /// <summary>
    /// Copies <paramref name="items"/> into a new array, so a producer that keeps the
    /// array it supplied cannot change the value afterwards. An empty array yields
    /// <see cref="EquatableArray{T}.Empty"/> without allocating.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is a reference type and
    /// an element is <c>null</c>.</exception>
    public static EquatableArray<T> Create<T>(T[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Create(new ReadOnlySpan<T>(items));
    }

    /// <summary>
    /// Copies <paramref name="items"/> into a new array, so a producer that keeps the
    /// collection it supplied cannot change the value afterwards. An empty sequence yields
    /// <see cref="EquatableArray{T}.Empty"/> and retains no buffer.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is a reference type and
    /// an element is <c>null</c>.</exception>
    public static EquatableArray<T> Create<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        return copy.Length == 0 ? default : Own(copy, nameof(items));
    }

    private static EquatableArray<T> Own<T>(T[] copy, string parameterName)
    {
        if (!typeof(T).IsValueType)
        {
            for (var index = 0; index < copy.Length; index++)
            {
                if (copy[index] is null)
                {
                    throw new ArgumentException(
                        $"Element {index} is null; an {nameof(EquatableArray)}<{typeof(T).Name}> holds no null elements.",
                        parameterName);
                }
            }
        }
        return new(copy);
    }
}

/// <summary>
/// A fixed-length, read-only sequence that compares by content. Two arrays are equal when
/// they hold equal elements in the same order, as decided by
/// <see cref="EqualityComparer{T}.Default"/>, and equal arrays hash alike, so a record
/// carrying one as a member keeps record-synthesized equality and still compares that
/// member by value.
/// </summary>
/// <typeparam name="T">The element type. No <see cref="IEquatable{T}"/> constraint: elements
/// compare through <see cref="EqualityComparer{T}.Default"/>, which uses one when the type
/// has it and falls back to <see cref="object.Equals(object)"/> otherwise. A nullable
/// annotation on a reference element type is not honoured: every factory rejects a
/// <c>null</c> element, so an <c>EquatableArray&lt;string?&gt;</c> holds no <c>null</c> and
/// the annotation buys nothing.</typeparam>
/// <remarks>
/// <para>
/// <c>default</c> is the empty array. Every member treats an uninitialized value as empty:
/// <see cref="Count"/> is zero, enumeration yields nothing, and <c>default</c> equals
/// <see cref="Empty"/>. That is why this is a <c>struct</c> rather than a sealed class: a
/// record member of this type has no <c>null</c> state to reach, not through a constructor
/// argument, not through a <c>with</c> expression, and not through reflection, so no null guard
/// is needed at any entry point.
/// </para>
/// <para>
/// When <typeparamref name="T"/> is a reference type the array holds no <c>null</c> element:
/// <see cref="EquatableArray.Create{T}(ReadOnlySpan{T})"/>, and so a collection expression,
/// rejects one. A nullable value type element is a value of its own type and is not checked.
/// </para>
/// <para>
/// It implements <see cref="IReadOnlyList{T}"/>, so code that reads the members through that
/// interface — LINQ, a <c>foreach</c> over the interface type, a call taking
/// <see cref="IEnumerable{T}"/> — compiles unchanged. Each such conversion boxes the struct;
/// a <c>foreach</c> over the array itself uses the allocation-free struct
/// <see cref="Enumerator"/>.
/// </para>
/// <para>
/// It also implements <see cref="IList{T}"/>, and through it <see cref="ICollection{T}"/>, so
/// that LINQ's indexed and counted paths take it: <c>ToArray</c> and <c>ToList</c> size their
/// result up front instead of growing a buffer, <c>Count</c> reads the count, and
/// <c>ElementAt</c>, <c>Last</c> and <c>Skip</c> index straight to the element instead of
/// walking to it — those three probe <see cref="IList{T}"/> and never
/// <see cref="IReadOnlyList{T}"/>. Both interfaces' mutating members — <c>Add</c>,
/// <c>Clear</c>, <c>Remove</c>, <c>Insert</c>, <c>RemoveAt</c> and the settable indexer — are
/// implemented explicitly, so they are off this type's own surface, and throw
/// <see cref="NotSupportedException"/>; <c>IsReadOnly</c> is <c>true</c>.
/// </para>
/// <para>
/// It travels as a plain JSON array through
/// <see cref="Daml.Runtime.Serialization.EquatableArrayJsonConverterFactory"/>, which it names
/// in a <see cref="JsonConverterAttribute"/>, so it converts on bare
/// <see cref="System.Text.Json.JsonSerializerOptions"/> with no registration.
/// <see cref="Daml.Runtime.Serialization.DamlJsonConverters.AddDamlConverters"/> is still what
/// makes a list the payload omits an error rather than an empty array, which is the one
/// guarantee an attribute cannot carry.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Empty names the default value the way ImmutableArray<T>.Empty and ReadOnlyCollection<T>.Empty do, so a reader who knows those finds it where they expect; the type argument is the array's own, not a second one to infer.")]
[SuppressMessage(
    "Naming",
    "CA1710:Identifiers should have correct suffix",
    Justification = "Array names the shape the way ImmutableArray<T> does: a fixed-length indexed sequence. The Collection suffix the rule asks for reads as a growable collection, which is the one thing this type refuses to be.")]
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
[JsonConverter(typeof(EquatableArrayJsonConverterFactory))]
[AbsenceBindsAnUnstatedValue]
public readonly struct EquatableArray<T> : IReadOnlyList<T>, IList<T>, IEquatable<EquatableArray<T>>
{
    private readonly T[]? _items;

    internal EquatableArray(T[] items) => _items = items;

    /// <summary>The empty array. Equal to <c>default</c>.</summary>
    public static EquatableArray<T> Empty => default;

    /// <summary>The number of elements; zero for <c>default</c>.</summary>
    public int Count => _items?.Length ?? 0;

    /// <summary>Whether the array holds no elements; <c>true</c> for <c>default</c>.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>The element at <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or
    /// not less than <see cref="Count"/>.</exception>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _items![index];
        }
    }

    private T[] Items => _items ?? Array.Empty<T>();

    /// <summary>
    /// The elements as a read-only span over the array's own storage, without copying.
    /// <c>default</c> and <see cref="Empty"/> yield an empty span.
    /// </summary>
    public ReadOnlySpan<T> AsSpan() => Items.AsSpan();

    /// <summary>
    /// Copies the elements into a new array the caller owns. An empty array yields the
    /// shared <see cref="Array.Empty{T}"/>, which has no element to write over.
    /// </summary>
    public T[] ToArray() => IsEmpty ? Array.Empty<T>() : AsSpan().ToArray();

    /// <summary>
    /// Copies the elements into <paramref name="array"/>, starting at
    /// <paramref name="arrayIndex"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is negative
    /// or past the end of <paramref name="array"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="array"/> has less room left from
    /// <paramref name="arrayIndex"/> than there are elements.</exception>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
        var room = array.Length - arrayIndex;
        if (room < Count)
        {
            throw new ArgumentException(
                $"There are {Count} elements to copy, but only room for {room} from index {arrayIndex}.",
                nameof(array));
        }
        AsSpan().CopyTo(array.AsSpan(arrayIndex));
    }

    /// <summary>
    /// The index of the first element equal to <paramref name="item"/> under
    /// <see cref="EqualityComparer{T}.Default"/>, or <c>-1</c> when no element is.
    /// </summary>
    public int IndexOf(T item) => AsSpan().IndexOf(item, EqualityComparer<T>.Default);

    /// <summary>
    /// Whether any element equals <paramref name="item"/> under
    /// <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <summary>
    /// Enumerates the elements in order without allocating. <c>foreach</c> over a value of
    /// this type binds to this method; the interface enumerators box.
    /// </summary>
    public Enumerator GetEnumerator() => new(Items);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection<T>.IsReadOnly => true;

    void ICollection<T>.Add(T item) => throw ReadOnly();

    void ICollection<T>.Clear() => throw ReadOnly();

    bool ICollection<T>.Remove(T item) => throw ReadOnly();

    T IList<T>.this[int index]
    {
        get => this[index];
        set => throw ReadOnly();
    }

    void IList<T>.Insert(int index, T item) => throw ReadOnly();

    void IList<T>.RemoveAt(int index) => throw ReadOnly();

    private static NotSupportedException ReadOnly() =>
        new($"{nameof(EquatableArray)}<{typeof(T).Name}> is read-only; build a new value instead.");

    /// <summary>
    /// Compares by content: equal elements, in the same order, under
    /// <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    public bool Equals(EquatableArray<T> other) =>
        Items.AsSpan().SequenceEqual(other.Items, EqualityComparer<T>.Default);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <summary>
    /// Hashes the length followed by every element, so equal arrays hash alike and
    /// <c>default</c> hashes as <see cref="Empty"/>.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        foreach (var item in this)
        {
            hash.Add(item, EqualityComparer<T>.Default);
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// Renders the elements as <c>[a, b]</c>, each through its own
    /// <see cref="object.ToString"/>. <c>default</c> and <see cref="Empty"/> render as
    /// <c>[]</c>, and a <c>null</c> element of a nullable value type renders as
    /// <c>null</c>.
    /// </summary>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return "[]";
        }

        var elements = AsSpan();
        var rendered = new StringBuilder("[").Append(ElementText(elements[0]));
        for (var i = 1; i < elements.Length; i++)
        {
            rendered.Append(", ").Append(ElementText(elements[i]));
        }
        return rendered.Append(']').ToString();
    }

    private static string ElementText(T item) => item?.ToString() ?? "null";

    /// <summary>Content equality; see <see cref="Equals(EquatableArray{T})"/>.</summary>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>Content inequality; see <see cref="Equals(EquatableArray{T})"/>.</summary>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>
    /// The allocation-free enumerator <c>foreach</c> uses over an
    /// <see cref="EquatableArray{T}"/>.
    /// </summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly T[]? _items;
        private int _index;

        internal Enumerator(T[] items)
        {
            _items = items;
            _index = -1;
        }

        private readonly T[] Items => _items ?? Array.Empty<T>();

        /// <inheritdoc/>
        public readonly T Current => Items[_index];

        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext() => ++_index < Items.Length;

        void IEnumerator.Reset() => _index = -1;

        /// <inheritdoc/>
        public readonly void Dispose()
        {
        }
    }
}
