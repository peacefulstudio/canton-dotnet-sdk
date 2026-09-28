// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbField = Daml.Codegen.Intermediate.Field;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbType = Daml.Codegen.Intermediate.Type;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Catalog-driven builtin conversion tests for <see cref="IntermediateDarReader"/>:
/// every proto <see cref="PbBuiltinType"/> value the catalog accepts must read into its
/// real <see cref="DamlPrimitive"/> identity, and every rejected value must throw the
/// exception its catalog row prescribes. The reader is position-blind — it converts any
/// <c>Type</c> wherever it appears — so these tests drive the conversion through a
/// synthetic record field; interface-method signature positions arrive with the
/// interface-method milestone.
/// </summary>
public partial class IntermediateDarReaderTests
{
    /// <summary>
    /// The catalog's <see cref="DamlPrimitiveDisposition.SignatureOnly"/> rows: the
    /// structural type-formers that must keep their real identity in the model.
    /// </summary>
    public static TheoryData<PbBuiltinType, DamlPrimitive> SignatureOnlyCatalogRows()
    {
        var rows = new TheoryData<PbBuiltinType, DamlPrimitive>();
        foreach (var row in DamlPrimitiveCatalog.Rows.Where(
                     row => row.Disposition == DamlPrimitiveDisposition.SignatureOnly))
        {
            rows.Add(row.Builtin, row.Primitive!.Value);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyCatalogRows))]
    public void IntermediateDarReader_signature_only_builtin_resolves_to_its_real_primitive_identity(
        PbBuiltinType builtin,
        DamlPrimitive expectedPrimitive)
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "SignaturePosition",
            IsSerializable = false,
            Record = new PbRecord
            {
                Fields = { new PbField { Name = "position", Type = new PbType { Builtin = builtin } } },
            },
        }));

        var model = IntermediateDarReader.Read(proto);

        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        record.Fields[0].Type.Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(expectedPrimitive,
                "the catalog carries the structural builtins with real identity — the reader must resolve them " +
                "to their own DamlPrimitive member, never erase them to Unit and never reject them");
    }

    /// <summary>
    /// The catalog's <see cref="DamlPrimitiveDisposition.Unsupported"/> rows other than the
    /// proto zero-value: encountering them must throw <see cref="NotSupportedException"/>
    /// whose message names the offending builtin.
    /// </summary>
    public static TheoryData<PbBuiltinType> ThrowingCatalogRows()
    {
        var rows = new TheoryData<PbBuiltinType>();
        foreach (var row in DamlPrimitiveCatalog.Rows.Where(
                     row => row.Disposition == DamlPrimitiveDisposition.Unsupported
                         && row.Builtin != PbBuiltinType.Unspecified))
        {
            rows.Add(row.Builtin);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(ThrowingCatalogRows))]
    public void IntermediateDarReader_unsupported_builtin_throws_not_supported_naming_the_builtin(PbBuiltinType builtin)
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Rejected",
            IsSerializable = false,
            Record = new PbRecord
            {
                Fields = { new PbField { Name = "position", Type = new PbType { Builtin = builtin } } },
            },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        var thrown = act.Should().Throw<NotSupportedException>(because:
            "an Unsupported builtin must be rejected loudly, never silently mapped or defaulted").Which;
        thrown.Message.Should().Contain(builtin.ToString(),
            "the exception must name the offending builtin");
        thrown.Message.Should().Contain("Unsupported",
            "the exception must record that the builtin's catalog disposition — not a mapping gap — is why it throws");
    }

    /// <summary>
    /// Every defined proto <see cref="PbBuiltinType"/> value joined against its catalog row:
    /// the reader's behavior must be exactly the catalog's, with no silent fall-through for
    /// any of the 23 values — accepted values read into their row's real
    /// <see cref="DamlPrimitive"/> identity, and rejected values throw the exception type
    /// their row prescribes.
    /// </summary>
    public static TheoryData<PbBuiltinType, DamlPrimitive?, DamlPrimitiveDisposition> AllProtoValuesWithCatalogRows()
    {
        var data = new TheoryData<PbBuiltinType, DamlPrimitive?, DamlPrimitiveDisposition>();
        foreach (var value in System.Enum.GetValues<PbBuiltinType>())
        {
            var row = DamlPrimitiveCatalog.Get(value);
            data.Add(value, row.Primitive, row.Disposition);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllProtoValuesWithCatalogRows))]
    public void IntermediateDarReader_builtin_conversion_matches_the_catalog_for_every_proto_value(
        PbBuiltinType builtin,
        DamlPrimitive? expectedPrimitive,
        DamlPrimitiveDisposition disposition)
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Probe",
            IsSerializable = false,
            Record = new PbRecord
            {
                Fields = { new PbField { Name = "position", Type = new PbType { Builtin = builtin } } },
            },
        }));

        DarModel? model = null;
        var act = () => model = IntermediateDarReader.Read(proto);

        switch (disposition)
        {
            case DamlPrimitiveDisposition.SupportedValue:
            case DamlPrimitiveDisposition.SignatureOnly:
                act.Should().NotThrow(because:
                    $"the catalog records {builtin} as {disposition}, so the reader must resolve it, not reject it");
                var record = (DamlRecordDefinition)model!.MainPackage.Modules[0].DataTypes[0].Definition;
                record.Fields[0].Type.Should().Be(new DamlPrimitiveType(expectedPrimitive!.Value),
                    "no proto value may map to a primitive other than its catalog row's");
                break;

            case DamlPrimitiveDisposition.Unsupported when builtin == PbBuiltinType.Unspecified:
                act.Should().Throw<InvalidDataException>(because:
                    "the proto zero-value keeps its long-standing reader contract: InvalidDataException, " +
                    "because an unset builtin carries no identity to map")
                    .WithMessage("*BUILTIN_TYPE_UNSPECIFIED*");
                break;

            case DamlPrimitiveDisposition.Unsupported:
                act.Should().Throw<NotSupportedException>(because:
                    $"the catalog records {builtin} as Unsupported, so the reader must reject it loudly")
                    .WithMessage($"*{builtin}*");
                break;

            default:
                throw new InvalidOperationException(
                    $"No reader behavior is defined for catalog disposition '{disposition}'.");
        }
    }

    [Fact]
    public void IntermediateDarReader_undefined_builtin_value_throws_invalid_data()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "FromTheFuture",
            IsSerializable = false,
            Record = new PbRecord
            {
                Fields = { new PbField { Name = "position", Type = new PbType { Builtin = (PbBuiltinType)23 } } },
            },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*23*",
                "a BuiltinType value outside the 23 defined proto values is wire data from a newer proto " +
                "schema than this reader understands — data corruption for this reader, not a mapping gap");
    }

    [Fact]
    public void IntermediateDarReader_structural_builtin_tree_reads_with_no_unit_substitution()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "SignatureTree",
            IsSerializable = false,
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "arrow",
                        Type = App(PbBuiltinType.Arrow, Builtin(PbBuiltinType.Text), Builtin(PbBuiltinType.Int64)),
                    },
                    new PbField { Name = "update", Type = App(PbBuiltinType.Update, Builtin(PbBuiltinType.Any)) },
                    new PbField { Name = "typeRep", Type = Builtin(PbBuiltinType.TypeRep) },
                    new PbField { Name = "anyException", Type = Builtin(PbBuiltinType.AnyException) },
                    new PbField { Name = "failureCategory", Type = Builtin(PbBuiltinType.FailureCategory) },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);

        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        var leaves = record.Fields.SelectMany(field => PrimitiveLeaves(field.Type)).ToList();
        leaves.Should().NotContain(DamlPrimitive.Unit,
            "Unit is a real Daml type: a package whose types contain no BUILTIN_TYPE_UNIT must never " +
            "gain a Unit leaf in the model — the no-erasure guarantee is part of the catalog policy, " +
            "and the reader must not regress it even though it never erased in the first place");
        leaves.Should().Contain(
        [
            DamlPrimitive.Arrow, DamlPrimitive.Text, DamlPrimitive.Int64, DamlPrimitive.Update,
            DamlPrimitive.Any, DamlPrimitive.TypeRep, DamlPrimitive.AnyException, DamlPrimitive.FailureCategory,
        ],
            "every structural type-former in the input keeps its real identity in the read model");
    }

    [Fact]
    public void IntermediateDarReader_unit_builtin_field_reads_as_unit()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "GenuineUnit",
            IsSerializable = false,
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField { Name = "before", Type = Builtin(PbBuiltinType.Text) },
                    new PbField { Name = "unit", Type = Builtin(PbBuiltinType.Unit) },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);

        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        var unitField = record.Fields.Single(f => f.Name == "unit");
        unitField.Type.Should().Be(new DamlPrimitiveType(DamlPrimitive.Unit),
            "the no-erasure guarantee must not break the real mapping: an actual BUILTIN_TYPE_UNIT " +
            "input keeps resolving to Unit");
    }

    private static PbType Builtin(PbBuiltinType builtin) => new() { Builtin = builtin };

    private static PbType App(PbBuiltinType builtin, params PbType[] arguments) => new()
    {
        TypeApp = new TypeApp { Function = Builtin(builtin), Arguments = { arguments } },
    };

    private static IEnumerable<DamlPrimitive> PrimitiveLeaves(DamlType type)
    {
        switch (type)
        {
            case DamlPrimitiveType primitive:
                yield return primitive.Primitive;
                break;
            case DamlTypeApp app:
                foreach (var leaf in PrimitiveLeaves(app.Base))
                {
                    yield return leaf;
                }
                foreach (var argument in app.Arguments)
                {
                    foreach (var leaf in PrimitiveLeaves(argument))
                    {
                        yield return leaf;
                    }
                }
                break;
        }
    }
}
