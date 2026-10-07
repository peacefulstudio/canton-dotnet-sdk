// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text.Json;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed record ConformanceProbe(string Owner) : ITemplate, IDamlRecord<ConformanceProbe>, IImplements<IConformanceProbe>
{
    public static Identifier TemplateId { get; } = new("pkg", "M", "ConformanceProbe");
    public static string PackageId => "pkg";
    public static string PackageName => "conformance";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", new DamlText(Owner)));

    public static ConformanceProbe FromRecord(DamlRecord record) =>
        new((record.GetField("owner") as DamlText)?.Value ?? string.Empty);

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        throw new NotSupportedException();
}
