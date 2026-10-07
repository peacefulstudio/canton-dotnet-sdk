// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterContractIdFilteringTests
{
    private const string LocalPackageId = "pkg-id";

    private static DamlPackage Package(DamlTemplate template) =>
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
                    Templates = [template],
                    DataTypes = [],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static DamlChoice Choice(string name, DamlType returnType) =>
        new()
        {
            Name = name,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Consuming = false,
        };

    private static DamlTemplate Template(params DamlChoice[] choices) =>
        new()
        {
            Name = "Factory",
            Choices = choices,
        };

    private static DamlTypeApp ContractIdOf(string templateName) =>
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef(LocalPackageId, "Main", templateName)]);

    private static (string NonContract, string Exercisers) Emit(DamlTemplate template)
    {
        var package = Package(template);
        var resolution = RealResolution.Of(package, new CodeGenOptions { NamespacePrefix = "Test.Package" });
        var context = resolution.Context;
        var resolver = resolution.Resolver;
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());

        var nonContractSb = new StringBuilder();
        var nonContractIndent = new IndentWriter(nonContractSb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(nonContractIndent, template);

        var exercisersSb = new StringBuilder();
        var exercisersIndent = new IndentWriter(exercisersSb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceAsyncExercisersClass(exercisersIndent, template, template.Name, []);

        return (nonContractSb.ToString(), exercisersSb.ToString());
    }

    [Fact]
    public void ChoiceEmitterContractIdFiltering_contract_id_return_routes_bare_contract_id_choice_to_the_exercisers_not_the_non_contract_class()
    {
        var (nonContract, exercisers) = Emit(Template(Choice("Mint", ContractIdOf("Coin"))));

        nonContract.Should().NotContain("FactoryNonContractExtensions");
        nonContract.Should().NotContain("ProjectMintResult");
        exercisers.Should().Contain("public static class FactoryExtensions");
        exercisers.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Factory.ChoiceMint, contractId);");
    }

    [Fact]
    public void ChoiceEmitterContractIdFiltering_contract_id_return_routes_optional_contract_id_choice_to_the_exercisers_not_the_non_contract_class()
    {
        var optionalCid = new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [ContractIdOf("Coin")]);

        var (nonContract, exercisers) = Emit(Template(Choice("MaybeMint", optionalCid)));

        nonContract.Should().NotContain("FactoryNonContractExtensions");
        nonContract.Should().NotContain("ProjectMaybeMintResult(");
        exercisers.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Factory.ChoiceMaybeMint, contractId);");
    }
}
