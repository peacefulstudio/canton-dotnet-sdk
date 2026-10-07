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
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RuntimeExerciseCommand = Daml.Runtime.Commands.ExerciseCommand;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcGeneratedTypeDiscoveryParityTests : GeneratedTypeDiscoveryParityTests, IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private readonly LedgerClientOptions _options = new()
    {
        GrpcAddress = "https://localhost:5001",
        UserId = "test-user",
    };

    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;

    public GrpcGeneratedTypeDiscoveryParityTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(Substitute.For<CallInvoker>());
    }

    public void Dispose() => _channel.Dispose();

    protected override async Task<ExerciseOutcome<OptionalTails>> ExerciseAnsweringEchoedTailsAsync(
        RuntimeIdentifier templateId, ChoiceName choice)
    {
        var transaction = new Transaction { UpdateId = "upd-1", Offset = 1L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00echo",
                TemplateId = DamlValueConverter.ToProtoIdentifier(templateId),
                Choice = choice.Value,
                ChoiceArgument = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                ExerciseResult = DamlValueConverter.ToProtoValue(EchoedTails.ToRecord()),
            },
        });
        LedgerClientTestFixtures.StubCommandServiceSuccess(
            _commandService, new SubmitAndWaitForTransactionResponse { Transaction = transaction });
        var client = new LedgerClient(_options, _channel, _commandService, new StaticTokenProvider("test-token"));
        var command = new RuntimeExerciseCommand(templateId, new ContractId<OptionalTails>("00echo"), choice, DamlUnit.Instance);

        return await client.TryExerciseAsync<OptionalTails>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);
    }
}
