// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using NSubstitute;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;
using DamlUnit = Daml.Runtime.Data.DamlUnit;
using Party = Daml.Runtime.Data.Party;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Invokes the emitted interface-choice exercisers for real, through reflection over a
/// compiled assembly, to pin where their argument-validation failures surface and what
/// they hand back for a valid call. The exerciser is an <c>async</c> method, so an
/// exception thrown before its first <c>await</c> — like the null-<c>client</c> guard —
/// is captured into the returned (faulted) <see cref="Task"/> rather than thrown
/// synchronously at the <c>MethodInfo.Invoke</c> call site; <see cref="Record.ExceptionAsync"/>
/// unwraps that without an extra <see cref="TargetInvocationException"/> layer.
/// </summary>
public class EmittedInterfaceChoiceExerciserInvocationTests
{
    private const string ChoiceMethodName = "TryTransferAsync";

    private static readonly Assembly Emitted = EmitToAssembly(GenerateCustodyInterfacePackage());

    [Fact]
    public async Task EmittedInterfaceChoiceExerciser_rejects_a_null_client_via_the_returned_faulted_task()
    {
        var exerciser = ChoiceExerciser();

        var task = (Task)exerciser.Invoke(null, Arguments(null))!;
        var thrown = await Record.ExceptionAsync(() => task);

        thrown.Should().BeOfType<ArgumentNullException>(
            "the null-client guard runs before the first await in an async method, so it faults the returned task instead of throwing at the call site");
    }

    [Fact]
    public async Task EmittedInterfaceChoiceExerciser_projects_the_committed_result_for_valid_arguments()
    {
        var exerciser = ChoiceExerciser();
        var client = Substitute.For<ILedgerWriter>();
        client.TrySubmitAndWaitForTransactionAsync(
                Arg.Any<CommandsSubmission>(),
                Arg.Any<SubmitterInfo>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.None()));

        var returned = exerciser.Invoke(null, Arguments(client));

        returned.Should().BeAssignableTo<Task<ExerciseOutcome<DamlUnit>>>(
            "the Transfer choice returns Unit, so the projected outcome is typed to DamlUnit rather than the untyped TransactionResult");
        var outcome = await (Task<ExerciseOutcome<DamlUnit>>)returned!;
        outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.None>(
            "ProjectCommitted re-wraps a non-committing outcome like None faithfully, without invoking the per-choice decode projector");
    }

    private static MethodInfo ChoiceExerciser() =>
        EmittedType("ICustodyExtensions")
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == ChoiceMethodName
                         && m.GetParameters().Any(p => p.ParameterType == typeof(SubmitterInfo)));

    private static object?[] Arguments(ILedgerWriter? client) =>
    [
        TypedContractId(),
        client,
        Submitter(),
        null,
        null,
        null,
        CancellationToken.None,
    ];

    private static SubmitterInfo Submitter() => new Party("alice");

    private static object TypedContractId() =>
        Activator.CreateInstance(
            typeof(ContractId<>).MakeGenericType(EmittedType("ICustody")),
            "cid-1")!;

    private static Type EmittedType(string name) => Emitted.GetTypes().Single(t => t.Name == name);

    private static IReadOnlyList<GeneratedFile> GenerateCustodyInterfacePackage()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Acme.Offers",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Offer",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Accept",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Acme.Offers", "Offer")]),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                    Observers = DamlPartyAnalysis.Static([]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Offer",
                    Definition = new DamlRecordDefinition(fields),
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Custody",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Dynamic,
                            Observers = DamlPartyAnalysis.Dynamic,
                        },
                    ],
                }
            ],
        };

        var dar = new DarModel
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
            Dependencies = [],
        };

        return CreateGenerator().Generate(dar);
    }
}
