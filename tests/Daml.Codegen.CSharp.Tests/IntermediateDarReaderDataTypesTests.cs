// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbEnum = Daml.Codegen.Intermediate.Enum;
using PbField = Daml.Codegen.Intermediate.Field;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbType = Daml.Codegen.Intermediate.Type;
using PbTypeApp = Daml.Codegen.Intermediate.TypeApp;
using PbTypeConName = Daml.Codegen.Intermediate.TypeConName;
using PbVariant = Daml.Codegen.Intermediate.Variant;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    [Fact]
    public void IntermediateDarReader_record_data_type_round_trips_with_field_types()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Greeting",
                IsSerializable = true,
                Record = new PbRecord
                {
                    Fields =
                    {
                        TextField("salutation"),
                        TextField("recipient"),
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var dataTypes = model.MainPackage.Modules[0].DataTypes;

        dataTypes.Should().HaveCount(1);
        dataTypes[0].Name.Should().Be("Greeting");
        dataTypes[0].Serializable.Should().BeTrue();
        var record = dataTypes[0].Definition.Should().BeOfType<DamlRecordDefinition>().Subject;
        record.Fields.Should().HaveCount(2);
        record.Fields[0].Name.Should().Be("salutation");
        record.Fields[0].Type.Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(DamlPrimitive.Text);
    }

    [Fact]
    public void IntermediateDarReader_variant_data_type_round_trips_constructors()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Outcome",
                IsSerializable = true,
                Variant = new PbVariant
                {
                    Constructors =
                    {
                        new PbField { Name = "Success", Type = new PbType { Builtin = PbBuiltinType.Unit } },
                        new PbField { Name = "Failure", Type = new PbType { Builtin = PbBuiltinType.Text } },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var variant = model.MainPackage.Modules[0].DataTypes[0].Definition
            .Should().BeOfType<DamlVariantDefinition>().Subject;
        variant.Constructors.Should().HaveCount(2);
        variant.Constructors[0].Name.Should().Be("Success");
        variant.Constructors[0].ArgumentType.Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(DamlPrimitive.Unit,
                "the proto path preserves wire-level fidelity — the Unit-vs-noarg collapse happens in the emitter, not the reader");
        variant.Constructors[1].Name.Should().Be("Failure");
        variant.Constructors[1].ArgumentType.Should().NotBeNull();
    }

    [Fact]
    public void IntermediateDarReader_enum_data_type_round_trips_constructors()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Colour",
                IsSerializable = true,
                EnumType = new PbEnum
                {
                    Constructors = { "Red", "Green", "Blue" },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var enumDef = model.MainPackage.Modules[0].DataTypes[0].Definition
            .Should().BeOfType<DamlEnumDefinition>().Subject;
        enumDef.Constructors.Should().Equal("Red", "Green", "Blue");
    }

    [Fact]
    public void IntermediateDarReader_type_app_over_folding_builtin_reads_as_typed_node()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Box",
                Record = new PbRecord
                {
                    Fields =
                    {
                        new PbField
                        {
                            Name = "contents",
                            Type = new PbType
                            {
                                TypeApp = new PbTypeApp
                                {
                                    Function = new PbType { Builtin = PbBuiltinType.List },
                                    Arguments = { new PbType { Builtin = PbBuiltinType.Text } },
                                },
                            },
                        },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        var list = record.Fields[0].Type.Should().BeOfType<DamlListType>().Subject;
        list.Element.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text),
            "a TypeApp over a folding builtin reads into its typed node at the reader boundary — " +
            "the raw DamlTypeApp shape survives only for applications the catalog does not fold");
    }

    [Fact]
    public void IntermediateDarReader_type_con_round_trips_to_daml_type_ref()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Wrapper",
                Record = new PbRecord
                {
                    Fields =
                    {
                        new PbField
                        {
                            Name = "inner",
                            Type = new PbType
                            {
                                TypeCon = new PbTypeConName
                                {
                                    PackageId = "other-pkg",
                                    ModuleNameSegments = { "Foo", "Bar" },
                                    NameSegments = { "Inner" },
                                },
                            },
                        },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        var typeRef = record.Fields[0].Type.Should().BeOfType<DamlTypeRef>().Subject;
        typeRef.PackageId.Should().Be("other-pkg");
        typeRef.Module.Should().Be("Foo.Bar");
        typeRef.Name.Should().Be("Inner");
    }

    [Fact]
    public void IntermediateDarReader_unspecified_builtin_throws_invalid_data()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Box",
                Record = new PbRecord
                {
                    Fields =
                    {
                        new PbField { Name = "v", Type = new PbType { Builtin = PbBuiltinType.Unspecified } },
                    },
                },
            });
        });

        var act = () => IntermediateDarReader.Read(proto);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*BUILTIN_TYPE_UNSPECIFIED*");
    }

    [Fact]
    public void IntermediateDarReader_unmapped_builtin_throws_not_supported()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Box",
                Record = new PbRecord
                {
                    Fields =
                    {
                        new PbField { Name = "v", Type = new PbType { Builtin = PbBuiltinType.Bignumeric } },
                    },
                },
            });
        });

        var act = () => IntermediateDarReader.Read(proto);
        act.Should().Throw<NotSupportedException>()
           .WithMessage("*BIGNUMERIC*");
    }

    [Fact]
    public void IntermediateDarReader_type_with_no_sort_set_throws_invalid_data()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Box",
                Record = new PbRecord
                {
                    Fields = { new PbField { Name = "v", Type = new PbType() } },
                },
            });
        });

        var act = () => IntermediateDarReader.Read(proto);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*sort*");
    }

    [Fact]
    public void IntermediateDarReader_nat_type_round_trips_as_type_var()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Decimal10",
                Record = new PbRecord
                {
                    Fields = { new PbField { Name = "scale", Type = new PbType { Nat = 10 } } },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;
        record.Fields[0].Type.Should().BeOfType<DamlTypeVar>()
            .Which.Name.Should().Be("10");
    }

    [Fact]
    public void IntermediateDarReader_field_with_null_type_throws()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "p",
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
                                Name = "R",
                                IsSerializable = true,
                                Record = new PbRecord
                                {
                                    Fields = { new PbField { Name = "x" } },
                                },
                            },
                        },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void IntermediateDarReader_variant_constructor_with_null_type_throws()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "p",
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
                                Name = "V",
                                IsSerializable = true,
                                Variant = new PbVariant
                                {
                                    Constructors = { new PbField { Name = "Ctor" } },
                                },
                            },
                        },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void IntermediateDarReader_data_type_with_no_shape_throws()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "p",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules =
                {
                    new PbModule
                    {
                        NameSegments = { "M" },
                        DataTypes = { new PbDataType { Name = "Bare", IsSerializable = true } },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Bare*");
    }
}
