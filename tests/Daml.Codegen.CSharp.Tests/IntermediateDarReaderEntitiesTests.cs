// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbChoice = Daml.Codegen.Intermediate.Choice;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbInterface = Daml.Codegen.Intermediate.Interface;
using PbInterfaceMethod = Daml.Codegen.Intermediate.InterfaceMethod;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbTemplate = Daml.Codegen.Intermediate.Template;
using PbType = Daml.Codegen.Intermediate.Type;
using PbTypeApp = Daml.Codegen.Intermediate.TypeApp;
using PbTypeConName = Daml.Codegen.Intermediate.TypeConName;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    [Fact]
    public void IntermediateDarReader_template_round_trips_with_choices_and_payload_record()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Iou",
                Record = new PbRecord
                {
                    Fields = { TextField("issuer"), TextField("currency") },
                },
            });
            module.Templates.Add(new PbTemplate
            {
                Name = "Iou",
                Choices =
                {
                    new PbChoice
                    {
                        Name = "Transfer",
                        Consuming = true,
                        ArgumentType = new PbType { Builtin = PbBuiltinType.Unit },
                        ReturnType = new PbType { Builtin = PbBuiltinType.Unit },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var module = model.MainPackage.Modules[0];
        var template = module.Templates.Single();
        template.Name.Should().Be("Iou");
        template.Choices.Should().HaveCount(1);
        template.Choices[0].Name.Should().Be("Transfer");
        template.Choices[0].Consuming.Should().BeTrue();
        var payload = module.DataTypes.Single(dt => dt.Name == "Iou");
        payload.Definition.Should().BeOfType<DamlRecordDefinition>()
            .Which.Fields.Select(f => f.Name).Should().Equal("issuer", "currency");
    }

    [Fact]
    public void IntermediateDarReader_interface_round_trips_with_view_type_and_choices()
    {
        var proto = MakePackageWith(module =>
        {
            module.Interfaces.Add(new PbInterface
            {
                Name = "Holding",
                ViewType = new PbType
                {
                    TypeCon = new PbTypeConName
                    {
                        PackageId = "self",
                        ModuleNameSegments = { "Foo" },
                        NameSegments = { "HoldingView" },
                    },
                },
                Choices =
                {
                    new PbChoice
                    {
                        Name = "Split",
                        Consuming = false,
                        ArgumentType = new PbType { Builtin = PbBuiltinType.Int64 },
                        ReturnType = new PbType { Builtin = PbBuiltinType.Unit },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var iface = model.MainPackage.Modules[0].Interfaces.Single();
        iface.Name.Should().Be("Holding");
        iface.ViewType.Should().BeOfType<DamlTypeRef>()
            .Which.Name.Should().Be("HoldingView");
        iface.Choices.Should().HaveCount(1);
        iface.Choices[0].Name.Should().Be("Split");
    }

    [Fact]
    public void IntermediateDarReader_template_with_no_key_type_yields_null_key()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "T",
                Record = new PbRecord { Fields = { TextField("v") } },
            });
            module.Templates.Add(new PbTemplate { Name = "T" });
        });

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.Modules[0].Templates.Single().Key.Should().BeNull();
    }

    [Fact]
    public void IntermediateDarReader_template_with_key_type_round_trips()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "T",
                Record = new PbRecord { Fields = { TextField("v") } },
            });
            module.Templates.Add(new PbTemplate
            {
                Name = "T",
                KeyType = new PbType { Builtin = PbBuiltinType.Party },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var key = model.MainPackage.Modules[0].Templates.Single().Key;
        key.Should().BeOfType<DamlPrimitiveType>()
            .Which.Primitive.Should().Be(DamlPrimitive.Party);
    }

    [Fact]
    public void IntermediateDarReader_interface_with_no_view_type_yields_null_view()
    {
        var proto = MakePackageWith(module =>
        {
            module.Interfaces.Add(new PbInterface { Name = "I" });
        });

        var model = IntermediateDarReader.Read(proto);
        model.MainPackage.Modules[0].Interfaces.Single().ViewType.Should().BeNull();
    }

    [Fact]
    public void IntermediateDarReader_interface_round_trips_methods_in_wire_order()
    {
        // Deliberately NOT ordinal-sorted: the reader must reproduce the wire
        // order verbatim, not sort or reorder. The return types cover the three
        // shapes interface methods carry on the wire: a bare builtin, an applied
        // builtin over a same-package type constructor, and a cross-package
        // type constructor.
        var proto = MakePackageWith(module =>
        {
            module.Interfaces.Add(new PbInterface
            {
                Name = "Holding",
                Methods =
                {
                    new PbInterfaceMethod
                    {
                        Name = "lock",
                        ReturnType = new PbType
                        {
                            TypeApp = new PbTypeApp
                            {
                                Function = new PbType { Builtin = PbBuiltinType.Optional },
                                Arguments =
                                {
                                    new PbType
                                    {
                                        TypeCon = new PbTypeConName
                                        {
                                            PackageId = "pkg-id-1",
                                            ModuleNameSegments = { "Foo" },
                                            NameSegments = { "Lock" },
                                        },
                                    },
                                },
                            },
                        },
                    },
                    new PbInterfaceMethod
                    {
                        Name = "owner",
                        ReturnType = new PbType { Builtin = PbBuiltinType.Party },
                    },
                    new PbInterfaceMethod
                    {
                        Name = "meta",
                        ReturnType = new PbType
                        {
                            TypeCon = new PbTypeConName
                            {
                                PackageId = "meta-pkg",
                                ModuleNameSegments = { "Splice", "Api", "Token", "MetadataV1" },
                                NameSegments = { "TokenMetadata" },
                            },
                        },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var methods = model.MainPackage.Modules[0].Interfaces.Single().Methods;

        methods.Should().HaveCount(3);
        methods.Select(m => m.Name).Should().Equal(["lock", "owner", "meta"],
            "the reader preserves the proto's wire order — entries come back exactly as laid out, not re-sorted");

        methods[0].ReturnType.Should().BeOfType<DamlOptionalType>()
            .Which.Value.Should().Be(new DamlTypeRef("pkg-id-1", "Foo", "Lock"),
                "an Optional-typed method return folds into its typed node like any other " +
                "application of a folding builtin");
        methods[1].ReturnType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        methods[2].ReturnType.Should().Be(new DamlTypeRef("meta-pkg", "Splice.Api.Token.MetadataV1", "TokenMetadata"));
    }

    [Fact]
    public void IntermediateDarReader_interface_without_methods_reads_empty_methods()
    {
        var proto = MakePackageWith(module =>
        {
            module.Interfaces.Add(new PbInterface { Name = "I" });
        });

        var model = IntermediateDarReader.Read(proto);
        var iface = model.MainPackage.Modules[0].Interfaces.Single();

        iface.Methods.Should().NotBeNull(
            "an intermediate produced without the methods field (proto3 repeated-field absence — what every "
            + ".NET-DarParser-produced intermediate carries today) must read as an empty list, never null and never an error");
        iface.Methods.Should().BeEmpty();
    }

    [Fact]
    public void IntermediateDarReader_interface_method_with_missing_return_type_throws()
    {
        var proto = MakePackageWith(module =>
        {
            module.Interfaces.Add(new PbInterface
            {
                Name = "Holding",
                Methods = { new PbInterfaceMethod { Name = "owner" } },
            });
        });

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*owner*return_type*");
    }

    [Fact]
    public void IntermediateDarReader_interface_methods_with_update_and_arrow_returns_read_real_identities()
    {
        // The common Daml shape is `method foo : Update X`; lock-style helper
        // methods mention ARROW. These are signature-only builtins (catalog
        // disposition SignatureOnly — the M2 catalog), and the reader must give
        // them their real DamlPrimitive identity rather than erasing to Unit or
        // throwing. No synthetic-free corpus covers this at a controllable
        // size, so the proto is constructed here directly.
        var proto = MakePackageWith(module =>
        {
            module.Interfaces.Add(new PbInterface
            {
                Name = "Holding",
                Methods =
                {
                    new PbInterfaceMethod
                    {
                        Name = "lock",
                        ReturnType = new PbType
                        {
                            TypeApp = new PbTypeApp
                            {
                                Function = new PbType { Builtin = PbBuiltinType.Update },
                                Arguments = { new PbType { TypeCon = SelfLockTypeCon() } },
                            },
                        },
                    },
                    new PbInterfaceMethod
                    {
                        Name = "release",
                        ReturnType = new PbType
                        {
                            TypeApp = new PbTypeApp
                            {
                                Function = new PbType { Builtin = PbBuiltinType.Arrow },
                                Arguments =
                                {
                                    new PbType { Builtin = PbBuiltinType.Party },
                                    new PbType
                                    {
                                        TypeApp = new PbTypeApp
                                        {
                                            Function = new PbType { Builtin = PbBuiltinType.Update },
                                            Arguments = { new PbType { Builtin = PbBuiltinType.Int64 } },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            });
        });

        var model = IntermediateDarReader.Read(proto);
        var methods = model.MainPackage.Modules[0].Interfaces.Single().Methods;

        methods[0].ReturnType.Should().BeOfType<DamlTypeApp>().Which.Should().Be(
            new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Update),
                [new DamlTypeRef("pkg-id-1", "Foo", "Lock")]),
            "Update as an interface-method return type keeps its real identity — never Unit, never a throw");
        methods[1].ReturnType.Should().BeOfType<DamlTypeApp>().Which.Should().Be(
            new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Arrow),
                [
                    new DamlPrimitiveType(DamlPrimitive.Party),
                    new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Update),
                        [new DamlPrimitiveType(DamlPrimitive.Int64)]),
                ]),
            "an arrow-typed method return keeps its real identity — never Unit, never a throw");
    }

    private static PbTypeConName SelfLockTypeCon() => new()
    {
        PackageId = "pkg-id-1",
        ModuleNameSegments = { "Foo" },
        NameSegments = { "Lock" },
    };

    /// <summary>
    /// End-to-end proof over a checked-in JVM-produced intermediate: the
    /// transfer-instruction-v2 snapshot's main package declares real interface
    /// methods on the wire, and the reader surfaces them with their exact names
    /// in wire order and their signature-only return-type identities intact.
    /// Wire evidence (re-derivable by parsing the snapshot with the generated
    /// C# proto types — no system protoc; decoded 2026-09-15): the
    /// <c>TransferInstruction</c> interface carries 6 methods
    /// (transferInstruction_{accept,acceptImpl,reject,rejectImpl,withdraw,
    /// withdrawImpl}-style names, ordinal-sorted on the wire by the JVM
    /// producer) whose returns are ARROW applications embedding
    /// <c>Update (ContractId …)</c> and <c>[Party]</c>; <c>TransferFactory</c>
    /// carries 3 (transferFactory_{publicFetchImpl,transferExtraObservers,
    /// transferImpl}).
    /// <para>
    /// NOTE: the mission's validation contract originally anchored this
    /// end-to-end assertion on the splice-api-token-holding-v1 snapshot with
    /// methods owner/holders/lock/meta. That anchor is stale: a typed proto
    /// decode of the vendored holding-v1 binpb (and the M0 producer-readiness
    /// measurement)
    /// show its two interfaces carry NO methods on the wire — see
    /// <see cref="IntermediateDarReader_methodless_snapshot_interfaces_read_empty_methods"/>.
    /// The method-bearing checked-in corpus lives here instead.
    /// </para>
    /// </summary>
    [Fact]
    public void IntermediateDarReader_transfer_instruction_v2_snapshot_yields_wire_methods_with_real_identities()
    {
        var proto = ParseSnapshot("splice-api-token-transfer-instruction-v2");

        var model = IntermediateDarReader.Read(proto);
        var module = model.MainPackage.Modules.Single(m => m.Name == "Splice.Api.Token.TransferInstructionV2");

        var instruction = module.Interfaces.Single(i => i.Name == "TransferInstruction");
        instruction.Methods.Select(m => m.Name).Should().Equal(
        [
            "transferInstruction_acceptExtraObservers",
            "transferInstruction_acceptImpl",
            "transferInstruction_rejectExtraObservers",
            "transferInstruction_rejectImpl",
            "transferInstruction_withdrawExtraObservers",
            "transferInstruction_withdrawImpl",
        ], "exactly the wire set, in the JVM producer's wire (ordinal-sorted) order");

        var acceptImpl = instruction.Methods.Single(m => m.Name == "transferInstruction_acceptImpl");
        var arrow = acceptImpl.ReturnType.Should().BeOfType<DamlTypeApp>().Subject;
        arrow.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Arrow),
            "the ARROW head keeps its real identity end-to-end — Arrow is not a folding builtin, so " +
            "the application stays generic");
        arrow.Arguments.Should().HaveCount(2);
        arrow.Arguments[0].Should().BeOfType<DamlContractIdType>()
            .Which.Payload.Should().BeOfType<DamlTypeRef>();
        var innerArrow = arrow.Arguments[1].Should().BeOfType<DamlTypeApp>().Subject;
        innerArrow.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Arrow));
        innerArrow.Arguments.Should().HaveCount(2);
        innerArrow.Arguments[1].Should().BeOfType<DamlTypeApp>().Which.Base
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Update),
            "the embedded Update return keeps its real identity — the pre-catalog reader erased signature-only "
            + "builtins or threw on them; the model must carry them as-is");

        var factory = module.Interfaces.Single(i => i.Name == "TransferFactory");
        factory.Methods.Select(m => m.Name).Should().Equal(
        [
            "transferFactory_publicFetchImpl",
            "transferFactory_transferExtraObservers",
            "transferFactory_transferImpl",
        ]);
    }

    /// <summary>
    /// The snapshots whose interfaces declare no methods on the wire —
    /// splice-api-token-holding-v1's Holding (the validation contract's
    /// original end-to-end anchor, whose planning-time owner/holders/lock/meta
    /// method set is disproven by decode: the vendored upstream Splice 0.7.5
    /// holding-v1 DAR declares none) and splice-api-token-metadata-v1's
    /// AnyContract — must read with an empty, non-null Methods list.
    /// </summary>
    [Theory]
    [InlineData("splice-api-token-holding-v1", "Holding")]
    [InlineData("splice-api-token-metadata-v1", "AnyContract")]
    public void IntermediateDarReader_methodless_snapshot_interfaces_read_empty_methods(string snapshotName, string interfaceName)
    {
        var proto = ParseSnapshot(snapshotName);

        var model = IntermediateDarReader.Read(proto);
        var iface = model.MainPackage.Modules.SelectMany(m => m.Interfaces)
            .Single(i => i.Name == interfaceName);

        iface.Methods.Should().NotBeNull();
        iface.Methods.Should().BeEmpty(
            "{0} declares no interface methods on the wire, so the reader yields the empty list the proto carries",
            snapshotName);
    }

    private static IntermediateDar ParseSnapshot(string snapshotName)
    {
        var protoPath = Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName, "intermediate.binpb");
        return IntermediateDar.Parser.ParseFrom(File.ReadAllBytes(protoPath));
    }

    [Fact]
    public void IntermediateDarReader_duplicate_template_and_data_type_name_collision_does_not_throw()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "Iou",
                Record = new PbRecord { Fields = { TextField("issuer") } },
            });
            module.Templates.Add(new PbTemplate { Name = "Iou" });
        });

        var model = IntermediateDarReader.Read(proto);
        var module = model.MainPackage.Modules[0];
        module.Templates.Single().Name.Should().Be("Iou");
        module.DataTypes.Single().Definition.Should().BeOfType<DamlRecordDefinition>()
            .Which.Fields.Single().Name.Should().Be("issuer");
    }

    [Fact]
    public void IntermediateDarReader_choice_with_null_argument_type_throws()
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
                                Name = "T",
                                IsSerializable = true,
                                Record = new PbRecord(),
                            },
                        },
                        Templates =
                        {
                            new PbTemplate
                            {
                                Name = "T",
                                Choices =
                                {
                                    new PbChoice
                                    {
                                        Name = "Exercise",
                                        Consuming = true,
                                        ReturnType = new PbType { Builtin = PbBuiltinType.Unit },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Exercise*argument_type*");
    }

    [Fact]
    public void IntermediateDarReader_choice_with_null_return_type_throws()
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
                                Name = "T",
                                IsSerializable = true,
                                Record = new PbRecord(),
                            },
                        },
                        Templates =
                        {
                            new PbTemplate
                            {
                                Name = "T",
                                Choices =
                                {
                                    new PbChoice
                                    {
                                        Name = "Exercise",
                                        Consuming = true,
                                        ArgumentType = new PbType { Builtin = PbBuiltinType.Unit },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Exercise*return_type*");
    }
}
