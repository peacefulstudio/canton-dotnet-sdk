// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Google.Protobuf;
using LfValue = Com.Digitalasset.Daml.Lf.Value;

namespace Canton.Ledger.Kernel.Wire;

internal static class LfValueReader
{
    internal static DamlValue Read(ReadOnlyMemory<byte> serializedValue)
    {
        try
        {
            var value = LfValue.Value.Parser.ParseFrom(serializedValue.Span);
            return MalformedResponse.Decoding(value, ToDamlValue);
        }
        catch (InvalidProtocolBufferException unreadable)
        {
            throw new MalformedResponseException($"the Daml-LF value is not readable: {unreadable.Message}", unreadable);
        }
    }

    private static DamlValue ToDamlValue(LfValue.Value value) =>
        value.SumCase switch
        {
            LfValue.Value.SumOneofCase.None => throw new MalformedResponseException("the Daml-LF value has no case set."),
            LfValue.Value.SumOneofCase.Unit => DamlUnit.Instance,
            LfValue.Value.SumOneofCase.Bool => new DamlBool(value.Bool),
            LfValue.Value.SumOneofCase.Int64 => new DamlInt64(value.Int64),
            LfValue.Value.SumOneofCase.Text => new DamlText(value.Text),
            LfValue.Value.SumOneofCase.Party => new DamlParty(value.Party),
            LfValue.Value.SumOneofCase.Numeric => ToNumeric(value.Numeric),
            LfValue.Value.SumOneofCase.Date => DamlDate.FromDaysSinceEpoch(value.Date),
            LfValue.Value.SumOneofCase.Timestamp => DamlTimestamp.FromMicrosecondsSinceEpoch(value.Timestamp),
            LfValue.Value.SumOneofCase.ContractId => new DamlContractId(Convert.ToHexStringLower(value.ContractId.Span)),
            LfValue.Value.SumOneofCase.Record => ToRecord(value.Record),
            LfValue.Value.SumOneofCase.Variant => new DamlVariant(null, value.Variant.Constructor, Required(value.Variant.Value)),
            LfValue.Value.SumOneofCase.Enum => new DamlEnum(null, value.Enum.Value),
            LfValue.Value.SumOneofCase.List => new DamlList(value.List.Elements.Select(ToDamlValue).ToList()),
            LfValue.Value.SumOneofCase.Optional => new DamlOptional(
                value.Optional.Value is null ? null : ToDamlValue(value.Optional.Value)),
            LfValue.Value.SumOneofCase.TextMap => ToTextMap(value.TextMap),
            LfValue.Value.SumOneofCase.Map => ToGenMap(value.Map),
            _ => throw new MalformedResponseException($"the Daml-LF value case {value.SumCase} is not one this SDK reads."),
        };

    private static DamlValue Required(LfValue.Value? value) =>
        ToDamlValue(value ?? throw new MalformedResponseException("the Daml-LF value has no case set."));

    private static DamlNumeric ToNumeric(string numeric) =>
        DamlNumeric.TryParseCanonical(numeric, out var parsed)
            ? parsed
            : throw new MalformedResponseException($"'{numeric}' is not a canonical Daml Numeric.");

    private static DamlRecord ToRecord(LfValue.Value.Types.Record record) =>
        new(null, record.Fields.Select(field => new DamlField(string.Empty, Required(field.Value))).ToList());

    private static DamlTextMap ToTextMap(LfValue.Value.Types.TextMap map)
    {
        var entries = new Dictionary<string, DamlValue>(map.Entries.Count, StringComparer.Ordinal);
        foreach (var entry in map.Entries)
        {
            if (!entries.TryAdd(entry.Key, Required(entry.Value)))
            {
                throw new MalformedResponseException($"the Daml-LF text map repeats the key '{entry.Key}'.");
            }
        }

        return new DamlTextMap(entries);
    }

    private static DamlGenMap ToGenMap(LfValue.Value.Types.Map map) =>
        new(map.Entries.Select(entry => (Required(entry.Key), Required(entry.Value))).ToList());
}
