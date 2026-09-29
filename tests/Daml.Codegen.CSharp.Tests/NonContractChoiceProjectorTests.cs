// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Testing.Roslyn;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using DamlInt64 = Daml.Runtime.Data.DamlInt64;
using DamlUnit = Daml.Runtime.Data.DamlUnit;
using Identifier = Daml.Runtime.Data.Identifier;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Behavior tests for the non-CID choice projector. Generates
/// the wrapper for a small Int-returning choice, compiles it through Roslyn into
/// an in-memory assembly, then reflectively invokes the emitted
/// <c>Project&lt;Choice&gt;Result</c> helper against hand-built
/// <see cref="TransactionResult"/> fixtures. Complements the shape-level string
/// assertions in <see cref="NonContractChoiceWrapperTests"/> and the compile
/// smoke test in <see cref="EmittedCodeCompilesTests"/> by executing the
/// projector end-to-end.
/// </summary>
public class NonContractChoiceProjectorTests
{
    private const string ModuleName = "Test.Oracle";
    private const string EntityName = "Oracle";
    private const string ChoiceName = "GetCount";
    private const string PackageId = "test-package-id";

    private const string GeneratedNamespace = ModuleName;
    private const string NonContractExtensionsSuffix = "NonContractExtensions";
    private const string ProjectorMethodPrefix = "Project";
    private const string ProjectorMethodSuffix = "Result";

    private static readonly Identifier OracleTemplateId = new(PackageId, ModuleName, EntityName);

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
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64),
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
                        [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
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
            assemblyName: "NonContractChoiceProjectorTests-emit",
            syntaxTrees: trees,
            references: ConsumerReferenceSet.Assemblies,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(
            "the generated wrapper must compile before the projector can be exercised, but got: {0}",
            string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)));

        stream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(stream.ToArray());
    }

    private static ExerciseOutcome<long> InvokeProjector(Assembly assembly, TransactionResult tx, string contractId)
    {
        var extensionsTypeName = $"{GeneratedNamespace}.{EntityName}{NonContractExtensionsSuffix}";
        var projectorMethodName = $"{ProjectorMethodPrefix}{ChoiceName}{ProjectorMethodSuffix}";

        var extensionsType = assembly.GetType(extensionsTypeName, throwOnError: true)!;
        var projector = extensionsType.GetMethod(
            projectorMethodName,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{projectorMethodName} not found on emitted extensions class");

        try
        {
            return (ExerciseOutcome<long>)projector.Invoke(null, [tx, contractId])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static ExercisedEvent ExercisedGetCount(string contractId, long result, Identifier? templateId = null) =>
        new(
            ContractId: contractId,
            TemplateId: templateId ?? OracleTemplateId,
            InterfaceId: null,
            ChoiceName: new Daml.Runtime.Commands.ChoiceName(ChoiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: new DamlInt64(result),
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

    [Fact]
    public void NonContractChoiceProjector_projector_returns_One_when_matching_exercise_event_present()
    {
        var tx = TransactionWith(ExercisedGetCount("contract-1", 42));

        var outcome = InvokeProjector(WrapperAssembly, tx, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Subject;
        one.Result.Should().Be(42L);
    }

    [Fact]
    public void NonContractChoiceProjector_projector_ignores_same_choice_exercised_on_a_different_contract()
    {
        var tx = TransactionWith(
            ExercisedGetCount("other-contract", 99),
            ExercisedGetCount("contract-1", 42));

        var outcome = InvokeProjector(WrapperAssembly, tx, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Subject;
        one.Result.Should().Be(42L);
    }

    [Fact]
    public void NonContractChoiceProjector_projector_matches_through_template_package_id_drift()
    {
        var driftedTemplateId = new Identifier("upgraded-package-id", ModuleName, EntityName);
        var tx = TransactionWith(ExercisedGetCount("contract-1", 42, driftedTemplateId));

        var outcome = InvokeProjector(WrapperAssembly, tx, "contract-1");

        var one = outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Subject;
        one.Result.Should().Be(42L);
    }

    [Fact]
    public void NonContractChoiceProjector_projector_throws_self_contained_ExercisedEvents_diagnostic_when_no_exercise_event_present()
    {
        var tx = TransactionWith();

        var act = () => InvokeProjector(WrapperAssembly, tx, "contract-1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no 'GetCount' exercise on contract 'contract-1'*")
            .WithMessage("*TransactionResult.ExercisedEvents*")
            .WithMessage("*a custom ILedgerWriter did not project the transaction's exercised events*");
    }

    [Fact]
    public void NonContractChoiceProjector_projector_throws_when_no_matching_contract_in_non_empty_events()
    {
        var tx = TransactionWith(ExercisedGetCount("other-contract", 99));

        var act = () => InvokeProjector(WrapperAssembly, tx, "contract-1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no 'GetCount' exercise on contract 'contract-1'*")
            .WithMessage("*TransactionResult.ExercisedEvents*")
            .WithMessage("*a custom ILedgerWriter did not project the transaction's exercised events*");
    }
}
