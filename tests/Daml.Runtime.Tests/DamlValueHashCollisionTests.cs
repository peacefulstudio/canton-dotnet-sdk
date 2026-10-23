// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlValueHashCollisionTests
{
    private const int KeyCount = 5_000;
    private const int MinimumDistinctHashes = 4_950;

    private static IEnumerable<long> KeysWithEqualLowAndHighHalves() =>
        Enumerable.Range(1, KeyCount).Select(k => k * 4_294_967_297L);

    private static int DistinctHashCount<T>(IEnumerable<T> values) =>
        values.Select(value => value!.GetHashCode()).Distinct().Count();

    [Fact]
    public void DamlInt64_spreads_keys_whose_low_and_high_halves_are_equal()
    {
        DistinctHashCount(KeysWithEqualLowAndHighHalves().Select(key => new DamlInt64(key)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void DamlInt64_spreads_keys_whose_halves_xor_to_the_same_value()
    {
        var keys = Enumerable.Range(1, KeyCount).Select(k => ((long)(k + 7) << 32) | (uint)(k + 7) ^ 0x1234L);
        DistinctHashCount(keys.Select(key => new DamlInt64(key)))
            .Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void DamlTimestamp_spreads_ticks_whose_low_and_high_halves_are_equal()
    {
        var timestamps = KeysWithEqualLowAndHighHalves()
            .Select(ticks => new DamlTimestamp(new DateTimeOffset(ticks, TimeSpan.Zero)));

        DistinctHashCount(timestamps).Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void DamlNumeric_spreads_values_whose_low_and_high_halves_are_equal()
    {
        var numerics = KeysWithEqualLowAndHighHalves().Select(key => new DamlNumeric((decimal)key));

        DistinctHashCount(numerics).Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }

    [Fact]
    public void DamlInt64_equal_values_keep_equal_hashes()
    {
        new DamlInt64(4_294_967_297L).GetHashCode().Should().Be(new DamlInt64(4_294_967_297L).GetHashCode());
        new DamlInt64(4_294_967_297L).Should().Be(new DamlInt64(4_294_967_297L));
        new DamlInt64(4_294_967_297L).Should().NotBe(new DamlInt64(8_589_934_594L));
    }

    [Fact]
    public void DamlTimestamp_equal_instants_in_different_offsets_keep_equal_hashes()
    {
        var utc = new DamlTimestamp(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var shifted = new DamlTimestamp(new DateTimeOffset(2026, 1, 1, 14, 0, 0, TimeSpan.FromHours(2)));

        shifted.GetHashCode().Should().Be(utc.GetHashCode());
        shifted.Should().Be(utc);
    }
}
