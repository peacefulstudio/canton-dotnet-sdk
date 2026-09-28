// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterCrossPackageChoiceTests
{
    private const string LocalPackageId = "pkg-id";
    private const string ForeignPackageId = "other-pkg-id";

    private sealed class StubResolver(string? resolvedName = null) : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => resolvedName ?? Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

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

    private static string EmitNonContract(DamlTemplate template, StubResolver resolver, params DamlDataType[] dataTypes)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [template], DataTypes = dataTypes, Interfaces = [] });
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        Emitter(context, resolver).TryWriteNonContractChoiceExtensions(indent, template, context.DataTypes);
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

    private static DamlRecordDefinition SingleTextField(string fieldName) =>
        new([new DamlFieldDefinition(fieldName, new DamlPrimitiveType(DamlPrimitive.Text))]);

    [Fact]
    public void ChoiceEmitterCrossPackageChoice_non_contract_exerciser_resolves_cross_package_argument_type()
    {
        var template = new DamlTemplate
        {
            Name = "Trader",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Submit",
                    Consuming = false,
                    ArgumentType = new DamlTypeRef(ForeignPackageId, "Other.Module", "OrderRequest"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                },
            ],
        };

        var output = EmitNonContract(template, new StubResolver("Other.Pkg.OrderRequest"));

        output.Should().Contain("TraderNonContractExtensions");
        output.Should().Contain("SubmitAsync(");
        output.Should().Contain("Other.Pkg.OrderRequest argument,");
        output.Should().Contain("argument.ToRecord()");
    }

    [Fact]
    public void ChoiceEmitterCrossPackageChoice_non_contract_exerciser_resolves_cross_package_argument_when_simple_name_collides_with_local_record()
    {
        var template = new DamlTemplate
        {
            Name = "Trader",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Submit",
                    Consuming = false,
                    ArgumentType = new DamlTypeRef(ForeignPackageId, "Other.Module", "Quote"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                },
            ],
        };
        var localQuote = new DamlDataType { Name = "Quote", Definition = SingleTextField("local") };

        var output = EmitNonContract(template, new StubResolver("Other.Pkg.Quote"), localQuote);

        output.Should().Contain("Other.Pkg.Quote argument,");
        output.Should().NotContain("Trader.Submit argument,");
    }

    [Fact]
    public void ChoiceEmitterCrossPackageChoice_interface_choice_resolves_cross_package_argument_type()
    {
        var iface = new DamlInterface
        {
            Name = "Transferable",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Transfer",
                    Consuming = false,
                    ArgumentType = new DamlTypeRef(ForeignPackageId, "Other.Module", "TransferRequest"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                },
            ],
            ViewType = null,
        };

        var output = EmitInterfaceExtensions(iface, "ITransferable", new StubResolver("Other.Pkg.TransferRequest"));

        output.Should().Contain("public static class ITransferableExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<DamlUnit>> TryTransferAsync(");
        output.Should().Contain("Other.Pkg.TransferRequest argument,");
        output.Should().Contain("argument.ToRecord()");
    }

    [Fact]
    public void ChoiceEmitterCrossPackageChoice_interface_choice_emits_primitive_argument_without_fallback_arg_type()
    {
        var iface = new DamlInterface
        {
            Name = "Quotable",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Quote",
                    Consuming = false,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Text),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                },
            ],
            ViewType = null,
        };

        var output = EmitInterfaceExtensions(iface, "IQuotable", new StubResolver());

        output.Should().Contain("public static class IQuotableExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<DamlUnit>> TryQuoteAsync(");
        output.Should().Contain("string argument,");
        output.Should().NotContain("QuoteArg argument,");
    }

    private static readonly DamlPackage StdlibStub = new()
    {
        PackageId = "daml-prim-pkg-id",
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    private static DarModel CreateDar(DamlModule module) =>
        new()
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [StdlibStub],
        };

    [Fact]
    public void Generate_throws_at_codegen_time_for_unresolvable_cross_package_ref()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Trader",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Submit",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("missing-pkg-id", "Other.Module", "OrderRequest"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Trader",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var act = () => CreateGenerator().Generate(CreateDar(module));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Other.Module:OrderRequest*missing-pkg-id*not present in the DAR*");
    }
}
