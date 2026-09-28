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
using PbTypeConName = Daml.Codegen.Intermediate.TypeConName;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Reader-boundary normalization of applied catalog builtins (the applied-type-nodes
/// milestone): a <c>TypeApp</c> over one of the five folding builtins — List, Optional,
/// TextMap, GenMap, ContractId — reads into its first-class typed node, with the exact
/// node type asserted at every nesting level, an arity disagreeing with the catalog
/// rejected as malformed, and every non-folding application (the Numeric scale pun,
/// user-defined type constructors, the signature-only structural formers) staying a
/// generic <see cref="DamlTypeApp"/>.
/// </summary>
public partial class IntermediateDarReaderTests
{
    [Fact]
    public void IntermediateDarReader_nested_builtins_read_as_typed_nodes_at_every_level()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Nested",
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "tree",
                        Type = App(
                            PbBuiltinType.List,
                            App(
                                PbBuiltinType.Optional,
                                App(PbBuiltinType.TextMap, Builtin(PbBuiltinType.Int64)))),
                    },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        var list = record.Fields[0].Type.Should().BeOfType<DamlListType>().Subject;
        var optional = list.Element.Should().BeOfType<DamlOptionalType>().Subject;
        var textMap = optional.Value.Should().BeOfType<DamlTextMapType>().Subject;
        textMap.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public void IntermediateDarReader_contract_id_application_reads_as_contract_id_node()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Holding",
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "cid",
                        Type = App(
                            PbBuiltinType.ContractId,
                            new PbType
                            {
                                TypeCon = new PbTypeConName
                                {
                                    ModuleNameSegments = { "Foo" },
                                    NameSegments = { "Asset" },
                                },
                            }),
                    },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        var contractId = record.Fields[0].Type.Should().BeOfType<DamlContractIdType>().Subject;
        var payload = contractId.Payload.Should().BeOfType<DamlTypeRef>().Subject;
        payload.Module.Should().Be("Foo");
        payload.Name.Should().Be("Asset");
    }

    [Fact]
    public void IntermediateDarReader_gen_map_application_keeps_key_and_value_positional()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Ledger",
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "balances",
                        Type = App(
                            PbBuiltinType.GenMap,
                            Builtin(PbBuiltinType.Party),
                            Builtin(PbBuiltinType.Int64)),
                    },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        var genMap = record.Fields[0].Type.Should().BeOfType<DamlGenMapType>().Subject;
        genMap.Key.Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        genMap.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public void IntermediateDarReader_curried_type_app_flattens_before_folding()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Curried",
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "balances",
                        Type = new PbType
                        {
                            TypeApp = new TypeApp
                            {
                                Function = App(PbBuiltinType.GenMap, Builtin(PbBuiltinType.Party)),
                                Arguments = { Builtin(PbBuiltinType.Int64) },
                            },
                        },
                    },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        record.Fields[0].Type.Should().Be(
            new DamlGenMapType(
                new DamlPrimitiveType(DamlPrimitive.Party),
                new DamlPrimitiveType(DamlPrimitive.Int64)),
            "a curried application spells the same complete Daml type as the flat one, so both " +
            "boundaries must read it into the same single typed node — the parser boundary flattens " +
            "curried TApp chains before folding, and the proto boundary must agree with it");
    }

    /// <summary>
    /// The catalog's five folding builtins, each paired with the typed node its
    /// correctly-applied wire shape reads into — eligibility is the five-node set, not
    /// "arity &gt; 0" (applied Numeric stays generic; see below).
    /// </summary>
    public static TheoryData<PbBuiltinType, DamlType, PbType[]> FoldingBuiltinsWithTheirNodes() => new()
    {
        {
            PbBuiltinType.List,
            new DamlListType(new DamlPrimitiveType(DamlPrimitive.Text)),
            [Builtin(PbBuiltinType.Text)]
        },
        {
            PbBuiltinType.Optional,
            new DamlOptionalType(new DamlPrimitiveType(DamlPrimitive.Text)),
            [Builtin(PbBuiltinType.Text)]
        },
        {
            PbBuiltinType.TextMap,
            new DamlTextMapType(new DamlPrimitiveType(DamlPrimitive.Text)),
            [Builtin(PbBuiltinType.Text)]
        },
        {
            PbBuiltinType.ContractId,
            new DamlContractIdType(new DamlPrimitiveType(DamlPrimitive.Text)),
            [Builtin(PbBuiltinType.Text)]
        },
        {
            PbBuiltinType.GenMap,
            new DamlGenMapType(
                new DamlPrimitiveType(DamlPrimitive.Party),
                new DamlPrimitiveType(DamlPrimitive.Int64)),
            [Builtin(PbBuiltinType.Party), Builtin(PbBuiltinType.Int64)]
        },
    };

    [Theory]
    [MemberData(nameof(FoldingBuiltinsWithTheirNodes))]
    public void IntermediateDarReader_folding_builtin_application_reads_as_its_typed_node(
        PbBuiltinType builtin,
        DamlType expectedNode,
        PbType[] wireArguments)
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Applied",
            Record = new PbRecord { Fields = { new PbField { Name = "value", Type = App(builtin, wireArguments) } } },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        record.Fields[0].Type.Should().Be(expectedNode);
    }

    /// <summary>
    /// Every argument count other than the catalog arity for each of the five folding
    /// builtins — the catalog's arity table is the oracle, so the pairs are derived from
    /// <see cref="DamlPrimitiveCatalog"/> rather than restated here.
    /// </summary>
    public static TheoryData<PbBuiltinType, int> FoldingBuiltinsWithWrongArgumentCounts()
    {
        var data = new TheoryData<PbBuiltinType, int>();
        foreach (var row in DamlPrimitiveCatalog.Rows.Where(IsFoldingRow))
        {
            for (var count = 0; count <= row.Arity + 1; count++)
            {
                if (count != row.Arity)
                {
                    data.Add(row.Builtin, count);
                }
            }
        }
        return data;
    }

    private static bool IsFoldingRow(DamlPrimitiveCatalogRow row) => row.Primitive is
        DamlPrimitive.List or DamlPrimitive.Optional or DamlPrimitive.TextMap
        or DamlPrimitive.GenMap or DamlPrimitive.ContractId;

    [Theory]
    [MemberData(nameof(FoldingBuiltinsWithWrongArgumentCounts))]
    public void IntermediateDarReader_wrong_arity_application_throws_invalid_data(
        PbBuiltinType builtin,
        int argumentCount)
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Malformed",
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "value",
                        Type = App(builtin, Enumerable.Repeat(Builtin(PbBuiltinType.Text), argumentCount).ToArray()),
                    },
                },
            },
        }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>()
            .WithMessage($"*'{builtin}'*catalog arity*",
                "a folding builtin applied to the wrong number of arguments names no Daml type — " +
                "the catalog arity table is the oracle and the intermediate is malformed input");
    }

    [Fact]
    public void IntermediateDarReader_partial_folding_builtin_subexpression_stays_generic_without_throwing()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Partial",
            IsSerializable = false,
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "balances",
                        Type = App(
                            PbBuiltinType.List,
                            App(PbBuiltinType.GenMap, Builtin(PbBuiltinType.Party))),
                    },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        var list = record.Fields[0].Type.Should().BeOfType<DamlListType>().Subject;
        list.Element.Should().Be(
            new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.GenMap),
                [new DamlPrimitiveType(DamlPrimitive.Party)]),
            "a partially applied folding builtin is a legal subexpression, not a malformed complete " +
            "type: the Daml-LF interner deduplicates partial subterms, and shipped stdlib DALFs carry " +
            "GenMap k nested inside another application. Reading must neither throw nor half-fold — " +
            "the one-argument GenMap stays the generic application the referencing context completes, " +
            "while the root List folds into its typed node exactly as a complete position must");
    }

    [Fact]
    public void IntermediateDarReader_numeric_application_stays_generic()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Amount",
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "scale",
                        Type = App(PbBuiltinType.Numeric, new PbType { Nat = 10 }),
                    },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        var app = record.Fields[0].Type.Should().BeOfType<DamlTypeApp>().Subject;
        app.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Numeric));
        app.Arguments.Should().ContainSingle()
            .Which.Should().Be(new DamlTypeVar("10"),
                "Numeric's argument is the Nat pun — folding eligibility is the five-node set, deliberately " +
                "excluding Numeric even though its catalog arity is 1");
    }

    [Fact]
    public void IntermediateDarReader_user_type_con_application_stays_generic()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Boxed",
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "boxed",
                        Type = new PbType
                        {
                            TypeApp = new TypeApp
                            {
                                Function = new PbType
                                {
                                    TypeCon = new PbTypeConName
                                    {
                                        ModuleNameSegments = { "Foo" },
                                        NameSegments = { "Box" },
                                    },
                                },
                                Arguments = { Builtin(PbBuiltinType.Text) },
                            },
                        },
                    },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        var app = record.Fields[0].Type.Should().BeOfType<DamlTypeApp>().Subject;
        var typeRef = app.Base.Should().BeOfType<DamlTypeRef>().Subject;
        typeRef.Name.Should().Be("Box");
        app.Arguments.Should().Equal(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public void IntermediateDarReader_signature_only_applications_stay_generic()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Signature",
            IsSerializable = false,
            Record = new PbRecord
            {
                Fields =
                {
                    new PbField
                    {
                        Name = "update",
                        Type = App(PbBuiltinType.Update, Builtin(PbBuiltinType.Text)),
                    },
                    new PbField
                    {
                        Name = "arrow",
                        Type = App(PbBuiltinType.Arrow, Builtin(PbBuiltinType.Party), Builtin(PbBuiltinType.Text)),
                    },
                },
            },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        var update = record.Fields[0].Type.Should().BeOfType<DamlTypeApp>().Subject;
        update.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Update));
        update.Arguments.Should().Equal(new DamlPrimitiveType(DamlPrimitive.Text));
        var arrow = record.Fields[1].Type.Should().BeOfType<DamlTypeApp>().Subject;
        arrow.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Arrow));
        arrow.Arguments.Should().Equal(
            new DamlPrimitiveType(DamlPrimitive.Party),
            new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public void IntermediateDarReader_bare_folding_builtin_reads_as_bare_primitive()
    {
        var proto = MakePackageWith(module => module.DataTypes.Add(new PbDataType
        {
            Name = "Bare",
            IsSerializable = false,
            Record = new PbRecord { Fields = { new PbField { Name = "head", Type = Builtin(PbBuiltinType.List) } } },
        }));

        var model = IntermediateDarReader.Read(proto);
        var record = (DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition;

        record.Fields[0].Type.Should().Be(new DamlPrimitiveType(DamlPrimitive.List),
            "a Type carrying the builtin and no type_app is a bare primitive, not an application — " +
            "the arity oracle governs applications, and the bare form stays the legal head a curried " +
            "TypeApp chain applies");
    }
}
