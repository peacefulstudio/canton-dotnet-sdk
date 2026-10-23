// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Holds the corpus bindings to hash keys whose two 32-bit halves are equal, such as every multiple
/// of <c>4294967297</c>, into distinct hash codes: the framework's own <c>long</c> hash maps all of
/// them to <c>0</c>, so a dictionary or set keyed by them under that hash decodes in quadratic time.
/// </summary>
public class GeneratedKeyHashCollisionTests
{
    private const int KeyCount = 1_000;
    private const int MinimumDistinctHashes = 990;

    private static IEnumerable<long> KeysWithEqualLowAndHighHalves() =>
        Enumerable.Range(1, KeyCount).Select(k => k * 4_294_967_297L);

    [Fact]
    public void TypeCorners_FromRecord_spreads_Int_genmap_keys_whose_halves_are_equal()
    {
        var crafted = new DamlGenMap(
            [.. KeysWithEqualLowAndHighHalves().Select(key => ((DamlValue)new DamlInt64(key), (DamlValue)new DamlText("v")))]);
        var record = GeneratedStjSampleTable.TypeCornersSample().ToRecord();
        var payload = DamlRecord.Create(
            [.. record.Fields.Select(field => field.Label == "labelByRank" ? DamlField.Create("labelByRank", crafted) : field)]);

        var labelByRank = TypeCorners.FromRecord(payload).LabelByRank;

        var comparer = labelByRank.Should().BeOfType<Dictionary<long, string>>().Subject.Comparer;
        labelByRank.Should().HaveCount(1_000);
        labelByRank.Keys.Select(key => comparer.GetHashCode(key)).Distinct().Count()
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void Profile_spreads_records_whose_Int_member_halves_are_equal()
    {
        KeysWithEqualLowAndHighHalves()
            .Select(key => Profile.FromRecord(new Profile("ace", key).ToRecord()).GetHashCode())
            .Distinct().Count()
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }
}
