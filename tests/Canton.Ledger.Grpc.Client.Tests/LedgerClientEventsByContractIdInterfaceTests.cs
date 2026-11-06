// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RpcStatus = Google.Rpc.Status;
using RuntimeCommands = Daml.Runtime.Commands;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientEventsByContractIdInterfaceTests : IDisposable
{
    private static readonly Party Alice = new("alice::ns1");
    private static readonly Party Bob = new("bob::ns1");
    private static readonly RuntimeCommands.SubmitterInfo Submitter = new(Alice, new HashSet<Party> { Bob });

    private static readonly ProtoIdentifier ImplementingTemplate = new()
    {
        PackageId = "impl-pkg",
        ModuleName = "Token.Impl",
        EntityName = "Asset",
    };

    private static readonly ProtoIdentifier ViewedInterface = new()
    {
        PackageId = "viewed-pkg",
        ModuleName = "Token.Api",
        EntityName = "IViewedHolding",
    };

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly EventQueryService.EventQueryServiceClient _eventQueryService;

    public LedgerClientEventsByContractIdInterfaceTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        _eventQueryService = Substitute.ForPartsOf<EventQueryService.EventQueryServiceClient>(Substitute.For<CallInvoker>());
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        Substitute.ForPartsOf<CommandService.CommandServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<StateService.StateServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(Substitute.For<CallInvoker>()),
        new StaticTokenProvider("test-token"),
        eventQueryService: _eventQueryService);

    [Fact]
    public async Task GetEventsByContractIdAsync_returns_the_creation_with_its_decoded_view_and_the_archival_of_an_archived_interface_contract()
    {
        StubEventsByContractId(new GetEventsByContractIdResponse
        {
            Created = new Created { CreatedEvent = CreatedWithView("00impl", amount: 42.5m, offset: 5L), SynchronizerId = "sync::a" },
            Archived = ArchivedAt("00impl", offset: 8L),
        });

        var lifecycle = await CreateClient().GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created!.ContractId.Value.Should().Be("00impl");
        lifecycle.Created.Payload.Amount.Should().Be(42.5m);
        lifecycle.Created.Offset.Should().Be(LedgerOffset.At(5));
        lifecycle.Created.SynchronizerId.Value.Should().Be("sync::a");
        lifecycle.Created.WitnessParties.Select(party => party.Value).Should().Equal("alice::ns1", "bob::ns1");
        lifecycle.Archived!.ContractId.Value.Should().Be("00impl");
        lifecycle.Archived.Offset.Should().Be(LedgerOffset.At(8));
        lifecycle.Archived.SynchronizerId.Value.Should().Be("sync::a");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_leaves_the_archival_empty_for_an_active_interface_contract()
    {
        StubEventsByContractId(new GetEventsByContractIdResponse
        {
            Created = new Created { CreatedEvent = CreatedWithView("00impl", amount: 1m, offset: 5L), SynchronizerId = "sync::a" },
        });

        var lifecycle = await CreateClient().GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created.Should().NotBeNull();
        lifecycle.Archived.Should().BeNull();
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_asks_the_participant_for_an_InterfaceFilter_carrying_the_view()
    {
        GetEventsByContractIdRequest? captured = null;
        StubEventsByContractId(
            request => captured = request,
            new GetEventsByContractIdResponse
            {
                Created = new Created { CreatedEvent = CreatedWithView("00impl", amount: 1m, offset: 5L), SynchronizerId = "sync::a" },
            });

        _ = await CreateClient().GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        captured!.ContractId.Should().Be("00impl");
        captured.EventFormat.FiltersByParty.Keys.Should().BeEquivalentTo("alice::ns1", "bob::ns1");
        var interfaceFilter = captured.EventFormat.FiltersByParty["alice::ns1"].Cumulative.Should().ContainSingle().Subject.InterfaceFilter;
        interfaceFilter.Should().NotBeNull();
        interfaceFilter.IncludeInterfaceView.Should().BeTrue();
        interfaceFilter.InterfaceId.EntityName.Should().Be("IViewedHolding");
        interfaceFilter.InterfaceId.PackageId.Should().Be("#viewed-token-api");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_ignores_the_implementing_templates_create_arguments()
    {
        var created = CreatedWithView("00impl", amount: 42.5m, offset: 5L);
        created.CreateArguments = new ProtoRecord
        {
            Fields = { new RecordField { Label = "amount", Value = new ProtoValue { Numeric = "999" } } },
        };
        StubEventsByContractId(new GetEventsByContractIdResponse { Created = new Created { CreatedEvent = created, SynchronizerId = "sync::a" } });

        var lifecycle = await CreateClient().GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created!.Payload.Amount.Should().Be(42.5m);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_surfaces_an_unknown_contract_id_as_a_not_committed_ledger_operation_failure()
    {
        _eventQueryService
            .GetEventsByContractIdAsync(Arg.Any<GetEventsByContractIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted(new RpcException(new Status(StatusCode.NotFound, "CONTRACT_EVENTS_NOT_FOUND"))));

        var act = async () => await CreateClient().GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00unknown"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.NotFound));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_refuses_a_creation_whose_interface_view_the_participant_did_not_compute()
    {
        var created = CreatedWithView("00impl", amount: 1m, offset: 5L);
        created.InterfaceViews[0].ViewStatus = new RpcStatus { Code = 9, Message = "view failed" };
        StubEventsByContractId(new GetEventsByContractIdResponse { Created = new Created { CreatedEvent = created, SynchronizerId = "sync::a" } });

        var act = async () => await CreateClient().GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*00impl*IViewedInterfaceMarker*interface view is unavailable*");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_view_lacks_a_required_field()
    {
        var created = CreatedWithView("00impl", amount: 1m, offset: 5L);
        created.InterfaceViews[0].ViewValue = new ProtoRecord();
        StubEventsByContractId(new GetEventsByContractIdResponse { Created = new Created { CreatedEvent = created, SynchronizerId = "sync::a" } });

        var act = async () => await CreateClient().GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.Detail.Should().Be("Required field 'amount' not found in record.");
    }

    private static Archived ArchivedAt(string contractId, long offset) =>
        new()
        {
            ArchivedEvent = new Com.Daml.Ledger.Api.V2.ArchivedEvent
            {
                ContractId = contractId,
                PackageName = "token-impl",
                TemplateId = ImplementingTemplate,
                Offset = offset,
                WitnessParties = { "alice::ns1" },
            },
            SynchronizerId = "sync::a",
        };

    private static ProtoCreatedEvent CreatedWithView(string contractId, decimal amount, long offset)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = contractId,
            PackageName = "token-impl",
            TemplateId = ImplementingTemplate,
            CreateArguments = new ProtoRecord(),
            Offset = offset,
            WitnessParties = { "alice::ns1", "bob::ns1" },
        };
        created.InterfaceViews.Add(new InterfaceView
        {
            InterfaceId = ViewedInterface,
            ViewStatus = new RpcStatus { Code = 0 },
            ViewValue = new ProtoRecord
            {
                Fields =
                {
                    new RecordField
                    {
                        Label = "amount",
                        Value = new ProtoValue { Numeric = amount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    },
                },
            },
        });
        return created;
    }

    private void StubEventsByContractId(GetEventsByContractIdResponse response) =>
        StubEventsByContractId(onRequest: null, response);

    private void StubEventsByContractId(Action<GetEventsByContractIdRequest>? onRequest, GetEventsByContractIdResponse response) =>
        _eventQueryService
            .GetEventsByContractIdAsync(
                Arg.Do<GetEventsByContractIdRequest>(request => onRequest?.Invoke(request)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(Unary(response));

    private static AsyncUnaryCall<GetEventsByContractIdResponse> Unary(GetEventsByContractIdResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<GetEventsByContractIdResponse> Faulted(RpcException exception) =>
        new(
            Task.FromException<GetEventsByContractIdResponse>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => new Metadata(),
            () => { });
}
