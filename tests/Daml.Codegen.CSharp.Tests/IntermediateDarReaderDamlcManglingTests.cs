// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbRecord = Daml.Codegen.Intermediate.Record;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    public static TheoryData<string> DocumentedDamlcManglingRows() =>
        [
            "baz$u0027",
            "$u003a$u002b$u003a",
            "na$u00efvet$u00e9",
            "$u003a$U0001f642$u003a",
        ];

    [Theory]
    [MemberData(nameof(DocumentedDamlcManglingRows))]
    public void Read_accepts_field_names_damlc_mangles_from_special_characters(string mangledName)
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Rec",
            Record = new PbRecord { Fields = { TextField(mangledName) } },
        }));

        var model = IntermediateDarReader.Read(proto);

        var record = model.MainPackage.Modules[0].DataTypes[0].Definition.Should().BeOfType<DamlRecordDefinition>().Subject;
        record.Fields[0].Name.Should().Be(mangledName);
    }

    [Fact]
    public void Read_accepts_a_data_type_name_that_damlc_mangles_to_a_leading_dollar()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "$u003a$u002b$u003a",
            Record = new PbRecord { Fields = { TextField("owner") } },
        }));

        var model = IntermediateDarReader.Read(proto);

        model.MainPackage.Modules[0].DataTypes[0].Name.Should().Be("$u003a$u002b$u003a");
    }
}
