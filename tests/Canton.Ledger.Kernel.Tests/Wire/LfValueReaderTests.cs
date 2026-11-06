// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Google.Protobuf;
using Xunit;
using LfValue = Com.Digitalasset.Daml.Lf.Value;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class LfValueReaderTests
{
    private static DamlValue Read(LfValue.Value value) => LfValueReader.Read(value.ToByteArray());

    [Fact]
    public void Read_reads_unit()
    {
        Read(new LfValue.Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() }).Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void Read_reads_bool()
    {
        Read(new LfValue.Value { Bool = true }).Should().Be(new DamlBool(true));
    }

    [Fact]
    public void Read_reads_int64_including_a_negative_value()
    {
        Read(new LfValue.Value { Int64 = -9_000_000_000L }).Should().Be(new DamlInt64(-9_000_000_000L));
    }

    [Fact]
    public void Read_reads_text()
    {
        Read(new LfValue.Value { Text = "hello" }).Should().Be(new DamlText("hello"));
    }

    [Fact]
    public void Read_reads_party()
    {
        Read(new LfValue.Value { Party = "alice::1220ab" }).Should().Be(new DamlParty("alice::1220ab"));
    }

    [Fact]
    public void Read_reads_numeric_keeping_its_scale()
    {
        var numeric = Read(new LfValue.Value { Numeric = "100.0000000000" }).Should().BeOfType<DamlNumeric>().Subject;

        numeric.ToCanonicalString().Should().Be("100.0");
        numeric.Value.Should().Be(100m);
    }

    [Fact]
    public void Read_reads_a_date_as_days_since_the_epoch()
    {
        Read(new LfValue.Value { Date = 20_751 }).Should().Be(new DamlDate(new DateOnly(2026, 10, 25)));
    }

    [Fact]
    public void Read_reads_a_timestamp_as_microseconds_since_the_epoch()
    {
        var timestamp = Read(new LfValue.Value { Timestamp = 1_761_404_714_404_398L });

        timestamp.Should().Be(new DamlTimestamp(new DateTimeOffset(2025, 10, 25, 15, 5, 14, TimeSpan.Zero).AddTicks(4043980)));
    }

    [Fact]
    public void Read_reads_a_contract_id_as_lower_case_hex()
    {
        Read(new LfValue.Value { ContractId = ByteString.CopyFrom(0x00, 0xab, 0xcd) })
            .Should().Be(new DamlContractId("00abcd"));
    }

    [Fact]
    public void Read_reads_a_record_without_labels_or_a_record_id()
    {
        var record = new LfValue.Value
        {
            Record = new LfValue.Value.Types.Record
            {
                Fields =
                {
                    new LfValue.Value.Types.Record.Types.Field { Value = new LfValue.Value { Party = "alice::1" } },
                    new LfValue.Value.Types.Record.Types.Field { Value = new LfValue.Value { Int64 = 7 } },
                },
            },
        };

        Read(record).Should().Be(DamlRecord.Create(new DamlField("", new DamlParty("alice::1")), new DamlField("", new DamlInt64(7))));
    }

    [Fact]
    public void Read_reads_a_variant_by_its_constructor()
    {
        var variant = new LfValue.Value
        {
            Variant = new LfValue.Value.Types.Variant { Constructor = "Some", Value = new LfValue.Value { Text = "x" } },
        };

        Read(variant).Should().Be(DamlVariant.Create("Some", new DamlText("x")));
    }

    [Fact]
    public void Read_reads_an_enum_by_its_constructor()
    {
        Read(new LfValue.Value { Enum = new LfValue.Value.Types.Enum { Value = "Red" } })
            .Should().Be(DamlEnum.Create("Red"));
    }

    [Fact]
    public void Read_reads_a_list_in_order()
    {
        var list = new LfValue.Value
        {
            List = new LfValue.Value.Types.List
            {
                Elements = { new LfValue.Value { Int64 = 3 }, new LfValue.Value { Int64 = 1 }, new LfValue.Value { Int64 = 2 } },
            },
        };

        Read(list).Should().Be(new DamlList([new DamlInt64(3), new DamlInt64(1), new DamlInt64(2)]));
    }

    [Fact]
    public void Read_reads_an_absent_optional_as_none()
    {
        Read(new LfValue.Value { Optional = new LfValue.Value.Types.Optional() }).Should().Be(DamlOptional.None);
    }

    [Fact]
    public void Read_reads_a_present_optional_as_some()
    {
        var optional = new LfValue.Value
        {
            Optional = new LfValue.Value.Types.Optional { Value = new LfValue.Value { Text = "x" } },
        };

        Read(optional).Should().Be(DamlOptional.Some(new DamlText("x")));
    }

    [Fact]
    public void Read_keeps_the_levels_of_a_nested_optional()
    {
        var someNone = new LfValue.Value
        {
            Optional = new LfValue.Value.Types.Optional
            {
                Value = new LfValue.Value { Optional = new LfValue.Value.Types.Optional() },
            },
        };

        Read(someNone).Should().Be(DamlOptional.Some(DamlOptional.None));
    }

    [Fact]
    public void Read_reads_a_text_map()
    {
        var textMap = new LfValue.Value
        {
            TextMap = new LfValue.Value.Types.TextMap
            {
                Entries =
                {
                    new LfValue.Value.Types.TextMap.Types.Entry { Key = "a", Value = new LfValue.Value { Int64 = 1 } },
                    new LfValue.Value.Types.TextMap.Types.Entry { Key = "b", Value = new LfValue.Value { Int64 = 2 } },
                },
            },
        };

        Read(textMap).Should().Be(DamlTextMap.Create(("a", new DamlInt64(1)), ("b", new DamlInt64(2))));
    }

    [Fact]
    public void Read_reads_a_gen_map_in_entry_order()
    {
        var genMap = new LfValue.Value
        {
            Map = new LfValue.Value.Types.Map
            {
                Entries =
                {
                    new LfValue.Value.Types.Map.Types.Entry
                    {
                        Key = new LfValue.Value { Int64 = 2 }, Value = new LfValue.Value { Text = "two" },
                    },
                    new LfValue.Value.Types.Map.Types.Entry
                    {
                        Key = new LfValue.Value { Int64 = 1 }, Value = new LfValue.Value { Text = "one" },
                    },
                },
            },
        };

        Read(genMap).Should().Be(DamlGenMap.Create((new DamlInt64(2), new DamlText("two")), (new DamlInt64(1), new DamlText("one"))));
    }

    [Fact]
    public void Read_rejects_bytes_that_are_not_a_value()
    {
        var act = () => LfValueReader.Read(new byte[] { 0xff, 0xff });

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().StartWith("the Daml-LF value is not readable");
    }

    [Fact]
    public void Read_rejects_a_value_with_no_case_set()
    {
        var act = () => LfValueReader.Read(ReadOnlyMemory<byte>.Empty);

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Be("the Daml-LF value has no case set.");
    }

    [Fact]
    public void Read_rejects_a_record_field_without_a_value()
    {
        var record = new LfValue.Value
        {
            Record = new LfValue.Value.Types.Record { Fields = { new LfValue.Value.Types.Record.Types.Field() } },
        };

        var act = () => Read(record);

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Be("the Daml-LF value has no case set.");
    }

    [Fact]
    public void Read_rejects_a_date_outside_the_Daml_LF_range()
    {
        var act = () => Read(new LfValue.Value { Date = int.MaxValue });

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Contain("Days since epoch must resolve to a date");
    }

    [Fact]
    public void Read_rejects_a_numeric_that_is_not_canonical()
    {
        var act = () => Read(new LfValue.Value { Numeric = "1e5" });

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Be("'1e5' is not a canonical Daml Numeric.");
    }

    [Fact]
    public void Read_rejects_an_empty_contract_id()
    {
        var act = () => Read(new LfValue.Value { ContractId = ByteString.Empty });

        act.Should().Throw<MalformedResponseException>();
    }

    [Fact]
    public void Read_rejects_a_text_map_with_a_repeated_key()
    {
        var textMap = new LfValue.Value
        {
            TextMap = new LfValue.Value.Types.TextMap
            {
                Entries =
                {
                    new LfValue.Value.Types.TextMap.Types.Entry { Key = "a", Value = new LfValue.Value { Int64 = 1 } },
                    new LfValue.Value.Types.TextMap.Types.Entry { Key = "a", Value = new LfValue.Value { Int64 = 2 } },
                },
            },
        };

        var act = () => Read(textMap);

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Contain("'a'");
    }
}
