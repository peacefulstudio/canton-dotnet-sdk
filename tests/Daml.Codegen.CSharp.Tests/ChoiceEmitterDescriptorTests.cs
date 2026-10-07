// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterDescriptorTests
{
    private const string LocalPackageId = "pkg-id";
    private const string StdlibPackageId = "stdlib-pkg";

    private static DamlPackage Package(params DamlDataType[] dataTypes) => PackageDeclaring([], dataTypes);

    private static DamlPackage PackageDeclaring(DamlTemplate template, params DamlDataType[] dataTypes) =>
        PackageDeclaring([template], dataTypes);

    private static DamlPackage PackageDeclaring(IReadOnlyList<DamlTemplate> templates, DamlDataType[] dataTypes) =>
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
                    Templates = templates,
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static CodeGenOptions Options => new() { NamespacePrefix = "Test.Package" };

    private static RealResolution Resolution(DamlPackage package, params DamlPackage[] dependencies) =>
        RealResolution.Of(package, Options, dependencies);

    private static ChoiceEmitter Emitter(PackageEmitContext context, RealResolution resolution) =>
        new(context, resolution.Resolver, Options, new DamlTypeMapper(context, resolution.Resolver), new PartyAnalysis());

    private static ChoiceEmitter Emitter(RealResolution resolution) => Emitter(resolution.Context, resolution);

    private static string EmitDescriptors(DamlTemplate template, DamlPackage package, params DamlPackage[] dependencies)
    {
        var emitter = Emitter(Resolution(package, dependencies));
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceDescriptors(indent, template);
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

    private static DamlTemplate Template(params DamlChoice[] choices) =>
        new()
        {
            Name = "Asset",
            Choices = choices,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    [Fact]
    public void ChoiceEmitterDescriptor_emits_a_choice_descriptor_property_for_a_unit_returning_choice()
    {
        var choice = Choice("Accept", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Template(choice), Package());

        output.Should().Contain("public static global::Daml.Runtime.Commands.Choice<Asset, global::Daml.Runtime.Data.DamlUnit, global::Daml.Runtime.Data.DamlUnit> ChoiceAccept { get; } = new()");
        output.Should().Contain("Name = new global::Daml.Runtime.Commands.ChoiceName(\"Accept\"),");
        output.Should().Contain("Consuming = true,");
        output.Should().Contain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance,");
        output.Should().Contain("ArgumentDecoder = val => val is global::Daml.Runtime.Data.DamlUnit u ? u : throw new global::System.InvalidOperationException(\"Choice 'Accept' argument must decode to DamlUnit.\"),");
        output.Should().Contain("ResultDecoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance");
        output.Should().Contain("ArgumentJsonReader = (json, context) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadUnit(json, context),");
    }

    [Fact]
    public void ChoiceEmitterDescriptor_validates_wire_shape_before_returning_the_singleton_for_a_synthetic_archive_argument()
    {
        var stdlibPackage = new DamlPackage
        {
            PackageId = StdlibPackageId,
            Name = "daml-prim",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };
        var choice = Choice("Archive", new DamlTypeRef(StdlibPackageId, "DA.Internal.Template", "Archive"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Template(choice), Package(), stdlibPackage);

        output.Should().Contain("ArgumentDecoder = val => val is global::Daml.Runtime.Data.DamlRecord { Fields.Count: 0 } ? global::Daml.Runtime.Data.DamlUnit.Instance : throw new global::System.InvalidOperationException(\"Choice 'Archive' argument must decode to an empty record.\"),");
        output.Should().Contain(
            "ArgumentJsonReader = (json, context) =>\n"
            + "    {\n"
            + "        global::Daml.Runtime.Serialization.DamlLfJsonDecoders.RequireObject(json, context);\n"
            + "        return global::Daml.Runtime.Data.DamlRecord.Create();\n"
            + "    },");
        output.Should().NotContain("ArgumentDecoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance,");
    }

    [Fact]
    public void ChoiceEmitterDescriptor_decodes_a_nested_record_argument_through_its_FromRecord()
    {
        var argRecord = new DamlDataType
        {
            Name = "TransferArg",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party))]),
        };
        var choice = Choice("Transfer", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Template(choice), PackageDeclaring(Template(choice), argRecord));

        output.Should().Contain("public static global::Daml.Runtime.Commands.Choice<Asset, Transfer, global::Daml.Runtime.Data.DamlUnit> ChoiceTransfer { get; } = new()");
        output.Should().Contain("ArgumentEncoder = arg => arg.ToRecord(),");
        output.Should().Contain("ArgumentDecoder = val => Transfer.FromRecord(val.As<global::Daml.Runtime.Data.DamlRecord>()),");
    }

    /// <summary>
    /// A choice literally named <c>DamlRecord</c> emits a nested argument record of that same
    /// name into the template partial (see <see cref="ChoiceEmitter.GetChoiceArgumentInfo"/>),
    /// which shadows the unqualified runtime <c>Daml.Runtime.Data.DamlRecord</c> reference this
    /// decoder casts through; <see cref="TypeReferenceQualifier"/> cannot see that shadow (it is
    /// scoped to the module, not this template), so the emitter must root-qualify it itself.
    /// </summary>
    [Fact]
    public void ChoiceEmitterDescriptor_root_qualifies_the_As_cast_when_the_nested_argument_is_named_DamlRecord()
    {
        var argRecord = new DamlDataType
        {
            Name = "TransferArg",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party))]),
        };
        var choice = Choice("DamlRecord", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Template(choice), PackageDeclaring(Template(choice), argRecord));

        output.Should().Contain("ArgumentDecoder = val => DamlRecord.FromRecord(val.As<global::Daml.Runtime.Data.DamlRecord>()),");
        output.Should().NotContain("val.As<DamlRecord>()");
    }

    [Fact]
    public void ChoiceEmitterDescriptor_decodes_an_optional_type_variable_return_through_the_wrapper()
    {
        var choice = Choice(
            "MaybeOf",
            new DamlPrimitiveType(DamlPrimitive.Unit),
            new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [new DamlTypeVar("a")]));

        var output = EmitDescriptors(Template(choice), Package());

        output.Should().Contain("ResultDecoder = val => global::Daml.Runtime.Stdlib.Optional<TA>.FromValue(");
        output.Should().NotContain(".AsOptional().HasValue");
        output.Should().Contain(
            "ResultJsonReader = (json, context) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadOptional(json, context, "
            + "(__json0, __ctx0) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadUnsupported(__json0, __ctx0, \"a\")),");
    }

    [Fact]
    public void WriteChoiceDescriptors_throws_instead_of_emitting_a_stub_arg_record()
    {
        var choice = Choice("Mystery", new DamlTypeVar("a"), new DamlPrimitiveType(DamlPrimitive.Unit));

        FluentActions.Invoking(() => EmitDescriptors(Template(choice), Package()))
            .Should().Throw<CodegenException>()
            .WithMessage("*Mystery*");
    }

    [Fact]
    public void GetChoiceArgumentInfo_classifies_unit_as_damlunit()
    {
        var emitter = Emitter(Resolution(Package()));
        var choice = Choice("Accept", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));

        var argument = emitter.GetChoiceArgumentInfo(choice);

        argument.TypeName.Should().Be("DamlUnit");
    }

    public static TheoryData<string, DamlType> UnmappableChoiceArgumentTypes() => new()
    {
        { "a bare Daml type variable", new DamlTypeVar("a") },
        { "a non-Unit primitive", new DamlPrimitiveType(DamlPrimitive.Text) },
        { "a generic type application", new DamlTypeApp(new DamlTypeRef("", "Main", "Unresolved"), [new DamlPrimitiveType(DamlPrimitive.Int64)]) },
    };

    [Theory]
    [MemberData(nameof(UnmappableChoiceArgumentTypes))]
    public void GetChoiceArgumentInfo_throws_codegen_exception_for_an_unmappable_argument_type(string label, DamlType argumentType)
    {
        var emitter = Emitter(Resolution(Package()));
        var choice = Choice("Mystery", argumentType, new DamlPrimitiveType(DamlPrimitive.Unit));

        emitter.Invoking(e => e.GetChoiceArgumentInfo(choice))
            .Should().Throw<CodegenException>(
                "{0} cannot back an exercisable choice-argument record, so generation must fail loudly", label)
            .WithMessage("*Mystery*");
    }

    [Fact]
    public void GetChoiceArgumentInfo_resolves_a_local_record_argument_to_a_nested_type()
    {
        var argRecord = new DamlDataType
        {
            Name = "TransferArg",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party))]),
        };
        var choice = Choice("Transfer", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"), new DamlPrimitiveType(DamlPrimitive.Unit));
        var package = PackageDeclaring(Template(choice), argRecord);
        var emitter = Emitter(Resolution(package));

        var argument = emitter.GetChoiceArgumentInfo(choice);

        argument.TypeName.Should().Be("Transfer");
        argument.IsNestedTemplateArg.Should().BeTrue();
        argument.Fields.Should().NotBeNull();
    }

    [Fact]
    public void GetChoiceArgumentInfo_resolves_a_foreign_package_argument_through_the_resolver_even_when_a_local_record_shares_its_module_and_name()
    {
        var argRecord = new DamlDataType
        {
            Name = "TransferArg",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party))]),
        };
        var localChoice = Choice("Transfer", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"), new DamlPrimitiveType(DamlPrimitive.Unit));
        var foreignChoice = Choice("Forward", new DamlTypeRef("other-pkg", "Main", "TransferArg"), new DamlPrimitiveType(DamlPrimitive.Unit));
        var foreignPackage = TestPackages.Named("other-pkg", "other-package", TestPackages.ModuleOf("Main", TestPackages.Record("TransferArg")));
        var emitter = Emitter(Resolution(PackageDeclaring(Template(localChoice, foreignChoice), argRecord), foreignPackage));

        var argument = emitter.GetChoiceArgumentInfo(foreignChoice);

        argument.TypeName.Should().Be("global::Main.TransferArg");
        argument.IsNestedTemplateArg.Should().BeFalse();
    }

    [Fact]
    public void GetChoiceArgumentInfo_resolves_the_argument_from_its_own_module_when_simple_names_collide()
    {
        var choice = Choice("Expire", new DamlTypeRef(LocalPackageId, "Agreement", "Expire"), new DamlPrimitiveType(DamlPrimitive.Unit));
        var package = new DamlPackage
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                ModuleWithExpireRecord(
                    "Agreement",
                    [new DamlFieldDefinition("actor", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Template(choice)),
                ModuleWithExpireRecord("Offer", []),
            ],
            DependencyReferences = [],
        };
        var resolution = Resolution(package);
        var emitter = Emitter(resolution.Contexts.Single(moduleContext => moduleContext.Module.Name == "Agreement"), resolution);

        var argument = emitter.GetChoiceArgumentInfo(choice);

        argument.Fields.Should().ContainSingle(field => field.Name == "actor");
    }

    private static DamlModule ModuleWithExpireRecord(string moduleName, DamlFieldDefinition[] expireFields, DamlTemplate? template = null) =>
        new()
        {
            Name = moduleName,
            Templates = template is null ? [] : [template],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Expire",
                    Definition = new DamlRecordDefinition(expireFields),
                },
            ],
            Interfaces = [],
        };
}
