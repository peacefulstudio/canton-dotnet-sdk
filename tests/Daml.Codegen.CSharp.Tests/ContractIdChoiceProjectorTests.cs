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

    private static DamlType OptionalOf(DamlType inner) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [inner]);

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
                        ChoiceReturning("Offer", Tuple2(ContractIdOf("Buyer"), OptionalOf(ContractIdOf("Seller")))),
                        ChoiceReturning("Nested", Tuple2(ContractIdOf("Buyer"), Tuple2(ContractIdOf("Half"), OptionalOf(ContractIdOf("Seller"))))),
                        ChoiceReturning("Roster", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [Tuple2(ContractIdOf("Buyer"), OptionalOf(ContractIdOf("Seller")))])),
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

    private static object? NullableResultOf(object outcome) =>
        outcome.GetType().GetProperty("Result")!.GetValue(outcome);

    private static object ResultOf(object outcome) => NullableResultOf(outcome)!;

    private static object Component(object tuple, string name) =>
        tuple.GetType().GetProperty(name)!.GetValue(tuple)!;

    private static string CidOf(object contractId) =>
        (string)contractId.GetType().GetProperty("Value")!.GetValue(contractId)!;

    private static IEnumerable<string> CidsOf(object contractIds) =>
        ((IEnumerable)contractIds).Cast<object>().Select(CidOf);

    private static bool HasValue(object optional) =>
        (bool)optional.GetType().GetProperty("HasValue")!.GetValue(optional)!;

    private static object ValueOf(object optional) =>
        optional.GetType().GetMethod("GetValueOrThrow")!.Invoke(optional, null)!;

    private static DamlRecord Pair(DamlValue first, DamlValue second) =>
        DamlRecord.Create(new DamlField("_1", first), new DamlField("_2", second));

    private static ExercisedEvent DeskExercised(string choiceName, DamlValue result, string contractId = DeskCid) =>
        new(
            ContractId: contractId,
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
    public void Trade_returns_each_tuple_component_as_the_contract_id_the_exercise_result_names()
    {
        var tx = Transaction([], DeskExercised("Trade", Pair(new DamlContractId("buyer-1"), new DamlContractId("seller-1"))));

        var outcome = Project("Trade", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(Component(ResultOf(outcome), "_1")).Should().Be("buyer-1");
        CidOf(Component(ResultOf(outcome), "_2")).Should().Be("seller-1");
    }

    [Fact]
    public void Split_returns_two_same_template_components_in_tuple_order_over_created_order()
    {
        var tx = Transaction(
            [Created("half-b", "Half"), Created("half-a", "Half")],
            DeskExercised("Split", Pair(new DamlContractId("half-a"), new DamlContractId("half-b"))));

        var outcome = Project("Split", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(Component(ResultOf(outcome), "_1")).Should().Be("half-a");
        CidOf(Component(ResultOf(outcome), "_2")).Should().Be("half-b");
    }

    [Fact]
    public void Split_reports_One_of_the_returned_halves_when_more_halves_are_visible_than_the_tuple_names()
    {
        var tx = Transaction(
            [Created("half-a", "Half"), Created("half-b", "Half"), Created("half-c", "Half")],
            DeskExercised("Split", Pair(new DamlContractId("half-a"), new DamlContractId("half-b"))));

        var outcome = Project("Split", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(Component(ResultOf(outcome), "_1")).Should().Be("half-a");
        CidOf(Component(ResultOf(outcome), "_2")).Should().Be("half-b");
    }

    [Fact]
    public void Split_throws_when_the_transaction_carries_no_Split_exercise_even_though_halves_were_created()
    {
        var tx = Transaction([Created("half-a", "Half"), Created("half-b", "Half")]);

        var act = () => Project("Split", tx);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Submission succeeded but no 'Split' exercise on contract 'desk-cid' was recorded on transaction update-desk. *");
    }

    [Fact]
    public void Split_ignores_the_same_choice_exercised_on_another_desk_in_the_transaction()
    {
        var tx = Transaction(
            [],
            DeskExercised("Split", Pair(new DamlContractId("nested-a"), new DamlContractId("nested-b")), contractId: "nested-desk-cid"),
            DeskExercised("Split", Pair(new DamlContractId("half-a"), new DamlContractId("half-b"))));

        var outcome = Project("Split", tx);

        CidOf(Component(ResultOf(outcome), "_1")).Should().Be("half-a");
        CidOf(Component(ResultOf(outcome), "_2")).Should().Be("half-b");
    }

    [Fact]
    public void Maybe_returns_the_contract_id_a_present_optional_carries()
    {
        var tx = Transaction([], DeskExercised("Maybe", DamlOptional.Some(new DamlContractId("buyer-1"))));

        var outcome = Project("Maybe", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(ResultOf(outcome)).Should().Be("buyer-1");
    }

    [Fact]
    public void Maybe_returns_no_contract_id_for_an_absent_optional_even_when_a_buyer_is_visible()
    {
        var tx = Transaction([Created("buyer-unrelated", "Buyer")], DeskExercised("Maybe", DamlOptional.None));

        var outcome = Project("Maybe", tx);

        CaseOf(outcome).Should().Be("One");
        NullableResultOf(outcome).Should().BeNull();
    }

    [Fact]
    public void Batch_returns_the_tuples_in_the_order_the_exercise_result_lists_them()
    {
        var tx = Transaction([], DeskExercised("Batch", new DamlList(
        [
            Pair(new DamlContractId("buyer-2"), new DamlInt64(2)),
            Pair(new DamlContractId("buyer-1"), new DamlInt64(1)),
        ])));

        var outcome = Project("Batch", tx);

        CaseOf(outcome).Should().Be("One");
        var batch = ((IEnumerable)ResultOf(outcome)).Cast<object>().ToList();
        batch.Select(entry => CidOf(Component(entry, "_1"))).Should().Equal("buyer-2", "buyer-1");
        batch.Select(entry => Component(entry, "_2")).Should().Equal(2L, 1L);
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

    [Fact]
    public void Offer_returns_an_empty_optional_for_an_omitted_trailing_optional_component()
    {
        var tx = Transaction([], DeskExercised("Offer", DamlRecord.Create(new DamlField("_1", new DamlContractId("buyer-1")))));

        var outcome = Project("Offer", tx);

        CaseOf(outcome).Should().Be("One");
        CidOf(Component(ResultOf(outcome), "_1")).Should().Be("buyer-1");
        HasValue(Component(ResultOf(outcome), "_2")).Should().BeFalse();
    }

    [Fact]
    public void Offer_returns_the_contract_id_a_present_trailing_optional_component_carries()
    {
        var tx = Transaction([], DeskExercised("Offer", Pair(new DamlContractId("buyer-1"), DamlOptional.Some(new DamlContractId("seller-1")))));

        var outcome = Project("Offer", tx);

        CidOf(Component(ResultOf(outcome), "_1")).Should().Be("buyer-1");
        CidOf(ValueOf(Component(ResultOf(outcome), "_2"))).Should().Be("seller-1");
    }

    [Fact]
    public void Offer_returns_CommittedUndecodable_when_the_non_optional_first_component_is_omitted()
    {
        var tx = Transaction([], DeskExercised("Offer", DamlRecord.Create()));

        var outcome = Project("Offer", tx);

        CaseOf(outcome).Should().Be("CommittedUndecodable");
    }

    [Fact]
    public void Trade_returns_CommittedUndecodable_when_the_non_optional_second_component_is_omitted()
    {
        var tx = Transaction([], DeskExercised("Trade", DamlRecord.Create(new DamlField("_1", new DamlContractId("buyer-1")))));

        var outcome = Project("Trade", tx);

        CaseOf(outcome).Should().Be("CommittedUndecodable");
    }

    [Fact]
    public void Nested_returns_an_empty_optional_for_an_omitted_trailing_optional_of_an_inner_tuple()
    {
        var inner = DamlRecord.Create(new DamlField("_1", new DamlContractId("half-1")));
        var tx = Transaction([], DeskExercised("Nested", Pair(new DamlContractId("buyer-1"), inner)));

        var outcome = Project("Nested", tx);

        var result = ResultOf(outcome);
        CidOf(Component(result, "_1")).Should().Be("buyer-1");
        var innerTuple = Component(result, "_2");
        CidOf(Component(innerTuple, "_1")).Should().Be("half-1");
        HasValue(Component(innerTuple, "_2")).Should().BeFalse();
    }

    [Fact]
    public void Nested_returns_CommittedUndecodable_when_the_inner_tuple_omits_a_non_optional_component()
    {
        var tx = Transaction([], DeskExercised("Nested", Pair(new DamlContractId("buyer-1"), DamlRecord.Create())));

        var outcome = Project("Nested", tx);

        CaseOf(outcome).Should().Be("CommittedUndecodable");
    }

    [Fact]
    public void Roster_returns_each_listed_tuple_with_an_omitted_trailing_optional_component_empty()
    {
        var tx = Transaction([], DeskExercised("Roster", new DamlList(
        [
            DamlRecord.Create(new DamlField("_1", new DamlContractId("buyer-1"))),
            Pair(new DamlContractId("buyer-2"), DamlOptional.Some(new DamlContractId("seller-2"))),
        ])));

        var outcome = Project("Roster", tx);

        var roster = ((IEnumerable)ResultOf(outcome)).Cast<object>().ToList();
        roster.Select(entry => CidOf(Component(entry, "_1"))).Should().Equal("buyer-1", "buyer-2");
        HasValue(Component(roster[0], "_2")).Should().BeFalse();
        CidOf(ValueOf(Component(roster[1], "_2"))).Should().Be("seller-2");
    }
}
