// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Google.Protobuf;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderDependencyTests
{
    private static Type ImportedCon(DalfPackageBuilder builder, string name, string module, string importedPkg)
    {
        var con = new Type.Types.Con
        {
            Tycon = builder.ImportedTypeConId(builder.DottedName(name), builder.DottedName(module), builder.Intern(importedPkg))
        };
        return new Type { Con = con };
    }

    private static DamlPackage ParseWithSingleRecordField(DalfPackageBuilder builder, Type fieldType)
    {
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType { FieldInternedStr = builder.Intern("f"), Type = fieldType });
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.DataTypes.Add(new DefDataType { NameInternedDname = builder.DottedName("R"), Record = record, Serializable = true });
        builder.Package.Modules.Add(module);

        return DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));
    }

    private static Type StructOver(DalfPackageBuilder builder, string fieldName, Type fieldType)
    {
        var structType = new Type.Types.Struct();
        structType.Fields.Add(new FieldWithType { FieldInternedStr = builder.Intern(fieldName), Type = fieldType });
        return new Type { Struct = structType };
    }

    [Fact]
    public void Parse_collects_dependency_reachable_only_through_an_interned_type()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.InternedTypes.Add(ImportedCon(builder, "Foreign", "Other", "interned-dep-pkg"));

        var result = ParseWithSingleRecordField(builder, new Type { InternedType = 0 });

        result.DependencyReferences.Select(r => r.PackageId).Should().BeEquivalentTo(["interned-dep-pkg"]);
    }

    [Fact]
    public void Parse_collects_dependency_through_a_chain_of_interned_types()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.InternedTypes.Add(ImportedCon(builder, "Elem", "Other", "chained-dep-pkg"));
        var listOfInterned = new Type.Types.Builtin { Builtin_ = BuiltinType.List };
        listOfInterned.Args.Add(new Type { InternedType = 0 });
        builder.Package.InternedTypes.Add(new Type { Builtin = listOfInterned });

        var result = ParseWithSingleRecordField(builder, new Type { InternedType = 1 });

        result.DependencyReferences.Select(r => r.PackageId).Should().BeEquivalentTo(["chained-dep-pkg"]);
    }

    [Fact]
    public void Parse_collects_explicit_package_imports()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.PackageImports = new PackageImports
        {
            ImportedPackages = { "dep-a", "dep-b", "" }
        };

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        result.DependencyReferences.Select(r => r.PackageId)
            .Should().BeEquivalentTo(["dep-a", "dep-b"]);
    }

    [Fact]
    public void Parse_collects_dependency_from_record_field_con_type()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType
        {
            FieldInternedStr = builder.Intern("f"),
            Type = ImportedCon(builder, "Foreign", "Other", "field-dep-pkg"),
        });
        var dataType = new DefDataType { NameInternedDname = builder.DottedName("R"), Record = record, Serializable = true };
        var module = new Module { NameInternedDname = moduleDn };
        module.DataTypes.Add(dataType);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        result.DependencyReferences.Select(r => r.PackageId).Should().BeEquivalentTo(["field-dep-pkg"]);
    }

    [Fact]
    public void Parse_collects_dependency_from_variant_field_inside_builtin_list()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var listOfForeign = new Type.Types.Builtin { Builtin_ = BuiltinType.List };
        listOfForeign.Args.Add(ImportedCon(builder, "Elem", "Other", "list-dep-pkg"));
        var variant = new DefDataType.Types.Fields();
        variant.Fields_.Add(new FieldWithType
        {
            FieldInternedStr = builder.Intern("Ctor"),
            Type = new Type { Builtin = listOfForeign },
        });
        var dataType = new DefDataType { NameInternedDname = builder.DottedName("V"), Variant = variant, Serializable = true };
        var module = new Module { NameInternedDname = moduleDn };
        module.DataTypes.Add(dataType);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        result.DependencyReferences.Select(r => r.PackageId).Should().BeEquivalentTo(["list-dep-pkg"]);
    }

    [Fact]
    public void Parse_collects_dependencies_from_template_key_choice_and_implements()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");

        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
            Key = new DefTemplate.Types.DefKey { Type = ImportedCon(builder, "Key", "Other", "key-dep") },
        };
        template.Choices.Add(new TemplateChoice
        {
            Controllers = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            NameInternedStr = builder.Intern("Ch"),
            ArgBinder = new VarWithType { Type = ImportedCon(builder, "Arg", "Other", "arg-dep") },
            RetType = ImportedCon(builder, "Ret", "Other", "ret-dep"),
        });
        template.Implements.Add(new DefTemplate.Types.Implements
        {
            Interface = builder.ImportedTypeConId(builder.DottedName("IFace"), moduleDn, builder.Intern("impl-dep")),
        });

        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        result.DependencyReferences.Select(r => r.PackageId)
            .Should().BeEquivalentTo(["key-dep", "arg-dep", "ret-dep", "impl-dep"]);
    }

    [Fact]
    public void Parse_collects_dependencies_from_interface_view_and_choices()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");

        var iface = new DefInterface
        {
            TyconInternedDname = builder.DottedName("I"),
            ParamInternedStr = builder.Intern("this"),
            View = ImportedCon(builder, "View", "Other", "view-dep"),
        };
        iface.Choices.Add(new TemplateChoice
        {
            Controllers = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            NameInternedStr = builder.Intern("Ch"),
            ArgBinder = new VarWithType { Type = ImportedCon(builder, "Arg", "Other", "iface-arg-dep") },
            RetType = ImportedCon(builder, "Ret", "Other", "iface-ret-dep"),
        });

        var module = new Module { NameInternedDname = moduleDn };
        module.Interfaces.Add(iface);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        result.DependencyReferences.Select(r => r.PackageId)
            .Should().BeEquivalentTo(["view-dep", "iface-arg-dep", "iface-ret-dep"]);
    }

    [Fact]
    public void Parse_collects_dependency_from_type_synonym_and_forall_and_tapp_and_var_args()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");

        var synModule = builder.DottedName("Syn");
        var tysyn = new TypeSynId
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { ImportedPackageIdInternedStr = builder.Intern("syn-dep") },
                ModuleNameInternedDname = synModule,
            },
            NameInternedDname = builder.DottedName("S"),
        };
        var syn = new Type.Types.Syn { Tysyn = tysyn };
        syn.Args.Add(ImportedCon(builder, "SynArg", "Other", "syn-arg-dep"));

        var typeVar = new Type.Types.Var { VarInternedStr = builder.Intern("a") };
        typeVar.Args.Add(ImportedCon(builder, "VarArg", "Other", "var-arg-dep"));

        var forall = new Type.Types.Forall { Body = new Type { Var = typeVar } };
        forall.Vars.Add(new TypeVarWithKind { VarInternedStr = builder.Intern("a") });

        var tapp = new Type.Types.TApp
        {
            Lhs = new Type { Syn = syn },
            Rhs = new Type { Forall = forall },
        };

        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType { FieldInternedStr = builder.Intern("f"), Type = new Type { Tapp = tapp } });
        var dataType = new DefDataType { NameInternedDname = builder.DottedName("R"), Record = record, Serializable = true };
        var module = new Module { NameInternedDname = moduleDn };
        module.DataTypes.Add(dataType);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        result.DependencyReferences.Select(r => r.PackageId)
            .Should().BeEquivalentTo(["syn-dep", "syn-arg-dep", "var-arg-dep"]);
    }

    [Fact]
    public void Parse_rejects_a_template_implements_reference_whose_package_import_index_is_out_of_range()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.PackageImports = new PackageImports { ImportedPackages = { "dep-a" } };
        var moduleDn = builder.DottedName("M");
        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
        };
        template.Implements.Add(new DefTemplate.Types.Implements
        {
            Interface = new TypeConId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { PackageImportId = 7 },
                    ModuleNameInternedDname = moduleDn,
                },
                NameInternedDname = builder.DottedName("IFace"),
            },
        });
        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*package-import index 7*");
    }

    [Fact]
    public void Parse_rejects_a_template_implements_reference_carrying_no_module()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
        };
        template.Implements.Add(new DefTemplate.Types.Implements
        {
            Interface = new TypeConId { NameInternedDname = builder.DottedName("IFace") },
        });
        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*carries no module reference*",
                "the reader resolves the package reference before the module and type names, so the "
                + "absent module surfaces as a diagnostic naming the archive rather than as a NullReferenceException");
    }

    [Fact]
    public void Parse_rejects_a_template_implements_reference_whose_package_id_has_no_case_set()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
        };
        template.Implements.Add(new DefTemplate.Types.Implements
        {
            Interface = new TypeConId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId(),
                    ModuleNameInternedDname = moduleDn,
                },
                NameInternedDname = builder.DottedName("IFace"),
            },
        });
        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*no package-id case set*",
                "an unset discriminator otherwise resolved to the empty package id, minting a "
                + "self-referential DamlTypeRef for an interface this package does not define");
    }

    [Fact]
    public void Parse_computes_package_id_from_payload_hash_when_archive_hash_is_empty()
    {
        var builder = new DalfPackageBuilder();
        var payload = new ArchivePayload
        {
            Minor = "1",
            DamlLf2 = ByteString.CopyFrom(builder.Package.ToByteArray()),
        };
        var payloadBytes = payload.ToByteArray();
        var archive = new Archive { Hash = "", Payload = ByteString.CopyFrom(payloadBytes) };

        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payloadBytes)).ToLowerInvariant();

        var result = DalfReader.Parse(archive.ToByteArray());

        result.PackageId.Should().Be(expected);
    }
}
