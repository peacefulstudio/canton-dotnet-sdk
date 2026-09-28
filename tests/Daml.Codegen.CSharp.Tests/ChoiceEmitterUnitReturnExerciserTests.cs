// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterUnitReturnExerciserTests
{
    private const string LocalPackageId = "pkg-id";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

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
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var resolver = new StubResolver();
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(indent, template, context.DataTypes);
        return (sb.ToString(), indent.RequiredUsings);
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_a_unit_returning_choice()
    {
        var (code, _) = EmitNonContract(Template(Choice("DoNothing", new DamlPrimitiveType(DamlPrimitive.Unit))));

        code.Should().Contain("public static async Task<ExerciseOutcome<DamlUnit>> TryDoNothingAsync(");
        code.Should().Contain("var decoded = Sink.ChoiceDoNothing.ResultDecoder!(exercised.ExerciseResult);");
        code.Should().Contain("new ExerciseOutcome<DamlUnit>.One(decoded)");
        code.Should().Contain("CommittedUndecodable");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_optional_unit()
    {
        var optionalUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.Optional),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, _) = EmitNonContract(Template(Choice("MaybeNothing", optionalUnit)));

        code.Should().Contain("public static async Task<ExerciseOutcome<DamlUnit?>> TryMaybeNothingAsync(");
        code.Should().Contain("var decoded = Sink.ChoiceMaybeNothing.ResultDecoder!(exercised.ExerciseResult);");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_list_of_unit()
    {
        var listOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.List),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, _) = EmitNonContract(Template(Choice("ListOfUnits", listOfUnit)));

        code.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyList<DamlUnit>>> TryListOfUnitsAsync(");
        code.Should().Contain("var decoded = Sink.ChoiceListOfUnits.ResultDecoder!(exercised.ExerciseResult);");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_textmap_of_unit()
    {
        var mapOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.TextMap),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, _) = EmitNonContract(Template(Choice("MapOfUnits", mapOfUnit)));

        code.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyDictionary<string, DamlUnit>>> TryMapOfUnitsAsync(");
        code.Should().Contain("var decoded = Sink.ChoiceMapOfUnits.ResultDecoder!(exercised.ExerciseResult);");
        code.Should().NotContain("Unit.Value");
    }

    [Fact]
    public void ChoiceEmitterUnitReturnExerciser_non_contract_exerciser_returns_DamlUnit_for_genmap_of_unit()
    {
        var genMapOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.GenMap),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, _) = EmitNonContract(Template(Choice("UnitsByText", genMapOfUnit)));

        code.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyDictionary<string, DamlUnit>>> TryUnitsByTextAsync(");
        code.Should().Contain("var decoded = Sink.ChoiceUnitsByText.ResultDecoder!(exercised.ExerciseResult);");
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

        code.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyDictionary<Party, IReadOnlyList<DamlUnit>>>> TryUnitListsByPartyAsync(");
        code.Should().Contain("var decoded = Sink.ChoiceUnitListsByParty.ResultDecoder!(exercised.ExerciseResult);");
        code.Should().NotContain("Unit.Value");
    }
}
