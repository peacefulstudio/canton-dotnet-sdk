// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceArgumentKindCodeGenTests
{
    private static DamlModule ModuleWhoseChoiceTakes(DamlDataType argument) =>
        new()
        {
            Name = "Test.Module",
            DataTypes =
            [
                new DamlDataType { Name = "Asset", Definition = new DamlRecordDefinition([]) },
                argument
            ],
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", argument.Name),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            Interfaces = []
        };

    [Fact]
    public void Generate_rejects_a_choice_whose_argument_is_a_local_enum()
    {
        var enumArgument = new DamlDataType { Name = "Direction", Definition = new DamlEnumDefinition(["Left", "Right"]) };
        var generator = CreateGenerator();
        var dar = new DamlModelBuilder().WithPackageName("p").WithModule(ModuleWhoseChoiceTakes(enumArgument)).Build();

        var generate = () => generator.Generate(dar).ToList();

        generate.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Choice 'Transfer' of Daml template 'Test.Module:Asset' in package 'p' takes the enum 'Test.Module:Direction' as its argument, but a choice argument must be a record. " +
            "damlc always synthesises a record for a choice, so the Daml model is malformed.");
    }

    [Fact]
    public void Generate_rejects_a_choice_whose_argument_is_a_local_variant()
    {
        var variantArgument = new DamlDataType
        {
            Name = "Instruction",
            Definition = new DamlVariantDefinition([new DamlVariantConstructor("Move", new DamlPrimitiveType(DamlPrimitive.Int64))])
        };
        var generator = CreateGenerator();
        var dar = new DamlModelBuilder().WithPackageName("p").WithModule(ModuleWhoseChoiceTakes(variantArgument)).Build();

        var generate = () => generator.Generate(dar).ToList();

        generate.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Choice 'Transfer' of Daml template 'Test.Module:Asset' in package 'p' takes the variant 'Test.Module:Instruction' as its argument, but a choice argument must be a record. " +
            "damlc always synthesises a record for a choice, so the Daml model is malformed.");
    }
}
