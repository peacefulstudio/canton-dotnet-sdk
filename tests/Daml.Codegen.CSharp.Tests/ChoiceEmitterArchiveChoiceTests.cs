// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
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
        Modules = [],
        DependencyReferences = [],
    };

    private sealed class StubResolver(
        string? resolvedName = null,
        IReadOnlyDictionary<string, DamlPackage>? packages = null) : ICrossPackageResolver
    {
        private readonly IReadOnlyDictionary<string, DamlPackage> _packages = packages ?? new Dictionary<string, DamlPackage>();

        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => resolvedName ?? Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) =>
            _packages.TryGetValue(packageId, out var package) ? package : null;
    }

    private static IReadOnlyDictionary<string, DamlPackage> Packages(params DamlPackage[] packages) =>
        packages.ToDictionary(p => p.PackageId, p => p);

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

    private static ChoiceEmitter Emitter(PackageEmitContext context, StubResolver resolver) =>
        new(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());

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

    private static string EmitNonContract(DamlTemplate template, StubResolver resolver)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [template], DataTypes = [], Interfaces = [] });
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        Emitter(context, resolver).TryWriteNonContractChoiceExtensions(indent, template, context.DataTypes);
        return sb.ToString();
    }

    private static string EmitContractIdExercisers(DamlTemplate template, StubResolver resolver)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [template], DataTypes = [], Interfaces = [] });
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        Emitter(context, resolver).WriteChoiceAsyncExercisersClass(indent, template, template.Name, [], context.DataTypes);
        return sb.ToString();
    }

    private static string EmitDescriptors(DamlTemplate template, StubResolver resolver)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [template], DataTypes = [], Interfaces = [] });
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        Emitter(context, resolver).WriteChoiceDescriptors(indent, template);
        return sb.ToString();
    }

    private static string EmitInterfaceExtensions(DamlInterface iface, string interfaceName, StubResolver resolver)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [], DataTypes = [], Interfaces = [iface] });
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);
        Emitter(context, resolver).WriteInterfaceChoiceExtensions(indent, iface, interfaceName);
        return sb.ToString();
    }

    [Fact]
    public void ArchiveChoice_emits_synthetic_stdlib_archive_in_non_contract_wrappers()
    {
        var resolver = new StubResolver(packages: Packages(StdlibPackage));

        var output = EmitNonContract(ItemTemplate(StdlibPackageId), resolver);

        output.Should().Contain("ItemNonContractExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<DamlUnit>> TryArchiveAsync(");
        output.Should().Contain("DamlRecord.Create()");
        output.Should().NotContain("DamlUnit.Instance");
    }

    [Fact]
    public void ArchiveChoice_keeps_user_archive_choice_from_non_stdlib_package()
    {
        var resolver = new StubResolver(packages: Packages(UserPackage));

        var output = EmitNonContract(ItemTemplate(UserPackageId), resolver);

        output.Should().Contain("ItemNonContractExtensions");
        output.Should().Contain("TryArchiveAsync(");
    }

    [Fact]
    public void ArchiveChoice_uses_actual_argument_type_not_damlunit_for_user_archive()
    {
        var resolver = new StubResolver("User.Package.Archive", Packages(UserPackage));
        var template = ItemTemplate(UserPackageId);

        var exerciser = EmitNonContract(template, resolver);
        var descriptor = EmitDescriptors(template, resolver);

        exerciser.Should().Contain("ItemNonContractExtensions");
        exerciser.Should().Contain("TryArchiveAsync(");
        exerciser.Should().Contain("User.Package.Archive argument,");
        exerciser.Should().Contain("argument.ToRecord()");
        descriptor.Should().Contain("ArgumentEncoder = arg => arg.ToRecord(),");
        descriptor.Should().NotContain("ArgumentEncoder = _ => DamlUnit.Instance");
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
        var resolver = new StubResolver("User.Package.Archive", Packages(UserPackage));

        var output = EmitInterfaceExtensions(iface, "IArchivable", resolver);

        output.Should().Contain("IArchivableExtensions");
        output.Should().Contain("TryArchiveAsync(");
        output.Should().Contain("User.Package.Archive argument,");
        output.Should().Contain("argument.ToRecord()");
    }

    [Fact]
    public void ArchiveChoice_encodes_synthetic_stdlib_archive_argument_as_empty_record_in_descriptor()
    {
        var resolver = new StubResolver(packages: Packages(StdlibPackage));
        var template = ItemTemplate(StdlibPackageId);

        var descriptor = EmitDescriptors(template, resolver);

        descriptor.Should().Contain("public static Choice<Item, DamlUnit, DamlUnit> ChoiceArchive { get; } = new()");
        descriptor.Should().Contain("ArgumentEncoder = _ => DamlRecord.Create(),");
        descriptor.Should().NotContain("ArgumentEncoder = _ => DamlUnit.Instance");
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
        var resolver = new StubResolver(packages: Packages(StdlibPackage));
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

        var output = EmitContractIdExercisers(template, resolver);

        output.Should().Contain($"public static ExerciseCommand {choiceName}Command(");
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
        var resolver = new StubResolver(packages: Packages(StdlibPackage));

        var output = EmitInterfaceExtensions(iface, "IArchivable", resolver);

        output.Should().Contain("ExerciseCommand.For<IArchivable>(contractId, new ChoiceName(\"Archive\"), DamlRecord.Create());");
        output.Should().NotContain("DamlUnit.Instance");
    }
}
