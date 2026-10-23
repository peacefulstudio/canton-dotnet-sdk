// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderTemplateTests
{
    private static Type Builtin(BuiltinType b) =>
        new() { Builtin = new Type.Types.Builtin { Builtin_ = b } };

    private static DamlTypeRef ParseSoleImplementedInterface(DalfPackageBuilder builder, TypeConId interfaceRef)
    {
        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
        };
        template.Implements.Add(new DefTemplate.Types.Implements { Interface = interfaceRef });

        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        return result.Modules.Single().Templates.Single().Implements.Should().ContainSingle().Which;
    }

    [Fact]
    public void Parse_template_resolves_name_consuming_choice_and_key_and_implements()
    {
        var builder = new DalfPackageBuilder();
        var thisStr = builder.Intern("this");
        var partyName = builder.Intern("Party");
        var moduleDn = builder.DottedName("TestModule");
        var templateDn = builder.DottedName("Asset");
        var keyTypeRef = builder.LocalTypeConId(builder.DottedName("AssetKey"), moduleDn);
        var choiceNameStr = builder.Intern("Transfer");
        var choiceArgRef = builder.LocalTypeConId(builder.DottedName("TransferArg"), moduleDn);
        var ifaceRef = builder.ImportedTypeConId(
            builder.DottedName("ITradeable"), moduleDn, importedPackageId: builder.Intern("dep-pkg-id"));

        var choice = new TemplateChoice
        {
            Controllers = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            NameInternedStr = choiceNameStr,
            Consuming = true,
            ArgBinder = new VarWithType { Type = new Type { Con = new Type.Types.Con { Tycon = choiceArgRef } } },
            RetType = Builtin(BuiltinType.Bool),
        };

        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = templateDn,
            ParamInternedStr = thisStr,
            Key = new DefTemplate.Types.DefKey
            {
                Type = new Type { Con = new Type.Types.Con { Tycon = keyTypeRef } }
            },
        };
        template.Choices.Add(choice);
        template.Implements.Add(new DefTemplate.Types.Implements { Interface = ifaceRef });

        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        var parsed = result.Modules.Single().Templates.Should().ContainSingle().Which;
        parsed.Name.Should().Be("Asset");
        parsed.Key.Should().BeOfType<DamlTypeRef>().Which.Name.Should().Be("AssetKey");
        parsed.Implements.Should().ContainSingle().Which.Should()
            .Be(new DamlTypeRef("dep-pkg-id", "TestModule", "ITradeable"));

        var parsedChoice = parsed.Choices.Should().ContainSingle().Which;
        parsedChoice.Name.Should().Be("Transfer");
        parsedChoice.Consuming.Should().BeTrue();
        parsedChoice.ArgumentType.Should().BeOfType<DamlTypeRef>().Which.Name.Should().Be("TransferArg");
        parsedChoice.ReturnType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Bool));
    }

    [Fact]
    public void Parse_template_choice_defaults_to_unit_when_arg_and_return_are_absent()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var templateDn = builder.DottedName("T");
        var choiceNameStr = builder.Intern("Noop");

        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = templateDn,
            ParamInternedStr = builder.Intern("this"),
        };
        template.Choices.Add(new TemplateChoice { Controllers = DalfTestFixtures.DeclaredNoParties(), Observers = DalfTestFixtures.DeclaredNoParties(), NameInternedStr = choiceNameStr, Consuming = false });

        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        var choice = result.Modules.Single().Templates.Single().Choices.Single();
        choice.Consuming.Should().BeFalse();
        choice.ArgumentType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Unit));
        choice.ReturnType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Unit));
    }

    [Fact]
    public void Parse_template_without_key_leaves_key_null()
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
        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        result.Modules.Single().Templates.Single().Key.Should().BeNull();
    }

    [Fact]
    public void Parse_template_implements_resolves_the_interface_package_id_named_by_the_import_table_index()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.PackageImports = new PackageImports
        {
            ImportedPackages = { "table-dep-a", "table-dep-b" },
        };
        var interfaceRef = new TypeConId
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { PackageImportId = 1 },
                ModuleNameInternedDname = builder.DottedName("Other"),
            },
            NameInternedDname = builder.DottedName("IForeign"),
        };

        var implemented = ParseSoleImplementedInterface(builder, interfaceRef);

        implemented.Should().Be(new DamlTypeRef("table-dep-b", "Other", "IForeign"));
    }

    [Fact]
    public void Parse_template_implements_resolves_an_interned_imported_interface_package_id()
    {
        var builder = new DalfPackageBuilder();
        var interfaceRef = builder.ImportedTypeConId(
            builder.DottedName("IForeign"),
            builder.DottedName("Other"),
            importedPackageId: builder.Intern("interned-dep-pkg"));

        var implemented = ParseSoleImplementedInterface(builder, interfaceRef);

        implemented.Should().Be(new DamlTypeRef("interned-dep-pkg", "Other", "IForeign"));
    }

    [Fact]
    public void Parse_interface_resolves_name_view_type_and_choices()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var ifaceDn = builder.DottedName("IToken");
        var viewRef = builder.LocalTypeConId(builder.DottedName("TokenView"), moduleDn);
        var choiceNameStr = builder.Intern("Mint");

        var iface = new DefInterface
        {
            TyconInternedDname = ifaceDn,
            ParamInternedStr = builder.Intern("this"),
            View = new Type { Con = new Type.Types.Con { Tycon = viewRef } },
        };
        iface.Choices.Add(new TemplateChoice
        {
            Controllers = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            NameInternedStr = choiceNameStr,
            Consuming = true,
            RetType = Builtin(BuiltinType.Int64),
        });

        var module = new Module { NameInternedDname = moduleDn };
        module.Interfaces.Add(iface);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        var parsed = result.Modules.Single().Interfaces.Should().ContainSingle().Which;
        parsed.Name.Should().Be("IToken");
        parsed.ViewType.Should().BeOfType<DamlTypeRef>().Which.Name.Should().Be("TokenView");
        var choice = parsed.Choices.Should().ContainSingle().Which;
        choice.Name.Should().Be("Mint");
        choice.ReturnType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public void Parse_interface_without_view_leaves_view_type_null()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var iface = new DefInterface
        {
            TyconInternedDname = builder.DottedName("I"),
            ParamInternedStr = builder.Intern("this"),
        };
        var module = new Module { NameInternedDname = moduleDn };
        module.Interfaces.Add(iface);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        result.Modules.Single().Interfaces.Single().ViewType.Should().BeNull();
    }

    private static Expr PartyList(params Expr[] heads)
    {
        var cons = new Expr.Types.Cons
        {
            Type = Builtin(BuiltinType.Party),
            Tail = new Expr { Nil = new Expr.Types.Nil { Type = Builtin(BuiltinType.Party) } },
        };
        cons.Front.Add(heads);
        return new Expr { Cons = cons };
    }

    private static Expr EmptyPartyList() =>
        new() { Nil = new Expr.Types.Nil { Type = Builtin(BuiltinType.Party) } };

    private static Expr PayloadField(int binderStr, int fieldStr) =>
        new() { RecProj = new Expr.Types.RecProj { FieldInternedStr = fieldStr, Record = new Expr { VarInternedStr = binderStr } } };

    private static TemplateChoice ChoiceWithParties(DalfPackageBuilder builder, int binderStr)
    {
        var holderStr = builder.Intern("holder");
        return new TemplateChoice
        {
            NameInternedStr = builder.Intern("Transfer"),
            Consuming = true,
            RetType = Builtin(BuiltinType.Int64),
            Controllers = PartyList(PayloadField(binderStr, holderStr)),
            Observers = EmptyPartyList(),
        };
    }

    [Fact]
    public void Parse_interface_choice_party_clauses_are_dynamic_even_when_they_resolve_statically()
    {
        var builder = new DalfPackageBuilder();
        var thisStr = builder.Intern("this");
        var iface = new DefInterface
        {
            TyconInternedDname = builder.DottedName("IToken"),
            ParamInternedStr = thisStr,
        };
        iface.Choices.Add(ChoiceWithParties(builder, thisStr));
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Interfaces.Add(iface);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        var choice = result.Modules.Single().Interfaces.Single().Choices.Single();
        choice.Controllers.Source.Should().Be(DamlPartySource.Dynamic);
        choice.Observers.Source.Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Parse_template_choice_party_clauses_resolve_statically()
    {
        var builder = new DalfPackageBuilder();
        var thisStr = builder.Intern("this");
        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = thisStr,
        };
        template.Choices.Add(ChoiceWithParties(builder, thisStr));
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        var choice = result.Modules.Single().Templates.Single().Choices.Single();
        choice.Controllers.Source.Should().Be(DamlPartySource.Static);
        choice.Controllers.Parties.Should().ContainSingle().Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("holder");
        choice.Observers.Source.Should().Be(DamlPartySource.Static);
        choice.Observers.Parties.Should().BeEmpty();
    }
}
