// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Encodes runtime <see cref="DamlValue"/>/<see cref="DamlRecord"/> instances for command submission by
/// writing them through <see cref="DamlJsonSerializer"/> and carrying the resulting Daml-LF JSON text as a
/// raw wire value, so every transport writes the same bytes.
/// </summary>
internal static class RestValueEncoder
{
    public static WireRecord ToWireRecord(DamlRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var wireRecord = new WireRecord();
        wireRecord.AdditionalProperties[WireValueNames.Idiomatic] = WithinWireDepth(DamlJsonSerializer.Serialize(record));
        return wireRecord;
    }

    public static WireValue ToWireValue(DamlValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var wireValue = new WireValue();
        var lfJson = value is DamlUndecodedJson undecoded ? undecoded.LfJson : DamlJsonSerializer.Serialize(value);
        wireValue.AdditionalProperties[WireValueNames.Idiomatic] = WithinWireDepth(lfJson);
        return wireValue;
    }

    private static string WithinWireDepth(string lfJson)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(lfJson));
        while (reader.Read())
        {
        }

        return lfJson;
    }
}
