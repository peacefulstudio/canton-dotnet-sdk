// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbChoice = Daml.Codegen.Intermediate.Choice;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbEnum = Daml.Codegen.Intermediate.Enum;
using PbField = Daml.Codegen.Intermediate.Field;
using PbInterface = Daml.Codegen.Intermediate.Interface;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbTemplate = Daml.Codegen.Intermediate.Template;
using PbType = Daml.Codegen.Intermediate.Type;
using PbVariant = Daml.Codegen.Intermediate.Variant;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    public static TheoryData<string> NamesOutsideTheDamlLfIdentifierGrammar() =>
        [
            "Evil\" + new object() + \"",
            "Back\\slashName",
            "Brace{Name}",
            "New\nLine",
            "Angle<Name>",
            "Semi;colon",
            "Foo\n",
        ];

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_template_names_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.Templates.Add(new PbTemplate { Name = maliciousName }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>(
                "a hand-crafted --intermediate proto must not be able to inject code into emitted C# string literals")
            .WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_field_names_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Rec",
            Record = new PbRecord { Fields = { TextField(maliciousName) } },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_enum_constructors_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Color",
            EnumType = new PbEnum { Constructors = { maliciousName } },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_choice_names_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.Templates.Add(new PbTemplate
        {
            Name = "T",
            Choices =
            {
                new PbChoice
                {
                    Name = maliciousName,
                    ArgumentType = new PbType { Builtin = PbBuiltinType.Unit },
                    ReturnType = new PbType { Builtin = PbBuiltinType.Unit },
                },
            },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_variant_constructors_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Outcome",
            Variant = new PbVariant { Constructors = { TextField(maliciousName) } },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_interface_names_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.Interfaces.Add(new PbInterface { Name = maliciousName }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_module_name_segments_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m =>
        {
            m.NameSegments.Clear();
            m.NameSegments.Add(maliciousName);
        });

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_type_parameters_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Box",
            TypeParameters = { maliciousName },
            Record = new PbRecord(),
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_type_variables_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Box",
            Record = new PbRecord
            {
                Fields = { new PbField { Name = "v", Type = new PbType { TypeVar = maliciousName } } },
            },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }

    [Fact]
    public void Read_accepts_dotted_type_names_and_tuple_style_field_names()
    {
        var proto = MakePackageWith(m => m.DataTypes.Add(new PbDataType
        {
            Name = "Outcome.Win",
            Record = new PbRecord { Fields = { TextField("_1") } },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().NotThrow("dotted Daml-LF type names and tuple field labels are inside the grammar");
    }

    public static TheoryData<string> NamesOutsideThePackageCoordinateGrammar() =>
        [
            "evil\"name",
            "back\\slash",
            "space name",
            "semi;colon",
            "new\nline",
            "pkg\n",
            "---",
            "-",
            ".",
            "._-",
        ];

    [Theory]
    [MemberData(nameof(NamesOutsideThePackageCoordinateGrammar))]
    public void Read_rejects_package_ids_outside_the_package_coordinate_grammar(string maliciousCoordinate)
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageId = maliciousCoordinate;

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package id*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideThePackageCoordinateGrammar))]
    public void Read_rejects_package_names_outside_the_package_coordinate_grammar(string maliciousCoordinate)
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageName = maliciousCoordinate;

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package name*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideThePackageCoordinateGrammar))]
    public void Read_rejects_upgraded_package_ids_outside_the_package_coordinate_grammar(string maliciousCoordinate)
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.UpgradedPackageId = maliciousCoordinate;

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*upgraded package id*");
    }

    [Fact]
    public void Read_rejects_a_package_id_one_character_short_of_a_package_id()
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde";

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*package id*'0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde'*64 lowercase hexadecimal characters*");
    }

    public static TheoryData<string> ValuesThatAreNotPackageIds() =>
        [
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0",
            "0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef",
            "g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n",
            "pkg-id-1",
        ];

    [Theory]
    [MemberData(nameof(ValuesThatAreNotPackageIds))]
    public void Read_rejects_main_package_ids_that_are_not_64_lowercase_hex(string notAPackageId)
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageId = notAPackageId;

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package id*64 lowercase hexadecimal characters*");
    }

    [Theory]
    [MemberData(nameof(ValuesThatAreNotPackageIds))]
    public void Read_rejects_upgraded_package_ids_that_are_not_64_lowercase_hex(string notAPackageId)
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.UpgradedPackageId = notAPackageId;

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*upgraded package id*64 lowercase hexadecimal characters*");
    }

    [Theory]
    [MemberData(nameof(ValuesThatAreNotPackageIds))]
    public void Read_rejects_type_constructor_package_ids_that_are_not_64_lowercase_hex(string notAPackageId)
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(RecordWithFieldOfTypeCon(notAPackageId)));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*type-constructor package id*64 lowercase hexadecimal characters*");
    }

    [Fact]
    public void Read_accepts_a_64_lowercase_hex_package_id_everywhere_a_package_id_is_read()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(RecordWithFieldOfTypeCon(OtherValidPackageId)));
        proto.Main.UpgradedPackageId = OtherValidPackageId;

        var model = IntermediateDarReader.Read(proto);

        model.MainPackage.PackageId.Should().Be("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        model.MainPackage.UpgradedPackageId.Should().Be("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210");
        var field = ((DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes.Single().Definition).Fields.Single();
        field.Type.Should().BeOfType<DamlTypeRef>().Which.PackageId
            .Should().Be("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210");
    }

    [Fact]
    public void Read_accepts_an_empty_type_constructor_package_id_as_the_same_package_marker()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(RecordWithFieldOfTypeCon(string.Empty)));

        var model = IntermediateDarReader.Read(proto);

        var field = ((DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes.Single().Definition).Fields.Single();
        field.Type.Should().BeOfType<DamlTypeRef>().Which.PackageId.Should().BeEmpty();
    }

    [Fact]
    public void Read_keeps_the_loose_package_coordinate_check_for_package_names()
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageName = "my-package_1.0";

        var model = IntermediateDarReader.Read(proto);

        model.MainPackage.Name.Should().Be("my-package_1.0");
    }

    private const string OtherValidPackageId = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    private static PbDataType RecordWithFieldOfTypeCon(string typeConPackageId) => new()
    {
        Name = "Holder",
        IsSerializable = true,
        Record = new PbRecord
        {
            Fields =
            {
                new PbField
                {
                    Name = "held",
                    Type = new PbType
                    {
                        TypeCon = new Daml.Codegen.Intermediate.TypeConName
                        {
                            PackageId = typeConPackageId,
                            ModuleNameSegments = { "Foo" },
                            NameSegments = { "Held" },
                        },
                    },
                },
            },
        },
    };

    [Fact]
    public void Read_rejects_package_names_outside_the_package_name_grammar()
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageName = "evil\"name";

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package name*");
    }

    [Fact]
    public void Read_rejects_an_empty_package_id()
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageId = "";

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package id*",
            "an empty package id would flow into generated csproj metadata and namespaces");
    }

    [Fact]
    public void Read_rejects_an_empty_package_name()
    {
        var proto = MakePackageWith(_ => { });
        proto.Main.PackageName = "";

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*package name*",
            "an empty package name would flow into generated csproj metadata and namespaces");
    }
}
