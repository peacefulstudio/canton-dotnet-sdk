// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderSelfPackageIdTests
{
    private static Type Builtin(BuiltinType b) =>
        new() { Builtin = new Type.Types.Builtin { Builtin_ = b } };

    private static (DamlPackage Parsed, string DeclaredPackageId) Parse(DalfPackageBuilder builder)
    {
        var dalf = DalfTestFixtures.SerializeArchive(builder.Package);
        return (DalfReader.Parse(dalf), DalfTestFixtures.PackageIdOf(dalf));
    }

    private static DamlPackage ParseWithSingleField(DalfPackageBuilder builder, Type fieldType, out string selfPackageId)
    {
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType { FieldInternedStr = builder.Intern("f"), Type = fieldType });
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.DataTypes.Add(new DefDataType
        {
            NameInternedDname = builder.DottedName("R"),
            Record = record,
            Serializable = true,
        });
        builder.Package.Modules.Add(module);
        var (parsed, declared) = Parse(builder);
        selfPackageId = declared;
        return parsed;
    }

    private static DamlType SoleFieldType(DamlPackage package) =>
        ((DamlRecordDefinition)package.Modules.Single().DataTypes.Single().Definition).Fields.Single().Type;

    private static Type SelfCon(DalfPackageBuilder builder, string name, params Type[] args)
    {
        var con = new Type.Types.Con
        {
            Tycon = builder.LocalTypeConId(builder.DottedName(name), builder.DottedName("M")),
        };
        con.Args.AddRange(args);
        return new Type { Con = con };
    }

    [Fact]
    public void Parse_gives_a_self_package_con_the_enclosing_package_id()
    {
        var builder = new DalfPackageBuilder();

        var package = ParseWithSingleField(builder, SelfCon(builder, "Asset"), out var selfPackageId);

        SoleFieldType(package).Should().Be(new DamlTypeRef(selfPackageId, "M", "Asset"));
    }

    [Fact]
    public void Parse_gives_a_self_package_con_the_pinned_enclosing_package_id()
    {
        var builder = new DalfPackageBuilder();

        var package = ParseWithSingleField(builder, SelfCon(builder, "Asset"), out _);

        SoleFieldType(package).Should().Be(new DamlTypeRef(
            "c2a59ddefdd8beee6f30d26c699629dd91b5b81dcf2908f43c0fe289bb1ba966", "M", "Asset"));
    }

    [Fact]
    public void Parse_gives_the_base_of_an_applied_self_package_con_the_enclosing_package_id()
    {
        var builder = new DalfPackageBuilder();

        var package = ParseWithSingleField(
            builder, SelfCon(builder, "Box", Builtin(BuiltinType.Text)), out var selfPackageId);

        SoleFieldType(package).Should().Be(new DamlTypeApp(
            new DamlTypeRef(selfPackageId, "M", "Box"),
            [new DamlPrimitiveType(DamlPrimitive.Text)]));
    }

    [Fact]
    public void Parse_gives_a_self_package_type_synonym_the_enclosing_package_id()
    {
        var builder = new DalfPackageBuilder();
        var syn = new Type.Types.Syn
        {
            Tysyn = new TypeSynId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                    ModuleNameInternedDname = builder.DottedName("M"),
                },
                NameInternedDname = builder.DottedName("Alias"),
            },
        };

        var package = ParseWithSingleField(builder, new Type { Syn = syn }, out var selfPackageId);

        SoleFieldType(package).Should().Be(new DamlTypeRef(selfPackageId, "M", "Alias"));
    }

    [Fact]
    public void Parse_gives_an_interned_self_package_con_the_enclosing_package_id()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.InternedTypes.Add(SelfCon(builder, "Asset"));

        var package = ParseWithSingleField(builder, new Type { InternedType = 0 }, out var selfPackageId);

        SoleFieldType(package).Should().Be(new DamlTypeRef(selfPackageId, "M", "Asset"));
    }

    [Fact]
    public void Parse_leaves_an_imported_con_with_its_imported_package_id()
    {
        var builder = new DalfPackageBuilder();
        var imported = new Type
        {
            Con = new Type.Types.Con
            {
                Tycon = builder.ImportedTypeConId(
                    builder.DottedName("Foreign"), builder.DottedName("Other"), builder.Intern("dep-pkg-id")),
            },
        };

        var package = ParseWithSingleField(builder, imported, out _);

        SoleFieldType(package).Should().Be(new DamlTypeRef("dep-pkg-id", "Other", "Foreign"));
    }

    [Fact]
    public void Parse_gives_an_implemented_self_package_interface_the_enclosing_package_id()
    {
        var builder = new DalfPackageBuilder();
        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
        };
        template.Implements.Add(new DefTemplate.Types.Implements
        {
            Interface = builder.LocalTypeConId(builder.DottedName("ILocal"), builder.DottedName("Other")),
        });
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var (parsed, selfPackageId) = Parse(builder);

        parsed.Modules.Single().Templates.Single().Implements.Should().Equal(
            new DamlTypeRef(selfPackageId, "Other", "ILocal"));
    }

    [Fact]
    public void Parse_sorts_implements_by_the_resolved_package_id_with_a_self_interface_among_imported_ones()
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
        foreach (var reference in new[]
        {
            builder.ImportedTypeConId(builder.DottedName("ILast"), moduleDn, builder.Intern("g-last-package")),
            builder.LocalTypeConId(builder.DottedName("ISelf"), moduleDn),
            builder.ImportedTypeConId(builder.DottedName("IFirst"), moduleDn, builder.Intern("0-first-package")),
        })
        {
            template.Implements.Add(new DefTemplate.Types.Implements { Interface = reference });
        }
        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var (parsed, selfPackageId) = Parse(builder);

        parsed.Modules.Single().Templates.Single().Implements.Should().Equal(
            new DamlTypeRef("0-first-package", "M", "IFirst"),
            new DamlTypeRef(selfPackageId, "M", "ISelf"),
            new DamlTypeRef("g-last-package", "M", "ILast"));
    }

    [Fact]
    public void Parse_gives_a_self_package_interface_view_type_the_enclosing_package_id()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var iface = new DefInterface
        {
            TyconInternedDname = builder.DottedName("IToken"),
            ParamInternedStr = builder.Intern("this"),
            View = SelfCon(builder, "TokenView"),
        };
        var module = new Module { NameInternedDname = moduleDn };
        module.Interfaces.Add(iface);
        builder.Package.Modules.Add(module);

        var (parsed, selfPackageId) = Parse(builder);

        parsed.Modules.Single().Interfaces.Single().ViewType.Should().Be(
            new DamlTypeRef(selfPackageId, "M", "TokenView"));
    }
}
