// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlFieldCollectionsTests
{
    [Fact]
    public void Copy_of_a_list_does_not_see_a_later_change_to_the_source()
    {
        var source = new List<string> { "a" };

        var copy = DamlFieldCollections.Copy((IReadOnlyList<string>)source);
        source.Add("b");

        copy.Should().Equal("a");
    }

    [Fact]
    public void Copy_of_a_dictionary_does_not_see_a_later_change_to_the_source()
    {
        var source = new Dictionary<string, string> { ["k"] = "v" };

        var copy = DamlFieldCollections.Copy((IReadOnlyDictionary<string, string>)source);
        source["k2"] = "v2";

        copy.Should().ContainSingle().Which.Key.Should().Be("k");
    }

    [Fact]
    public void Copy_of_a_dictionary_cannot_be_downcast_to_a_mutable_Dictionary()
    {
        var copy = DamlFieldCollections.Copy((IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["k"] = "v" });

        copy.Should().NotBeAssignableTo<Dictionary<string, string>>();
    }

    [Fact]
    public void Copy_of_a_dictionary_refuses_an_entry_added_through_its_dictionary_interface()
    {
        var copy = DamlFieldCollections.Copy((IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["k"] = "v" });

        var act = () => ((IDictionary<string, string>)copy)["added"] = "x";

        act.Should().ThrowExactly<NotSupportedException>();
        copy.Should().ContainSingle().Which.Key.Should().Be("k");
    }

    [Fact]
    public void Copy_of_a_list_refuses_an_element_written_through_its_list_interface()
    {
        var copy = DamlFieldCollections.Copy((IReadOnlyList<string>)new List<string> { "a" });

        var act = () => ((IList<string>)copy)[0] = "b";

        copy.Should().NotBeAssignableTo<string[]>();
        act.Should().ThrowExactly<NotSupportedException>();
        copy.Should().Equal("a");
    }

    [Fact]
    public void Copy_passes_null_through()
    {
        DamlFieldCollections.Copy((IReadOnlyList<string>?)null).Should().BeNull();
        DamlFieldCollections.Copy((IReadOnlyDictionary<string, string>?)null).Should().BeNull();
    }

    [Fact]
    public void Copy_of_an_empty_collection_returns_an_empty_independent_instance()
    {
        DamlFieldCollections.Copy((IReadOnlyList<string>)[]).Should().BeEmpty().And.NotBeAssignableTo<Array>();
        DamlFieldCollections.Copy((IReadOnlyDictionary<string, string>)new Dictionary<string, string>()).Should().BeEmpty();
    }

    [Fact]
    public void Equal_reads_lists_element_by_element_in_order()
    {
        IReadOnlyList<string> left = ["a", "b"];

        DamlFieldCollections.Equal(left, ["a", "b"]).Should().BeTrue();
        DamlFieldCollections.Equal(left, ["b", "a"]).Should().BeFalse();
        DamlFieldCollections.Equal(left, ["a"]).Should().BeFalse();
    }

    [Fact]
    public void Equal_reads_dictionaries_independently_of_insertion_order()
    {
        IReadOnlyDictionary<string, string> left = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" };
        IReadOnlyDictionary<string, string> reordered = new Dictionary<string, string> { ["k2"] = "v2", ["k1"] = "v1" };

        DamlFieldCollections.Equal(left, reordered).Should().BeTrue();
    }

    [Fact]
    public void Equal_separates_a_differing_value_from_a_differing_key()
    {
        IReadOnlyDictionary<string, string> left = new Dictionary<string, string> { ["k"] = "v" };

        DamlFieldCollections.Equal(left, new Dictionary<string, string> { ["k"] = "other" }).Should().BeFalse();
        DamlFieldCollections.Equal(left, new Dictionary<string, string> { ["other"] = "v" }).Should().BeFalse();
    }

    [Fact]
    public void Equal_treats_two_nulls_as_equal_and_one_null_as_different()
    {
        DamlFieldCollections.Equal((IReadOnlyList<string>?)null, null).Should().BeTrue();
        DamlFieldCollections.Equal(null, (IReadOnlyList<string>?)[]).Should().BeFalse();
        DamlFieldCollections.Equal((IReadOnlyDictionary<string, string>?)null, null).Should().BeTrue();
    }

    [Fact]
    public void Hash_agrees_with_Equal_on_lists()
    {
        DamlFieldCollections.Hash<string>(["a", "b"])
            .Should().Be(DamlFieldCollections.Hash<string>(["a", "b"]))
            .And.NotBe(DamlFieldCollections.Hash<string>(["b", "a"]));
    }

    [Fact]
    public void Hash_agrees_with_Equal_on_dictionaries_regardless_of_insertion_order()
    {
        var hash = DamlFieldCollections.Hash(new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" });

        hash.Should().Be(DamlFieldCollections.Hash(new Dictionary<string, string> { ["k2"] = "v2", ["k1"] = "v1" }));
    }

    [Fact]
    public void Hash_of_null_is_zero()
    {
        DamlFieldCollections.Hash((IReadOnlyList<string>?)null).Should().Be(0);
        DamlFieldCollections.Hash((IReadOnlyDictionary<string, string>?)null).Should().Be(0);
    }
}
