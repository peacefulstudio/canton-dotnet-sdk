// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class StructuralTypeArgumentEqualityTests
{
    private static IReadOnlyList<long> Longs(params long[] values) => new List<long>(values);

    private static IReadOnlyList<IReadOnlyList<long>> Rows(params long[][] rows) =>
        rows.Select(row => Longs(row)).ToList();

    private static IReadOnlyDictionary<string, long> Dictionary(params (string Key, long Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value);

    private static void ShouldBeEqualWithEqualHashes<T>(T left, T right)
    {
        left!.Equals(right).Should().BeTrue();
        right!.Equals(left).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlEqualityComparer_compares_lists_by_element_in_order()
    {
        var comparer = DamlEqualityComparer<IReadOnlyList<long>>.Default;

        comparer.Equals(Longs(1, 2), Longs(1, 2)).Should().BeTrue();
        comparer.GetHashCode(Longs(1, 2)).Should().Be(comparer.GetHashCode(Longs(1, 2)));
        comparer.Equals(Longs(1, 2), Longs(2, 1)).Should().BeFalse();
        comparer.Equals(Longs(1, 2), Longs(1)).Should().BeFalse();
        comparer.Equals(Longs(), Longs()).Should().BeTrue();
        comparer.GetHashCode(Longs(1, 2)).Should().NotBe(comparer.GetHashCode(Longs(2, 1)));
    }

    [Fact]
    public void DamlEqualityComparer_compares_lists_of_lists_recursively()
    {
        var comparer = DamlEqualityComparer<IReadOnlyList<IReadOnlyList<long>>>.Default;

        comparer.Equals(Rows([1, 2], [3]), Rows([1, 2], [3])).Should().BeTrue();
        comparer.GetHashCode(Rows([1, 2], [3])).Should().Be(comparer.GetHashCode(Rows([1, 2], [3])));
        comparer.Equals(Rows([1, 2], [3]), Rows([1, 2], [4])).Should().BeFalse();
    }

    [Fact]
    public void DamlEqualityComparer_compares_null_lists()
    {
        var comparer = DamlEqualityComparer<IReadOnlyList<long>>.Default;

        comparer.Equals(null, null).Should().BeTrue();
        comparer.Equals(null, Longs()).Should().BeFalse();
        comparer.Equals(Longs(), null).Should().BeFalse();
    }

    [Fact]
    public void DamlEqualityComparer_compares_dictionaries_by_entry_independently_of_insertion_order()
    {
        var comparer = DamlEqualityComparer<IReadOnlyDictionary<string, long>>.Default;
        var forward = Dictionary(("a", 1), ("b", 2));
        var backward = Dictionary(("b", 2), ("a", 1));

        comparer.Equals(forward, backward).Should().BeTrue();
        comparer.GetHashCode(forward).Should().Be(comparer.GetHashCode(backward));
        comparer.Equals(forward, Dictionary(("a", 1), ("b", 3))).Should().BeFalse();
        comparer.Equals(forward, Dictionary(("a", 1))).Should().BeFalse();
        comparer.Equals(forward, Dictionary(("a", 1), ("c", 2))).Should().BeFalse();
        comparer.GetHashCode(forward).Should().NotBe(comparer.GetHashCode(Dictionary(("a", 1), ("b", 3))));
    }

    [Fact]
    public void DamlEqualityComparer_compares_dictionary_values_that_are_lists()
    {
        var comparer = DamlEqualityComparer<IReadOnlyDictionary<string, IReadOnlyList<long>>>.Default;
        IReadOnlyDictionary<string, IReadOnlyList<long>> left = new Dictionary<string, IReadOnlyList<long>> { ["k"] = Longs(1, 2) };
        IReadOnlyDictionary<string, IReadOnlyList<long>> right = new Dictionary<string, IReadOnlyList<long>> { ["k"] = Longs(1, 2) };
        IReadOnlyDictionary<string, IReadOnlyList<long>> other = new Dictionary<string, IReadOnlyList<long>> { ["k"] = Longs(1, 3) };

        comparer.Equals(left, right).Should().BeTrue();
        comparer.GetHashCode(left).Should().Be(comparer.GetHashCode(right));
        comparer.Equals(left, other).Should().BeFalse();
    }

    [Fact]
    public void Tuple2_with_a_list_component_equals_another_decode_of_the_same_content()
    {
        var left = new Tuple2<string, IReadOnlyList<long>>("alice", Longs(1, 2));
        var right = new Tuple2<string, IReadOnlyList<long>>("alice", Longs(1, 2));

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Tuple2<string, IReadOnlyList<long>>("alice", Longs(2, 1))).Should().BeFalse();
        left.Equals(new Tuple2<string, IReadOnlyList<long>>("bob", Longs(1, 2))).Should().BeFalse();
    }

    [Fact]
    public void Tuple2_with_a_list_of_lists_component_compares_recursively()
    {
        var left = new Tuple2<string, IReadOnlyList<IReadOnlyList<long>>>("alice", Rows([1], [2, 3]));
        var right = new Tuple2<string, IReadOnlyList<IReadOnlyList<long>>>("alice", Rows([1], [2, 3]));

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Tuple2<string, IReadOnlyList<IReadOnlyList<long>>>("alice", Rows([1], [2, 4]))).Should().BeFalse();
    }

    [Fact]
    public void Tuple2_with_a_dictionary_component_equals_another_decode_of_the_same_content()
    {
        var left = new Tuple2<string, IReadOnlyDictionary<string, long>>("alice", Dictionary(("a", 1), ("b", 2)));
        var right = new Tuple2<string, IReadOnlyDictionary<string, long>>("alice", Dictionary(("b", 2), ("a", 1)));

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Tuple2<string, IReadOnlyDictionary<string, long>>("alice", Dictionary(("a", 1)))).Should().BeFalse();
    }

    [Fact]
    public void Tuple3_with_a_list_component_equals_another_decode_of_the_same_content()
    {
        var left = new Tuple3<string, IReadOnlyList<long>, long>("alice", Longs(1, 2), 3);
        var right = new Tuple3<string, IReadOnlyList<long>, long>("alice", Longs(1, 2), 3);

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Tuple3<string, IReadOnlyList<long>, long>("alice", Longs(1, 2), 4)).Should().BeFalse();
        left.Equals(new Tuple3<string, IReadOnlyList<long>, long>("alice", Longs(1), 3)).Should().BeFalse();
    }

    [Fact]
    public void Tuple4_with_collection_components_equals_another_decode_of_the_same_content()
    {
        var left = new Tuple4<string, IReadOnlyList<long>, IReadOnlyDictionary<string, long>, long>(
            "alice", Longs(1, 2), Dictionary(("a", 1), ("b", 2)), 4);
        var right = new Tuple4<string, IReadOnlyList<long>, IReadOnlyDictionary<string, long>, long>(
            "alice", Longs(1, 2), Dictionary(("b", 2), ("a", 1)), 4);

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Tuple4<string, IReadOnlyList<long>, IReadOnlyDictionary<string, long>, long>(
            "alice", Longs(2, 1), Dictionary(("a", 1), ("b", 2)), 4)).Should().BeFalse();
        left.Equals(new Tuple4<string, IReadOnlyList<long>, IReadOnlyDictionary<string, long>, long>(
            "alice", Longs(1, 2), Dictionary(("a", 1)), 4)).Should().BeFalse();
        left.Equals(new Tuple4<string, IReadOnlyList<long>, IReadOnlyDictionary<string, long>, long>(
            "alice", Longs(1, 2), Dictionary(("a", 1), ("b", 2)), 5)).Should().BeFalse();
    }

    [Fact]
    public void Tuple20_with_a_list_in_its_last_component_equals_another_decode_of_the_same_content()
    {
        static object Tuple20EndingIn(IReadOnlyList<long> last)
        {
            var definition = typeof(Tuple4<,,,>).Assembly.GetType("Daml.Runtime.Stdlib.Tuple20`20")!;
            var typeArguments = Enumerable.Repeat(typeof(long), 19).Append(typeof(IReadOnlyList<long>)).ToArray();
            var components = Enumerable.Range(1, 19).Select(position => (object)(long)position).Append(last).ToArray();
            return Activator.CreateInstance(definition.MakeGenericType(typeArguments), components)!;
        }

        ShouldBeEqualWithEqualHashes(Tuple20EndingIn(Longs(1, 2)), Tuple20EndingIn(Longs(1, 2)));
        Tuple20EndingIn(Longs(1, 2)).Equals(Tuple20EndingIn(Longs(1, 3))).Should().BeFalse();
    }

    [Fact]
    public void Validation_Success_with_a_list_equals_another_decode_of_the_same_content()
    {
        var left = new Validation<string, IReadOnlyList<long>>.Success(Longs(1, 2));
        var right = new Validation<string, IReadOnlyList<long>>.Success(Longs(1, 2));

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Validation<string, IReadOnlyList<long>>.Success(Longs(2, 1))).Should().BeFalse();
    }

    [Fact]
    public void Optional_Some_with_a_list_equals_another_decode_of_the_same_content()
    {
        var left = new Optional<IReadOnlyList<long>>.Some(Longs(1, 2));
        var right = new Optional<IReadOnlyList<long>>.Some(Longs(1, 2));

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Optional<IReadOnlyList<long>>.Some(Longs(1, 3))).Should().BeFalse();
        left.Equals(new Optional<IReadOnlyList<long>>.None()).Should().BeFalse();
    }

    [Fact]
    public void Optional_Some_seen_through_its_base_type_compares_by_content()
    {
        Optional<IReadOnlyList<long>> left = new Optional<IReadOnlyList<long>>.Some(Longs(1, 2));
        Optional<IReadOnlyList<long>> right = new Optional<IReadOnlyList<long>>.Some(Longs(1, 2));

        left.Equals(right).Should().BeTrue();
        left.Equals((object)right).Should().BeTrue();
    }

    [Fact]
    public void Either_Left_with_a_list_equals_another_decode_of_the_same_content()
    {
        var left = new Either<IReadOnlyList<long>, string>.Left(Longs(1, 2));
        var right = new Either<IReadOnlyList<long>, string>.Left(Longs(1, 2));

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Either<IReadOnlyList<long>, string>.Left(Longs(2, 1))).Should().BeFalse();
        left.Equals(new Either<IReadOnlyList<long>, string>.Right("x")).Should().BeFalse();
    }

    [Fact]
    public void Either_Right_with_a_dictionary_equals_another_decode_of_the_same_content()
    {
        var left = new Either<string, IReadOnlyDictionary<string, long>>.Right(Dictionary(("a", 1), ("b", 2)));
        var right = new Either<string, IReadOnlyDictionary<string, long>>.Right(Dictionary(("b", 2), ("a", 1)));

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Either<string, IReadOnlyDictionary<string, long>>.Right(Dictionary(("a", 1), ("b", 3)))).Should().BeFalse();
    }

    [Fact]
    public void Either_arm_seen_through_its_base_type_compares_by_content()
    {
        Either<IReadOnlyList<long>, string> left = new Either<IReadOnlyList<long>, string>.Left(Longs(1, 2));
        Either<IReadOnlyList<long>, string> right = new Either<IReadOnlyList<long>, string>.Left(Longs(1, 2));

        left.Equals(right).Should().BeTrue();
        left.Equals((object)right).Should().BeTrue();
    }

    [Fact]
    public void Map_with_list_values_equals_another_decode_of_the_same_content()
    {
        var left = new Map<string, IReadOnlyList<long>>([new("a", Longs(1, 2)), new("b", Longs(3))]);
        var right = new Map<string, IReadOnlyList<long>>([new("a", Longs(1, 2)), new("b", Longs(3))]);

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Map<string, IReadOnlyList<long>>([new("a", Longs(1, 2)), new("b", Longs(4))])).Should().BeFalse();
    }

    [Fact]
    public void Map_with_list_keys_equals_another_decode_of_the_same_content()
    {
        var left = new Map<IReadOnlyList<long>, string>([new(Longs(1, 2), "x")]);
        var right = new Map<IReadOnlyList<long>, string>([new(Longs(1, 2), "x")]);

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Map<IReadOnlyList<long>, string>([new(Longs(2, 1), "x")])).Should().BeFalse();
    }

    [Fact]
    public void Map_hash_reads_the_values_of_entries_with_reference_keys()
    {
        var left = new Map<string, long>([new("a", 1)]);
        var other = new Map<string, long>([new("a", 2)]);

        left.GetHashCode().Should().NotBe(other.GetHashCode());
    }

    [Fact]
    public void Set_of_lists_collapses_elements_with_equal_content()
    {
        var set = new Set<IReadOnlyList<long>>([Longs(1, 2), Longs(1, 2), Longs(2, 1)]);

        set.Count.Should().Be(2);
        set.Contains(Longs(2, 1)).Should().BeTrue();
    }

    [Fact]
    public void Set_of_lists_equals_another_decode_of_the_same_content_independently_of_order()
    {
        var left = new Set<IReadOnlyList<long>>([Longs(1, 2), Longs(3)]);
        var right = new Set<IReadOnlyList<long>>([Longs(3), Longs(1, 2)]);

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new Set<IReadOnlyList<long>>([Longs(1, 2), Longs(4)])).Should().BeFalse();
    }

    [Fact]
    public void NonEmpty_of_lists_equals_another_decode_of_the_same_content()
    {
        var left = new NonEmpty<IReadOnlyList<long>>(Longs(1), [Longs(2, 3), Longs(4)]);
        var right = new NonEmpty<IReadOnlyList<long>>(Longs(1), [Longs(2, 3), Longs(4)]);

        ShouldBeEqualWithEqualHashes(left, right);
        left.Equals(new NonEmpty<IReadOnlyList<long>>(Longs(1), [Longs(2, 3), Longs(5)])).Should().BeFalse();
        left.Equals(new NonEmpty<IReadOnlyList<long>>(Longs(9), [Longs(2, 3), Longs(4)])).Should().BeFalse();
    }

    [Fact]
    public void DamlFieldCollections_Equal_compares_nested_lists_by_content()
    {
        DamlFieldCollections.Equal(Rows([1, 2], [3]), Rows([1, 2], [3])).Should().BeTrue();
        DamlFieldCollections.Hash(Rows([1, 2], [3])).Should().Be(DamlFieldCollections.Hash(Rows([1, 2], [3])));
        DamlFieldCollections.Equal(Rows([1, 2], [3]), Rows([1, 2], [4])).Should().BeFalse();
    }

    [Fact]
    public void DamlFieldCollections_Equal_compares_dictionary_values_that_are_lists_by_content()
    {
        IReadOnlyDictionary<string, IReadOnlyList<long>> left = new Dictionary<string, IReadOnlyList<long>> { ["k"] = Longs(1) };
        IReadOnlyDictionary<string, IReadOnlyList<long>> right = new Dictionary<string, IReadOnlyList<long>> { ["k"] = Longs(1) };

        DamlFieldCollections.Equal(left, right).Should().BeTrue();
        DamlFieldCollections.Hash(left).Should().Be(DamlFieldCollections.Hash(right));
    }
}
