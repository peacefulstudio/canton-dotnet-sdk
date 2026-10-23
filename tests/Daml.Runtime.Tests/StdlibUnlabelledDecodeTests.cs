// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class StdlibUnlabelledDecodeTests
{
    private static DamlField Unlabelled(DamlValue value) => DamlField.Create("", value);

    [Fact]
    public void RelTime_FromRecord_decodes_an_unlabelled_record_by_position()
    {
        var record = DamlRecord.Create(Unlabelled(new DamlInt64(60_000_000)));

        RelTime.FromRecord(record).Should().Be(new RelTime(60_000_000));
    }

    [Fact]
    public void Tuple2_FromRecord_decodes_an_unlabelled_record_by_position()
    {
        var record = DamlRecord.Create(Unlabelled(new DamlInt64(42)), Unlabelled(new DamlText("hello")));

        var recovered = Tuple2<long, string>.FromRecord(
            record, v => v.As<DamlInt64>().Value, null, v => v.As<DamlText>().Value, null);

        recovered.Should().Be(new Tuple2<long, string>(42L, "hello"));
    }

    [Fact]
    public void Tuple3_FromRecord_decodes_an_unlabelled_record_by_position()
    {
        var record = DamlRecord.Create(
            Unlabelled(new DamlInt64(1)), Unlabelled(new DamlInt64(2)), Unlabelled(new DamlInt64(3)));

        var recovered = Tuple3<long, long, long>.FromRecord(
            record,
            v => v.As<DamlInt64>().Value, null,
            v => v.As<DamlInt64>().Value, null,
            v => v.As<DamlInt64>().Value, null);

        recovered.Should().Be(new Tuple3<long, long, long>(1L, 2L, 3L));
    }

    [Fact]
    public void NonEmpty_FromRecord_decodes_an_unlabelled_record_by_position()
    {
        var record = DamlRecord.Create(
            Unlabelled(new DamlInt64(1)),
            Unlabelled(new DamlList([new DamlInt64(2), new DamlInt64(3)])));

        var recovered = NonEmpty<long>.FromRecord(record, v => v.As<DamlInt64>().Value);

        recovered.Hd.Should().Be(1L);
        recovered.Tl.Should().Equal(2L, 3L);
    }

    [Fact]
    public void Set_FromRecord_decodes_an_unlabelled_record_by_position()
    {
        var record = DamlRecord.Create(
            Unlabelled(DamlGenMap.Create((new DamlText("a"), (DamlValue)DamlUnit.Instance))));

        var recovered = Set<string>.FromRecord(record, v => v.As<DamlText>().Value);

        recovered.Should().Be(new Set<string>(["a"]));
    }

    [Fact]
    public void Map_FromRecord_decodes_an_unlabelled_record_by_position()
    {
        var record = DamlRecord.Create(
            Unlabelled(DamlGenMap.Create((new DamlText("k"), (DamlValue)new DamlInt64(5)))));

        var recovered = Map<string, long>.FromRecord(
            record, v => v.As<DamlText>().Value, v => v.As<DamlInt64>().Value);

        recovered.Entries.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, long>("k", 5L));
    }
}
