// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the guarantee every list member of the event records now carries through its type:
/// <see cref="EquatableArray{T}"/> is never <c>null</c>, compares by content, and owns its
/// elements. A record member of this type cannot be nulled by a constructor argument, a
/// <c>with</c> expression or reflection, so no per-member guard and no per-record equality
/// override is needed to keep that promise.
/// </summary>
public class EquatableArrayTests
{
    private static readonly Party Alice = new("alice");
    private static readonly Party Bob = new("bob");
    private static readonly Party Carol = new("carol");

    [Fact]
    public void Default_has_no_elements()
    {
        EquatableArray<Party> parties = default;

        parties.Count.Should().Be(0);
        parties.Should().BeEmpty();
    }

    [Fact]
    public void Default_equals_Empty_and_hashes_like_it()
    {
        EquatableArray<Party> parties = default;

        (parties == EquatableArray<Party>.Empty).Should().BeTrue();
        parties.Equals(EquatableArray<Party>.Empty).Should().BeTrue();
        parties.GetHashCode().Should().Be(EquatableArray<Party>.Empty.GetHashCode());
    }

    [Fact]
    public void Default_indexer_throws_ArgumentOutOfRangeException()
    {
        EquatableArray<Party> parties = default;

        var act = () => parties[0];

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_from_an_array_holds_the_elements_in_order()
    {
        var parties = EquatableArray.Create(new[] { Alice, Bob });

        parties.Count.Should().Be(2);
        parties[0].Should().Be(Alice);
        parties[1].Should().Be(Bob);
    }

    [Fact]
    public void Create_from_a_list_holds_the_elements_in_order()
    {
        var parties = EquatableArray.Create(new List<Party> { Alice, Bob });

        parties.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void Create_from_an_empty_source_is_the_default_value()
    {
        (EquatableArray.Create(new List<Party>()) == default).Should().BeTrue();
        (EquatableArray.Create(ReadOnlySpan<Party>.Empty) == default).Should().BeTrue();
    }

    [Fact]
    public void CollectionExpression_builds_an_EquatableArray()
    {
        EquatableArray<Party> literal = [Alice, Bob];
        EquatableArray<Party> empty = [];
        EquatableArray<Party> spread = [.. new List<Party> { Bob, Alice }];

        literal.Should().Equal(Alice, Bob);
        (empty == default).Should().BeTrue();
        spread.Should().Equal(Bob, Alice);
    }

    [Fact]
    public void Equals_is_true_for_equal_contents_built_separately()
    {
        EquatableArray<Party> first = [Alice, Bob];
        EquatableArray<Party> second = [Alice, Bob];

        first.Equals(second).Should().BeTrue();
        (first == second).Should().BeTrue();
        (first != second).Should().BeFalse();
        first.Equals((object)second).Should().BeTrue();
    }

    [Fact]
    public void Equals_is_false_when_order_differs()
    {
        EquatableArray<Party> first = [Alice, Bob];
        EquatableArray<Party> second = [Bob, Alice];

        first.Equals(second).Should().BeFalse();
        (first != second).Should().BeTrue();
    }

    [Fact]
    public void Equals_is_false_when_length_differs()
    {
        EquatableArray<Party> first = [Alice, Bob];
        EquatableArray<Party> second = [Alice];

        first.Equals(second).Should().BeFalse();
        second.Equals(default).Should().BeFalse();
    }

    [Fact]
    public void Equals_compares_a_plain_class_element_by_reference_as_EqualityComparer_Default_does()
    {
        var shared = new Opaque();
        EquatableArray<Opaque> first = [shared];
        EquatableArray<Opaque> sameInstance = [shared];
        EquatableArray<Opaque> otherInstance = [new Opaque()];

        first.Equals(sameInstance).Should().BeTrue();
        first.Equals(otherInstance).Should().BeFalse();
    }

    [Fact]
    public void Equals_compares_a_record_element_by_value_as_EqualityComparer_Default_does()
    {
        EquatableArray<Labelled> first = [new Labelled("x")];
        EquatableArray<Labelled> second = [new Labelled("x")];

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_is_equal_for_equal_arrays()
    {
        EquatableArray<Party> first = [Alice, Bob];
        EquatableArray<Party> second = [Alice, Bob];

        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_differs_between_an_array_and_its_prefix()
    {
        EquatableArray<Party> whole = [Alice, Bob];
        EquatableArray<Party> prefix = [Alice];

        whole.GetHashCode().Should().NotBe(prefix.GetHashCode());
    }

    [Fact]
    public void GetHashCode_separates_two_members_by_length_rather_than_by_concatenation()
    {
        EquatableArray<Party> firstLeft = [Alice, Bob];
        EquatableArray<Party> firstRight = [];
        EquatableArray<Party> secondLeft = [Alice];
        EquatableArray<Party> secondRight = [Bob];

        HashCode.Combine(firstLeft, firstRight).Should().NotBe(HashCode.Combine(secondLeft, secondRight));
    }

    [Fact]
    public void IReadOnlyList_view_exposes_Count_indexer_and_enumeration()
    {
        EquatableArray<Party> parties = [Alice, Bob];
        IReadOnlyList<Party> view = parties;

        view.Count.Should().Be(2);
        view[1].Should().Be(Bob);
        view.Select(party => party.Value).Should().Equal("alice", "bob");
        view.ToList().Should().Equal(Alice, Bob);
    }

    [Fact]
    public void Indexer_throws_ArgumentOutOfRangeException_outside_the_array()
    {
        EquatableArray<Party> parties = [Alice];

        var pastTheEnd = () => parties[1];
        var negative = () => parties[-1];

        pastTheEnd.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetEnumerator_is_a_value_type_so_foreach_does_not_allocate()
    {
        var enumeratorType = typeof(EquatableArray<Party>)
            .GetMethod(nameof(EquatableArray<Party>.GetEnumerator), Type.EmptyTypes)!
            .ReturnType;

        enumeratorType.IsValueType.Should().BeTrue();
    }

    [Fact]
    public void Foreach_yields_the_elements_in_order()
    {
        EquatableArray<Party> parties = [Alice, Bob];
        var seen = new List<Party>();

        foreach (var party in parties)
        {
            seen.Add(party);
        }

        seen.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void Create_copies_a_span_so_mutating_the_source_afterwards_changes_nothing()
    {
        var source = new[] { Alice };
        var parties = EquatableArray.Create(source.AsSpan());
        var hashBefore = parties.GetHashCode();

        source[0] = Bob;

        parties.Should().Equal(Alice);
        parties.GetHashCode().Should().Be(hashBefore);
    }

    [Fact]
    public void Create_copies_an_enumerable_so_mutating_the_source_afterwards_changes_nothing()
    {
        var source = new List<Party> { Alice };
        var parties = EquatableArray.Create(source);
        var hashBefore = parties.GetHashCode();

        source.Add(Bob);
        source[0] = Bob;

        parties.Should().Equal(Alice);
        parties.GetHashCode().Should().Be(hashBefore);
    }

    [Fact]
    public void Create_rejects_a_null_enumerable_naming_the_parameter()
    {
        var act = () => EquatableArray.Create<Party>((IEnumerable<Party>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("items");
    }

    [Fact]
    public void Create_from_an_enumerable_rejects_a_null_element_of_a_reference_type_naming_the_parameter()
    {
        var act = () => EquatableArray.Create(new List<string> { "a", null! });

        act.Should().Throw<ArgumentException>().WithParameterName("items");
    }

    [Fact]
    public void Create_from_a_span_rejects_a_null_element_of_a_reference_type_naming_the_parameter()
    {
        var act = () => EquatableArray.Create<string>(["a", null!]);

        act.Should().Throw<ArgumentException>().WithParameterName("items");
    }

    [Fact]
    public void CollectionExpression_rejects_a_null_element_of_a_reference_type()
    {
        var act = () =>
        {
            EquatableArray<Labelled> labels = [new Labelled("x"), null!];
            return labels;
        };

        act.Should().Throw<ArgumentException>().WithParameterName("items");
    }

    [Fact]
    public void Equals_is_false_for_null_and_for_an_object_of_another_type()
    {
        EquatableArray<Party> parties = [Alice];
        EquatableArray<string> names = ["alice"];

        parties.Equals(null).Should().BeFalse();
        parties.Equals("alice").Should().BeFalse();
        parties.Equals(new[] { Alice }).Should().BeFalse();
        parties.Equals(names).Should().BeFalse();
    }

    [Fact]
    public void Enumerator_Reset_through_the_interface_restarts_the_enumeration()
    {
        EquatableArray<Party> parties = [Alice, Bob];
        IEnumerator<Party> enumerator = parties.GetEnumerator();
        enumerator.MoveNext();
        enumerator.MoveNext();
        enumerator.MoveNext().Should().BeFalse();

        enumerator.Reset();

        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Should().Be(Alice);
    }

    [Fact]
    public void Enumerator_Reset_is_reachable_only_through_IEnumerator()
    {
        typeof(EquatableArray<Party>.Enumerator).GetMethod("Reset").Should().BeNull();
    }

    [Fact]
    public void Enumerator_default_Reset_through_the_interface_leaves_it_empty()
    {
        IEnumerator<Party> enumerator = default(EquatableArray<Party>.Enumerator);

        enumerator.Reset();

        enumerator.MoveNext().Should().BeFalse();
    }

    [Fact]
    public void IEnumerable_view_enumerates_the_elements_in_order()
    {
        EquatableArray<Party> parties = [Alice, Bob];
        System.Collections.IEnumerable view = parties;

        view.Cast<Party>().Should().Equal(Alice, Bob);
    }

    [Fact]
    public void Create_enumerates_a_sequence_exactly_once()
    {
        var enumerations = 0;
        IEnumerable<Party> Source()
        {
            enumerations++;
            yield return Alice;
            yield return Bob;
        }

        var parties = EquatableArray.Create(Source());

        parties.Should().Equal(Alice, Bob);
        enumerations.Should().Be(1);
    }

    [Fact]
    public void Create_rejects_a_null_array_naming_the_parameter()
    {
        var act = () => EquatableArray.Create((Party[])null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("items");
    }

    [Fact]
    public void Enumerator_default_enumerates_as_empty_instead_of_throwing()
    {
        var enumerator = default(EquatableArray<Party>.Enumerator);
        var seen = new List<Party>();

        while (enumerator.MoveNext())
        {
            seen.Add(enumerator.Current);
        }

        seen.Should().BeEmpty();
    }

    [Fact]
    public void EquatableArray_operator_equality_and_inequality_follow_content()
    {
        EquatableArray<Party> parties = [Alice, Bob];
        EquatableArray<Party> sameContent = [Alice, Bob];
        EquatableArray<Party> otherContent = [Bob];

        (parties == sameContent).Should().BeTrue();
        (parties != sameContent).Should().BeFalse();
        (parties == otherContent).Should().BeFalse();
        (parties != otherContent).Should().BeTrue();
        (parties == default).Should().BeFalse();
        (parties != default).Should().BeTrue();
        (default(EquatableArray<Party>) == EquatableArray<Party>.Empty).Should().BeTrue();
        (default(EquatableArray<Party>) != EquatableArray<Party>.Empty).Should().BeFalse();
    }

    [Fact]
    public void EquatableArray_nested_in_itself_compares_and_hashes_by_content()
    {
        EquatableArray<EquatableArray<Party>> first = [[Alice], [Bob]];
        EquatableArray<EquatableArray<Party>> second = [[Alice], [Bob]];
        EquatableArray<EquatableArray<Party>> regrouped = [[Alice, Bob]];

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Equals(regrouped).Should().BeFalse();
        first.GetHashCode().Should().NotBe(regrouped.GetHashCode());
    }

    [Fact]
    public void ToString_renders_the_elements_between_brackets()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        parties.ToString().Should().Be("[alice, bob]");
    }

    [Fact]
    public void ToString_renders_a_single_element_between_brackets()
    {
        EquatableArray<Party> parties = [Alice];

        parties.ToString().Should().Be("[alice]");
    }

    [Fact]
    public void ToString_renders_the_default_value_and_Empty_as_empty_brackets()
    {
        default(EquatableArray<Party>).ToString().Should().Be("[]");
        EquatableArray<Party>.Empty.ToString().Should().Be("[]");
    }

    [Fact]
    public void ToString_renders_a_null_element_of_a_nullable_value_type_as_null()
    {
        EquatableArray<int?> numbers = [1, null];

        numbers.ToString().Should().Be("[1, null]");
    }

    [Fact]
    public void AsSpan_reads_the_elements_in_order()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var span = parties.AsSpan();

        span.Length.Should().Be(2);
        span[0].Should().Be(Alice);
        span[1].Should().Be(Bob);
    }

    [Fact]
    public void AsSpan_hands_out_the_stored_elements_rather_than_a_copy()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        ref readonly var first = ref MemoryMarshal.GetReference(parties.AsSpan());
        ref readonly var second = ref MemoryMarshal.GetReference(parties.AsSpan());

        Unsafe.AreSame(in first, in second).Should().BeTrue();
    }

    [Fact]
    public void AsSpan_on_the_default_value_and_on_Empty_is_an_empty_span()
    {
        default(EquatableArray<Party>).AsSpan().IsEmpty.Should().BeTrue();
        EquatableArray<Party>.Empty.AsSpan().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ToArray_returns_a_fresh_array_the_caller_may_mutate()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var copy = parties.ToArray();
        copy[0] = Bob;

        parties.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void ToArray_on_the_default_value_and_on_Empty_returns_the_shared_empty_array()
    {
        default(EquatableArray<Party>).ToArray().Should().BeSameAs(Array.Empty<Party>());
        EquatableArray<Party>.Empty.ToArray().Should().BeSameAs(Array.Empty<Party>());
    }

    [Fact]
    public void CopyTo_writes_the_elements_starting_at_the_destination_index()
    {
        EquatableArray<Party> parties = [Alice, Bob];
        var destination = new Party[3];

        parties.CopyTo(destination, 1);

        destination.Should().Equal(default, Alice, Bob);
    }

    [Fact]
    public void CopyTo_from_the_default_value_writes_nothing()
    {
        var destination = new[] { Alice };

        default(EquatableArray<Party>).CopyTo(destination, 0);

        destination.Should().Equal(Alice);
    }

    [Fact]
    public void CopyTo_rejects_a_null_destination_naming_the_parameter()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var act = () => parties.CopyTo(null!, 0);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("array");
    }

    [Fact]
    public void CopyTo_rejects_a_negative_index_naming_the_parameter()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var act = () => parties.CopyTo(new Party[2], -1);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("arrayIndex");
    }

    [Fact]
    public void CopyTo_rejects_an_index_past_the_end_of_the_destination_naming_the_parameter()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var act = () => parties.CopyTo(new Party[2], 5);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("arrayIndex");
    }

    [Fact]
    public void CopyTo_rejects_a_destination_with_too_little_room_left_naming_the_parameter()
    {
        EquatableArray<Party> parties = [Alice, Bob];

        var act = () => parties.CopyTo(new Party[2], 1);

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("array");
    }

    [Fact]
    public void IsEmpty_is_true_for_the_default_value_and_Empty_and_false_for_a_populated_array()
    {
        EquatableArray<Party> parties = [Alice];

        default(EquatableArray<Party>).IsEmpty.Should().BeTrue();
        EquatableArray<Party>.Empty.IsEmpty.Should().BeTrue();
        parties.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IndexOf_returns_the_first_match_and_minus_one_for_an_absent_element()
    {
        EquatableArray<Party> parties = [Alice, Bob, Alice];

        parties.IndexOf(Alice).Should().Be(0);
        parties.IndexOf(Bob).Should().Be(1);
        parties.IndexOf(new Party("carol")).Should().Be(-1);
        default(EquatableArray<Party>).IndexOf(Alice).Should().Be(-1);
    }

    [Fact]
    public void IndexOf_and_Contains_find_a_null_element_of_a_nullable_value_type()
    {
        EquatableArray<int?> numbers = [1, null];

        numbers.IndexOf(null).Should().Be(1);
        numbers.Contains(null).Should().BeTrue();
    }

    [Fact]
    public void IndexOf_and_Contains_report_no_match_for_a_null_reference_argument()
    {
        EquatableArray<string> texts = ["a"];

        texts.IndexOf(null!).Should().Be(-1);
        texts.Contains(null!).Should().BeFalse();
    }

    [Fact]
    public void Contains_finds_an_element_through_EqualityComparer_Default()
    {
        EquatableArray<Labelled> labels = [new Labelled("x")];

        labels.Contains(new Labelled("x")).Should().BeTrue();
        labels.Contains(new Labelled("y")).Should().BeFalse();
        default(EquatableArray<Labelled>).Contains(new Labelled("x")).Should().BeFalse();
    }

    [Fact]
    public void ICollection_view_reports_the_count_and_the_elements_in_order()
    {
        EquatableArray<Party> parties = [Alice, Bob];
        ICollection<Party> view = parties;

        view.Count.Should().Be(2);
        view.ToArray().Should().Equal(Alice, Bob);
        view.ToList().Should().Equal(Alice, Bob);
    }

    [Fact]
    public void ICollection_view_is_read_only_and_refuses_every_mutation()
    {
        EquatableArray<Party> parties = [Alice];
        ICollection<Party> view = parties;

        var add = () => view.Add(Bob);
        var clear = () => view.Clear();
        var remove = () => view.Remove(Alice);

        view.IsReadOnly.Should().BeTrue();
        add.Should().Throw<NotSupportedException>();
        clear.Should().Throw<NotSupportedException>();
        remove.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ICollection_mutators_are_reachable_only_through_ICollection()
    {
        typeof(EquatableArray<Party>).GetMethod("Add").Should().BeNull();
        typeof(EquatableArray<Party>).GetMethod("Clear").Should().BeNull();
        typeof(EquatableArray<Party>).GetMethod("Remove").Should().BeNull();
    }

    [Fact]
    public void IList_view_exposes_Count_the_indexer_and_IndexOf()
    {
        EquatableArray<Party> parties = [Alice, Bob];
        IList<Party> view = parties;

        view.Count.Should().Be(2);
        view[1].Should().Be(Bob);
        view.IndexOf(Bob).Should().Be(1);
        view.IndexOf(Carol).Should().Be(-1);
    }

    [Fact]
    public void IList_view_is_read_only_and_refuses_every_mutation()
    {
        EquatableArray<Party> parties = [Alice];
        IList<Party> view = parties;

        var assign = () => view[0] = Bob;
        var insert = () => view.Insert(0, Bob);
        var removeAt = () => view.RemoveAt(0);

        assign.Should().Throw<NotSupportedException>();
        insert.Should().Throw<NotSupportedException>();
        removeAt.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void IList_mutators_are_reachable_only_through_IList()
    {
        typeof(EquatableArray<Party>).GetMethod("Insert").Should().BeNull();
        typeof(EquatableArray<Party>).GetMethod("RemoveAt").Should().BeNull();
        typeof(EquatableArray<Party>).GetProperties()
            .Should().ContainSingle(property => property.Name == "Item")
            .Which.SetMethod.Should().BeNull();
    }

    [Fact]
    public void Linq_operators_that_probe_IList_agree_with_the_walk_they_replace()
    {
        EquatableArray<Party> parties = [Alice, Bob, Carol];
        IEnumerable<Party> indexed = parties;
        var walked = Walk(parties);

        indexed.First().Should().Be(walked.First()).And.Be(Alice);
        indexed.Last().Should().Be(walked.Last()).And.Be(Carol);
        indexed.ElementAt(1).Should().Be(walked.ElementAt(1)).And.Be(Bob);
        indexed.ElementAtOrDefault(1).Should().Be(walked.ElementAtOrDefault(1)).And.Be(Bob);
        indexed.ElementAtOrDefault(9).Should().Be(walked.ElementAtOrDefault(9)).And.Be(default(Party));
        indexed.Skip(2).Should().Equal(walked.Skip(2)).And.Equal(Carol);
        indexed.LastOrDefault().Should().Be(walked.LastOrDefault()).And.Be(Carol);
    }

    [Fact]
    public void Linq_operators_that_probe_IList_agree_with_the_walk_on_an_empty_array()
    {
        EquatableArray<Party> parties = default;
        IEnumerable<Party> indexed = parties;
        var walked = Walk(parties);

        indexed.LastOrDefault().Should().Be(walked.LastOrDefault()).And.Be(default(Party));
        indexed.ElementAtOrDefault(0).Should().Be(walked.ElementAtOrDefault(0)).And.Be(default(Party));
        indexed.Skip(1).Should().Equal(walked.Skip(1)).And.BeEmpty();

        var indexedLast = () => indexed.Last();
        var walkedLast = () => walked.Last();
        var indexedFirst = () => indexed.First();
        var walkedFirst = () => walked.First();
        indexedLast.Should().Throw<InvalidOperationException>();
        walkedLast.Should().Throw<InvalidOperationException>();
        indexedFirst.Should().Throw<InvalidOperationException>();
        walkedFirst.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Linq_SequenceEqual_agrees_with_the_walk_it_replaces_on_both_operands()
    {
        EquatableArray<Party> parties = [Alice, Bob, Carol];
        EquatableArray<Party> sameContent = [Alice, Bob, Carol];
        EquatableArray<Party> reordered = [Alice, Carol, Bob];
        EquatableArray<Party> prefix = [Alice, Bob];
        IEnumerable<Party> indexed = parties;
        var walked = Walk(parties);

        indexed.SequenceEqual(sameContent).Should()
            .Be(walked.SequenceEqual(Walk(sameContent))).And.BeTrue();
        indexed.SequenceEqual(reordered).Should()
            .Be(walked.SequenceEqual(Walk(reordered))).And.BeFalse();
        indexed.SequenceEqual(prefix).Should()
            .Be(walked.SequenceEqual(Walk(prefix))).And.BeFalse();
        indexed.SequenceEqual(default(EquatableArray<Party>)).Should()
            .Be(walked.SequenceEqual(Walk(default))).And.BeFalse();
    }

    [Fact]
    public void Linq_Take_agrees_with_the_walk_it_replaces()
    {
        EquatableArray<Party> parties = [Alice, Bob, Carol];
        IEnumerable<Party> indexed = parties;
        var walked = Walk(parties);

        indexed.Take(2).Should().Equal(walked.Take(2)).And.Equal(Alice, Bob);
        indexed.Take(0).Should().Equal(walked.Take(0)).And.BeEmpty();
        indexed.Take(9).Should().Equal(walked.Take(9)).And.Equal(Alice, Bob, Carol);
        indexed.Take(..2).Should().Equal(walked.Take(..2)).And.Equal(Alice, Bob);
        indexed.Take(^2..).Should().Equal(walked.Take(^2..)).And.Equal(Bob, Carol);
        indexed.Take(1..^1).Should().Equal(walked.Take(1..^1)).And.Equal(Bob);
        indexed.Take(^9..).Should().Equal(walked.Take(^9..)).And.Equal(Alice, Bob, Carol);
    }

    [Fact]
    public void Linq_SkipLast_and_TakeLast_agree_with_the_walk_they_replace()
    {
        EquatableArray<Party> parties = [Alice, Bob, Carol];
        IEnumerable<Party> indexed = parties;
        var walked = Walk(parties);

        indexed.SkipLast(1).Should().Equal(walked.SkipLast(1)).And.Equal(Alice, Bob);
        indexed.SkipLast(9).Should().Equal(walked.SkipLast(9)).And.BeEmpty();
        indexed.TakeLast(2).Should().Equal(walked.TakeLast(2)).And.Equal(Bob, Carol);
        indexed.TakeLast(9).Should().Equal(walked.TakeLast(9)).And.Equal(Alice, Bob, Carol);
    }

    [Fact]
    public void Linq_Select_specializations_agree_with_the_walk_they_replace()
    {
        EquatableArray<Party> parties = [Alice, Bob, Carol];
        IEnumerable<Party> indexed = parties;
        var walked = Walk(parties);

        indexed.Select(party => party.Value).ToArray().Should()
            .Equal(walked.Select(party => party.Value).ToArray()).And.Equal("alice", "bob", "carol");
        indexed.Select(party => party.Value).Count().Should()
            .Be(walked.Select(party => party.Value).Count()).And.Be(3);
        indexed.Select(party => party.Value).Last().Should()
            .Be(walked.Select(party => party.Value).Last()).And.Be("carol");
    }

    [Fact]
    public void Linq_range_and_projection_operators_agree_with_the_walk_on_an_empty_array()
    {
        EquatableArray<Party> parties = default;
        IEnumerable<Party> indexed = parties;
        var walked = Walk(parties);

        indexed.Take(2).Should().Equal(walked.Take(2)).And.BeEmpty();
        indexed.Take(^2..).Should().Equal(walked.Take(^2..)).And.BeEmpty();
        indexed.SkipLast(1).Should().Equal(walked.SkipLast(1)).And.BeEmpty();
        indexed.TakeLast(1).Should().Equal(walked.TakeLast(1)).And.BeEmpty();
        indexed.SequenceEqual(default(EquatableArray<Party>)).Should()
            .Be(walked.SequenceEqual(Walk(default))).And.BeTrue();
        indexed.Select(party => party.Value).ToArray().Should()
            .Equal(walked.Select(party => party.Value).ToArray()).And.BeEmpty();
        indexed.Select(party => party.Value).Count().Should()
            .Be(walked.Select(party => party.Value).Count()).And.Be(0);
    }

    [Fact]
    public void ElementAt_outside_the_array_throws_the_same_way_indexed_and_walked()
    {
        EquatableArray<Party> parties = [Alice];
        IEnumerable<Party> indexed = parties;
        var walked = Walk(parties);

        var indexedPastTheEnd = () => indexed.ElementAt(1);
        var walkedPastTheEnd = () => walked.ElementAt(1);
        var indexedNegative = () => indexed.ElementAt(-1);
        var walkedNegative = () => walked.ElementAt(-1);

        indexedPastTheEnd.Should().Throw<ArgumentOutOfRangeException>();
        walkedPastTheEnd.Should().Throw<ArgumentOutOfRangeException>();
        indexedNegative.Should().Throw<ArgumentOutOfRangeException>();
        walkedNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static IEnumerable<Party> Walk(EquatableArray<Party> parties)
    {
        foreach (var party in parties)
        {
            yield return party;
        }
    }

    private sealed class Opaque
    {
    }

    private sealed record Labelled(string Name);
}
