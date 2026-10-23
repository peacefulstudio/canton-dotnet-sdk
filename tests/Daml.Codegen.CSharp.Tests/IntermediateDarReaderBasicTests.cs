// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbField = Daml.Codegen.Intermediate.Field;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbType = Daml.Codegen.Intermediate.Type;
using PbVariant = Daml.Codegen.Intermediate.Variant;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    [Fact]
    public void IntermediateDar_proto_types_are_available()
    {
        var dar = new IntermediateDar();
        dar.Should().NotBeNull();
    }

    [Fact]
    public void IntermediateDarReader_upgraded_package_id_round_trips_from_proto()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                UpgradedPackageId = "4444444444444444444444444444444444444444444444444444444444444444",
            },
        };

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.UpgradedPackageId.Should().Be("4444444444444444444444444444444444444444444444444444444444444444");
    }

    [Fact]
    public void IntermediateDarReader_empty_upgraded_package_id_round_trips_as_null()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
            },
        };

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.UpgradedPackageId.Should().BeNull();
    }

    [Fact]
    public void IntermediateDarReader_empty_intermediate_dar_round_trips_to_empty_model()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                PackageName = "test-pkg",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
            },
        };

        var model = IntermediateDarReader.Read(proto);

        model.MainPackage.PackageId.Should().Be("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        model.MainPackage.Name.Should().Be("test-pkg");
        model.MainPackage.Version.Should().Be(new Version(1, 0, 0));
        model.MainPackage.LfVersion.Should().Be("2.1");
        model.MainPackage.Modules.Should().BeEmpty();
        model.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void IntermediateDarReader_keeps_the_full_snapshot_version_string_beside_the_three_part_version()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                PackageName = "daml-stdlib",
                PackageVersion = "3.3.0.20250502.13767.0",
                LanguageVersion = "2.1",
            },
        };

        var model = IntermediateDarReader.Read(proto);

        model.MainPackage.RawVersion.Should().Be("3.3.0.20250502.13767.0");
        model.MainPackage.Version.Should().Be(new Version(3, 3, 0));
    }

    [Fact]
    public void IntermediateDarReader_pads_a_two_part_package_version_to_three_parts()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                PackageName = "my-package",
                PackageVersion = "1.0",
                LanguageVersion = "2.1",
            },
        };

        var model = IntermediateDarReader.Read(proto);

        model.MainPackage.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public void IntermediateDarReader_null_proto_throws_argument_null_exception()
    {
        var act = () => IntermediateDarReader.Read(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IntermediateDarReader_proto_without_main_throws_invalid_data_exception()
    {
        var proto = new IntermediateDar();
        var act = () => IntermediateDarReader.Read(proto);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*main is required*");
    }

    [Fact]
    public void IntermediateDarReader_module_without_name_segments_throws_invalid_data_exception()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules = { new PbModule() },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
           .WithMessage("*module name segment*at least one*");
    }

    [Fact]
    public void IntermediateDarReader_dependencies_round_trip_in_order()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "1111111111111111111111111111111111111111111111111111111111111111",
                PackageName = "main",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
            },
            Dependencies =
            {
                new PbPackage
                {
                    PackageId = "2222222222222222222222222222222222222222222222222222222222222222", PackageName = "depA",
                    PackageVersion = "0.1.0", LanguageVersion = "2.1",
                },
                new PbPackage
                {
                    PackageId = "3333333333333333333333333333333333333333333333333333333333333333", PackageName = "depB",
                    PackageVersion = "0.2.0", LanguageVersion = "2.1",
                },
            },
        };

        var model = IntermediateDarReader.Read(proto);
        model.Dependencies.Should().HaveCount(2);
        model.Dependencies[0].PackageId.Should().Be("2222222222222222222222222222222222222222222222222222222222222222");
        model.Dependencies[1].PackageId.Should().Be("3333333333333333333333333333333333333333333333333333333333333333");
    }

    [Fact]
    public void IntermediateDarReader_module_name_segments_join_with_dot()
    {
        var proto = MakePackageWith(module =>
        {
            module.NameSegments.Clear();
            module.NameSegments.Add("DA");
            module.NameSegments.Add("Internal");
            module.NameSegments.Add("Template");
        });

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.Modules[0].Name.Should().Be("DA.Internal.Template");
    }

    [Fact]
    public void IntermediateDarReader_invalid_version_falls_back_to_three_part_zero()
    {
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                PackageName = "test",
                PackageVersion = "abc",
                LanguageVersion = "2.1",
            },
        };

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.Version.Should().Be(new Version(0, 0, 0));
    }

    [Fact]
    public void IntermediateDarReader_unit_typed_variant_constructor_emits_noarg_record()
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
                    },
                },
            });
        });
        var model = IntermediateDarReader.Read(proto);

        var options = new CodeGen.CodeGenOptions();
        var generator = new CodeGen.CSharpCodeGenerator(options);

        var files = generator.Generate(model);

        var outcomeFile = files.Should().Contain(f => f.RelativePath.EndsWith("Outcome.cs", StringComparison.Ordinal)).Which;
        outcomeFile.Content.Should().Contain("public sealed record Success() :",
            "unit-typed variant constructors collapse to no-arg records in the emitter, not the reader");
        outcomeFile.Content.Should().NotContain("Success(Unit Value)");
    }

    [Fact]
    public void IntermediateDarReader_generator_accepts_dar_model_from_intermediate_reader()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Note",
                IsSerializable = true,
                Record = new PbRecord { Fields = { TextField("body") } },
            });
        });
        var model = IntermediateDarReader.Read(proto);

        var options = new CodeGen.CodeGenOptions();
        var generator = new CodeGen.CSharpCodeGenerator(options);

        var files = generator.Generate(model);

        files.Should().NotBeNull();
        files.Should().Contain(f => f.RelativePath.EndsWith("Note.cs", StringComparison.Ordinal));
    }

    private static IntermediateDar MakePackageWith(Action<IntermediateModule> configure)
    {
        var module = new PbModule { NameSegments = { "Foo" } };
        configure(module);
        return new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules = { module },
            },
        };
    }

    private static Field TextField(string name) => new()
    {
        Name = name,
        Type = new PbType { Builtin = PbBuiltinType.Text },
    };

    private static Field PartyField(string name) => new()
    {
        Name = name,
        Type = new PbType { Builtin = PbBuiltinType.Party },
    };
}
