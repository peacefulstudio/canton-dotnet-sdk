// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderOrderingTests
{
    private static TemplateChoice ChoiceNamed(DalfPackageBuilder builder, string name) =>
        new()
        {
            NameInternedStr = builder.Intern(name),
            Controllers = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            Consuming = true,
            RetType = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } },
        };

    private static DamlModule ParseSoleModule(DalfPackageBuilder builder, Module module)
    {
        builder.Package.Modules.Add(module);
        return DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package)).Modules.Single();
    }

    [Fact]
    public void Parse_template_orders_choices_by_ordinal_name()
    {
        var builder = new DalfPackageBuilder();
        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
        };
        foreach (var name in new[] { "Transfer", "alpha", "Archive", "Allocate" })
            template.Choices.Add(ChoiceNamed(builder, name));
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Templates.Add(template);

        var parsed = ParseSoleModule(builder, module).Templates.Single();

        parsed.Choices.Select(choice => choice.Name).Should().Equal("Allocate", "Archive", "Transfer", "alpha");
    }

    [Fact]
    public void Parse_interface_orders_choices_by_ordinal_name()
    {
        var builder = new DalfPackageBuilder();
        var iface = new DefInterface
        {
            TyconInternedDname = builder.DottedName("I"),
            ParamInternedStr = builder.Intern("this"),
        };
        foreach (var name in new[] { "Withdraw", "alpha", "Archive", "Cancel" })
            iface.Choices.Add(ChoiceNamed(builder, name));
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Interfaces.Add(iface);

        var parsed = ParseSoleModule(builder, module).Interfaces.Single();

        parsed.Choices.Select(choice => choice.Name).Should().Equal("Archive", "Cancel", "Withdraw", "alpha");
    }

    [Fact]
    public void Parse_template_orders_implements_by_package_id_then_module_then_name()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var otherModuleDn = builder.DottedName("A.B");
        var template = new DefTemplate
        {
            Signatories = DalfTestFixtures.DeclaredNoParties(),
            Observers = DalfTestFixtures.DeclaredNoParties(),
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
        };
        var zebraPackage = builder.Intern("zzz-package");
        var aardvarkPackage = builder.Intern("aaa-package");
        foreach (var reference in new[]
        {
            builder.ImportedTypeConId(builder.DottedName("IHolding"), moduleDn, zebraPackage),
            builder.ImportedTypeConId(builder.DottedName("IZ"), moduleDn, aardvarkPackage),
            builder.ImportedTypeConId(builder.DottedName("IA"), moduleDn, aardvarkPackage),
            builder.ImportedTypeConId(builder.DottedName("IZ"), otherModuleDn, aardvarkPackage),
        })
        {
            template.Implements.Add(new DefTemplate.Types.Implements { Interface = reference });
        }
        var module = new Module { NameInternedDname = moduleDn };
        module.Templates.Add(template);

        var parsed = ParseSoleModule(builder, module).Templates.Single();

        parsed.Implements.Should().Equal(
            new DamlTypeRef("aaa-package", "A.B", "IZ"),
            new DamlTypeRef("aaa-package", "M", "IA"),
            new DamlTypeRef("aaa-package", "M", "IZ"),
            new DamlTypeRef("zzz-package", "M", "IHolding"));
    }
}
