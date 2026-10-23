// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Holds the emitted value semantics of a generated record's <c>List</c> and map fields
/// against the corpus types that carry them: <c>RichRecord</c> (a <c>[Text]</c> and a
/// <c>TextMap Text</c>) and <c>TypeCorners</c> (two <c>Map</c>s). Record-synthesized
/// equality would read each of these members by reference.
/// </summary>
public class GeneratedCollectionFieldValueSemanticsTests
{
    private static RichRecord Sample(
        IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, string>? attributes = null) => new(
        Owner: new Party("alice"),
        Count: 42,
        Amount: 19.95m,
        Label: "first",
        Active: true,
        AsOf: new DateOnly(2026, 6, 4),
        ObservedAt: new DateTimeOffset(2026, 6, 4, 12, 30, 0, TimeSpan.Zero),
        Note: "hello",
        NestedNote: new Optional<Optional<string>>.None(),
        Tags: tags ?? ["a", "b"],
        Attributes: attributes ?? new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
        GenMap: new Dictionary<Party, long>(),
        UnitField: DamlUnit.Instance,
        Marker: new ContractId<Marker>("marker-cid"),
        HoldingCid: new ContractId<IHolding>("00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"),
        HoldingCids: [new ContractId<IHolding>("0011112222333344445555666677778888999900001111222233334444555566aa")],
        Profile: new Profile("ace", 7),
        Outcome: new Outcome.Win(new Outcome_Win(Prize: 12.34m, Tier: "gold")),
        Suit: Suit.Hearts,
        Fee: 1.5m);

    [Fact]
    public void RichRecord_equals_another_built_from_separate_but_equal_collections()
    {
        Sample().Should().Be(Sample());
        Sample().GetHashCode().Should().Be(Sample().GetHashCode());
    }

    [Fact]
    public void FromRecord_returns_a_value_equal_to_the_one_ToRecord_encoded()
    {
        var original = Sample();

        RichRecord.FromRecord(original.ToRecord()).Should().Be(original);
    }

    [Fact]
    public void RichRecord_compares_a_map_field_independently_of_insertion_order()
    {
        var forward = Sample(attributes: new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" });
        var reversed = Sample(attributes: new Dictionary<string, string> { ["k2"] = "v2", ["k1"] = "v1" });

        forward.Should().Be(reversed);
        forward.GetHashCode().Should().Be(reversed.GetHashCode());
    }

    [Fact]
    public void RichRecord_compares_a_list_field_in_order()
    {
        Sample(tags: ["a", "b"]).Should().NotBe(Sample(tags: ["b", "a"]));
    }

    [Fact]
    public void RichRecord_is_unequal_when_one_list_element_differs()
    {
        Sample(tags: ["a", "b"]).Should().NotBe(Sample(tags: ["a", "c"]));
    }

    [Fact]
    public void RichRecord_is_findable_by_content_in_a_HashSet()
    {
        var set = new HashSet<RichRecord> { Sample() };

        set.Should().Contain(Sample());
    }

    [Fact]
    public void RichRecord_ignores_a_change_to_the_collections_it_was_constructed_from()
    {
        var tags = new List<string> { "a", "b" };
        var attributes = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" };

        var value = Sample(tags, attributes);
        tags.Add("c");
        attributes["k3"] = "v3";

        value.Tags.Should().Equal("a", "b");
        value.Attributes.Should().HaveCount(2);
        value.Should().Be(Sample());
    }

    [Fact]
    public void RichRecord_copies_the_collection_a_with_expression_supplies()
    {
        var replacement = new List<string> { "x" };

        var value = Sample() with { Tags = replacement };
        replacement.Add("y");

        value.Tags.Should().Equal("x");
    }

    [Fact]
    public void TypeCorners_compares_its_GenMap_fields_by_content()
    {
        var quotas = new Dictionary<Party, long> { [new Party("alice")] = 1 };
        var labels = new Dictionary<long, string> { [1] = "one" };

        TypeCornersWith(quotas, labels).Should().Be(TypeCornersWith(
            new Dictionary<Party, long> { [new Party("alice")] = 1 },
            new Dictionary<long, string> { [1] = "one" }));
    }

    [Fact]
    public void DamlFieldAttribute_survives_on_the_redeclared_collection_properties()
    {
        DamlFieldName(typeof(RichRecord), nameof(RichRecord.Tags)).Should().Be("tags");
        DamlFieldName(typeof(RichRecord), nameof(RichRecord.Attributes)).Should().Be("attributes");
        DamlFieldName(typeof(TypeCorners), nameof(TypeCorners.QuotaByParty)).Should().Be("quotaByParty");
    }

    private static string? DamlFieldName(Type declaringType, string propertyName) =>
        declaringType.GetProperty(propertyName)!.GetCustomAttribute<DamlFieldAttribute>()?.Name;

    private static TypeCorners TypeCornersWith(
        IReadOnlyDictionary<Party, long> quotaByParty,
        IReadOnlyDictionary<long, string> labelByRank) => new(
        Owner: new Party("alice"),
        BoxedText: new Box<string>("boxed"),
        BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
        Slot: new Slot<long>.Filled(11),
        NestedNote: null,
        MaybeMaybeNote: new Optional<Optional<string>>.None(),
        Crate: new Crate<string>(new Optional<string>.Some("crated")),
        QuotaByParty: quotaByParty,
        LabelByRank: labelByRank,
        RankOrLabel: new Either<long, string>.Right("runner-up"),
        NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
        Pair: new Tuple2<string, long>("pair", 3),
        Triple: new Tuple3<string, long, bool>("triple", 4, true),
        Branch: new Branch("root", []),
        Whole: 42m,
        Finest: 0.5m);
}
