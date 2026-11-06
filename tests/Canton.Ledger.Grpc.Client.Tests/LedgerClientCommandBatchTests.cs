// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientCommandBatchTests : IDisposable
{
    private const string RichTypesPackageId = "3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c";
    private const string RichTypesPackageName = "richtypes-h07285b495f9e";

    private static readonly Party Alice = new("party::alice");

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientCommandBatchTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(Substitute.For<CallInvoker>());
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient() => new(_options, _channel, _commandService, _tokenProvider);

    [Fact]
    public async Task TrySubmitBatchAsync_sends_every_command_in_one_request_and_reads_each_result_from_its_root_node()
    {
        SubmitAndWaitForTransactionRequest? captured = null;
        LedgerClientTestFixtures.StubCommandServiceSuccess(
            _commandService, MixedBatchResponse(), request => captured = request);
        var batch = new CommandBatch();
        var firstAsset = batch.AddCreate(new Asset(Alice, 10m));
        var minted = batch.Add(Marker.ChoiceMint, new ContractId<Marker>("00marker"), new Marker.Mint(5m));
        var secondAsset = batch.AddCreate(new Asset(Alice, 20m));
        var archived = batch.Add(Marker.ChoiceArchive, new ContractId<Marker>("00marker-old"), DamlUnit.Instance);

        var outcome = await CreateClient().TrySubmitBatchAsync(
            batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        captured!.Commands.Commands_.Select(command => command.CommandCase).Should().Equal(
            Command.CommandOneofCase.Create,
            Command.CommandOneofCase.Exercise,
            Command.CommandOneofCase.Create,
            Command.CommandOneofCase.Exercise);
        captured.Commands.Commands_[1].Exercise.Choice.Should().Be("Mint");
        captured.Commands.Commands_[3].Exercise.ContractId.Should().Be("00marker-old");
        var results = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.One>().Subject.Result;
        results.Get(firstAsset).Should().Be(new ContractId<Asset>("00asset-first"));
        results.Get(minted).Should().Be(new ContractId<Asset>("00asset-minted"));
        results.Get(secondAsset).Should().Be(new ContractId<Asset>("00asset-second"));
        results.Get(archived).Should().Be(DamlUnit.Instance);
        outcome.Operation!.Label.Should().Be("Asset.Create ×2, Marker.Mint, Marker.Archive");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_rejected_by_a_command_partway_through_names_every_command()
    {
        LedgerClientTestFixtures.StubCommandServiceFailure(
            _commandService,
            LedgerClientTestFixtures.MakeDamlRpcException(
                "CONTRACT_NOT_FOUND", "Contract could not be found with id 00marker-old", "InvalidGivenCurrentSystemStateResourceMissing"));
        var batch = new CommandBatch();
        batch.AddCreate(new Asset(Alice, 10m));
        batch.Add(Marker.ChoiceArchive, new ContractId<Marker>("00marker-old"), DamlUnit.Instance);
        batch.AddCreate(new Asset(Alice, 20m));

        var outcome = await CreateClient().TrySubmitBatchAsync(
            batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.DamlError>().Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        var act = () => outcome.OneOrThrow();
        act.Should().Throw<LedgerOperationException>()
            .Which.Operation.Should().Be("Asset.Create ×2, Marker.Archive");
    }

    private static SubmitAndWaitForTransactionResponse MixedBatchResponse()
    {
        var transaction = new Transaction { UpdateId = "u-batch", Offset = 3L };
        transaction.Events.Add(new Event { Created = CreatedAsset("00asset-first", 0) });
        transaction.Events.Add(new Event
        {
            Exercised = Exercised("00marker", "Mint", new ProtoValue { ContractId = "00asset-minted" }, 1, 2, consuming: false),
        });
        transaction.Events.Add(new Event { Created = CreatedAsset("00asset-minted", 2) });
        transaction.Events.Add(new Event { Created = CreatedAsset("00asset-second", 3) });
        transaction.Events.Add(new Event
        {
            Exercised = Exercised("00marker-old", "Archive", new ProtoValue { Unit = new Empty() }, 4, 4, consuming: true),
        });
        return new SubmitAndWaitForTransactionResponse { Transaction = transaction };
    }

    private static ProtoCreatedEvent CreatedAsset(string contractId, int nodeId) => new()
    {
        AcsDelta = true,
        NodeId = nodeId,
        ContractId = contractId,
        PackageName = RichTypesPackageName,
        TemplateId = new ProtoIdentifier { PackageId = RichTypesPackageId, ModuleName = "RichTypes", EntityName = "Asset" },
        CreateArguments = new ProtoRecord(),
    };

    private static ProtoExercisedEvent Exercised(
        string contractId, string choice, ProtoValue result, int nodeId, int lastDescendantNodeId, bool consuming) => new()
    {
        NodeId = nodeId,
        LastDescendantNodeId = lastDescendantNodeId,
        ContractId = contractId,
        PackageName = RichTypesPackageName,
        TemplateId = new ProtoIdentifier { PackageId = RichTypesPackageId, ModuleName = "RichTypes", EntityName = "Marker" },
        Choice = choice,
        ChoiceArgument = new ProtoValue { Record = new ProtoRecord() },
        ExerciseResult = result,
        Consuming = consuming,
    };
}
