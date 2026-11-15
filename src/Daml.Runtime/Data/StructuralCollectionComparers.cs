// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace Daml.Runtime.Data;

internal static class StructuralCollectionComparers
{
    internal static IEqualityComparer<T>? For<T>()
    {
        var type = typeof(T);
        if (!type.IsConstructedGenericType)
        {
            return null;
        }

        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();
        if (definition == typeof(IReadOnlyList<>))
        {
            return Instantiate<T>(typeof(ListComparer<>), arguments);
        }

        if (definition == typeof(IReadOnlyDictionary<,>))
        {
            return Instantiate<T>(typeof(DictionaryComparer<,>), arguments);
        }

        return null;
    }

    private static IEqualityComparer<T> Instantiate<T>(Type openComparer, Type[] arguments) =>
        (IEqualityComparer<T>)Activator.CreateInstance(openComparer.MakeGenericType(arguments))!;

    private sealed class ListComparer<TElement> : IEqualityComparer<IReadOnlyList<TElement>>
    {
        public bool Equals(IReadOnlyList<TElement>? x, IReadOnlyList<TElement>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Count != y.Count)
            {
                return false;
            }

            var elements = DamlEqualityComparer<TElement>.Default;
            for (var index = 0; index < x.Count; index++)
            {
                if (!elements.Equals(x[index], y[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode([DisallowNull] IReadOnlyList<TElement> obj)
        {
            var hash = new HashCode();
            foreach (var element in obj)
            {
                hash.Add(element, DamlEqualityComparer<TElement>.Default);
            }

            return hash.ToHashCode();
        }
    }

    private sealed class DictionaryComparer<TKey, TValue> : IEqualityComparer<IReadOnlyDictionary<TKey, TValue>>
        where TKey : notnull
    {
        public bool Equals(IReadOnlyDictionary<TKey, TValue>? x, IReadOnlyDictionary<TKey, TValue>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Count != y.Count)
            {
                return false;
            }

            var values = DamlEqualityComparer<TValue>.Default;
            foreach (var (key, value) in x)
            {
                if (!y.TryGetValue(key, out var otherValue) || !values.Equals(value, otherValue))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode([DisallowNull] IReadOnlyDictionary<TKey, TValue> obj)
        {
            var hash = 0;
            foreach (var (key, value) in obj)
            {
                var entryHash = new HashCode();
                entryHash.Add(key, DamlEqualityComparer<TKey>.Default);
                entryHash.Add(value, DamlEqualityComparer<TValue>.Default);
                hash ^= entryHash.ToHashCode();
            }

            return hash;
        }
    }
}
