// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Tests;

internal sealed record TestTemplate(string Owner) : ITemplate, IDamlRecord<TestTemplate>
{
    public static Identifier TemplateId { get; } = new("pkg", "M", "TestTemplate");
    public static string PackageId => "pkg";
    public static string PackageName => "test";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("owner", new DamlText(Owner)));

    public static TestTemplate FromRecord(DamlRecord record) =>
        new((record.GetField("owner") as DamlText)?.Value ?? string.Empty);

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        throw new NotSupportedException();
}
