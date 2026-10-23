// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public partial class CodeGenEdgeCaseTests
{
    #region Choice with Various Argument Types

    [Fact]
    public void Generate_should_handle_choice_with_party_return_type()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetOwner",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Party)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var contractFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.cs", global::System.StringComparison.Ordinal));

        contractFile.Should().NotBeNull();
        var code = contractFile!.Content;

        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Asset, global::Daml.Runtime.Data.DamlUnit, global::Daml.Runtime.Data.Party> ChoiceGetOwner");
        code.Should().Contain("ResultDecoder = val => global::Daml.Runtime.Data.Party.FromDamlValue(val.As<global::Daml.Runtime.Data.DamlParty>())");
    }

    [Fact]
    public void Generate_should_handle_choice_with_complex_return_type()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetData",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlPrimitiveType(DamlPrimitive.Text)])
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("data", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var contractFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.cs", global::System.StringComparison.Ordinal));

        contractFile.Should().NotBeNull();
        var code = contractFile!.Content;

        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Asset, global::Daml.Runtime.Data.DamlUnit, global::System.Collections.Generic.IReadOnlyList<string>>");
        code.Should().Contain(
            "ResultDecoder = val => (global::System.Collections.Generic.IReadOnlyList<string>)val.As<global::Daml.Runtime.Data.DamlList>().Values.Select(x => x.As<global::Daml.Runtime.Data.DamlText>().Value).ToList()",
            because: "list results decode element by element, since IReadOnlyList<T> has no FromRecord");
    }

    #endregion

    #region Template Gets Fields from Data Type

    [Fact]
    public void Generate_should_get_fields_from_the_same_named_data_type()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var assetFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.cs", global::System.StringComparison.Ordinal));

        assetFile.Should().NotBeNull();
        var code = assetFile!.Content;

        code.Should().Contain("Party Owner");
        code.Should().Contain("decimal Value");
    }

    #endregion

    #region Data Type Not Generated for Templates

    [Fact]
    public void Generate_should_not_create_separate_data_type_for_template()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Token",
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                },
                new DamlDataType
                {
                    Name = "OtherType",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);

        var tokenFiles = files.Where(f => f.RelativePath.EndsWith("Token.cs", global::System.StringComparison.Ordinal)).ToList();
        tokenFiles.Should().HaveCount(1, because: "a data type named like its template must not produce a second Token.cs");

        tokenFiles[0].Content.Should().Contain(": global::Daml.Runtime.Contracts.ITemplate");

        var otherTypeFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("OtherType.cs", global::System.StringComparison.Ordinal));
        otherTypeFile.Should().NotBeNull();
        otherTypeFile!.Content.Should().Contain(": global::Daml.Runtime.Data.IDamlRecord<OtherType>");
    }

    #endregion

    #region Variant Without Argument

    [Fact]
    public void Generate_should_handle_variant_constructor_without_argument()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Maybe",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Nothing", null),
                        new DamlVariantConstructor("Just", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var maybeFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Maybe.cs", global::System.StringComparison.Ordinal));

        maybeFile.Should().NotBeNull();
        var code = maybeFile!.Content;

        code.Should().Contain("public sealed record Nothing() : Maybe");
        code.Should().Contain("public sealed record Just(string Value) : Maybe");
    }

    #endregion
}
