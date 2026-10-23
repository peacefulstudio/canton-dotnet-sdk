// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed record OwnedTemplate : ITemplate, IDamlRecord<OwnedTemplate>
{
    private static readonly Party Alice = new("party::alice");

    public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "OwnedTemplate");
    public static string PackageId => "pkg";
    public static string PackageName => "pkg-name";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
    public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        TestRecordReader.Read(
            json,
            context,
            ("owner", DamlLfJsonDecoders.ReadParty));
    public static OwnedTemplate FromRecord(DamlRecord record) =>
        new();
}
