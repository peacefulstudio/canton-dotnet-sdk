// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterArchiveChoiceTests
{
    private const string LocalPackageId = "pkg-id";
    private const string StdlibPackageId = "daml-prim-pkg-id";
    private const string UserPackageId = "user-pkg-id";

    private static readonly DamlPackage StdlibPackage = new()
    {
        PackageId = StdlibPackageId,
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    private static readonly DamlPackage UserPackage = new()
    {
        PackageId = UserPackageId,
        Name = "user-package",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [TestPackages.ModuleOf("DA.Internal.Template", TestPackages.Record("Archive"))],
        DependencyReferences = [],
    };

    private static DamlPackage Package(DamlModule module) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

    private static ChoiceEmitter Emitter(RealResolution resolution) =>
        new(resolution.Context, resolution.Resolver, Options, new DamlTypeMapper(resolution.Context, resolution.Resolver), new PartyAnalysis());

    private static CodeGenOptions Options => new() { NamespacePrefix = "Test.Package" };

    private static DamlChoice ArchiveChoice(string packageId) =>
        new()
        {
            Name = "Archive",
            Consuming = true,
            ArgumentType = new DamlTypeRef(packageId, "DA.Internal.Template", "Archive"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
        };

    private static DamlTemplate ItemTemplate(string archiveArgPackageId) =>
        new()
        {
            Name = "Item",
            Choices = [ArchiveChoice(archiveArgPackageId)],
        };

    private static string EmitNonContract(DamlTemplate template, params DamlPackage[] dependencies)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [template], DataTypes = [], Interfaces = [] });
        var resolution = RealResolution.Of(package, Options, dependencies);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        Emitter(resolution).TryWriteNonContractChoiceExtensions(indent, template);
        return sb.ToString();
    }

    private static string EmitContractIdExercisers(DamlTemplate template, params DamlPackage[] dependencies)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [template], DataTypes = [], Interfaces = [] });
        var resolution = RealResolution.Of(package, Options, dependencies);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        Emitter(resolution).WriteChoiceAsyncExercisersClass(indent, template, template.Name, []);
        return sb.ToString();
    }

    private static string EmitDescriptors(DamlTemplate template, params DamlPackage[] dependencies)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [template], DataTypes = [], Interfaces = [] });
        var resolution = RealResolution.Of(package, Options, dependencies);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        Emitter(resolution).WriteChoiceDescriptors(indent, template);
        return sb.ToString();
    }

    private static string EmitInterfaceExtensions(DamlInterface iface, string interfaceName, params DamlPackage[] dependencies)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [], DataTypes = [], Interfaces = [iface] });
        var resolution = RealResolution.Of(package, Options, dependencies);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);
        Emitter(resolution).WriteInterfaceChoiceExtensions(indent, iface, interfaceName);
        return sb.ToString();
    }

    [Fact]
    public void ArchiveChoice_emits_synthetic_stdlib_archive_in_non_contract_wrappers()
    {
        
        var output = EmitNonContract(ItemTemplate(StdlibPackageId), StdlibPackage);

        output.Should().Contain("ItemNonContractExtensions");
        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryArchiveAsync(");
        output.Should().Contain("DamlRecord.Create()");
        output.Should().NotContain("DamlUnit.Instance");
    }

    [Fact]
    public void ArchiveChoice_keeps_user_archive_choice_from_non_stdlib_package()
    {
        
        var output = EmitNonContract(ItemTemplate(UserPackageId), UserPackage);

        output.Should().Contain("ItemNonContractExtensions");
        output.Should().Contain("TryArchiveAsync(");
    }

    [Fact]
    public void ArchiveChoice_uses_actual_argument_type_not_damlunit_for_user_archive()
    {
                var template = ItemTemplate(UserPackageId);

        var exerciser = EmitNonContract(template, UserPackage);
        var descriptor = EmitDescriptors(template, UserPackage);

        exerciser.Should().Contain("ItemNonContractExtensions");
        exerciser.Should().Contain("TryArchiveAsync(");
        exerciser.Should().Contain("global::DA.Internal.Template.Archive argument,");
        exerciser.Should().Contain("argument.ToRecord()");
        descriptor.Should().Contain("ArgumentEncoder = arg => arg.ToRecord(),");
        descriptor.Should().NotContain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance");
    }

    [Fact]
    public void ArchiveChoice_uses_actual_argument_type_not_damlunit_for_user_archive_interface_choice()
    {
        var iface = new DamlInterface
        {
            Name = "Archivable",
            Choices = [ArchiveChoice(UserPackageId)],
            ViewType = null,
        };
        
        var output = EmitInterfaceExtensions(iface, "IArchivable", UserPackage);

        output.Should().Contain("IArchivableExtensions");
        output.Should().Contain("TryArchiveAsync(");
        output.Should().Contain("global::DA.Internal.Template.Archive argument,");
        output.Should().Contain("argument.ToRecord()");
    }

    [Fact]
    public void ArchiveChoice_encodes_synthetic_stdlib_archive_argument_as_empty_record_in_descriptor()
    {
                var template = ItemTemplate(StdlibPackageId);

        var descriptor = EmitDescriptors(template, StdlibPackage);

        descriptor.Should().Contain("public static global::Daml.Runtime.Commands.Choice<Item, global::Daml.Runtime.Data.DamlUnit, global::Daml.Runtime.Data.DamlUnit> ChoiceArchive { get; } = new()");
        descriptor.Should().Contain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlRecord.Create(),");
        descriptor.Should().NotContain("ArgumentEncoder = _ => global::Daml.Runtime.Data.DamlUnit.Instance");
    }

    public static TheoryData<string, DamlType, string, string> ArgumentlessContractIdCommandEncodings => new()
    {
        {
            "Archive",
            new DamlTypeRef(StdlibPackageId, "DA.Internal.Template", "Archive"),
            "DamlRecord.Create());",
            "DamlUnit.Instance"
        },
        {
            "Accept",
            new DamlPrimitiveType(DamlPrimitive.Unit),
            "DamlUnit.Instance);",
            "DamlRecord.Create()"
        },
    };

    [Theory]
    [MemberData(nameof(ArgumentlessContractIdCommandEncodings))]
    public void ArchiveChoice_contract_id_command_builder_encodes_argument_like_the_descriptor(
        string choiceName,
        DamlType argumentType,
        string encodedArgument,
        string rejectedArgument)
    {
                var template = new DamlTemplate
        {
            Name = "Item",
            Choices =
            [
                new DamlChoice
                {
                    Name = choiceName,
                    Consuming = true,
                    ArgumentType = argumentType,
                    ReturnType = new DamlTypeApp(
                        new DamlPrimitiveType(DamlPrimitive.ContractId),
                        [new DamlTypeRef(LocalPackageId, "Main", "Item")]),
                }
            ],
        };

        var output = EmitContractIdExercisers(template, StdlibPackage);

        output.Should().Contain($"public static global::Daml.Runtime.Commands.ExerciseCommand {choiceName}Command(");
        output.Should().Contain(encodedArgument);
        output.Should().NotContain(rejectedArgument);
    }

    [Fact]
    public void ArchiveChoice_encodes_synthetic_stdlib_interface_archive_argument_as_empty_record()
    {
        var iface = new DamlInterface
        {
            Name = "Archivable",
            Choices = [ArchiveChoice(StdlibPackageId)],
            ViewType = null,
        };
        
        var output = EmitInterfaceExtensions(iface, "IArchivable", StdlibPackage);

        output.Should().Contain("global::Daml.Runtime.Commands.ExerciseCommand.For<IArchivable>(contractId, new global::Daml.Runtime.Commands.ChoiceName(\"Archive\"), global::Daml.Runtime.Data.DamlRecord.Create());");
        output.Should().NotContain("DamlUnit.Instance");
    }
}
