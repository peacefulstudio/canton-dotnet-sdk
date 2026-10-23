// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class TypedKeyHashCollisionTests
{
    private const int KeyCount = 1_000;
    private const int MinimumDistinctHashes = 990;

    private static IEnumerable<long> KeysWithEqualLowAndHighHalves() =>
        Enumerable.Range(1, KeyCount).Select(k => k * 4_294_967_297L);

    private static IEnumerable<DateTimeOffset> InstantsWhoseTickHalvesAreEqual() =>
        Enumerable.Range(1, KeyCount).Select(k => new DateTimeOffset(k * 42_949_672_970L, TimeSpan.Zero));

    private static DamlRecord SetRecordOf(IEnumerable<DamlValue> elements) =>
        DamlRecord.Create(DamlField.Create(
            "map",
            new DamlGenMap([.. elements.Select(element => (element, (DamlValue)DamlUnit.Instance))])));

    private static int DistinctHashesUnderTheSetsComparer<T>(Set<T> set)
        where T : notnull
    {
        var comparer = set.Elements.Should().BeOfType<HashSet<T>>().Subject.Comparer;
        return set.Elements.Select(element => comparer.GetHashCode(element)).Distinct().Count();
    }

    private static int DistinctHashesOfSetElements<T>(IEnumerable<T> elements)
        where T : notnull
    {
        var set = new Set<T>(elements);
        set.Count.Should().Be(1_000);
        return DistinctHashesUnderTheSetsComparer(set);
    }

    [Fact]
    public void Set_FromRecord_spreads_Int_elements_whose_halves_are_equal()
    {
        var set = Set<long>.FromRecord(
            SetRecordOf(KeysWithEqualLowAndHighHalves().Select(key => (DamlValue)new DamlInt64(key))),
            value => value.As<DamlInt64>().Value);

        set.Count.Should().Be(1_000);
        DistinctHashesUnderTheSetsComparer(set).Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Set_FromRecord_spreads_Numeric_elements_whose_halves_are_equal()
    {
        var set = Set<decimal>.FromRecord(
            SetRecordOf(KeysWithEqualLowAndHighHalves().Select(key => (DamlValue)new DamlNumeric((decimal)key))),
            value => value.As<DamlNumeric>().Value);

        set.Count.Should().Be(1_000);
        DistinctHashesUnderTheSetsComparer(set).Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Set_FromRecord_spreads_Time_elements_whose_tick_halves_are_equal()
    {
        var set = Set<DateTimeOffset>.FromRecord(
            SetRecordOf(InstantsWhoseTickHalvesAreEqual().Select(instant => (DamlValue)new DamlTimestamp(instant))),
            value => value.As<DamlTimestamp>().Value);

        set.Count.Should().Be(1_000);
        DistinctHashesUnderTheSetsComparer(set).Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Tuple2_spreads_Int_components_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => new Tuple2<long, long>(key, key)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Tuple3_spreads_Int_components_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => new Tuple3<string, long, bool>("k", key, true)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void RelTime_spreads_microseconds_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => new RelTime(key)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Either_spreads_Left_and_Right_Int_values_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => (Either<long, string>)new Either<long, string>.Left(key)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => (Either<string, long>)new Either<string, long>.Right(key)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Optional_spreads_Some_Int_values_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => (Optional<long>)new Optional<long>.Some(key)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void NonEmpty_spreads_Int_elements_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => new NonEmpty<long>(1, [key])))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Map_spreads_Int_keys_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => new Map<long, long>([new(key, 7)])))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Set_spreads_nested_Int_sets_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => new Set<long>([key])))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Down_spreads_a_generated_record_over_Int_values_whose_halves_are_equal()
    {
        DistinctHashesOfSetElements(KeysWithEqualLowAndHighHalves().Select(key => new Down<long>(key)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void DamlEqualityComparer_spreads_nullable_Int_values_whose_halves_are_equal()
    {
        KeysWithEqualLowAndHighHalves()
            .Select(key => DamlEqualityComparer<long?>.Default.GetHashCode(key))
            .Distinct().Count()
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void DamlEqualityComparer_gives_equal_Numerics_of_different_scale_equal_hashes()
    {
        var comparer = DamlEqualityComparer<decimal>.Default;

        comparer.Equals(1.5m, 1.50m).Should().BeTrue();
        comparer.GetHashCode(1.50m).Should().Be(comparer.GetHashCode(1.5m));
        comparer.GetHashCode(-0.000m).Should().Be(comparer.GetHashCode(0m));
        comparer.GetHashCode(-1.5m).Should().NotBe(comparer.GetHashCode(1.5m));
    }

    [Fact]
    public void DamlEqualityComparer_gives_one_instant_in_two_offsets_equal_hashes()
    {
        var comparer = DamlEqualityComparer<DateTimeOffset>.Default;
        var utc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var shifted = new DateTimeOffset(2026, 1, 1, 14, 0, 0, TimeSpan.FromHours(2));

        comparer.Equals(utc, shifted).Should().BeTrue();
        comparer.GetHashCode(shifted).Should().Be(comparer.GetHashCode(utc));
    }

    [Fact]
    public void DamlEqualityComparer_compares_other_types_as_the_default_comparer_does()
    {
        var comparer = DamlEqualityComparer<string>.Default;

        comparer.Equals("alice", "alice").Should().BeTrue();
        comparer.Equals("alice", "bob").Should().BeFalse();
        comparer.GetHashCode("alice").Should().Be("alice".GetHashCode(StringComparison.Ordinal));
    }

    [Fact]
    public void DamlFieldCollections_Copy_keys_the_copied_map_by_the_collision_resistant_comparer()
    {
        var source = KeysWithEqualLowAndHighHalves().ToDictionary(key => key, _ => "v");

        var copy = DamlFieldCollections.Copy((IReadOnlyDictionary<long, string>)source);

        var comparer = copy.Should().BeOfType<Dictionary<long, string>>().Subject.Comparer;
        copy.Keys.Select(key => comparer.GetHashCode(key)).Distinct().Count()
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void DamlFieldCollections_Hash_spreads_lists_of_Int_values_whose_halves_are_equal()
    {
        KeysWithEqualLowAndHighHalves()
            .Select(key => DamlFieldCollections.Hash((IReadOnlyList<long>)[key]))
            .Distinct().Count()
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void DamlFieldCollections_Hash_spreads_maps_of_Int_keys_whose_halves_are_equal()
    {
        KeysWithEqualLowAndHighHalves()
            .Select(key => DamlFieldCollections.Hash((IReadOnlyDictionary<long, long>)new Dictionary<long, long> { [key] = key }))
            .Distinct().Count()
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }
}
