// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Globalization;
using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Testing.Roslyn;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Identifier = Daml.Runtime.Data.Identifier;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Behavior tests for the contract-id choice projector. Generates a template whose choices
/// return tuple, optional and list-of-tuple <c>ContractId</c> shapes, compiles it through Roslyn
/// into an in-memory assembly, then reflectively invokes each emitted
/// <c>Project&lt;Choice&gt;Result</c> helper against hand-built <see cref="TransactionResult"/>
/// fixtures, so the exercise-result walk each shape emits runs end to end.
/// </summary>
public class ContractIdChoiceProjectorTests
{
    private const string PackageId = "desk-package-id";
    private const string StdlibPackageId = "daml-prim-id";
    private const string ModuleName = "Test.Desk";
    private const string DeskCid = "desk-cid";

    private static readonly Assembly DeskAssembly = CompileDeskAssembly();

    private static DamlType ContractIdOf(string template) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef(PackageId, ModuleName, template)]);

    private static DamlType Tuple2(DamlType first, DamlType second) =>
        new DamlTypeApp(new DamlTypeRef(StdlibPackageId, "DA.Types", "Tuple2"), [first, second]);

    private static DamlChoice ChoiceReturning(string name, DamlType returnType) =>
        new()
        {
            Name = name,
            Consuming = false,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
        };

    private static DamlDataType OwnedRecord(string name) =>
        new()
        {
            Name = name,
            Definition = new DamlRecordDefinition(
                [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
        };

    private static Assembly CompileDeskAssembly()
    {
        var stdlibPackage = new DamlPackage
        {
            PackageId = StdlibPackageId,
            Name = "daml-prim",
            Version = new Version(0, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Tuple2",
                            TypeParams = ["a", "b"],
                            Definition = new DamlRecordDefinition(
                            [
                                new DamlFieldDefinition("_1", new DamlTypeVar("a")),
                                new DamlFieldDefinition("_2", new DamlTypeVar("b")),
                            ]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var module = new DamlModule
        {
            Name = ModuleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Desk",
                    Choices =
                    [
                        ChoiceReturning("Trade", Tuple2(ContractIdOf("Buyer"), ContractIdOf("Seller"))),
                        ChoiceReturning("Split", Tuple2(ContractIdOf("Half"), ContractIdOf("Half"))),
                        ChoiceReturning("Maybe", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [ContractIdOf("Buyer")])),
                        ChoiceReturning("Batch", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [Tuple2(ContractIdOf("Buyer"), new DamlPrimitiveType(DamlPrimitive.Int64))])),
                    ],
                },
                new DamlTemplate { Name = "Buyer", Choices = [] },
                new DamlTemplate { Name = "Seller", Choices = [] },
                new DamlTemplate { Name = "Half", Choices = [] },
            ],
            DataTypes = [OwnedRecord("Desk"), OwnedRecord("Buyer"), OwnedRecord("Seller"), OwnedRecord("Half")],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = PackageId,
            Name = "desk-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var generator = new CSharpCodeGenerator(new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
        });
        return EmitAssembly(generator.Generate(new DarModel { MainPackage = package, Dependencies = [stdlibPackage] }));
    }

    private static Assembly EmitAssembly(IReadOnlyList<GeneratedFile> files)
    {
        var parseOptions = new CSharpParseOptions(documentationMode: DocumentationMode.Parse);
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.RelativePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "ContractIdChoiceProjectorTests-emit",
            syntaxTrees: trees,
            references: ConsumerReferenceSet.Assemblies,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(
            "the generated desk must compile before its projectors can be exercised, but got: {0}",
            string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)));

        stream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(stream.ToArray());
    }

    private static object Project(string choiceName, TransactionResult tx)
    {
        var extensionsType = DeskAssembly.GetType($"{ModuleName}.DeskExtensions", throwOnError: true)!;
        var projector = extensionsType.GetMethod($"Project{choiceName}Result", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Project{choiceName}Result not found on the emitted DeskExtensions");

        try
        {
            return projector.Invoke(null, [tx, DeskCid])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static string CaseOf(object outcome) => outcome.GetType().Name;

    private static object? SlotOf(object outcome, string field)
    {
        var result = outcome.GetType().GetProperty("Result")!.GetValue(outcome)!;
        return result.GetType().GetProperty(field)!.GetValue(result);
    }

    private static string? CidOf(object? contractId) =>
        (string?)contractId?.GetType().GetProperty("Value")!.GetValue(contractId);

    private static IEnumerable<string?> CidsOf(object? contractIds) =>
        ((IEnumerable)contractIds!).Cast<object>().Select(CidOf);

    private static IEnumerable<string> ManyIdsOf(object outcome) =>
        ((IEnumerable)outcome.GetType().GetProperty("ContractIds")!.GetValue(outcome)!).Cast<string>();

    private static DamlRecord Pair(DamlValue first, DamlValue second) =>
        DamlRecord.Create(new DamlField("_1", first), new DamlField("_2", second));

    private static ExercisedEvent DeskExercised(string choiceName, DamlValue result) =>
        new(
            ContractId: DeskCid,
            TemplateId: new Identifier(PackageId, ModuleName, "Desk"),
            InterfaceId: null,
            ChoiceName: new Daml.Runtime.Commands.ChoiceName(choiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: result,
            Consuming: false,
            ActingParties: [],
            WitnessParties: []);

    private static CreatedContract Created(string contractId, string template) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: new Identifier(PackageId, ModuleName, template),
            Payload: DamlRecord.Create(),
            WitnessParties: [],
            Signatories: [],
            Observers: []);

    private static TransactionResult Transaction(IReadOnlyList<CreatedContract> created, params ExercisedEvent[] exercised) =>
        new(
            UpdateId: "update-desk",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [.. created],
            ArchivedContractIds: [],
            CommandId: default)
        {
            ExercisedEvents = [.. exercised],
        };

    [Fact]
    public void Trade_projects_each_tuple_component_to_its_slot_when_no_created_contract_is_visible()
    {
        var tx = Transaction([], DeskExercised("Trade", Pair(new DamlContractId("buyer-1"), new DamlContractId("seller-1"))));

        var outcome = Project("Trade", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(SlotOf(outcome, "Buyer")).Should().Be("buyer-1");
        CidOf(SlotOf(outcome, "Seller")).Should().Be("seller-1");
    }

    [Fact]
    public void Split_projects_two_same_template_components_in_tuple_order_over_created_order()
    {
        var tx = Transaction(
            [Created("half-b", "Half"), Created("half-a", "Half")],
            DeskExercised("Split", Pair(new DamlContractId("half-a"), new DamlContractId("half-b"))));

        var outcome = Project("Split", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(SlotOf(outcome, "Half")).Should().Be("half-a");
        CidOf(SlotOf(outcome, "Half2")).Should().Be("half-b");
    }

    [Fact]
    public void Split_keeps_reporting_Many_when_more_halves_are_visible_than_the_tuple_names()
    {
        var tx = Transaction(
            [Created("half-a", "Half"), Created("half-b", "Half"), Created("half-c", "Half")],
            DeskExercised("Split", Pair(new DamlContractId("half-a"), new DamlContractId("half-b"))));

        var outcome = Project("Split", tx);

        CaseOf(outcome).Should().Be("Many");
        ManyIdsOf(outcome).Should().Equal("half-b", "half-c");
    }

    [Fact]
    public void Split_falls_back_to_the_visible_halves_when_no_Split_exercise_is_present()
    {
        var tx = Transaction([Created("half-a", "Half"), Created("half-b", "Half")]);

        var outcome = Project("Split", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(SlotOf(outcome, "Half")).Should().Be("half-a");
        CidOf(SlotOf(outcome, "Half2")).Should().Be("half-b");
    }

    [Fact]
    public void Maybe_projects_the_contract_id_a_present_optional_carries()
    {
        var tx = Transaction([], DeskExercised("Maybe", DamlOptional.Some(new DamlContractId("buyer-1"))));

        var outcome = Project("Maybe", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(SlotOf(outcome, "Buyer")).Should().Be("buyer-1");
    }

    [Fact]
    public void Maybe_projects_the_contract_id_a_present_optional_chain_level_carries()
    {
        var tx = Transaction([], DeskExercised("Maybe", DamlOptionalChain.Some(new DamlContractId("buyer-1"))));

        var outcome = Project("Maybe", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(SlotOf(outcome, "Buyer")).Should().Be("buyer-1");
    }

    [Fact]
    public void Maybe_projects_a_bare_contract_id_as_present()
    {
        var tx = Transaction([], DeskExercised("Maybe", new DamlContractId("buyer-1")));

        var outcome = Project("Maybe", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(SlotOf(outcome, "Buyer")).Should().Be("buyer-1");
    }

    [Fact]
    public void Maybe_projects_an_absent_optional_to_no_contract_id_even_when_a_buyer_is_visible()
    {
        var tx = Transaction([Created("buyer-unrelated", "Buyer")], DeskExercised("Maybe", DamlOptional.None));

        var outcome = Project("Maybe", tx);

        CaseOf(outcome).Should().Be("One");
        SlotOf(outcome, "Buyer").Should().BeNull();
    }

    [Fact]
    public void Batch_projects_the_contract_id_component_of_every_listed_tuple_in_order()
    {
        var tx = Transaction([], DeskExercised("Batch", new DamlList(
        [
            Pair(new DamlContractId("buyer-2"), new DamlInt64(2)),
            Pair(new DamlContractId("buyer-1"), new DamlInt64(1)),
        ])));

        var outcome = Project("Batch", tx);

        CaseOf(outcome).Should().Be("One");
        CidsOf(SlotOf(outcome, "Buyer")).Should().Equal("buyer-2", "buyer-1");
    }

    [Fact]
    public void Trade_returns_CommittedUndecodable_when_the_exercise_result_is_no_tuple()
    {
        var tx = Transaction([], DeskExercised("Trade", new DamlContractId("buyer-1")));

        var outcome = Project("Trade", tx);

        CaseOf(outcome).Should().Be("CommittedUndecodable");
        outcome.GetType().GetProperty("UpdateId")!.GetValue(outcome).Should().Be("update-desk");
        outcome.GetType().GetProperty("Message")!.GetValue(outcome).Should().Be("Cannot cast DamlContractId to DamlRecord");
    }
}
