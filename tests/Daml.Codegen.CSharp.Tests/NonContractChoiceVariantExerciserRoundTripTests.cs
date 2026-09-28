// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Testing.Roslyn;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NSubstitute;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Round-trip test for a variant-returning non-CID choice exerciser. Generates the wrapper for
/// a <c>Resolve : Verdict</c> choice over <c>data Verdict = Win Int | Idle ()</c>, compiles it
/// through Roslyn, invokes the emitted <c>TryResolveAsync</c> through an
/// <see cref="ILedgerWriter"/> substitute, and writes the decoded result with
/// <see cref="JsonSerializer"/> on bare options. That pins both halves a variant result crosses:
/// the Try-prefixed exerciser's <c>ResultDecoder</c>, and the <c>"$case"</c> shape the generated
/// variant's <c>DamlVariantJsonConverterFactory</c> writes — including a constructor whose Daml
/// argument is <c>()</c>.
/// </summary>
public class NonContractChoiceVariantExerciserRoundTripTests
{
    private const string ModuleName = "Test.Court";
    private const string EntityName = "Court";
    private const string VariantName = "Verdict";
    private const string ResolveChoiceName = "Resolve";
    private const string PackageId = "test-package-id";

    private static readonly Identifier CourtTemplateId = new(PackageId, ModuleName, EntityName);

    private static readonly Assembly WrapperAssembly = CompileWrapperAssembly();

    private static Assembly CompileWrapperAssembly()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = EntityName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = ResolveChoiceName,
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeRef(PackageId, ModuleName, VariantName),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = EntityName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("judge", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = VariantName,
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Win", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlVariantConstructor("Idle", new DamlPrimitiveType(DamlPrimitive.Unit)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = PackageId,
            Name = "test-package",
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

        return EmitAssembly(generator.Generate(new DarModel { MainPackage = package, Dependencies = [] }));
    }

    private static Assembly EmitAssembly(IReadOnlyList<GeneratedFile> files)
    {
        var parseOptions = new CSharpParseOptions(documentationMode: DocumentationMode.Parse);
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.RelativePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "NonContractChoiceVariantExerciserRoundTripTests-emit",
            syntaxTrees: trees,
            references: ConsumerReferenceSet.Assemblies,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(
            "the generated variant-returning wrapper must compile before it can be exercised, but got: {0}",
            string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)));

        stream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(stream.ToArray());
    }

    private static ILedgerWriter LedgerReturning(DamlValue exerciseResult)
    {
        var exercised = new ExercisedEvent(
            ContractId: "contract-1",
            TemplateId: CourtTemplateId,
            InterfaceId: null,
            ChoiceName: new ChoiceName(ResolveChoiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: exerciseResult,
            Consuming: false,
            ActingParties: [],
            WitnessParties: []);
        var transaction = new TransactionResult(
            UpdateId: "update-1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: default)
        {
            ExercisedEvents = [exercised],
        };

        var client = Substitute.For<ILedgerWriter>();
        client.TrySubmitAndWaitForTransactionAsync(
                Arg.Any<CommandsSubmission>(), Arg.Any<SubmitterInfo>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.One(transaction)));
        return client;
    }

    private static async Task<object> ResolvedVerdict(DamlValue exerciseResult)
    {
        var courtType = WrapperAssembly.GetType($"{ModuleName}.{EntityName}", throwOnError: true)!;
        var verdictType = WrapperAssembly.GetType($"{ModuleName}.{VariantName}", throwOnError: true)!;
        var exerciser = WrapperAssembly
            .GetType($"{ModuleName}.{EntityName}NonContractExtensions", throwOnError: true)!
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "TryResolveAsync"
                         && m.GetParameters().Any(p => p.ParameterType == typeof(SubmitterInfo)));
        var contractId = Activator.CreateInstance(typeof(ContractId<>).MakeGenericType(courtType), "contract-1")!;
        SubmitterInfo submitter = new Party("alice");

        var task = (Task)exerciser.Invoke(
            null,
            [contractId, LedgerReturning(exerciseResult), submitter, null, null, null, CancellationToken.None])!;
        await task;

        var outcome = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var oneType = typeof(ExerciseOutcome<>.One).MakeGenericType(verdictType);
        outcome.Should().BeOfType(oneType);
        return oneType.GetProperty("Result")!.GetValue(outcome)!;
    }

    [Fact]
    public async Task TryResolveAsync_decodes_a_payload_arm_that_writes_its_case_through_System_Text_Json()
    {
        var verdict = await ResolvedVerdict(DamlVariant.Create("Win", new DamlInt64(5)));

        JsonSerializer.Serialize(verdict, verdict.GetType().BaseType!)
            .Should().Be("""{"$case":"Win","Value":5,"Tag":"Win"}""");
    }

    [Fact]
    public async Task TryResolveAsync_decodes_a_unit_argument_arm_that_writes_its_case_through_System_Text_Json()
    {
        var verdict = await ResolvedVerdict(DamlVariant.Create("Idle", DamlUnit.Instance));

        JsonSerializer.Serialize(verdict, verdict.GetType().BaseType!)
            .Should().Be("""{"$case":"Idle","Tag":"Idle"}""");
    }

    [Fact]
    public async Task TryResolveAsync_result_reads_back_from_its_case_json_as_an_equal_arm()
    {
        var verdict = await ResolvedVerdict(DamlVariant.Create("Win", new DamlInt64(5)));
        var verdictType = verdict.GetType().BaseType!;

        var restored = JsonSerializer.Deserialize("""{"$case":"Win","Value":5,"Tag":"Win"}""", verdictType);

        restored.Should().Be(verdict);
    }
}
