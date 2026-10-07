// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterCrossPackageChoiceTests
{
    private const string LocalPackageId = "pkg-id";
    private const string ForeignPackageId = "other-pkg-id";

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

    private static CodeGenOptions Options => new() { NamespacePrefix = "Test.Package" };

    private static DamlPackage ForeignPackage(params string[] recordNames) =>
        TestPackages.Named(
            ForeignPackageId,
            "other-package",
            TestPackages.ModuleOf("Other.Module", recordNames.Select(name => TestPackages.Record(name)).ToArray()));

    private static ChoiceEmitter Emitter(RealResolution resolution) =>
        new(resolution.Context, resolution.Resolver, Options, new DamlTypeMapper(resolution.Context, resolution.Resolver), new PartyAnalysis());

    private static string EmitNonContract(DamlTemplate template, DamlPackage[] dependencies, params DamlDataType[] dataTypes)
    {
        var package = Package(new DamlModule { Name = "Main", Templates = [template], DataTypes = dataTypes, Interfaces = [] });
        var resolution = RealResolution.Of(package, Options, dependencies);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        Emitter(resolution).TryWriteNonContractChoiceExtensions(indent, template);
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

        var output = EmitNonContract(template, [ForeignPackage("OrderRequest")]);

        output.Should().Contain("TraderNonContractExtensions");
        output.Should().Contain("SubmitAsync(");
        output.Should().Contain("global::Other.Module.OrderRequest argument,");
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

        var output = EmitNonContract(template, [ForeignPackage("Quote")], localQuote);

        output.Should().Contain("global::Other.Module.Quote argument,");
        output.Should().NotContain("Trader.Submit argument,");
        output.Should().NotContain("global::Test.Package.Main.Quote argument,");
    }

    [Fact]
    public void ChoiceEmitterCrossPackageChoice_non_contract_exerciser_resolves_a_foreign_choice_argument_through_the_template_that_nests_it()
    {
        var foreignVault = new DamlTemplate
        {
            Name = "Vault",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Withdraw",
                    Consuming = false,
                    ArgumentType = new DamlTypeRef(ForeignPackageId, "Other.Module", "WithdrawArgs"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                },
            ],
        };
        var foreignPackage = TestPackages.Named(
            ForeignPackageId,
            "other-package",
            new DamlModule
            {
                Name = "Other.Module",
                Templates = [foreignVault],
                DataTypes = [TestPackages.Record("Vault"), TestPackages.Record("WithdrawArgs")],
                Interfaces = [],
            });
        var template = new DamlTemplate
        {
            Name = "Trader",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Submit",
                    Consuming = false,
                    ArgumentType = new DamlTypeRef(ForeignPackageId, "Other.Module", "WithdrawArgs"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                },
            ],
        };

        var output = EmitNonContract(template, [foreignPackage]);

        output.Should().Contain("global::Other.Module.Vault.Withdraw argument,");
        output.Should().NotContain("global::Other.Module.WithdrawArgs argument,");
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

        var output = EmitInterfaceExtensions(iface, "ITransferable", ForeignPackage("TransferRequest"));

        output.Should().Contain("public static class ITransferableExtensions");
        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryTransferAsync(");
        output.Should().Contain("global::Other.Module.TransferRequest argument,");
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

        var output = EmitInterfaceExtensions(iface, "IQuotable", ForeignPackage());

        output.Should().Contain("public static class IQuotableExtensions");
        output.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryQuoteAsync(");
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
