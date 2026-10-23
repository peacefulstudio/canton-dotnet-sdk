// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderTypeConversionTests
{
    private const int DeepestAcceptedTypeNesting = 256;

    public enum ConversionSite
    {
        VariantConstructor,
        TemplateKey,
        ChoiceArgument,
        ChoiceResult,
        InterfaceView,
    }

    private static Type Builtin(BuiltinType b, params Type[] args)
    {
        var builtin = new Type.Types.Builtin { Builtin_ = b };
        builtin.Args.AddRange(args);
        return new Type { Builtin = builtin };
    }

    private static DamlType ParseSingleFieldType(DalfPackageBuilder builder, Type fieldType)
    {
        var moduleDn = builder.DottedName("M");
        var typeDn = builder.DottedName("R");
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType { FieldInternedStr = builder.Intern("f"), Type = fieldType });
        var dataType = new DefDataType { NameInternedDname = typeDn, Record = record, Serializable = true };
        var module = new Module { NameInternedDname = moduleDn };
        module.DataTypes.Add(dataType);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));
        var definition = (DamlRecordDefinition)result.Modules.Single().DataTypes.Single().Definition;
        return definition.Fields.Single().Type;
    }

    [Theory]
    [InlineData(BuiltinType.Unit, DamlPrimitive.Unit)]
    [InlineData(BuiltinType.Bool, DamlPrimitive.Bool)]
    [InlineData(BuiltinType.Int64, DamlPrimitive.Int64)]
    [InlineData(BuiltinType.Text, DamlPrimitive.Text)]
    [InlineData(BuiltinType.Date, DamlPrimitive.Date)]
    [InlineData(BuiltinType.Timestamp, DamlPrimitive.Timestamp)]
    [InlineData(BuiltinType.Party, DamlPrimitive.Party)]
    public void Parse_maps_nullary_builtin_to_primitive(BuiltinType proto, DamlPrimitive expected)
    {
        var fieldType = ParseSingleFieldType(new DalfPackageBuilder(), Builtin(proto));

        fieldType.Should().Be(new DamlPrimitiveType(expected));
    }

    [Fact]
    public void Parse_preserves_numeric_scale_as_the_type_application_argument()
    {
        var nat = new Type { Nat = 10 };
        var fieldType = ParseSingleFieldType(new DalfPackageBuilder(), Builtin(BuiltinType.Numeric, nat));

        var app = fieldType.Should().BeOfType<DamlTypeApp>().Subject;
        app.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Numeric));
        app.Arguments.Should().ContainSingle()
            .Which.Should().Be(new DamlTypeVar("10"));
    }

    [Fact]
    public void Parse_maps_optional_text_to_optional_typed_node()
    {
        var fieldType = ParseSingleFieldType(
            new DalfPackageBuilder(), Builtin(BuiltinType.Optional, Builtin(BuiltinType.Text)));

        fieldType.Should().Be(new DamlOptionalType(new DamlPrimitiveType(DamlPrimitive.Text)),
            "an applied Optional folds into its typed node at the reader boundary, parser-direct and " +
            "proto paths alike");
    }

    [Fact]
    public void Parse_maps_contract_id_applied_to_type_ref()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var con = new Type { Con = new Type.Types.Con { Tycon = builder.LocalTypeConId(builder.DottedName("Asset"), moduleDn) } };

        var fieldType = ParseSingleFieldType(builder, Builtin(BuiltinType.ContractId, con));

        var contractId = fieldType.Should().BeOfType<DamlContractIdType>().Subject;
        contractId.Payload.Should().BeOfType<DamlTypeRef>().Which.Name.Should().Be("Asset");
    }

    [Fact]
    public void Parse_maps_textmap_to_text_map_typed_node()
    {
        var fieldType = ParseSingleFieldType(
            new DalfPackageBuilder(), Builtin(BuiltinType.Textmap, Builtin(BuiltinType.Int64)));

        fieldType.Should().Be(new DamlTextMapType(new DamlPrimitiveType(DamlPrimitive.Int64)));
    }

    [Fact]
    public void Parse_maps_genmap_to_gen_map_typed_node()
    {
        var fieldType = ParseSingleFieldType(
            new DalfPackageBuilder(), Builtin(BuiltinType.Genmap, Builtin(BuiltinType.Party), Builtin(BuiltinType.Int64)));

        fieldType.Should().Be(
            new DamlGenMapType(
                new DamlPrimitiveType(DamlPrimitive.Party),
                new DamlPrimitiveType(DamlPrimitive.Int64)));
    }

    [Fact]
    public void Parse_maps_nested_builtins_to_typed_nodes_at_every_level()
    {
        var fieldType = ParseSingleFieldType(
            new DalfPackageBuilder(),
            Builtin(BuiltinType.List,
                Builtin(BuiltinType.Optional,
                    Builtin(BuiltinType.Textmap, Builtin(BuiltinType.Int64)))));

        var list = fieldType.Should().BeOfType<DamlListType>().Subject;
        var optional = list.Element.Should().BeOfType<DamlOptionalType>().Subject;
        var textMap = optional.Value.Should().BeOfType<DamlTextMapType>().Subject;
        textMap.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Theory]
    [InlineData(BuiltinType.Arrow, DamlPrimitive.Arrow)]
    [InlineData(BuiltinType.Update, DamlPrimitive.Update)]
    [InlineData(BuiltinType.FailureCategory, DamlPrimitive.FailureCategory)]
    [InlineData(BuiltinType.TypeRep, DamlPrimitive.TypeRep)]
    [InlineData(BuiltinType.Any, DamlPrimitive.Any)]
    [InlineData(BuiltinType.AnyException, DamlPrimitive.AnyException)]
    public void Parse_maps_structural_type_former_to_its_real_primitive_identity(
        BuiltinType builtin,
        DamlPrimitive expected)
    {
        var fieldType = ParseSingleFieldType(new DalfPackageBuilder(), Builtin(builtin));

        fieldType.Should().Be(new DamlPrimitiveType(expected),
            "the catalog carries the structural type-formers as SignatureOnly with real identity — " +
            "the parser must never erase a type the archive declares to Unit");
    }

    [Theory]
    [InlineData(BuiltinType.Bignumeric)]
    [InlineData(BuiltinType.RoundingMode)]
    public void Parse_throws_not_supported_for_value_level_deferred_builtin(BuiltinType builtin)
    {
        var act = () => ParseSingleFieldType(new DalfPackageBuilder(), Builtin(builtin));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{builtin}*");
    }

    [Fact]
    public void Parse_maps_bare_type_var_to_type_var()
    {
        var builder = new DalfPackageBuilder();
        var aStr = builder.Intern("a");
        var fieldType = ParseSingleFieldType(builder, new Type { Var = new Type.Types.Var { VarInternedStr = aStr } });

        fieldType.Should().Be(new DamlTypeVar("a"));
    }

    [Fact]
    public void Parse_maps_applied_type_var_to_type_application_over_var()
    {
        var builder = new DalfPackageBuilder();
        var fStr = builder.Intern("f");
        var typeVar = new Type.Types.Var { VarInternedStr = fStr };
        typeVar.Args.Add(Builtin(BuiltinType.Int64));
        var fieldType = ParseSingleFieldType(builder, new Type { Var = typeVar });

        var app = fieldType.Should().BeOfType<DamlTypeApp>().Subject;
        app.Base.Should().Be(new DamlTypeVar("f"));
        app.Arguments.Should().ContainSingle().Which.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public void Parse_maps_con_with_args_to_type_application_over_type_ref()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var con = new Type.Types.Con { Tycon = builder.LocalTypeConId(builder.DottedName("Box"), moduleDn) };
        con.Args.Add(Builtin(BuiltinType.Text));
        var fieldType = ParseSingleFieldType(builder, new Type { Con = con });

        var app = fieldType.Should().BeOfType<DamlTypeApp>().Subject;
        app.Base.Should().BeOfType<DamlTypeRef>().Which.Name.Should().Be("Box");
        app.Arguments.Should().ContainSingle().Which.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public void Parse_resolves_con_imported_package_id_on_type_ref()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("Other");
        var importedPkg = builder.Intern("imported-pkg-id");
        var con = new Type.Types.Con
        {
            Tycon = builder.ImportedTypeConId(builder.DottedName("Foreign"), moduleDn, importedPkg)
        };
        var fieldType = ParseSingleFieldType(builder, new Type { Con = con });

        var typeRef = fieldType.Should().BeOfType<DamlTypeRef>().Subject;
        typeRef.Name.Should().Be("Foreign");
        typeRef.Module.Should().Be("Other");
        typeRef.PackageId.Should().Be("imported-pkg-id");
    }

    [Fact]
    public void Parse_unwraps_forall_to_its_body_type()
    {
        var builder = new DalfPackageBuilder();
        var forall = new Type.Types.Forall { Body = Builtin(BuiltinType.Bool) };
        forall.Vars.Add(new TypeVarWithKind { VarInternedStr = builder.Intern("a") });
        var fieldType = ParseSingleFieldType(builder, new Type { Forall = forall });

        fieldType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Bool));
    }

    [Fact]
    public void Parse_maps_nat_type_to_its_scale_type_var()
    {
        var fieldType = ParseSingleFieldType(new DalfPackageBuilder(), new Type { Nat = 7 });

        fieldType.Should().Be(new DamlTypeVar("7"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(28)]
    [InlineData(37)]
    public void Parse_preserves_every_supported_numeric_scale(long scale)
    {
        var fieldType = ParseSingleFieldType(
            new DalfPackageBuilder(), Builtin(BuiltinType.Numeric, new Type { Nat = scale }));

        fieldType.Should().BeOfType<DamlTypeApp>()
            .Which.Arguments.Should().ContainSingle()
            .Which.Should().Be(new DamlTypeVar(scale.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Parse_follows_type_synonym_reference_with_args()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("Synonyms");
        var tysyn = new TypeSynId
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                ModuleNameInternedDname = moduleDn,
            },
            NameInternedDname = builder.DottedName("MyMap"),
        };
        var syn = new Type.Types.Syn { Tysyn = tysyn };
        syn.Args.Add(Builtin(BuiltinType.Text));
        var fieldType = ParseSingleFieldType(builder, new Type { Syn = syn });

        var app = fieldType.Should().BeOfType<DamlTypeApp>().Subject;
        app.Base.Should().BeOfType<DamlTypeRef>().Which.Name.Should().Be("MyMap");
        app.Arguments.Should().ContainSingle().Which.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public void Parse_flattens_direct_tapp_chain_into_typed_gen_map_node()
    {
        var builder = new DalfPackageBuilder();
        var inner = new Type { Tapp = new Type.Types.TApp { Lhs = Builtin(BuiltinType.Genmap), Rhs = Builtin(BuiltinType.Party) } };
        var outer = new Type { Tapp = new Type.Types.TApp { Lhs = inner, Rhs = Builtin(BuiltinType.Int64) } };

        var fieldType = ParseSingleFieldType(builder, outer);

        fieldType.Should().Be(
            new DamlGenMapType(
                new DamlPrimitiveType(DamlPrimitive.Party),
                new DamlPrimitiveType(DamlPrimitive.Int64)),
            "the two curried TApps spell one complete GenMap application; the flatten that already " +
            "merged them into a single DamlTypeApp now continues into the typed node");
    }

    [Fact]
    public void Parse_rejects_a_gen_map_builtin_applied_to_one_argument()
    {
        var act = () => ParseSingleFieldType(
            new DalfPackageBuilder(),
            Builtin(BuiltinType.Genmap, Builtin(BuiltinType.Party)));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*'GenMap'*catalog arity is 2*",
                "an under-applied GenMap names no Daml type — the catalog arity table is the oracle " +
                "and the archive is malformed");
    }

    [Fact]
    public void Parse_rejects_a_list_builtin_applied_to_two_arguments()
    {
        var act = () => ParseSingleFieldType(
            new DalfPackageBuilder(),
            Builtin(BuiltinType.List, Builtin(BuiltinType.Text), Builtin(BuiltinType.Int64)));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*'List'*catalog arity is 1*",
                "an over-applied List names no Daml type — the catalog arity table is the oracle " +
                "and the archive is malformed");
    }

    [Fact]
    public void Parse_rejects_a_curried_tapp_chain_that_over_applies_a_list()
    {
        var inner = new Type { Tapp = new Type.Types.TApp { Lhs = Builtin(BuiltinType.List), Rhs = Builtin(BuiltinType.Text) } };
        var outer = new Type { Tapp = new Type.Types.TApp { Lhs = inner, Rhs = Builtin(BuiltinType.Int64) } };

        var act = () => ParseSingleFieldType(new DalfPackageBuilder(), outer);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*'List'*catalog arity is 1*",
                "the chain spells one two-argument List application — flatten composes the arity " +
                "the fold checks, so currying cannot smuggle an over-applied builtin through");
    }

    [Fact]
    public void Parse_follows_nullary_type_synonym_to_bare_type_ref()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("Synonyms");
        var tysyn = new TypeSynId
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                ModuleNameInternedDname = moduleDn,
            },
            NameInternedDname = builder.DottedName("Alias"),
        };
        var fieldType = ParseSingleFieldType(builder, new Type { Syn = new Type.Types.Syn { Tysyn = tysyn } });

        fieldType.Should().BeOfType<DamlTypeRef>().Which.Name.Should().Be("Alias");
    }

    [Fact]
    public void Parse_rejects_a_struct_type_whatever_its_fields_carry()
    {
        var builder = new DalfPackageBuilder();
        var structType = new Type.Types.Struct();
        structType.Fields.Add(new FieldWithType
        {
            FieldInternedStr = builder.Intern("inner"),
            Type = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } },
        });

        var act = () => ParseSingleFieldType(builder, new Type { Struct = structType });

        act.Should().Throw<InvalidDataException>().WithMessage("*struct type is not supported*");
    }

    [Fact]
    public void Parse_resolves_a_con_package_id_named_by_the_import_table_index()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.PackageImports = new PackageImports
        {
            ImportedPackages = { "table-dep-a", "table-dep-b" },
        };
        var con = new Type.Types.Con
        {
            Tycon = new TypeConId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { PackageImportId = 1 },
                    ModuleNameInternedDname = builder.DottedName("Other"),
                },
                NameInternedDname = builder.DottedName("Foreign"),
            },
        };

        var fieldType = ParseSingleFieldType(builder, new Type { Con = con });

        fieldType.Should().BeOfType<DamlTypeRef>().Which.PackageId.Should().Be("table-dep-b");
    }

    [Fact]
    public void Parse_field_type_nested_past_the_bound_throws_before_managed_stack_overflow()
    {
        var act = () => ParseSingleFieldType(
            new DalfPackageBuilder(), DalfTestFixtures.DeeplyNestedListType(depth: 300));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*maximum type depth of 256*");
    }

    [Fact]
    public void Parse_field_type_at_the_deepest_accepted_nesting_converts_without_throwing()
    {
        var fieldType = ParseSingleFieldType(
            new DalfPackageBuilder(), DalfTestFixtures.DeeplyNestedListType(depth: DeepestAcceptedTypeNesting));

        var depth = 0;
        var node = fieldType;
        while (node is DamlListType list)
        {
            node = list.Element;
            depth++;
        }
        depth.Should().Be(DeepestAcceptedTypeNesting,
            "the deepest accepted nesting folds one DamlListType per List level — normalization composes " +
            "depth across the whole tree, and the bound still admits exactly the nesting it did before");
        node.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public void Parse_field_type_one_level_past_the_deepest_accepted_nesting_throws()
    {
        var act = () => ParseSingleFieldType(
            new DalfPackageBuilder(), DalfTestFixtures.DeeplyNestedListType(depth: DeepestAcceptedTypeNesting + 1));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*maximum type depth of 256*");
    }

    [Theory]
    [InlineData(ConversionSite.VariantConstructor)]
    [InlineData(ConversionSite.TemplateKey)]
    [InlineData(ConversionSite.ChoiceArgument)]
    [InlineData(ConversionSite.ChoiceResult)]
    [InlineData(ConversionSite.InterfaceView)]
    public void Parse_type_nested_past_the_bound_throws_wherever_the_reader_converts_a_type(ConversionSite site)
    {
        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(PackageWithDeeplyNestedTypeAt(site)));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*maximum type depth of 256*");
    }

    [Fact]
    public void Parse_field_type_and_interned_type_reject_excessive_depth_with_the_same_diagnostic()
    {
        var fieldPath = Record.Exception(
            () => ParseSingleFieldType(new DalfPackageBuilder(), DalfTestFixtures.DeeplyNestedListType(depth: 300)));

        var internedPackage = new Package();
        internedPackage.InternedTypes.Add(DalfTestFixtures.DeeplyNestedListType(depth: 300));
        var internedPath = Record.Exception(() => new PackageContext(internedPackage));

        fieldPath.Should().BeOfType<InvalidDataException>()
            .Which.Message.Should().Match("*maximum type depth of 256*");
        internedPath.Should().BeOfType<InvalidDataException>()
            .Which.Message.Should().Match("*maximum type depth of 256*");
        fieldPath!.Message.Should().Be(internedPath!.Message);
    }

    private static Package PackageWithDeeplyNestedTypeAt(ConversionSite site)
    {
        var builder = new DalfPackageBuilder();
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        var tooDeep = DalfTestFixtures.DeeplyNestedListType(depth: 300);

        switch (site)
        {
            case ConversionSite.VariantConstructor:
            {
                var variant = new DefDataType.Types.Fields();
                variant.Fields_.Add(new FieldWithType { FieldInternedStr = builder.Intern("Ctor"), Type = tooDeep });
                module.DataTypes.Add(new DefDataType { NameInternedDname = builder.DottedName("V"), Variant = variant, Serializable = true });
                break;
            }

            case ConversionSite.TemplateKey:
            {
                var keyed = TemplateNamed(builder, "Keyed");
                keyed.Key = new DefTemplate.Types.DefKey { Type = tooDeep };
                module.Templates.Add(keyed);
                break;
            }

            case ConversionSite.ChoiceArgument:
            {
                var deepArgument = TemplateNamed(builder, "DeepArgument");
                deepArgument.Choices.Add(new TemplateChoice
                {
                    Controllers = DalfTestFixtures.DeclaredNoParties(),
                    Observers = DalfTestFixtures.DeclaredNoParties(),
                    NameInternedStr = builder.Intern("Ch"),
                    ArgBinder = new VarWithType { Type = tooDeep },
                });
                module.Templates.Add(deepArgument);
                break;
            }

            case ConversionSite.ChoiceResult:
            {
                var deepResult = TemplateNamed(builder, "DeepResult");
                deepResult.Choices.Add(new TemplateChoice
                {
                    Controllers = DalfTestFixtures.DeclaredNoParties(),
                    Observers = DalfTestFixtures.DeclaredNoParties(),
                    NameInternedStr = builder.Intern("Ch"),
                    RetType = tooDeep,
                });
                module.Templates.Add(deepResult);
                break;
            }

            case ConversionSite.InterfaceView:
            {
                module.Interfaces.Add(new DefInterface
                {
                    TyconInternedDname = builder.DottedName("I"),
                    ParamInternedStr = builder.Intern("this"),
                    View = tooDeep,
                });
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(site));
        }

        builder.Package.Modules.Add(module);
        return builder.Package;
    }

    private static DefTemplate TemplateNamed(DalfPackageBuilder builder, string name) => new()
    {
        TyconInternedDname = builder.DottedName(name),
        ParamInternedStr = builder.Intern("this"),
    };
}
