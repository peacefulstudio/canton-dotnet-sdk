// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using System.IO;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceCodeGenTests
{
    private static readonly DamlPackage StdlibStub = new()
    {
        PackageId = "daml-prim-pkg-id",
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    [Fact]
    public void Generate_should_reference_existing_type_for_local_data_type_argument()
    {
        // Arrange
        var transferArgFields = new[]
        {
            new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
        };

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
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Transfer"),
                            ReturnType = new DamlTypeRef("", "Test.Module", "Transfer_Result")
                        }
                    ]
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
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                },
                new DamlDataType
                {
                    Name = "Transfer",
                    Definition = new DamlRecordDefinition(transferArgFields)
                },
                new DamlDataType
                {
                    Name = "Transfer_Result",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces = []
        };

        var dar = new DamlModelBuilder().WithModule(module).WithDependency(StdlibStub).Build();
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var assetFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));

        // Assert
        assetFile.Should().NotBeNull();
        var code = assetFile!.Content;

        // Should reference the Transfer data type, not generate a nested TransferArgument
        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Asset, Transfer, global::Test.Module.Transfer_Result>");
        code.Should().Contain("ArgumentEncoder = arg => arg.ToRecord()");
        code.Should().Contain("ResultDecoder = val => global::Test.Module.Transfer_Result.FromRecord(val.As<global::Daml.Runtime.Data.DamlRecord>())");

        // Should NOT contain a nested argument class
        code.Should().NotContain("public sealed record TransferArgument");
        code.Should().NotContain("public sealed record TransferArg");
    }

    [Fact]
    public void GenerateNestedChoiceArgumentType_emits_indented_signature_without_stray_spaces()
    {
        var module = NestedTransferArgumentModule();
        var dar = new DamlModelBuilder().WithModule(module).WithDependency(StdlibStub).Build();
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var nestedFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.Transfer.cs", StringComparison.Ordinal));

        nestedFile.Should().NotBeNull("the choice argument record is emitted as a nested type in a partial template file");
        nestedFile!.Content.Should().Contain(
            "\n    public sealed record Transfer(\n        [property: global::Daml.Runtime.Data.DamlFieldAttribute(\"newOwner\")] global::Daml.Runtime.Data.Party NewOwner,\n        [property: global::Daml.Runtime.Data.DamlFieldAttribute(\"amount\")] decimal Amount\n    ) : global::Daml.Runtime.Data.IDamlRecord",
            "the nested record signature must be indented one level, carry one parameter per line indented one level further, and close on its own line with the base list");
    }

    [Fact]
    public void Generate_should_apply_root_filter_to_nested_choice_argument_types()
    {
        var module = TwoTemplatesEachWithNestedChoiceArgument();
        var dar = new DamlModelBuilder().WithModule(module).WithDependency(StdlibStub).Build();
        var generator = CreateGenerator(new CodeGenOptions { RootFilter = "Test\\.Module:KeepAsset" });

        var files = generator.Generate(dar).ToList();

        files.Should().Contain(
            f => f.RelativePath.EndsWith("KeepAsset.Transfer.cs", StringComparison.Ordinal),
            "the root filter admits KeepAsset, so its nested choice-argument type is still generated");
        files.Should().NotContain(
            f => f.RelativePath.EndsWith("SkipAsset.Transfer.cs", StringComparison.Ordinal),
            "the choice-argument loop walks every template in the module independently of the payload-record "
            + "loop above it, so it must apply the same root filter or a filtered-out template still leaks its "
            + "nested choice-argument file");
    }

    private static DamlModule TwoTemplatesEachWithNestedChoiceArgument()
    {
        DamlTemplate TemplateWithTransfer(string name) => new()
        {
            Name = name,
            Choices =
            [
                new DamlChoice
                {
                    Name = "Transfer",
                    Consuming = true,
                    ArgumentType = new DamlTypeRef("", "Test.Module", $"{name}Transfer"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                }
            ]
        };

        DamlDataType RecordType(string name) => new()
        {
            Name = name,
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
        };

        return new DamlModule
        {
            Name = "Test.Module",
            Templates = [TemplateWithTransfer("KeepAsset"), TemplateWithTransfer("SkipAsset")],
            DataTypes =
            [
                RecordType("KeepAsset"),
                RecordType("SkipAsset"),
                RecordType("KeepAssetTransfer"),
                RecordType("SkipAssetTransfer"),
            ],
            Interfaces = []
        };
    }

    private static DamlModule NestedTransferArgumentModule()
    {
        DamlFieldDefinition[] transferArgFields =
        [
            new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric)),
        ];

        return new DamlModule
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
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Transfer"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
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
                },
                new DamlDataType
                {
                    Name = "Transfer",
                    Definition = new DamlRecordDefinition(transferArgFields)
                },
            ],
            Interfaces = []
        };
    }

    [Fact]
    public void Generate_should_use_DamlUnit_for_Unit_argument()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Close",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "MyTemplate",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = new DamlModelBuilder().WithModule(module).WithDependency(StdlibStub).Build();
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var contractFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("MyTemplate.cs", StringComparison.Ordinal));

        // Assert
        contractFile.Should().NotBeNull();
        var code = contractFile!.Content;

        code.Should().Contain("global::Daml.Runtime.Commands.Choice<MyTemplate, global::Daml.Runtime.Data.DamlUnit, global::Daml.Runtime.Data.DamlUnit>");
        code.Should().Contain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance");
        code.Should().Contain("ResultDecoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance");
    }

    [Fact]
    public void Generate_should_encode_Archive_choice_argument_as_empty_record()
    {
        // Arrange - Archive is a special external type reference
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(StdlibStub.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "MyTemplate",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("data", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var dar = new DamlModelBuilder().WithModule(module).WithDependency(StdlibStub).Build();
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("MyTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("global::Daml.Runtime.Commands.Choice<MyTemplate, global::Daml.Runtime.Data.DamlUnit, global::Daml.Runtime.Data.DamlUnit>");
        code.Should().Contain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlRecord.Create()");
        code.Should().NotContain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance");
    }

    [Fact]
    public void Generate_should_decode_ContractId_result_correctly()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Factory",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Create",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "CreateArgs"),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("", "Test.Module", "Product")])
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Factory",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                },
                new DamlDataType
                {
                    Name = "CreateArgs",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("name", new DamlPrimitiveType(DamlPrimitive.Text))])
                },
                new DamlDataType
                {
                    Name = "Product",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces = []
        };

        var dar = new DamlModelBuilder().WithModule(module).WithDependency(StdlibStub).Build();
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var factoryFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Factory.cs", StringComparison.Ordinal));

        // Assert
        factoryFile.Should().NotBeNull();
        var code = factoryFile!.Content;

        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Factory, Create, global::Daml.Runtime.Contracts.ContractId<global::Test.Module.Product>>");
        code.Should().Contain("ResultDecoder = val => new global::Daml.Runtime.Contracts.ContractId<global::Test.Module.Product>(val.As<global::Daml.Runtime.Contracts.DamlContractId>().Value)");
    }

    [Fact]
    public void Generate_should_decode_primitive_results_correctly()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Counter",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetCount",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64)
                        },
                        new DamlChoice
                        {
                            Name = "GetName",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Text)
                        },
                        new DamlChoice
                        {
                            Name = "IsValid",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Bool)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Counter",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64))])
                }
            ],
            Interfaces = []
        };

        var dar = new DamlModelBuilder().WithModule(module).WithDependency(StdlibStub).Build();
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var counterFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Counter.cs", StringComparison.Ordinal));

        // Assert
        counterFile.Should().NotBeNull();
        var code = counterFile!.Content;

        // Check primitive result decoders
        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Counter, global::Daml.Runtime.Data.DamlUnit, long> ChoiceGetCount");
        code.Should().Contain("ResultDecoder = val => val.As<global::Daml.Runtime.Data.DamlInt64>().Value");

        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Counter, global::Daml.Runtime.Data.DamlUnit, string> ChoiceGetName");
        code.Should().Contain("ResultDecoder = val => val.As<global::Daml.Runtime.Data.DamlText>().Value");

        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Counter, global::Daml.Runtime.Data.DamlUnit, bool> ChoiceIsValid");
        code.Should().Contain("ResultDecoder = val => val.As<global::Daml.Runtime.Data.DamlBool>().Value");
    }

    [Fact]
    public void Generate_should_handle_multiple_choices_with_different_arg_types()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Choices =
                    [
                        // Local data type argument
                        new DamlChoice
                        {
                            Name = "Split",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Split"),
                            ReturnType = new DamlTypeRef("", "Test.Module", "Split_Result")
                        },
                        // Unit argument
                        new DamlChoice
                        {
                            Name = "GetBalance",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric)
                        },
                        // External Archive argument
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(StdlibStub.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))])
                },
                new DamlDataType
                {
                    Name = "Split",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("splitAmount", new DamlPrimitiveType(DamlPrimitive.Numeric))])
                },
                new DamlDataType
                {
                    Name = "Split_Result",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces = []
        };

        var dar = new DamlModelBuilder().WithModule(module).WithDependency(StdlibStub).Build();
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holdingFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal));

        // Assert
        holdingFile.Should().NotBeNull();
        var code = holdingFile!.Content;

        // Split - uses local data type
        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Holding, Split, global::Test.Module.Split_Result> ChoiceSplit");
        code.Should().Contain("ArgumentEncoder = arg => arg.ToRecord(),");

        // GetBalance - uses Unit argument
        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Holding, global::Daml.Runtime.Data.DamlUnit, decimal> ChoiceGetBalance");

        // Archive - DamlUnit type parameters, but the wire argument encodes as an empty DamlRecord
        code.Should().Contain("global::Daml.Runtime.Commands.Choice<Holding, global::Daml.Runtime.Data.DamlUnit, global::Daml.Runtime.Data.DamlUnit> ChoiceArchive");
    }

    [Fact]
    public void Generate_should_keep_choice_argument_fields_when_simple_names_collide_across_modules()
    {
        var agreementModule = ModuleWithExpireChoice(
            "Test.Agreement",
            "Agreement",
            [new DamlFieldDefinition("actor", new DamlPrimitiveType(DamlPrimitive.Party))]);
        var offerModule = ModuleWithExpireChoice("Test.Offer", "Offer", []);

        var dar = new DamlModelBuilder()
            .WithModules(agreementModule, offerModule)
            .WithDependency(StdlibStub)
            .Build();
        var generator = CreateGenerator();

        var files = generator.Generate(dar).ToList();
        var agreementExpire = files.FirstOrDefault(f => f.RelativePath.EndsWith("Agreement.Expire.cs", StringComparison.Ordinal));
        var offerExpire = files.FirstOrDefault(f => f.RelativePath.EndsWith("Offer.Expire.cs", StringComparison.Ordinal));

        agreementExpire.Should().NotBeNull();
        agreementExpire!.Content.Should().Contain(
            "public sealed record Expire(\n        [property: global::Daml.Runtime.Data.DamlFieldAttribute(\"actor\")] global::Daml.Runtime.Data.Party Actor\n    ) : global::Daml.Runtime.Data.IDamlRecord",
            "the Agreement module's Expire argument record must keep its own fields instead of the Offer module's");
        offerExpire.Should().NotBeNull();
        offerExpire!.Content.Should().Contain(
            "public sealed record Expire : global::Daml.Runtime.Data.IDamlRecord",
            "the Offer module's Expire argument record is genuinely field-less");
        offerExpire.Content.Should().NotContain("Actor");
    }

    private static DamlModule ModuleWithExpireChoice(
        string moduleName,
        string templateName,
        DamlFieldDefinition[] expireFields) =>
        new()
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Expire",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", moduleName, "Expire"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = templateName,
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                },
                new DamlDataType
                {
                    Name = "Expire",
                    Definition = new DamlRecordDefinition(expireFields)
                },
            ],
            Interfaces = []
        };
}
