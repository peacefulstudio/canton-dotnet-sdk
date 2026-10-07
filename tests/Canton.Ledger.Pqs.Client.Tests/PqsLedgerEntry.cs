// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Canton.Ledger.Pqs.Client.Tests;

internal sealed record PqsLedgerEntry(
    [property: DamlFieldAttribute("owner")] string Owner,
    [property: DamlFieldAttribute("quantity")] long Quantity,
    [property: DamlFieldAttribute("price")] decimal Price,
    [property: DamlFieldAttribute("note")] string? Note) : ITemplate, IDamlRecord<PqsLedgerEntry>
{
    public static Identifier TemplateId { get; } = new("pkg123", "Test.Module", "PqsLedgerEntry");
    public static string PackageId => "pkg123";
    public static string PackageName => "test-package";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => throw new NotSupportedException();

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
    {
        DamlLfJsonDecoders.RequireObject(json, context);
        var fields = new List<DamlField>(4);
        fields.Add(DamlField.Create("owner", DamlLfJsonDecoders.ReadParty(DamlLfJsonDecoders.RequireField(json, context, "owner"), context.Field("owner"))));
        fields.Add(DamlField.Create("quantity", DamlLfJsonDecoders.ReadInt64(DamlLfJsonDecoders.RequireField(json, context, "quantity"), context.Field("quantity"))));
        fields.Add(DamlField.Create("price", DamlLfJsonDecoders.ReadNumeric(DamlLfJsonDecoders.RequireField(json, context, "price"), context.Field("price"))));
        DamlLfJsonDecoders.AddFieldIfPresent(fields, json, "note", present => DamlLfJsonDecoders.ReadOptional(present, context.Field("note"), DamlLfJsonDecoders.ReadText));
        return DamlRecord.Create(fields.ToArray());
    }

    public static PqsLedgerEntry FromRecord(DamlRecord record) => new(
        Owner: record.GetRequiredField("owner").As<DamlParty>().Value,
        Quantity: record.GetRequiredField("quantity").As<DamlInt64>().Value,
        Price: record.GetRequiredField("price").As<DamlNumeric>().Value,
        Note: record.GetOptionalField("note").As<DamlOptional>().Value?.As<DamlText>().Value);
}
