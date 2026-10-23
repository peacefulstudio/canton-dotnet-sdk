// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderSerializableFilterTests
{
    private const int FieldNameStrIdx = 4;

    public static TheoryData<string> DataShapes() => ["record", "variant", "enum"];

    [Theory]
    [MemberData(nameof(DataShapes))]
    public void Parse_drops_a_non_serializable_data_type(string shape)
    {
        var pkg = DalfTestFixtures.PackageWithDataType(DataTypeOfShape(shape, serializable: false), "f");

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        result.Modules.Should().ContainSingle()
            .Which.DataTypes.Should().BeEmpty(
                "the JVM helper's SignatureErasure strips every non-serializable data type, and the two "
                + "decoders must hand the emitter the same Intermediate");
    }

    [Theory]
    [MemberData(nameof(DataShapes))]
    public void Parse_keeps_a_serializable_data_type(string shape)
    {
        var pkg = DalfTestFixtures.PackageWithDataType(DataTypeOfShape(shape, serializable: true), "f");

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        result.Modules.Should().ContainSingle()
            .Which.DataTypes.Should().ContainSingle()
            .Which.Name.Should().Be("TestType");
    }

    [Fact]
    public void Parse_drops_a_non_serializable_record_whose_field_is_a_function_arrow()
    {
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType { FieldInternedStr = FieldNameStrIdx, Type = Int64ToInt64Arrow() });
        var pkg = DalfTestFixtures.PackageWithDataType(new DefDataType { Record = record, Serializable = false }, "f");

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        result.Modules.Should().ContainSingle()
            .Which.DataTypes.Should().BeEmpty(
                "`data Fn = Fn with f : Int -> Int` is non-serializable, so it must not reach the emitter, "
                + "which rejects a signature-only Arrow in a data position");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_keeps_an_interface_data_type_whatever_its_serializable_flag(bool serializable)
    {
        var pkg = DalfTestFixtures.PackageWithDataType(new DefDataType { Interface = new Unit(), Serializable = serializable });

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        result.Modules.Should().ContainSingle()
            .Which.DataTypes.Should().ContainSingle(
                "the JVM helper always emits an empty-record placeholder for an interface, re-adding it "
                + "when SignatureErasure strips the interface's own data type")
            .Which.Definition.Should().BeOfType<DamlRecordDefinition>()
            .Which.Fields.Should().BeEmpty();
    }

    private static DefDataType DataTypeOfShape(string shape, bool serializable)
    {
        var int64 = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } };
        var fields = new DefDataType.Types.Fields();
        fields.Fields_.Add(new FieldWithType { FieldInternedStr = FieldNameStrIdx, Type = int64 });
        var dataType = new DefDataType { Serializable = serializable };
        switch (shape)
        {
            case "record":
                dataType.Record = fields;
                break;
            case "variant":
                dataType.Variant = fields;
                break;
            case "enum":
                var constructors = new DefDataType.Types.EnumConstructors();
                constructors.ConstructorsInternedStr.Add(FieldNameStrIdx);
                dataType.Enum = constructors;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
        return dataType;
    }

    private static Type Int64ToInt64Arrow()
    {
        var arrow = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Arrow } };
        var int64 = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } };
        var arrowFromInt64 = new Type { Tapp = new Type.Types.TApp { Lhs = arrow, Rhs = int64 } };
        return new Type { Tapp = new Type.Types.TApp { Lhs = arrowFromInt64, Rhs = int64 } };
    }
}
