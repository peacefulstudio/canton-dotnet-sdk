// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterInterfaceDescriptorTests
{
    private const string LocalPackageId = "pkg-id";
    private const string StdlibPackageId = "stdlib-pkg";

    private static DamlPackage Package(params DamlInterface[] interfaces) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Main",
                    Templates = [],
                    DataTypes = [],
                    Interfaces = interfaces,
                },
            ],
            DependencyReferences = [],
        };

    private static DamlPackage StdlibPackage() =>
        new()
        {
            PackageId = StdlibPackageId,
            Name = "daml-stdlib",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };

    private static CodeGenOptions Options => new() { NamespacePrefix = "Test.Package" };

    private static ChoiceEmitter Emitter(DamlPackage package, params DamlPackage[] dependencies)
    {
        var resolution = RealResolution.Of(package, Options, dependencies);
        return new ChoiceEmitter(resolution.Context, resolution.Resolver, Options, new DamlTypeMapper(resolution.Context, resolution.Resolver), new PartyAnalysis());
    }

    private static string EmitDescriptors(DamlInterface iface, string interfaceName, DamlPackage package, params DamlPackage[] dependencies)
    {
        var emitter = Emitter(package, dependencies);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = interfaceName };
        emitter.WriteInterfaceChoiceDescriptors(indent, iface, interfaceName);
        return sb.ToString();
    }

    private static DamlChoice Choice(string name, DamlType argumentType, DamlType returnType, bool consuming = true) =>
        new()
        {
            Name = name,
            ArgumentType = argumentType,
            ReturnType = returnType,
            Consuming = consuming,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlInterface Interface(string name, params DamlChoice[] choices) =>
        new() { Name = name, Choices = choices, ViewType = null };

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_emits_a_choice_descriptor_property_for_a_unit_returning_choice()
    {
        var choice = Choice("Accept", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IAsset", choice), "IAsset", Package());

        output.Should().Contain("public static global::Daml.Runtime.Commands.Choice<IAsset, global::Daml.Runtime.Data.DamlUnit, global::Daml.Runtime.Data.DamlUnit> ChoiceAccept { get; } = new()");
        output.Should().Contain("Name = new global::Daml.Runtime.Commands.ChoiceName(\"Accept\"),");
        output.Should().Contain("Consuming = true,");
        output.Should().Contain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance,");
        output.Should().Contain("ArgumentDecoder = val => val is global::Daml.Runtime.Data.DamlUnit u ? u : throw new global::System.InvalidOperationException(\"Choice 'Accept' argument must decode to DamlUnit.\"),");
        output.Should().Contain("ResultDecoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance");
        output.Should().Contain("ArgumentJsonReader = (json, context) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadUnit(json, context),");
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_requires_the_namespace_for_a_bare_primitive_argument_type_not_only_the_return_type()
    {
        var choice = Choice("SetDate", new DamlPrimitiveType(DamlPrimitive.Date), new DamlPrimitiveType(DamlPrimitive.Unit));
        var emitter = Emitter(Package());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = "IAsset" };

        emitter.WriteInterfaceChoiceDescriptors(indent, Interface("IAsset", choice), "IAsset");

        indent.RequiredUsings.Should().Contain("System");
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_decodes_a_record_argument_through_its_FromRecord()
    {
        var choice = Choice("Transfer", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IAsset", choice), "IAsset", Package());

        output.Should().Contain("public static global::Daml.Runtime.Commands.Choice<IAsset, global::Test.Package.Main.TransferArg, global::Daml.Runtime.Data.DamlUnit> ChoiceTransfer { get; } = new()");
        output.Should().Contain("ArgumentEncoder = arg => arg.ToRecord(),");
        output.Should().Contain("ArgumentDecoder = val => global::Test.Package.Main.TransferArg.FromRecord(val.As<global::Daml.Runtime.Data.DamlRecord>()),");
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_encodes_synthetic_stdlib_interface_archive_argument_as_empty_record()
    {
        var choice = Choice("Archive", new DamlTypeRef(StdlibPackageId, "DA.Internal.Template", "Archive"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IArchivable", choice), "IArchivable", Package(), StdlibPackage());

        output.Should().Contain("public static global::Daml.Runtime.Commands.Choice<IArchivable, global::Daml.Runtime.Data.DamlUnit, global::Daml.Runtime.Data.DamlUnit> ChoiceArchive { get; } = new()");
        output.Should().Contain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlRecord.Create(),");
        output.Should().NotContain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance,");
        output.Should().Contain("ArgumentDecoder = val => val is global::Daml.Runtime.Data.DamlRecord { Fields.Count: 0 } ? global::Daml.Runtime.Data.DamlUnit.Instance : throw new global::System.InvalidOperationException(\"Choice 'Archive' argument must decode to an empty record.\"),");
        output.Should().Contain(
            "ArgumentJsonReader = (json, context) =>\n"
            + "    {\n"
            + "        global::Daml.Runtime.Serialization.DamlLfJsonDecoders.RequireObject(json, context);\n"
            + "        return global::Daml.Runtime.Data.DamlRecord.Create();\n"
            + "    },");
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_emits_one_property_per_choice_in_declaration_order()
    {
        var accept = Choice("Accept", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));
        var reject = Choice("Reject", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IAsset", accept, reject), "IAsset", Package());

        output.IndexOf("ChoiceAccept", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf("ChoiceReject", StringComparison.Ordinal));
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_decodes_a_bare_primitive_argument_through_the_general_value_conversion()
    {
        var choice = Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Party), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IHolding", choice), "IHolding", Package());

        output.Should().Contain("public static global::Daml.Runtime.Commands.Choice<IHolding, global::Daml.Runtime.Data.Party, global::Daml.Runtime.Data.DamlUnit> ChoiceTransfer { get; } = new()");
        output.Should().Contain("ArgumentEncoder = arg => arg.ToDamlValue(),");
        output.Should().Contain("ArgumentDecoder = val => global::Daml.Runtime.Data.Party.FromDamlValue(val.As<global::Daml.Runtime.Data.DamlParty>()),");
    }
}
