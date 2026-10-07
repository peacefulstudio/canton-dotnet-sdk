// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// The view <see cref="IConformanceProbe"/> projects: one Numeric field, <c>amount</c>. The kit
/// expects <c>42.5</c> on every probe contract an interface read serves.
/// </summary>
/// <param name="Amount">The amount the participant-computed view reports.</param>
public sealed record ConformanceProbeView([property: DamlFieldAttribute("amount")] decimal Amount)
    : IDamlRecord<ConformanceProbeView>
{
    /// <inheritdoc />
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

    /// <summary>Creates a view from the wire record an interface view carries.</summary>
    /// <param name="record">The wire record.</param>
    /// <returns>The view the record decodes to.</returns>
    public static ConformanceProbeView FromRecord(DamlRecord record) =>
        new(record.GetRequiredField("amount").As<DamlNumeric>().Value);

    /// <inheritdoc cref="IDamlRecord{TSelf}.__ReadDamlLfJson" />
    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        DamlRecord.Create(
            DamlField.Create(
                "amount",
                DamlLfJsonDecoders.ReadNumeric(
                    DamlLfJsonDecoders.RequireField(DamlLfJsonDecoders.RequireObject(json, context), context, "amount"),
                    context.Field("amount"))));
}
