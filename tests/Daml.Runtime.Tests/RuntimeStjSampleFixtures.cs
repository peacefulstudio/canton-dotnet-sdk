// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Tests;

internal sealed record SampleTemplate(Party Owner, long Amount) : ITemplate, IDamlRecord<SampleTemplate>
{
    public static Identifier TemplateId { get; } = new("sample-pkg", "Sample.Module", "SampleTemplate");
    public static string PackageId => "sample-pkg";
    public static string PackageName => "sample";
    public static Version PackageVersion { get; } = new(1, 2, 3);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("owner", Owner.ToDamlValue()),
        DamlField.Create("amount", new DamlInt64(Amount)));

    public static SampleTemplate FromRecord(DamlRecord record) =>
        new(
            Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
            record.GetRequiredField("amount").As<DamlInt64>().Value);

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        throw new NotSupportedException();
}

internal sealed record KeyedSampleTemplate(Party Owner, string AssetId)
    : ITemplate, IHasKey<KeyedSampleTemplate, string>
{
    public static Identifier TemplateId { get; } = new("sample-pkg", "Sample.Module", "KeyedSampleTemplate");
    public static string PackageId => "sample-pkg";
    public static string PackageName => "sample";
    public static Version PackageVersion { get; } = new(1, 2, 3);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public static KeyDescriptor<KeyedSampleTemplate, string> Key { get; } = new()
    {
        KeyEncoder = static key => new DamlText(key),
        KeyDecoder = static value => value.As<DamlText>().Value,
        KeyJsonReader = static (_, _) => throw new NotSupportedException(),
    };

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("owner", Owner.ToDamlValue()),
        DamlField.Create("assetId", new DamlText(AssetId)));

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        throw new NotSupportedException();
}

internal interface ISampleInterface : IDamlInterface, IHasView<SampleView>
{
    static Identifier IDamlInterface.InterfaceId => new("sample-pkg", "Sample.Module", "ISampleInterface");
    static string IDamlInterface.PackageId => "sample-pkg";
    static string IDamlInterface.PackageName => "sample";
    static Version IDamlInterface.PackageVersion => new(1, 2, 3);
    static DamlTypeDescriptor IDamlType.DamlTypeId =>
        new(new Identifier("sample-pkg", "Sample.Module", "ISampleInterface"), DamlTypeKind.Interface, "sample");
}

internal sealed record SampleView(string Tier) : IDamlRecord<SampleView>
{
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("tier", new DamlText(Tier)));

    public static SampleView FromRecord(DamlRecord record) =>
        new(record.GetRequiredField("tier").As<DamlText>().Value);

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        throw new NotSupportedException();
}
