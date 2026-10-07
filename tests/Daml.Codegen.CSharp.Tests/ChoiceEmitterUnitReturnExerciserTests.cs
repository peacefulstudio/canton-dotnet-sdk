// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterUnitReturnExerciserTests
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
            Name = "Sink",
            Choices = choices,
        };

    private static (string Code, IReadOnlyCollection<string> Usings) EmitNonContract(DamlTemplate template)
    {
        var package = Package(template);
        var resolution = RealResolution.Of(package, new CodeGenOptions { NamespacePrefix = "Test.Package" });
        var context = resolution.Context;
        var resolver = resolution.Resolver;
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(indent, template);
        return (sb.ToString(), indent.RequiredUsings);
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_a_unit_returning_choice()
    {
        var (code, _) = EmitNonContract(Template(Choice("DoNothing", new DamlPrimitiveType(DamlPrimitive.Unit))));

        code.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit>> TryDoNothingAsync(");
        code.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Sink.ChoiceDoNothing, contractId);");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_optional_unit()
    {
        var optionalUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.Optional),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, _) = EmitNonContract(Template(Choice("MaybeNothing", optionalUnit)));

        code.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::Daml.Runtime.Data.DamlUnit?>> TryMaybeNothingAsync(");
        code.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Sink.ChoiceMaybeNothing, contractId);");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_list_of_unit()
    {
        var listOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.List),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, _) = EmitNonContract(Template(Choice("ListOfUnits", listOfUnit)));

        code.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Data.DamlUnit>>> TryListOfUnitsAsync(");
        code.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Sink.ChoiceListOfUnits, contractId);");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_textmap_of_unit()
    {
        var mapOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.TextMap),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, _) = EmitNonContract(Template(Choice("MapOfUnits", mapOfUnit)));

        code.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::System.Collections.Generic.IReadOnlyDictionary<string, global::Daml.Runtime.Data.DamlUnit>>> TryMapOfUnitsAsync(");
        code.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Sink.ChoiceMapOfUnits, contractId);");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_genmap_of_unit()
    {
        var genMapOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.GenMap),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, _) = EmitNonContract(Template(Choice("UnitsByText", genMapOfUnit)));

        code.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::System.Collections.Generic.IReadOnlyDictionary<string, global::Daml.Runtime.Data.DamlUnit>>> TryUnitsByTextAsync(");
        code.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Sink.ChoiceUnitsByText, contractId);");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_nested_in_a_genmap_value()
    {
        var genMapOfListOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.GenMap),
            [
                new DamlPrimitiveType(DamlPrimitive.Party),
                new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.List), [new DamlPrimitiveType(DamlPrimitive.Unit)]),
            ]);

        var (code, _) = EmitNonContract(Template(Choice("UnitListsByParty", genMapOfListOfUnit)));

        code.Should().Contain("public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<global::System.Collections.Generic.IReadOnlyDictionary<global::Daml.Runtime.Data.Party, global::System.Collections.Generic.IReadOnlyList<global::Daml.Runtime.Data.DamlUnit>>>> TryUnitListsByPartyAsync(");
        code.Should().Contain("tx.ProjectChoiceResult(global::Test.Package.Main.Sink.ChoiceUnitListsByParty, contractId);");
        code.Should().NotContain("Unit.Value");
    }
}
