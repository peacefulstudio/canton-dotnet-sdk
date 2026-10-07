// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client;

internal static class RestValueDecoder
{
    public static DamlRecord ToDamlRecord<TRecord>(WireRecord record)
        where TRecord : IDamlRecord<TRecord> =>
        ToDamlRecord(record, TRecord.__ReadDamlLfJson, typeof(TRecord).Name);

    public static DamlRecord ToDamlRecord(WireRecord record, DamlLfElementReader reader, string rootPath) =>
        MalformedResponse.Decoding(
            record,
            wireRecord => Decode(
                wireRecord.AdditionalProperties,
                (json, context) => reader(json, context).As<DamlRecord>(),
                rootPath));

    public static DamlValue ToDamlValue(WireValue value, DamlLfElementReader reader, string rootPath) =>
        MalformedResponse.Decoding(value, wireValue => Decode(wireValue.AdditionalProperties, reader.Invoke, rootPath));

    public static DamlUndecodedJson ToUndecodedJson(WireRecord record) =>
        MalformedResponse.Decoding(record, wireRecord => new DamlUndecodedJson(DamlLfJsonTextOf(wireRecord.AdditionalProperties)));

    public static DamlUndecodedJson ToUndecodedJson(WireValue value) =>
        MalformedResponse.Decoding(value, wireValue => new DamlUndecodedJson(DamlLfJsonTextOf(wireValue.AdditionalProperties)));

    private static TDecoded Decode<TDecoded>(
        IDictionary<string, object> readFields,
        Func<JsonElement, DamlLfJsonDecodeContext, TDecoded> reader,
        string rootPath)
    {
        using var document = JsonDocument.Parse(DamlLfJsonTextOf(readFields));
        return reader(document.RootElement, DamlLfJsonDecodeContext.Root(rootPath));
    }

    private static string DamlLfJsonTextOf(IDictionary<string, object> readFields) =>
        readFields.Count == 1 && readFields.TryGetValue(WireValueNames.Idiomatic, out var rawText) && rawText is string lfJson
            ? lfJson
            : throw new InvalidOperationException(
                $"the value carries no Daml-LF JSON under '{WireValueNames.Idiomatic}'; only a value read from a participant response can be decoded.");

    public static bool IsJsonNull(WireValue value) =>
        value.AdditionalProperties.TryGetValue(WireValueNames.Idiomatic, out var rawText) && rawText is "null";
}
