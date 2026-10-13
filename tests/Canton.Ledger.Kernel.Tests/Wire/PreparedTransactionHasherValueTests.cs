// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Enum = Com.Daml.Ledger.Api.V2.Enum;
using Empty = Google.Protobuf.WellKnownTypes.Empty;
using Record = Com.Daml.Ledger.Api.V2.Record;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

/// <summary>
/// Pins the encoding of every Ledger API value kind to the vectors of Canton v3.6.1
/// <c>community/common/src/test/scala/com/digitalasset/canton/protocol/hash/ValueHashTest.scala</c>,
/// written here as the hex of the encoded bytes that test traces.
/// </summary>
public class PreparedTransactionHasherValueTests
{
    public static TheoryData<string, Value, string> CantonValueVectors() => new()
    {
        { "unit", new Value { Unit = new Empty() }, "00" },
        { "true", new Value { Bool = true }, "0101" },
        { "false", new Value { Bool = false }, "0100" },
        { "text", new Value { Text = "hello world!" }, "07" + "0000000c" + "68656c6c6f20776f726c6421" },
        { "numeric", new Value { Numeric = "125.1002" }, "03" + "00000008" + "3132352e31303032" },
        {
            "contract id",
            new Value { ContractId = "0059b59ad7a6b6066e77b91ced54b8282f0e24e7089944685cb8f22f32fcbc4e1b" },
            "08" + "00000021" + "0059b59ad7a6b6066e77b91ced54b8282f0e24e7089944685cb8f22f32fcbc4e1b"
        },
        {
            "enum",
            new Value { Enum = new Enum { EnumId = ModuleName(), Constructor = "ENUM" } },
            "0e" + "01" + ModuleNameHex + "00000004" + "454e554d"
        },
        { "int64", new Value { Int64 = 10 }, "02" + "000000000000000a" },
        {
            "variant",
            new Value { Variant = new Variant { VariantId = ModuleName(), Constructor = "ENUM", Value = new Value { Bool = true } } },
            "0d" + "01" + ModuleNameHex + "00000004" + "454e554d" + "0101"
        },
        {
            "list",
            new Value
            {
                List = new List
                {
                    Elements = { new Value { Text = "five" }, new Value { Int64 = 5 }, new Value { Bool = true } },
                },
            },
            "0a" + "00000003" + "07" + "00000004" + "66697665" + "02" + "0000000000000005" + "0101"
        },
        {
            "text map",
            new Value
            {
                TextMap = new TextMap
                {
                    Entries =
                    {
                        new TextMap.Types.Entry { Key = "bar", Value = new Value { Text = "1284" } },
                        new TextMap.Types.Entry { Key = "foo", Value = new Value { Numeric = "31380.0" } },
                    },
                },
            },
            "0b" + "00000002"
                + "00000003" + "626172" + "07" + "00000004" + "31323834"
                + "00000003" + "666f6f" + "03" + "00000007" + "33313338302e30"
        },
        {
            "gen map",
            new Value
            {
                GenMap = new GenMap
                {
                    Entries =
                    {
                        new GenMap.Types.Entry { Key = new Value { Int64 = 5 }, Value = new Value { Text = "five" } },
                        new GenMap.Types.Entry { Key = new Value { Int64 = 10 }, Value = new Value { Text = "ten" } },
                    },
                },
            },
            "0f" + "00000002"
                + "02" + "0000000000000005" + "07" + "00000004" + "66697665"
                + "02" + "000000000000000a" + "07" + "00000003" + "74656e"
        },
        { "optional empty", new Value { Optional = new Optional() }, "09" + "00" },
        {
            "optional defined",
            new Value { Optional = new Optional { Value = new Value { Text = "hello" } } },
            "09" + "01" + "07" + "00000005" + "68656c6c6f"
        },
        { "timestamp", new Value { Timestamp = 1729788226000000L }, "04" + "0006253bb4bf5480" },
        { "date", new Value { Date = 20020 }, "05" + "00004e34" },
        {
            "record",
            new Value
            {
                Record = new Record
                {
                    RecordId = ModuleName(),
                    Fields =
                    {
                        new RecordField { Label = "field1", Value = new Value { Bool = true } },
                        new RecordField { Label = "field2", Value = new Value { Text = "hello" } },
                    },
                },
            },
            "0c" + "01" + ModuleNameHex + "00000002"
                + "01" + "00000006" + "6669656c6431" + "0101"
                + "01" + "00000006" + "6669656c6432" + "07" + "00000005" + "68656c6c6f"
        },
    };

    [Theory]
    [MemberData(nameof(CantonValueVectors))]
    public void EncodeValue_matches_the_Canton_vector(string kind, Value value, string expectedHex)
    {
        Convert.ToHexStringLower(PreparedTransactionHasher.EncodeValue(value)).Should().Be(expectedHex, kind);
    }

    private const string ModuleNameHex =
        "00000007" + "7061636b616765" + "00000001" + "00000006" + "6d6f64756c65" + "00000001" + "00000004" + "6e616d65";

    private static Identifier ModuleName() =>
        new() { PackageId = "package", ModuleName = "module", EntityName = "name" };
}
