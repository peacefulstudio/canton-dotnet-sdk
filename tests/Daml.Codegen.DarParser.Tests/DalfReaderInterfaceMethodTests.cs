// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderInterfaceMethodTests
{
    private static Type Builtin(BuiltinType builtin) =>
        new() { Builtin = new Type.Types.Builtin { Builtin_ = builtin } };

    private static InterfaceMethod MethodNamed(DalfPackageBuilder builder, string name, Type returnType) =>
        new() { MethodInternedName = builder.Intern(name), Type = returnType };

    private static DefInterface InterfaceWith(DalfPackageBuilder builder, params InterfaceMethod[] methods)
    {
        var iface = new DefInterface
        {
            TyconInternedDname = builder.DottedName("I"),
            ParamInternedStr = builder.Intern("this"),
        };
        iface.Methods.AddRange(methods);
        return iface;
    }

    private static DamlPackage ParseModuleHolding(DalfPackageBuilder builder, DefInterface iface)
    {
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Interfaces.Add(iface);
        builder.Package.Modules.Add(module);
        return DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));
    }

    [Fact]
    public void Parse_interface_reads_each_method_name_and_return_type()
    {
        var builder = new DalfPackageBuilder();
        var iface = InterfaceWith(
            builder,
            MethodNamed(builder, "holder", Builtin(BuiltinType.Party)),
            MethodNamed(builder, "amount", Builtin(BuiltinType.Int64)));

        var methods = ParseModuleHolding(builder, iface).Modules.Single().Interfaces.Single().Methods;

        methods.Should().Equal(
            new DamlInterfaceMethod("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
            new DamlInterfaceMethod("holder", new DamlPrimitiveType(DamlPrimitive.Party)));
    }

    [Fact]
    public void Parse_interface_orders_methods_by_ordinal_name()
    {
        var builder = new DalfPackageBuilder();
        var iface = InterfaceWith(
            builder,
            MethodNamed(builder, "transfer", Builtin(BuiltinType.Text)),
            MethodNamed(builder, "alpha", Builtin(BuiltinType.Text)),
            MethodNamed(builder, "Archive", Builtin(BuiltinType.Text)),
            MethodNamed(builder, "cancel", Builtin(BuiltinType.Text)));

        var methods = ParseModuleHolding(builder, iface).Modules.Single().Interfaces.Single().Methods;

        methods.Select(method => method.Name).Should().Equal("Archive", "alpha", "cancel", "transfer");
    }

    [Fact]
    public void Parse_interface_without_methods_yields_an_empty_method_list()
    {
        var builder = new DalfPackageBuilder();

        var methods = ParseModuleHolding(builder, InterfaceWith(builder)).Modules.Single().Interfaces.Single().Methods;

        methods.Should().BeEmpty();
    }

    [Fact]
    public void Parse_collects_dependency_from_an_interface_method_return_type()
    {
        var builder = new DalfPackageBuilder();
        var foreign = new Type
        {
            Con = new Type.Types.Con
            {
                Tycon = builder.ImportedTypeConId(
                    builder.DottedName("Foreign"), builder.DottedName("Other"), builder.Intern("method-dep"))
            }
        };
        var iface = InterfaceWith(builder, MethodNamed(builder, "getForeign", foreign));

        var package = ParseModuleHolding(builder, iface);

        package.DependencyReferences.Select(reference => reference.PackageId).Should().Equal("method-dep");
    }
}
