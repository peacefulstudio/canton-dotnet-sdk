// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RuntimeExerciseCommand = Daml.Runtime.Commands.ExerciseCommand;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcChoiceResultProjectionParityTests : ChoiceResultProjectionParityTests, IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private readonly LedgerClientOptions _options = new()
    {
        GrpcAddress = "https://localhost:5001",
        UserId = "test-user",
    };

    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;

    public GrpcChoiceResultProjectionParityTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(Substitute.For<CallInvoker>());
    }

    public void Dispose() => _channel.Dispose();

    protected override async Task<ExerciseOutcome<TResult>> ExerciseOnWireAsync<TResult>(WireExercise exercise)
    {
        var transaction = new Transaction { UpdateId = "upd-1", Offset = 1L };
        transaction.Events.Add(new Event { Exercised = ProtoExercised(exercise) });
        LedgerClientTestFixtures.StubCommandServiceSuccess(
            _commandService, new SubmitAndWaitForTransactionResponse { Transaction = transaction });
        var client = new LedgerClient(_options, _channel, _commandService, new StaticTokenProvider("test-token"));
        var command = new RuntimeExerciseCommand(
            exercise.CommandTarget,
            new ContractId<HoldingOwner>(exercise.ContractId),
            new ChoiceName(exercise.Choice),
            DamlUnit.Instance);

        return await client.TryExerciseAsync<TResult>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static ProtoExercisedEvent ProtoExercised(WireExercise exercise)
    {
        var exercised = new ProtoExercisedEvent
        {
            ContractId = exercise.ContractId,
            TemplateId = DamlValueConverter.ToProtoIdentifier(exercise.TemplateId),
            Choice = exercise.Choice,
            ChoiceArgument = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            ExerciseResult = DamlValueConverter.ToProtoValue(exercise.Result),
        };
        if (exercise.InterfaceId is { } interfaceId)
        {
            exercised.InterfaceId = DamlValueConverter.ToProtoIdentifier(interfaceId);
        }

        return exercised;
    }

    protected override ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, Daml.Runtime.Commands.ExerciseCommand command) =>
        GrpcTransactionResultProjector.ProjectChoiceResult<TResult>(outcome, command);
}
