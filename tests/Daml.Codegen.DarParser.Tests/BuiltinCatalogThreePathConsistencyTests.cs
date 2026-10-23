// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate;
using Daml.Codegen.Intermediate.Model;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using AwesomeAssertions;
using Xunit;
using DalfType = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;
using LfBuiltin = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.BuiltinType;
using PbBuiltin = Daml.Codegen.Intermediate.BuiltinType;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbDar = Daml.Codegen.Intermediate.IntermediateDar;
using PbField = Daml.Codegen.Intermediate.Field;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbType = Daml.Codegen.Intermediate.Type;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Per-value behavioral consistency across the three builtin conversion paths —
/// <see cref="IntermediateDarReader"/> (proto to model), <see cref="IntermediateDarWriter"/>
/// (model to proto), and <see cref="PackageContext"/> (Daml-LF to model, reached through the
/// <see cref="DamlLfBuiltinBridge"/>) — for every one of the 23 defined proto
/// <c>BuiltinType</c> values. The single source of truth is the immutable static
/// <see cref="DamlPrimitiveCatalog"/> registry: each leg is driven only by its catalog row
/// (accept with the row's real <see cref="DamlPrimitive"/> identity, or throw per the row's
/// disposition), never by an injected or re-declared table, so a converter that disagrees
/// with the catalog on any value fails here. The reader's per-value theory, the writer's
/// per-value theory, and the parser's per-value theory live in their own assemblies' test
/// projects; this test is the cross-path join that proves they answer the same question the
/// same way for every value.
/// </summary>
public sealed class BuiltinCatalogThreePathConsistencyTests
{
    /// <summary>
    /// Every defined proto <c>BuiltinType</c> value — all 23 — paired with the Daml-LF
    /// <c>BuiltinType</c> value that bridges to it, or <c>null</c> when the Daml-LF enum has
    /// no counterpart: the proto-only <c>BUILTIN_TYPE_UNSPECIFIED</c> zero-value, and
    /// <c>BUILTIN_TYPE_SCENARIO</c> (reserved 1004, removed in Daml-LF 2.x).
    /// </summary>
    public static TheoryData<PbBuiltin, LfBuiltin?> EveryProtoValueWithItsDalfBuiltin()
    {
        var dalfBuiltinsByProto = DalfBuiltinsKeyedByTheirUniqueProtoBuiltin();

        var data = new TheoryData<PbBuiltin, LfBuiltin?>();
        foreach (var value in System.Enum.GetValues<PbBuiltin>())
        {
            data.Add(value, dalfBuiltinsByProto.TryGetValue(value, out var dalfBuiltin) ? dalfBuiltin : null);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryProtoValueWithItsDalfBuiltin))]
    public void BuiltinCatalog_reader_writer_and_parser_agree_on_accept_vs_throw_for_every_proto_value(
        PbBuiltin builtin,
        LfBuiltin? dalfBuiltin)
    {
        var row = DamlPrimitiveCatalog.Get(builtin);

        AssertReaderFollowsCatalog(builtin, row);
        AssertWriterRoundTripsAcceptedPrimitive(builtin, row);
        if (dalfBuiltin is { } bridgedDalfBuiltin)
        {
            AssertPackageContextFollowsReader(builtin, bridgedDalfBuiltin, row);
        }
    }

    private static Dictionary<PbBuiltin, LfBuiltin> DalfBuiltinsKeyedByTheirUniqueProtoBuiltin() =>
        System.Enum.GetValues<LfBuiltin>().ToDictionary(DamlLfBuiltinBridge.ToProtoBuiltin);

    private static void AssertReaderFollowsCatalog(PbBuiltin builtin, DamlPrimitiveCatalogRow row)
    {
        var proto = ProtoWithFieldTyped(builtin);
        DarModel? read = null;
        Action readProto = () => read = IntermediateDarReader.Read(proto);

        switch (row.Disposition)
        {
            case DamlPrimitiveDisposition.SupportedValue:
            case DamlPrimitiveDisposition.SignatureOnly:
                readProto.Should().NotThrow(because:
                    $"the catalog records {builtin} as {row.Disposition}, so the reader must accept it with its real identity");
                ReaderField(read!).Should().Be(new DamlPrimitiveType(row.Primitive!.Value),
                    "the reader's accept is the anchor the writer and parser legs are checked against: " +
                    "the same proto value must yield the same real DamlPrimitive identity");
                break;

            case DamlPrimitiveDisposition.Unsupported when builtin == PbBuiltin.Unspecified:
                readProto.Should().Throw<InvalidDataException>(because:
                    "the proto zero-value keeps its long-standing reader contract: an unset builtin carries no identity to map")
                    .WithMessage("*BUILTIN_TYPE_UNSPECIFIED*");
                break;

            case DamlPrimitiveDisposition.Unsupported:
                readProto.Should().Throw<NotSupportedException>(because:
                    $"the catalog records {builtin} as Unsupported, so the reader must reject it loudly")
                    .WithMessage($"*{builtin}*");
                break;

            default:
                throw new InvalidOperationException(
                    $"No reader behavior is defined for catalog disposition '{row.Disposition}'.");
        }
    }

    private static void AssertWriterRoundTripsAcceptedPrimitive(PbBuiltin builtin, DamlPrimitiveCatalogRow row)
    {
        if (row.Primitive is { } primitive)
        {
            var model = SingleRecordModel(new DamlPrimitiveType(primitive));
            var written = IntermediateDarWriter.Write(model);

            var writtenField = written.Main.Modules.Single().DataTypes.Single().Record.Fields.Single();
            writtenField.Type.Builtin.Should().Be(builtin,
                "whatever the reader accepts, the writer must write back to the identical proto BuiltinType value");

            var reread = IntermediateDarReader.Read(written);
            ReaderField(reread).Should().Be(new DamlPrimitiveType(primitive),
                "the reader must accept what its own writer wrote back, reproducing the same identity");
        }
    }

    private static void AssertPackageContextFollowsReader(PbBuiltin builtin, LfBuiltin dalfBuiltin, DamlPrimitiveCatalogRow row)
    {
        DamlType? converted = null;
        Action convertDalf = () => converted = new PackageContext(new Package()).ConvertType(
            new DalfType { Builtin = new DalfType.Types.Builtin { Builtin_ = dalfBuiltin } });

        switch (row.Disposition)
        {
            case DamlPrimitiveDisposition.SupportedValue:
            case DamlPrimitiveDisposition.SignatureOnly:
                convertDalf.Should().NotThrow(because:
                    $"the catalog records {dalfBuiltin} (bridging to {builtin}) as {row.Disposition}, so the parser must accept it");
                converted.Should().Be(new DamlPrimitiveType(row.Primitive!.Value),
                    $"the parser must produce the same real DamlPrimitive identity for {dalfBuiltin} that the " +
                    $"reader produces for {builtin} — the catalog row is shared, so the paths cannot disagree");
                break;

            case DamlPrimitiveDisposition.Unsupported:
                convertDalf.Should().Throw<NotSupportedException>(because:
                    $"the catalog records {dalfBuiltin} (bridging to {builtin}) as Unsupported, so the parser " +
                    "must reject it exactly as the reader rejects its proto spelling")
                    .WithMessage($"*{dalfBuiltin}*");
                break;

            default:
                throw new InvalidOperationException(
                    $"No parser behavior is defined for catalog disposition '{row.Disposition}'.");
        }
    }

    private static PbDar ProtoWithFieldTyped(PbBuiltin builtin) => new()
    {
        Main = new PbPackage
        {
            PackageId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            PackageName = "test",
            PackageVersion = "1.0.0",
            LanguageVersion = "2.1",
            Modules =
            {
                new PbModule
                {
                    NameSegments = { "M" },
                    DataTypes =
                    {
                        new PbDataType
                        {
                            Name = "Probe",
                            IsSerializable = false,
                            Record = new PbRecord
                            {
                                Fields = { new PbField { Name = "position", Type = new PbType { Builtin = builtin } } },
                            },
                        },
                    },
                },
            },
        },
    };

    private static DarModel SingleRecordModel(DamlType fieldType) => new()
    {
        MainPackage = new DamlPackage
        {
            PackageId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            Name = "pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "M",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "RoundTrip",
                            Definition = new DamlRecordDefinition([new DamlFieldDefinition("position", fieldType)]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        },
        Dependencies = [],
    };

    private static DamlType ReaderField(DarModel model) =>
        ((DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition).Fields[0].Type;
}
