// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class PackageContextTests
{
    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    public void GetString_throws_for_an_index_outside_the_interned_string_table(int index)
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.GetString(index);

        act.Should().Throw<InvalidDataException>()
            .WithMessage($"*interned-string index {index} is out of range of the interned-string table of size 0*");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void GetDottedName_throws_for_an_index_outside_the_interned_dotted_name_table(int index)
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.GetDottedName(index);

        act.Should().Throw<InvalidDataException>()
            .WithMessage($"*interned dotted-name index {index} is out of range of the interned dotted-name table of size 0*");
    }

    [Fact]
    public void GetDottedName_throws_when_a_segment_index_is_outside_the_interned_string_table()
    {
        var package = new Package();
        package.InternedStrings.Add("DA");
        var dottedName = new InternedDottedName();
        dottedName.SegmentsInternedStr.Add(0);
        dottedName.SegmentsInternedStr.Add(5);
        package.InternedDottedNames.Add(dottedName);
        var ctx = new PackageContext(package);

        var act = () => ctx.GetDottedName(0);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*interned-string index 5 is out of range of the interned-string table of size 1*");
    }

    [Fact]
    public void GetDottedName_joins_segments_with_dots()
    {
        var package = new Package();
        package.InternedStrings.Add("DA");
        package.InternedStrings.Add("Internal");
        package.InternedStrings.Add("Template");
        var dn = new InternedDottedName();
        dn.SegmentsInternedStr.Add(0);
        dn.SegmentsInternedStr.Add(1);
        dn.SegmentsInternedStr.Add(2);
        package.InternedDottedNames.Add(dn);

        new PackageContext(package).GetDottedName(0).Should().Be("DA.Internal.Template");
    }

    [Fact]
    public void GetType_rejects_an_out_of_range_interned_type_index()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.GetType(5);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*interned-type index 5*");
    }

    [Fact]
    public void GetInternedExpr_returns_null_for_out_of_bounds_index()
    {
        var ctx = new PackageContext(new Package());

        ctx.GetInternedExpr(2).Should().BeNull();
    }

    [Fact]
    public void GetInternedExpr_returns_the_expression_at_the_index()
    {
        var package = new Package();
        var expr = new Expr { VarInternedStr = 0 };
        package.InternedExprs.Add(expr);

        new PackageContext(package).GetInternedExpr(0).Should().BeSameAs(expr);
    }

    [Fact]
    public void PackageContext_interned_builtin_type_resolves_through_get_type()
    {
        var package = new Package();
        package.InternedTypes.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Party } });

        new PackageContext(package).GetType(0).Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
    }

    [Fact]
    public void PackageContext_interned_builtin_type_with_args_reads_as_typed_node()
    {
        var package = new Package();
        var optional = new Type.Types.Builtin { Builtin_ = BuiltinType.Optional };
        optional.Args.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } });
        package.InternedTypes.Add(new Type { Builtin = optional });

        new PackageContext(package).GetType(0).Should().Be(
            new DamlOptionalType(new DamlPrimitiveType(DamlPrimitive.Text)),
            "the interned pool is converted eagerly at construction, so its applied builtins fold " +
            "into typed nodes exactly as the same shape read through ConvertType does");
    }

    [Fact]
    public void PackageContext_interned_applied_list_resolves_to_list_node()
    {
        var package = new Package();
        var list = new Type.Types.Builtin { Builtin_ = BuiltinType.List };
        list.Args.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } });
        package.InternedTypes.Add(new Type { Builtin = list });

        new PackageContext(package).GetType(0).Should().Be(
            new DamlListType(new DamlPrimitiveType(DamlPrimitive.Int64)),
            "an interned Builtin(List) applied to Int64 is the same Daml type as the proto boundary's " +
            "TypeApp(List, [Int64]) — both boundaries read it into the identical DamlListType node");
    }

    [Fact]
    public void PackageContext_interned_nested_builtins_read_as_typed_nodes_at_every_level()
    {
        var package = new Package();
        var list = new Type.Types.Builtin { Builtin_ = BuiltinType.List };
        var optional = new Type.Types.Builtin { Builtin_ = BuiltinType.Optional };
        var textMap = new Type.Types.Builtin { Builtin_ = BuiltinType.Textmap };
        textMap.Args.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } });
        optional.Args.Add(new Type { Builtin = textMap });
        list.Args.Add(new Type { Builtin = optional });
        package.InternedTypes.Add(new Type { Builtin = list });

        var composed = new PackageContext(package).GetType(0);

        var listNode = composed.Should().BeOfType<DamlListType>().Subject;
        var optionalNode = listNode.Element.Should().BeOfType<DamlOptionalType>().Subject;
        var textMapNode = optionalNode.Value.Should().BeOfType<DamlTextMapType>().Subject;
        textMapNode.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public void PackageContext_interned_builtin_type_with_excessive_depth_throws_before_managed_stack_overflow()
    {
        var package = new Package();
        package.InternedTypes.Add(DalfTestFixtures.DeeplyNestedListType(depth: 300));

        var act = () => new PackageContext(package);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*maximum type depth of 256*");
    }

    [Fact]
    public void CollectTypeDependencies_rejects_a_type_nested_past_the_depth_bound()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.CollectTypeDependencies(
            DalfTestFixtures.DeeplyNestedListType(depth: 300), new HashSet<string>());

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*maximum type depth of 256*");
    }

    [Fact]
    public void PackageContext_interned_type_chain_composing_past_the_depth_bound_throws()
    {
        var package = PackageWithInternedListChain(length: 1000);

        var act = () => new PackageContext(package);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*maximum type depth of 256*");
    }

    [Fact]
    public void PackageContext_interned_type_chain_within_the_depth_bound_composes_to_the_full_graph()
    {
        var package = PackageWithInternedListChain(length: 256);

        var composed = new PackageContext(package).GetType(256);

        NestingDepth(composed).Should().Be(256);
    }

    [Fact]
    public void PackageContext_interned_type_var_with_args_resolves_to_application_over_var()
    {
        var package = new Package();
        package.InternedStrings.Add("f");
        var typeVar = new Type.Types.Var { VarInternedStr = 0 };
        typeVar.Args.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } });
        package.InternedTypes.Add(new Type { Var = typeVar });

        var app = new PackageContext(package).GetType(0).Should().BeOfType<DamlTypeApp>().Subject;
        app.Base.Should().Be(new DamlTypeVar("f"));
        app.Arguments.Should().ContainSingle().Which.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public void PackageContext_interned_con_type_with_imported_package_id_resolves_to_type_ref()
    {
        var package = new Package();
        package.InternedStrings.Add("imported-pkg");
        var moduleDn = new InternedDottedName();
        moduleDn.SegmentsInternedStr.Add(AddString(package, "Other"));
        package.InternedDottedNames.Add(moduleDn);
        var nameDn = new InternedDottedName();
        nameDn.SegmentsInternedStr.Add(AddString(package, "Foreign"));
        package.InternedDottedNames.Add(nameDn);

        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { ImportedPackageIdInternedStr = 0 },
                    ModuleNameInternedDname = 0,
                },
                NameInternedDname = 1,
            }
        };
        con.Args.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } });
        package.InternedTypes.Add(new Type { Con = con });

        var app = new PackageContext(package).GetType(0).Should().BeOfType<DamlTypeApp>().Subject;
        var typeRef = app.Base.Should().BeOfType<DamlTypeRef>().Subject;
        typeRef.PackageId.Should().Be("imported-pkg");
        typeRef.Module.Should().Be("Other");
        typeRef.Name.Should().Be("Foreign");
    }

    [Fact]
    public void PackageContext_interned_syn_type_with_args_resolves_to_type_application()
    {
        var package = new Package();
        var moduleDn = new InternedDottedName();
        moduleDn.SegmentsInternedStr.Add(AddString(package, "Syn"));
        package.InternedDottedNames.Add(moduleDn);
        var nameDn = new InternedDottedName();
        nameDn.SegmentsInternedStr.Add(AddString(package, "MyMap"));
        package.InternedDottedNames.Add(nameDn);

        var syn = new Type.Types.Syn
        {
            Tysyn = new TypeSynId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                    ModuleNameInternedDname = 0,
                },
                NameInternedDname = 1,
            }
        };
        syn.Args.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Party } });
        package.InternedTypes.Add(new Type { Syn = syn });

        var app = new PackageContext(package).GetType(0).Should().BeOfType<DamlTypeApp>().Subject;
        app.Base.Should().BeOfType<DamlTypeRef>().Which.Name.Should().Be("MyMap");
    }

    [Fact]
    public void PackageContext_interned_forall_type_unwraps_to_body()
    {
        var package = new Package();
        package.InternedStrings.Add("a");
        var forall = new Type.Types.Forall { Body = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Bool } } };
        forall.Vars.Add(new TypeVarWithKind { VarInternedStr = 0 });
        package.InternedTypes.Add(new Type { Forall = forall });

        new PackageContext(package).GetType(0).Should().Be(new DamlPrimitiveType(DamlPrimitive.Bool));
    }

    [Fact]
    public void PackageContext_interned_nat_type_resolves_to_its_scale_type_var()
    {
        var package = new Package();
        package.InternedTypes.Add(new Type { Nat = 10 });

        new PackageContext(package).GetType(0).Should().Be(new DamlTypeVar("10"));
    }

    [Fact]
    public void PackageContext_interned_curried_tapp_flattens_into_typed_gen_map_node()
    {
        var package = new Package();
        var inner = new Type { Tapp = new Type.Types.TApp
        {
            Lhs = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Genmap } },
            Rhs = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Party } },
        }};
        var outer = new Type { Tapp = new Type.Types.TApp
        {
            Lhs = inner,
            Rhs = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } },
        }};
        package.InternedTypes.Add(outer);

        new PackageContext(package).GetType(0).Should().Be(
            new DamlGenMapType(
                new DamlPrimitiveType(DamlPrimitive.Party),
                new DamlPrimitiveType(DamlPrimitive.Int64)),
            "the two curried TApps spell one complete GenMap application; the flatten that already " +
            "merged them into a single DamlTypeApp now continues into the typed node");
    }

    /// <summary>
    /// Every wrong argument count of at least one, for each of the five folding builtins,
    /// joined against the catalog rows that carry the arity oracle. The Daml-LF wire cannot
    /// spell a zero-argument application: <c>Builtin</c> with no args is the bare primitive
    /// (a legal curried-TApp head), and the zero-argument <c>TypeApp(List, [])</c> shape is
    /// pinned on the proto boundary, which can spell it.
    /// </summary>
    public static TheoryData<BuiltinType, int> FoldingBuiltinsWithWrongDalfArgumentCounts()
    {
        var data = new TheoryData<BuiltinType, int>();
        foreach (var lfBuiltin in new[]
                 { BuiltinType.List, BuiltinType.Optional, BuiltinType.Textmap, BuiltinType.ContractId, BuiltinType.Genmap })
        {
            var row = DamlPrimitiveCatalog.Get(DamlLfBuiltinBridge.ToProtoBuiltin(lfBuiltin));
            for (var count = 1; count <= row.Arity + 1; count++)
            {
                if (count != row.Arity)
                {
                    data.Add(lfBuiltin, count);
                }
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(FoldingBuiltinsWithWrongDalfArgumentCounts))]
    public void PackageContext_builtin_application_with_wrong_arity_throws_invalid_data(
        BuiltinType builtin,
        int argumentCount)
    {
        var row = DamlPrimitiveCatalog.Get(DamlLfBuiltinBridge.ToProtoBuiltin(builtin));
        var args = Enumerable.Repeat(
            new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } }, argumentCount);

        var builtinApplication = () => new PackageContext(new Package()).ConvertType(
            new Type { Builtin = new Type.Types.Builtin { Builtin_ = builtin, Args = { args } } });

        builtinApplication.Should().Throw<InvalidDataException>()
            .WithMessage($"*'{row.Primitive}'*catalog arity is {row.Arity}*",
                "a folding builtin applied to the wrong number of arguments names no Daml type — " +
                "the catalog arity table is the oracle and the archive is malformed");
    }

    [Theory]
    [MemberData(nameof(FoldingBuiltinsWithWrongDalfArgumentCounts))]
    public void PackageContext_curried_tapp_chain_with_wrong_arity_throws_invalid_data(
        BuiltinType builtin,
        int argumentCount)
    {
        Type type = new Type { Builtin = new Type.Types.Builtin { Builtin_ = builtin } };
        foreach (var _ in Enumerable.Range(0, argumentCount))
        {
            type = new Type { Tapp = new Type.Types.TApp
            {
                Lhs = type,
                Rhs = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } },
            }};
        }

        var finalType = type;
        var row = DamlPrimitiveCatalog.Get(DamlLfBuiltinBridge.ToProtoBuiltin(builtin));
        var act = () => new PackageContext(new Package()).ConvertType(finalType);

        act.Should().Throw<InvalidDataException>()
            .WithMessage($"*'{row.Primitive}'*catalog arity is {row.Arity}*",
                "a curried chain of N TApps spells one N-argument application; the arity the chain " +
                "accumulates is the arity the fold checks, so the one-argument GenMap chain — the " +
                "TypeApp(GenMap, [K]) shape — fails exactly as the under-applied Builtin form does");
    }

    [Fact]
    public void PackageContext_partial_folding_builtin_subexpression_stays_generic_without_throwing()
    {
        var partialGenMap = new Type { Tapp = new Type.Types.TApp
        {
            Lhs = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Genmap } },
            Rhs = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Party } },
        }};
        var list = new Type { Tapp = new Type.Types.TApp
        {
            Lhs = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.List } },
            Rhs = partialGenMap,
        }};

        var converted = new PackageContext(new Package()).ConvertType(list);

        converted.Should().Be(
            new DamlListType(new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.GenMap),
                [new DamlPrimitiveType(DamlPrimitive.Party)])),
            "a partially applied folding builtin is a legal subexpression, not a malformed complete " +
            "type: the Daml-LF interner deduplicates partial subterms, and shipped stdlib DALFs carry " +
            "GenMap k nested inside another application. The conversion must neither throw nor " +
            "half-fold — the one-argument GenMap stays the generic application the referencing context " +
            "completes, while the root List folds into its typed node exactly as a complete position " +
            "must, matching the proto boundary's lenient subexpression fold");
    }

    [Fact]
    public void PackageContext_bare_folding_builtin_stays_a_legal_application_head()
    {
        new PackageContext(new Package()).ConvertType(
                new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.List } })
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.List),
                "a bare Builtin(List) is the head a curried TApp applies, so the arity oracle governs " +
                "applications and the bare primitive stays legal — matching the proto boundary, where " +
                "a Type carrying only `builtin` reads as the bare primitive");
    }

    [Fact]
    public void PackageContext_interned_type_referencing_out_of_range_interned_index_throws_invalid_data()
    {
        var package = new Package();
        package.InternedTypes.Add(new Type { InternedType = 99 });

        var act = () => new PackageContext(package);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*interned-type index 99*");
    }

    [Fact]
    public void ConvertType_rejects_an_out_of_range_interned_type_index()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.ConvertType(new Type { InternedType = 3 });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*interned-type index 3*");
    }

    [Fact]
    public void CollectTypeDependencies_rejects_an_out_of_range_interned_type_index()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.CollectTypeDependencies(new Type { InternedType = 4 }, new HashSet<string>());

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*interned-type index 4*");
    }

    [Fact]
    public void CollectTypeDependencies_rejects_an_out_of_range_interned_type_index_nested_under_an_argument()
    {
        var package = new Package();
        package.InternedTypes.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } });
        var optional = new Type.Types.Builtin { Builtin_ = BuiltinType.Optional };
        optional.Args.Add(new Type { InternedType = 6 });

        var act = () => new PackageContext(package)
            .CollectTypeDependencies(new Type { Builtin = optional }, new HashSet<string>());

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*interned-type index 6*");
    }

    [Fact]
    public void ConvertType_rejects_a_negative_interned_type_index()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.ConvertType(new Type { InternedType = -1 });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*interned-type index -1*");
    }

    [Fact]
    public void PackageContext_interned_type_referencing_a_later_pool_entry_throws_during_construction()
    {
        var package = new Package();
        package.InternedTypes.Add(new Type { InternedType = 1 });
        package.InternedTypes.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } });

        var act = () => new PackageContext(package);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*interned-type index 1*",
                "interning is sequential, so an entry may only name a smaller index; index 1 is in range of "
                + "the finished pool but not of the pool as it stood while entry 0 was being converted");
    }

    [Fact]
    public void ConvertType_rejects_an_out_of_range_interned_string_package_id()
    {
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { ImportedPackageIdInternedStr = 4 },
                },
            },
        };

        var act = () => new PackageContext(new Package()).ConvertType(new Type { Con = con });

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*package-id string index 4*",
                "archives predating Daml-LF 2.2 carry no import table, so this arm is the only way they name an "
                + "imported package — resolving it to GetString's placeholder would mint '<unknown:4>' as a real package id");
    }

    [Fact]
    public void PackageContext_rejects_resolving_an_interned_struct_type_yet_accepts_holding_one()
    {
        var package = new Package();
        var structType = new Type.Types.Struct();
        structType.Fields.Add(new FieldWithType
        {
            FieldInternedStr = AddString(package, "inner"),
            Type = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } },
        });
        package.InternedTypes.Add(new Type { Struct = structType });

        var ctx = new PackageContext(package);

        var act = () => ctx.GetType(0);

        act.Should().Throw<InvalidDataException>().WithMessage("*struct type is not supported*");
    }

    [Fact]
    public void CollectTypeDependencies_walks_the_field_types_of_a_struct()
    {
        var package = new Package();
        var structType = new Type.Types.Struct();
        structType.Fields.Add(new FieldWithType
        {
            FieldInternedStr = AddString(package, "inner"),
            Type = ImportedCon(package, "struct-field-dep"),
        });
        var dependencies = new HashSet<string>();

        new PackageContext(package).CollectTypeDependencies(new Type { Struct = structType }, dependencies);

        dependencies.Should().BeEquivalentTo(["struct-field-dep"]);
    }

    [Fact]
    public void CollectTypeDependencies_resolves_a_package_id_named_by_the_import_table_index()
    {
        var package = new Package
        {
            PackageImports = new PackageImports { ImportedPackages = { "table-dep-a", "table-dep-b" } },
        };
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId { PackageImportId = 1 } },
            },
        };
        var dependencies = new HashSet<string>();

        new PackageContext(package).CollectTypeDependencies(new Type { Con = con }, dependencies);

        dependencies.Should().BeEquivalentTo(["table-dep-b"]);
    }

    [Fact]
    public void CollectTypeDependencies_rejects_a_package_import_index_out_of_range()
    {
        var package = new Package
        {
            PackageImports = new PackageImports { ImportedPackages = { "table-dep-a" } },
        };
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId { PackageImportId = 5 } },
            },
        };

        var act = () => new PackageContext(package).CollectTypeDependencies(new Type { Con = con }, new HashSet<string>());

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*package-import index 5*");
    }

    [Fact]
    public void ConvertType_rejects_a_con_type_whose_package_import_index_is_out_of_range()
    {
        var package = new Package
        {
            PackageImports = new PackageImports { ImportedPackages = { "table-dep-a" } },
        };
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId { PackageImportId = 5 } },
            },
        };

        var act = () => new PackageContext(package).ConvertType(new Type { Con = con });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*package-import index 5*");
    }

    [Fact]
    public void ConvertType_rejects_a_syn_type_whose_package_import_index_is_out_of_range()
    {
        var package = new Package
        {
            PackageImports = new PackageImports { ImportedPackages = { "table-dep-a" } },
        };
        var syn = new Type.Types.Syn
        {
            Tysyn = new TypeSynId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId { PackageImportId = 5 } },
            },
        };

        var act = () => new PackageContext(package).ConvertType(new Type { Syn = syn });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*package-import index 5*");
    }

    [Fact]
    public void ConvertType_rejects_a_package_import_reference_when_the_package_has_no_import_table()
    {
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId { PackageImportId = 0 } },
            },
        };

        var act = () => new PackageContext(new Package()).ConvertType(new Type { Con = con });

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*carries no package-import table*",
                "both index messages contain the index itself, so asserting only on that would pass "
                + "whichever diagnostic fired and never prove the missing-table case was distinguished");
    }

    [Theory]
    [InlineData(BuiltinType.Arrow, DamlPrimitive.Arrow)]
    [InlineData(BuiltinType.Update, DamlPrimitive.Update)]
    [InlineData(BuiltinType.FailureCategory, DamlPrimitive.FailureCategory)]
    [InlineData(BuiltinType.TypeRep, DamlPrimitive.TypeRep)]
    [InlineData(BuiltinType.Any, DamlPrimitive.Any)]
    [InlineData(BuiltinType.AnyException, DamlPrimitive.AnyException)]
    public void PackageContext_interned_structural_type_former_resolves_to_its_real_primitive_identity(
        BuiltinType builtin,
        DamlPrimitive expected)
    {
        var package = new Package();
        package.InternedTypes.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = builtin } });

        new PackageContext(package).GetType(0).Should().Be(new DamlPrimitiveType(expected),
            "the catalog carries the structural type-formers as SignatureOnly with real identity — " +
            "the parser must never erase a type the archive declares to Unit");
    }

    [Theory]
    [InlineData(BuiltinType.Bignumeric)]
    [InlineData(BuiltinType.RoundingMode)]
    public void PackageContext_interned_value_level_deferred_builtin_throws_not_supported(BuiltinType builtin)
    {
        var package = new Package();
        package.InternedTypes.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = builtin } });

        var act = () => new PackageContext(package).GetType(0);

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{builtin}*");
    }

    /// <summary>
    /// Every defined Daml-LF <c>BuiltinType</c> value, joined against the catalog row of
    /// its bridged intermediate-proto value: the parser's builtin conversion must be
    /// exactly the catalog's, with no silent fall-through — the Daml-LF enum is a
    /// different enum from the proto one the catalog keys on, so this pins the
    /// <see cref="DamlLfBuiltinBridge"/> stays complete against both.
    /// </summary>
    public static TheoryData<BuiltinType> AllDamlLfBuiltinValues()
    {
        var data = new TheoryData<BuiltinType>();
        foreach (var value in System.Enum.GetValues<BuiltinType>())
        {
            data.Add(value);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllDamlLfBuiltinValues))]
    public void PackageContext_builtin_conversion_matches_the_catalog_for_every_daml_lf_value(BuiltinType builtin)
    {
        var row = DamlPrimitiveCatalog.Get(DamlLfBuiltinBridge.ToProtoBuiltin(builtin));

        DamlType? converted = null;
        var act = () => converted = new PackageContext(new Package()).ConvertType(
            new Type { Builtin = new Type.Types.Builtin { Builtin_ = builtin } });

        switch (row.Disposition)
        {
            case DamlPrimitiveDisposition.SupportedValue:
            case DamlPrimitiveDisposition.SignatureOnly:
                act.Should().NotThrow(because:
                    $"the catalog records {builtin} as {row.Disposition}, so the parser must resolve it, not reject it");
                converted.Should().Be(new DamlPrimitiveType(row.Primitive!.Value),
                    "no Daml-LF builtin may map to a primitive other than its catalog row's");
                break;

            case DamlPrimitiveDisposition.Unsupported:
                act.Should().Throw<NotSupportedException>(because:
                    $"the catalog records {builtin} as Unsupported, so the parser must reject it loudly")
                    .WithMessage($"*{builtin}*");
                break;

            default:
                throw new InvalidOperationException(
                    $"No parser behavior is defined for catalog disposition '{row.Disposition}'.");
        }
    }

    [Fact]
    public void PackageContext_unknown_daml_lf_builtin_value_throws_not_supported()
    {
        var act = () => new PackageContext(new Package()).ConvertType(
            new Type { Builtin = new Type.Types.Builtin { Builtin_ = (BuiltinType)999 } });

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*999*unknown*",
                "a Daml-LF BuiltinType value outside the defined values is wire data from a newer Daml-LF " +
                "schema than this parser understands — it must fail loudly, not guess a mapping");
    }

    [Fact]
    public void PackageContext_type_tree_of_structural_builtins_produces_no_unit_leaf()
    {
        var tree = TreeOfAllStructuralTypeFormersOverNonUnitLeaves();

        var converted = new PackageContext(new Package()).ConvertType(tree);

        var leaves = PrimitiveLeaves(converted).ToList();
        leaves.Should().NotContain(DamlPrimitive.Unit,
            "Unit is a real Daml type: a tree whose input contains no UNIT builtin must never gain a " +
            "Unit leaf — before the catalog, the parser erased every structural type-former to Unit");
        leaves.Should().Contain([
            DamlPrimitive.Arrow, DamlPrimitive.Update, DamlPrimitive.Any,
            DamlPrimitive.List, DamlPrimitive.TypeRep, DamlPrimitive.FailureCategory,
            DamlPrimitive.Text, DamlPrimitive.Optional, DamlPrimitive.AnyException],
            "every structural type-former in the input keeps its real identity in the converted model");
    }

    [Fact]
    public void PackageContext_unit_builtin_input_still_maps_to_unit()
    {
        var converted = new PackageContext(new Package())
            .ConvertType(Builtin(BuiltinType.Unit));

        converted.Should().Be(new DamlPrimitiveType(DamlPrimitive.Unit),
            "the no-erasure regression must not break the real mapping: an actual Daml () / UNIT " +
            "builtin input keeps resolving to Unit");
    }

    private static Type TreeOfAllStructuralTypeFormersOverNonUnitLeaves()
    {
        var innerArrow = Builtin(BuiltinType.Arrow,
            Builtin(BuiltinType.Update, Builtin(BuiltinType.Any)),
            Builtin(BuiltinType.List, Builtin(BuiltinType.TypeRep)));
        var lhs = new Type { Tapp = new Type.Types.TApp { Lhs = Builtin(BuiltinType.Arrow, Builtin(BuiltinType.Text), innerArrow), Rhs = Builtin(BuiltinType.FailureCategory) } };
        return new Type
        {
            Tapp = new Type.Types.TApp
            {
                Lhs = lhs,
                Rhs = Builtin(BuiltinType.Optional, Builtin(BuiltinType.AnyException)),
            },
        };
    }

    private static Type Builtin(BuiltinType builtin, params Type[] args)
    {
        var type = new Type.Types.Builtin { Builtin_ = builtin };
        type.Args.AddRange(args);
        return new Type { Builtin = type };
    }

    private static IEnumerable<DamlPrimitive> PrimitiveLeaves(DamlType type)
    {
        switch (type)
        {
            case DamlPrimitiveType primitive:
                yield return primitive.Primitive;
                break;
            case DamlTypeApp app:
                foreach (var leaf in PrimitiveLeaves(app.Base))
                {
                    yield return leaf;
                }
                foreach (var argument in app.Arguments)
                {
                    foreach (var leaf in PrimitiveLeaves(argument))
                    {
                        yield return leaf;
                    }
                }
                break;
            case DamlListType list:
                yield return DamlPrimitive.List;
                foreach (var leaf in PrimitiveLeaves(list.Element))
                {
                    yield return leaf;
                }
                break;
            case DamlOptionalType optional:
                yield return DamlPrimitive.Optional;
                foreach (var leaf in PrimitiveLeaves(optional.Value))
                {
                    yield return leaf;
                }
                break;
            case DamlTextMapType textMap:
                yield return DamlPrimitive.TextMap;
                foreach (var leaf in PrimitiveLeaves(textMap.Value))
                {
                    yield return leaf;
                }
                break;
            case DamlGenMapType genMap:
                yield return DamlPrimitive.GenMap;
                foreach (var leaf in PrimitiveLeaves(genMap.Key).Concat(PrimitiveLeaves(genMap.Value)))
                {
                    yield return leaf;
                }
                break;
            case DamlContractIdType contractId:
                yield return DamlPrimitive.ContractId;
                foreach (var leaf in PrimitiveLeaves(contractId.Payload))
                {
                    yield return leaf;
                }
                break;
        }
    }

    [Fact]
    public void CollectTypeDependencies_rejects_a_con_whose_module_carries_no_package_id()
    {
        var con = new Type.Types.Con { Tycon = new TypeConId { Module = new ModuleId() } };
        con.Args.Add(new Type { InternedType = 6 });

        var act = () => new PackageContext(new Package())
            .CollectTypeDependencies(new Type { Con = con }, new HashSet<string>());

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*carries no package-id field*",
                "in proto3 an unset message field deserializes to null, so reading SumCase off it "
                + "escaped as a bare NullReferenceException naming neither the type nor the archive. "
                + "The out-of-range InternedType arg is deliberate and load-bearing: it is never "
                + "walked today because the package-id read precedes the args loop, so it pins that "
                + "precedence — were the loop ever to run first, this would throw the interned-type "
                + "diagnostic instead and the message assertion would fail");
    }

    [Fact]
    public void CollectTypeDependencies_rejects_a_syn_whose_module_carries_no_package_id()
    {
        var syn = new Type.Types.Syn { Tysyn = new TypeSynId { Module = new ModuleId() } };

        var act = () => new PackageContext(new Package())
            .CollectTypeDependencies(new Type { Syn = syn }, new HashSet<string>());

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*carries no package-id field*");
    }

    [Fact]
    public void ConvertType_rejects_a_con_whose_module_carries_no_package_id()
    {
        var con = new Type.Types.Con { Tycon = new TypeConId { Module = new ModuleId() } };

        var act = () => new PackageContext(new Package()).ConvertType(new Type { Con = con });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*carries no package-id field*");
    }

    [Fact]
    public void ConvertType_rejects_a_syn_whose_module_carries_no_package_id()
    {
        var syn = new Type.Types.Syn { Tysyn = new TypeSynId { Module = new ModuleId() } };

        var act = () => new PackageContext(new Package()).ConvertType(new Type { Syn = syn });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*carries no package-id field*");
    }

    [Fact]
    public void ConvertType_rejects_a_con_whose_package_reference_has_no_case_set()
    {
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId() },
            },
        };

        var act = () => new PackageContext(new Package()).ConvertType(new Type { Con = con });

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*no package-id case set*",
                "an unset discriminator shared the null arm with SelfPackageId, so it minted a "
                + "self-referential DamlTypeRef — a wrong-but-plausible model that parsed cleanly");
    }

    [Fact]
    public void CollectTypeDependencies_rejects_a_con_whose_package_reference_has_no_case_set()
    {
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId() },
            },
        };

        var act = () => new PackageContext(new Package())
            .CollectTypeDependencies(new Type { Con = con }, new HashSet<string>());

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*no package-id case set*",
                "the silent read recorded no dependency edge at all, so the malformation "
                + "survived the walk as a missing import rather than as a diagnostic");
    }

    [Fact]
    public void ConvertType_rejects_a_syn_whose_package_reference_has_no_case_set()
    {
        var syn = new Type.Types.Syn
        {
            Tysyn = new TypeSynId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId() },
            },
        };

        var act = () => new PackageContext(new Package()).ConvertType(new Type { Syn = syn });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*no package-id case set*");
    }

    [Fact]
    public void CollectTypeDependencies_rejects_a_syn_whose_package_reference_has_no_case_set()
    {
        var syn = new Type.Types.Syn
        {
            Tysyn = new TypeSynId
            {
                Module = new ModuleId { PackageId = new SelfOrImportedPackageId() },
            },
        };

        var act = () => new PackageContext(new Package())
            .CollectTypeDependencies(new Type { Syn = syn }, new HashSet<string>());

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*no package-id case set*");
    }

    [Fact]
    public void GetImportedPackageId_rejects_a_type_con_id_carrying_no_module()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.GetImportedPackageId(new TypeConId());

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*carries no module reference*",
                "the public overload dereferences Module one frame before the package-id read, "
                + "so leaving it unchecked kept a NullReferenceException on the same malformation family");
    }

    [Fact]
    public void GetImportedPackageId_rejects_a_type_con_id_whose_module_carries_no_package_id()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.GetImportedPackageId(new TypeConId { Module = new ModuleId() });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*carries no package-id field*");
    }

    [Fact]
    public void GetImportedPackageId_rejects_a_type_con_id_whose_package_reference_has_no_case_set()
    {
        var ctx = new PackageContext(new Package());

        var typeConId = new TypeConId
        {
            Module = new ModuleId { PackageId = new SelfOrImportedPackageId() },
        };

        var act = () => ctx.GetImportedPackageId(typeConId);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*no package-id case set*");
    }

    [Fact]
    public void GetImportedPackageId_resolves_a_self_package_reference_to_no_imported_package_id()
    {
        var ctx = new PackageContext(new Package());

        var typeConId = new TypeConId
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
            },
        };

        ctx.GetImportedPackageId(typeConId).Should().BeNull(
            "SelfPackageId names this package, so null stays its legitimate reading — "
            + "only the absent field and the unset discriminator change behaviour");
    }

    [Fact]
    public void CollectTypeDependencies_records_no_dependency_for_a_self_package_reference()
    {
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                },
            },
        };
        var dependencies = new HashSet<string>();

        new PackageContext(new Package()).CollectTypeDependencies(new Type { Con = con }, dependencies);

        dependencies.Should().BeEmpty();
    }

    private static Package PackageWithInternedListChain(int length)
    {
        var package = new Package();
        package.InternedTypes.Add(new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } });

        for (var index = 1; index <= length; index++)
        {
            var list = new Type.Types.Builtin { Builtin_ = BuiltinType.List };
            list.Args.Add(new Type { InternedType = index - 1 });
            package.InternedTypes.Add(new Type { Builtin = list });
        }

        return package;
    }

    private static int NestingDepth(DamlType type) => type switch
    {
        DamlTypeApp app => app.Arguments.Max(NestingDepth) + 1,
        DamlListType list => NestingDepth(list.Element) + 1,
        DamlOptionalType optional => NestingDepth(optional.Value) + 1,
        DamlTextMapType textMap => NestingDepth(textMap.Value) + 1,
        DamlGenMapType genMap => Math.Max(NestingDepth(genMap.Key), NestingDepth(genMap.Value)) + 1,
        DamlContractIdType contractId => NestingDepth(contractId.Payload) + 1,
        _ => 0,
    };

    private static Type ImportedCon(Package package, string importedPackageId)
    {
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId
                    {
                        ImportedPackageIdInternedStr = AddString(package, importedPackageId),
                    },
                },
            },
        };
        return new Type { Con = con };
    }

    [Fact]
    public void GetValueExpr_rejects_a_value_id_carrying_no_module()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.GetValueExpr(new ValueId());

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*carries no module reference*",
                "in proto3 an unset message field deserializes to null, so reading PackageId off it "
                + "escaped as a bare NullReferenceException carrying no archive context — the same "
                + "malformation family the type-reference reads already reject");
    }

    [Fact]
    public void GetValueExpr_rejects_a_value_id_whose_module_carries_no_package_id()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.GetValueExpr(new ValueId { Module = new ModuleId() });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*carries no package-id field*");
    }

    [Fact]
    public void GetValueExpr_names_the_value_reference_rather_than_a_type_reference()
    {
        var ctx = new PackageContext(new Package());

        var act = () => ctx.GetValueExpr(new ValueId());

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*value reference*",
                "the thrower is shared with the type-reference reads, so it must name which kind of "
                + "reference was malformed rather than always claiming a type reference");
    }

    [Fact]
    public void GetValueExpr_reads_a_package_reference_with_no_case_set_as_unresolvable()
    {
        var ctx = new PackageContext(new Package());

        var valueId = new ValueId
        {
            Module = new ModuleId { PackageId = new SelfOrImportedPackageId() },
        };

        ctx.GetValueExpr(valueId).Should().BeNull(
            "a reference naming neither this package nor an import resolves to nothing, which is "
            + "what this method's null already means — unlike an absent field, which describes no "
            + "reference at all and is rejected. The asymmetry with the type-reference reads, which "
            + "throw on the unset case, is deliberate");
    }

    [Fact]
    public void ConvertType_rejects_a_type_reference_that_is_absent()
    {
        var act = () => new PackageContext(new Package()).ConvertType(null);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*carries no type*",
                "in proto3 an unset message field deserializes to null, so an absent type describes "
                + "no type expression at all — reading it as Unit mints a shape the archive never declared");
    }

    [Fact]
    public void ConvertType_rejects_a_type_whose_sum_case_is_unset()
    {
        var act = () => new PackageContext(new Package()).ConvertType(new Type());

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*no sum case*",
                "a Type populating none of its sum cases names no type expression, and Unit is a "
                + "real Daml type rather than an honest reading of nothing");
    }

    [Fact]
    public void PackageContext_rejects_construction_with_an_interned_type_whose_sum_case_is_unset()
    {
        var package = new Package();
        package.InternedTypes.Add(new Type());

        var act = () => new PackageContext(package);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*no sum case*",
                "the interned-type pool is converted eagerly during construction, so a shapeless "
                + "pool entry has to fail there rather than at the first reference to it");
    }

    private static int AddString(Package package, string value)
    {
        var index = package.InternedStrings.Count;
        package.InternedStrings.Add(value);
        return index;
    }
}
