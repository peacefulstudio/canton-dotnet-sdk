// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Grpc.Core;
using Grpc.Net.Client;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RuntimeExerciseCommand = Daml.Runtime.Commands.ExerciseCommand;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

/// <summary>
/// Scenarios that run inside an isolated load context, where the generated conformance assembly is
/// referenced by the dependency manifest but no code has touched it. A scenario that must find the binding
/// through the registry names no conformance type.
/// </summary>
internal static class GrpcUntouchedBindingScenarios
{
    private const string RichTypesPackageId = "e72ec259be271bedbcb0d33f19a47008de832963a569e5a04f1ed7b8efb434e9";

    private sealed record GenericResultsMarker : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier(RichTypesPackageId, "RichTypes", "GenericResults"), DamlTypeKind.Template, "rich-types");
    }

    private sealed class AnsweringCommandService(SubmitAndWaitForTransactionResponse response)
        : CommandService.CommandServiceClient
    {
        public override AsyncUnaryCall<SubmitAndWaitForTransactionResponse> SubmitAndWaitForTransactionAsync(
            SubmitAndWaitForTransactionRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default) =>
            new(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
    }

    internal static async Task<string> ReturnOutcomeNamingTheBindingType()
    {
        var first = await ExerciseReturnOutcomeAsync<Outcome>();
        return $"first call: {DescribeOutcome(first)}";
    }

    private sealed class OlderGenericResults : IDamlType, IHasChoices<OlderGenericResults>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier("older-version-package", "RichTypes", "GenericResults"), DamlTypeKind.Template, "rich-types");

        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            new Choice<OlderGenericResults, DamlUnit, object>
            {
                Name = new ChoiceName("ReturnOutcome"),
                Consuming = false,
                ArgumentEncoder = _ => DamlUnit.Instance,
                ArgumentDecoder = _ => DamlUnit.Instance,
                ResultDecoder = _ => "older-version-result",
                ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
                ResultJsonReader = DamlLfJsonDecoders.ReadText,
            },
        ];
    }

    internal static async Task<string> ReturnOutcomeAsAnObjectWhileAnOlderVersionIsRegistered()
    {
        GeneratedTypeReaders.ForChoices<OlderGenericResults>();
        var first = await ExerciseReturnOutcomeAsync<object>();
        return $"first call: {DescribeObject(first)}";
    }

    internal static async Task<string> ReturnOutcomeAsAnObject()
    {
        var first = await ExerciseReturnOutcomeAsync<object>();
        return $"first call: {DescribeObject(first)}";
    }

    private static async Task<ExerciseOutcome<TResult>> ExerciseReturnOutcomeAsync<TResult>()
    {
        var options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
        using var channel = GrpcChannel.ForAddress(options.GrpcAddress);
        var client = new LedgerClient(
            options, channel, new AnsweringCommandService(WinTransaction()), new StaticTokenProvider("test-token"));
        var command = RuntimeExerciseCommand.For(
            new ContractId<GenericResultsMarker>("00results"), new ChoiceName("ReturnOutcome"), DamlUnit.Instance);

        return await client.TryExerciseAsync<TResult>(command, new Party("party::alice"));
    }

    private static SubmitAndWaitForTransactionResponse WinTransaction()
    {
        var win = DamlVariant.Create(
            "Win",
            DamlRecord.Create(DamlField.Create("prize", new DamlNumeric(12.5m)), DamlField.Create("tier", new DamlText("gold"))));
        var transaction = new Transaction { UpdateId = "upd-1", Offset = 1L };
        transaction.Events.Add(new Event
        {
            Exercised = new Com.Daml.Ledger.Api.V2.ExercisedEvent
            {
                ContractId = "00results",
                TemplateId = DamlValueConverter.ToProtoIdentifier(GenericResultsMarker.DamlTypeId.Identifier),
                Choice = "ReturnOutcome",
                ChoiceArgument = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                ExerciseResult = DamlValueConverter.ToProtoValue(win),
            },
        });
        return new SubmitAndWaitForTransactionResponse { Transaction = transaction };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string DescribeOutcome(ExerciseOutcome<Outcome> outcome) =>
        outcome switch
        {
            ExerciseOutcome<Outcome>.One { Result: Outcome.Win win } => $"One(Win({win.Value.Prize}, {win.Value.Tier}))",
            ExerciseOutcome<Outcome>.One one => $"One({one.Result.Tag})",
            ExerciseOutcome<Outcome>.CommittedUndecodable undecodable => $"CommittedUndecodable({undecodable.SourceException?.Message})",
            _ => outcome.GetType().Name,
        };

    private static string DescribeObject(ExerciseOutcome<object> outcome) =>
        outcome switch
        {
            ExerciseOutcome<object>.One one => $"One({one.Result.GetType().FullName})",
            ExerciseOutcome<object>.CommittedUndecodable undecodable => $"CommittedUndecodable({undecodable.SourceException?.Message})",
            _ => outcome.GetType().Name,
        };
}
