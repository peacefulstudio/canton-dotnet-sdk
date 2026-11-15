// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using RuntimeCommands = Daml.Runtime.Commands;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientPointReadAcsDeltaTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private static readonly RuntimeCommands.SubmitterInfo AliceSubmitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    private static readonly ProtoIdentifier TemplateId = new()
    {
        PackageId = "pkg",
        ModuleName = "Module",
        EntityName = "Ephemeral",
    };

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001" };
    private readonly GrpcChannel _channel;
    private readonly UpdateService.UpdateServiceClient _updateService;

    public LedgerClientPointReadAcsDeltaTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(Substitute.For<CallInvoker>());
        var churn = new GetUpdateResponse { Transaction = ChurnTransaction() };
        _updateService
            .GetUpdateByOffsetAsync(
                Arg.Any<GetUpdateByOffsetRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Ok(churn));
        _updateService
            .GetUpdateByIdAsync(
                Arg.Any<GetUpdateByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Ok(churn));
        _updateService
            .GetUpdatesPageAsync(
                Arg.Any<GetUpdatesPageRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Ok(new GetUpdatesPageResponse
            {
                Updates = { churn },
                LowestPageOffsetExclusive = 6L,
                HighestPageOffsetInclusive = 7L,
            }));
    }

    public void Dispose() => _channel.Dispose();

    [Fact]
    public async Task GetUpdateByOffsetAsync_reports_only_the_acsDelta_creates_and_the_archives_of_consuming_exercises()
    {
        var client = CreateClient();

        var result = await client.GetUpdateByOffsetAsync(
            LedgerOffset.At(7), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        result.CreatedContracts.Select(created => created.ContractId).Should().Equal("00kept");
        result.ArchivedContractIds.Should().Equal("00retiring");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_reports_only_the_acsDelta_creates_and_the_archives_of_consuming_exercises()
    {
        var client = CreateClient();

        var result = await client.GetUpdateByIdAsync(
            "upd-churn", AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        result.CreatedContracts.Select(created => created.ContractId).Should().Equal("00kept");
        result.ArchivedContractIds.Should().Equal("00retiring");
    }

    [Fact]
    public async Task GetUpdatesPageAsync_reports_only_the_acsDelta_creates_and_the_archives_of_consuming_exercises()
    {
        var client = CreateClient();

        var page = await client.GetUpdatesPageAsync(
            AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        var result = page.Updates.Should().ContainSingle().Subject;
        result.CreatedContracts.Select(created => created.ContractId).Should().Equal("00kept");
        result.ArchivedContractIds.Should().Equal("00retiring");
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_still_lists_every_exercise()
    {
        var client = CreateClient();

        var result = await client.GetUpdateByOffsetAsync(
            LedgerOffset.At(7), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        result.ExercisedEvents.Select(exercised => exercised.ContractId)
            .Should().Equal("00factory", "00retiring", "00transient");
    }

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        new CommandService.CommandServiceClient(_channel),
        _updateService,
        new StateService.StateServiceClient(_channel),
        tokenProvider: new StaticTokenProvider("test-token"));

    private static Transaction ChurnTransaction()
    {
        var transaction = new Transaction { UpdateId = "upd-churn", CommandId = "cmd-churn", Offset = 7L };
        transaction.Events.Add(Exercised(0, "00factory", "Churn", consuming: false, acsDelta: false, lastDescendantNodeId: 4));
        transaction.Events.Add(Exercised(1, "00retiring", "Archive", consuming: true, acsDelta: true, lastDescendantNodeId: 1));
        transaction.Events.Add(Created(2, "00kept", acsDelta: true));
        transaction.Events.Add(Created(3, "00transient", acsDelta: false));
        transaction.Events.Add(Exercised(4, "00transient", "Archive", consuming: true, acsDelta: false, lastDescendantNodeId: 4));
        return transaction;
    }

    private static Event Created(int nodeId, string contractId, bool acsDelta) =>
        new()
        {
            Created = new ProtoCreatedEvent
            {
                NodeId = nodeId,
                ContractId = contractId,
                TemplateId = TemplateId,
                CreateArguments = new ProtoRecord(),
                AcsDelta = acsDelta,
            },
        };

    private static Event Exercised(
        int nodeId, string contractId, string choice, bool consuming, bool acsDelta, int lastDescendantNodeId) =>
        new()
        {
            Exercised = new ProtoExercisedEvent
            {
                NodeId = nodeId,
                LastDescendantNodeId = lastDescendantNodeId,
                ContractId = contractId,
                TemplateId = TemplateId,
                Choice = choice,
                ChoiceArgument = new Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                ExerciseResult = new Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                Consuming = consuming,
                AcsDelta = acsDelta,
                ActingParties = { "party::alice" },
                WitnessParties = { "party::alice" },
            },
        };

    private static AsyncUnaryCall<T> Ok<T>(T value) =>
        new(
            Task.FromResult(value),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
