// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class CollectionBackingStoreDowncastTests
{
    public static TheoryData<string, Func<object>, Type> PublicCollectionsAndTheirFormerBackingStores() => new()
    {
        { "Set<string>.Elements", () => new Set<string>(["a", "b"]).Elements, typeof(HashSet<string>) },
        { "Map<string, string>.Entries", () => SampleMap().Entries, typeof(KeyValuePair<string, string>[]) },
        { "NonEmpty<string>.Tl", () => SampleNonEmpty().Tl, typeof(string[]) },
        { "DamlError.Metadata", () => SampleError().Metadata, typeof(Dictionary<string, string>) },
        { "CommandsSubmission.Commands", () => SampleSubmission().Commands, typeof(ICommand[]) },
        { "DamlRecord.Fields", () => SampleRecord().Fields, typeof(DamlField[]) },
    };

    public static TheoryData<string, Func<object>> EmptyPublicCollections() => new()
    {
        { "Set<string>.Elements", () => new Set<string>([]).Elements },
        { "Map<string, string>.Entries", () => new Map<string, string>([]).Entries },
        { "NonEmpty<string>.Tl", () => new NonEmpty<string>("a", []).Tl },
        { "DamlError.Metadata", () => SampleError(new Dictionary<string, string>()).Metadata },
        { "CommandsSubmission.Commands", () => new CommandsSubmission([]).Commands },
        { "DamlRecord.Fields", () => DamlRecord.Create().Fields },
    };

    [Theory]
    [MemberData(nameof(PublicCollectionsAndTheirFormerBackingStores))]
    public void PublicCollection_cannot_be_downcast_to_a_mutable_backing_store(
        string member, Func<object> read, Type formerBackingStore)
    {
        var collection = read();

        collection.Should().NotBeAssignableTo(formerBackingStore, "{0} must not hand out its backing store", member);
        collection.Should().NotBeAssignableTo<Array>("{0} must not hand out an array", member);
    }

    [Theory]
    [MemberData(nameof(EmptyPublicCollections))]
    public void EmptyPublicCollection_is_not_an_array(string member, Func<object> read)
    {
        read().Should().NotBeAssignableTo<Array>("{0} must not hand out an array, empty or not", member);
    }

    [Fact]
    public void Set_Elements_refuses_a_null_added_through_its_collection_interface()
    {
        var set = new Set<string>(["a"]);

        var act = () => ((ICollection<string>)set.Elements).Add(null!);

        act.Should().ThrowExactly<NotSupportedException>();
        set.Elements.Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void NonEmpty_Tl_refuses_a_null_written_through_its_list_interface()
    {
        var nonEmpty = SampleNonEmpty();

        var act = () => ((IList<string>)nonEmpty.Tl)[0] = null!;

        act.Should().ThrowExactly<NotSupportedException>();
        nonEmpty.Tl.Should().Equal("b");
    }

    [Fact]
    public void Map_Entries_refuses_a_null_value_written_through_its_list_interface()
    {
        var map = SampleMap();

        var act = () => ((IList<KeyValuePair<string, string>>)map.Entries)[0] = new("k", null!);

        act.Should().ThrowExactly<NotSupportedException>();
        map.Entries.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("k", "v"));
    }

    [Fact]
    public void DamlError_Metadata_refuses_an_entry_added_through_its_dictionary_interface()
    {
        var error = SampleError();

        var act = () => ((IDictionary<string, string>)error.Metadata)["added"] = "x";

        act.Should().ThrowExactly<NotSupportedException>();
        error.Metadata.Should().ContainSingle().Which.Key.Should().Be("category");
    }

    private static Map<string, string> SampleMap() => new([new KeyValuePair<string, string>("k", "v")]);

    private static NonEmpty<string> SampleNonEmpty() => new("a", ["b"]);

    private static ExerciseOutcome<long>.DamlError SampleError(Dictionary<string, string>? metadata = null) =>
        new(
            DamlErrorCategory.ContentionOnSharedResources,
            "LOCAL_VERDICT_LOCKED_CONTRACTS",
            "contract is locked",
            metadata ?? new Dictionary<string, string> { ["category"] = "10" });

    private static CommandsSubmission SampleSubmission() =>
        new([new CreateCommand(new Identifier("pkg", "Module", "Template"), DamlRecord.Create())]);

    private static DamlRecord SampleRecord() => DamlRecord.Create(new DamlField("owner", new DamlParty("alice")));
}
