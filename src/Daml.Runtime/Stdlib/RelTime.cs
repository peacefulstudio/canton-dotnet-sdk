// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib type DA.Time.Types.RelTime — relative time as microseconds.
/// Hand-coded into Daml.Runtime since daml-stdlib-DA-Time-Types 1.0.0 is a frozen
/// stdlib package (no NuGet equivalent) referenced by many Splice DARs.
/// </summary>
public sealed record RelTime([property: DamlFieldAttribute("microseconds")] long Microseconds) : IDamlRecord<RelTime>
{
    /// <inheritdoc />
    public override int GetHashCode() => DamlHash.Int64(Microseconds);

    /// <summary>Encodes this RelTime as the single-field <c>microseconds</c> wire record.</summary>
    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("microseconds", new DamlInt64(Microseconds))
    );

    /// <summary>Decodes a RelTime from its <c>microseconds</c> wire record.</summary>
    public static RelTime FromRecord(DamlRecord record) => new(
        Microseconds: record.GetRequiredField("microseconds", 0).As<DamlInt64>().Value
    );

    /// <summary>Decodes a RelTime directly from its <c>microseconds</c> Daml-LF JSON record.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
    {
        var record = DamlLfJsonDecoders.RequireObject(json, context);
        return DamlRecord.Create(DamlField.Create(
            "microseconds",
            DamlLfJsonDecoders.ReadInt64(
                DamlLfJsonDecoders.RequireField(record, context, "microseconds"),
                context.Field("microseconds"))));
    }
}
