// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Testing.Roslyn;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NSubstitute;
using Xunit;
using DamlUnit = Daml.Runtime.Data.DamlUnit;
using Identifier = Daml.Runtime.Data.Identifier;
using Party = Daml.Runtime.Data.Party;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Round-trip test for a <c>Unit</c>-returning non-CID choice exerciser. Generates
/// the wrapper for a <c>DoNothing : ()</c> choice, compiles it through Roslyn into an
/// in-memory assembly, then invokes the emitted <c>TryDoNothingAsync</c> extension
/// end-to-end through an <see cref="ILedgerWriter"/> substitute and asserts the
/// projected <see cref="ExerciseOutcome{T}"/> over <see cref="DamlUnit"/>. Runs the
/// choice descriptor's <c>ResultDecoder</c> at runtime, which the emit-string specs in
/// <c>ChoiceEmitterUnitReturnExerciserTests</c> do not.
/// </summary>
public class NonContractChoiceUnitExerciserRoundTripTests
{
    private const string ModuleName = "Test.Sink";
    private const string EntityName = "Sink";
    private const string ChoiceName = "DoNothing";
    private const string PackageId = "test-package-id";
    private const string GeneratedNamespace = ModuleName;
    private const string NonContractExtensionsSuffix = "NonContractExtensions";

    private static readonly Identifier SinkTemplateId = new(PackageId, ModuleName, EntityName);

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
                            Name = ChoiceName,
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
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

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
        };
        var generator = new CSharpCodeGenerator(options);
        var files = generator.Generate(new DarModel { MainPackage = package, Dependencies = [] });

        return EmitAssembly(files);
    }

    private static Assembly EmitAssembly(IReadOnlyList<GeneratedFile> files)
    {
        var parseOptions = new CSharpParseOptions(documentationMode: DocumentationMode.Parse);
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.RelativePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "NonContractChoiceUnitExerciserRoundTripTests-emit",
            syntaxTrees: trees,
            references: ConsumerReferenceSet.Assemblies,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(
            "the generated Unit-returning wrapper must compile before it can be exercised, but got: {0}",
            string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)));

        stream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(stream.ToArray());
    }

    private static ExercisedEvent ExercisedDoNothing(string contractId) =>
        new(
            ContractId: contractId,
            TemplateId: SinkTemplateId,
            InterfaceId: null,
            ChoiceName: new Daml.Runtime.Commands.ChoiceName(ChoiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: false,
            ActingParties: [],
            WitnessParties: []);

    private static TransactionResult TransactionWith(params ExercisedEvent[] events) =>
        new(
            UpdateId: "update-1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: default)
        {
            ExercisedEvents = [.. events],
        };

    private static readonly Assembly WrapperAssembly = CompileWrapperAssembly();

    private static MethodInfo TryDoNothingAsyncExerciser()
    {
        var extensionsType = WrapperAssembly.GetType(
            $"{GeneratedNamespace}.{EntityName}{NonContractExtensionsSuffix}", throwOnError: true)!;
        return extensionsType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "TryDoNothingAsync"
                         && m.GetParameters().Any(p => p.ParameterType == typeof(SubmitterInfo)));
    }

    private static object TypedContractId(string contractId)
    {
        var sinkType = WrapperAssembly.GetType($"{GeneratedNamespace}.{EntityName}", throwOnError: true)!;
        return Activator.CreateInstance(typeof(ContractId<>).MakeGenericType(sinkType), contractId)!;
    }

    private static async Task<ExerciseOutcome<DamlUnit>> InvokeTryDoNothingAsync(
        ILedgerWriter client,
        string contractId,
        SubmitterInfo submitter)
    {
        var task = (Task<ExerciseOutcome<DamlUnit>>)TryDoNothingAsyncExerciser().Invoke(
            null,
            [TypedContractId(contractId), client, submitter, null, null, null, null, CancellationToken.None])!;
        return await task;
    }

    [Fact]
    public async Task TryDoNothingAsync_projects_a_unit_outcome_through_the_ledger_writer()
    {
        var client = Substitute.For<ILedgerWriter>();
        client.TrySubmitAndWaitForTransactionAsync(
                Arg.Any<CommandsSubmission>(), Arg.Any<SubmitterInfo>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.One(TransactionWith(ExercisedDoNothing("contract-1")))));

        SubmitterInfo singleParty = new Party("alice");

        var outcome = await InvokeTryDoNothingAsync(client, "contract-1", singleParty);

        var one = outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.One>().Subject;
        one.Result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public async Task TryDoNothingAsync_forwards_the_readAs_parties_of_an_explicit_submitter()
    {
        var client = Substitute.For<ILedgerWriter>();
        client.TrySubmitAndWaitForTransactionAsync(
                Arg.Any<CommandsSubmission>(), Arg.Any<SubmitterInfo>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.One(TransactionWith(ExercisedDoNothing("contract-1")))));

        var submitter = new SubmitterInfo(
            actAs: new HashSet<Party> { new("alice"), new("carol") },
            readAs: new HashSet<Party> { new("bob") });

        await InvokeTryDoNothingAsync(client, "contract-1", submitter);

        await client.Received(1).TrySubmitAndWaitForTransactionAsync(
            Arg.Any<CommandsSubmission>(),
            Arg.Is<SubmitterInfo>(s => s.ReadAs.Contains(new Party("bob")) && s.ActAs.Count == 2),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Regression: a Daml package declaring its own <c>enum Unit</c> (as
    /// <c>splice-wallet-payments</c> does) must still compile the wrapper of a
    /// <c>Unit</c>-returning choice, whose outcome is typed to <see cref="DamlUnit"/> and
    /// never to a bare <c>Unit</c> the flat generated namespace would bind to the
    /// package-local enum.
    /// </summary>
    [Fact]
    public void NonContractChoiceUnitExerciserRoundTrip_generated_wrapper_compiles_clean_when_the_package_declares_its_own_unit_enum()
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
                            Name = ChoiceName,
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Unit",
                    Definition = new DamlEnumDefinition(["USDUnit", "AmuletUnit", "ExtUnit"]),
                }
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

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
        };
        var generator = new CSharpCodeGenerator(options);
        var files = generator.Generate(new DarModel { MainPackage = package, Dependencies = [] });

        EmitAssembly(files);
    }
}
