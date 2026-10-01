// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientTypedReadsTests : IDisposable
{
    private static readonly Party Alice = new("alice::ns1");
    private static readonly Party Bob = new("bob::ns1");
    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(Alice, new HashSet<Party> { Bob });

    private static readonly ProtoIdentifier FooBarTemplate = new()
    {
        PackageId = "test-pkg",
        ModuleName = "Sample.Foo",
        EntityName = "FooBar",
    };

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly StateService.StateServiceClient _stateService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly ContractService.ContractServiceClient _contractService;
    private readonly EventQueryService.EventQueryServiceClient _eventQueryService;
    private readonly CommandCompletionService.CommandCompletionServiceClient _completionService;

    public LedgerClientTypedReadsTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _contractService = Substitute.ForPartsOf<ContractService.ContractServiceClient>(callInvoker);
        _eventQueryService = Substitute.ForPartsOf<EventQueryService.EventQueryServiceClient>(callInvoker);
        _completionService = Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        Substitute.ForPartsOf<CommandService.CommandServiceClient>(Substitute.For<CallInvoker>()),
        _updateService,
        _stateService,
        Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(Substitute.For<CallInvoker>()),
        _completionService,
        new StaticTokenProvider("test-token"),
        contractService: _contractService,
        eventQueryService: _eventQueryService);

    [Fact]
    public async Task GetLatestPrunedOffsetsAsync_projects_both_offsets()
    {
        _stateService
            .GetLatestPrunedOffsetsAsync(Arg.Any<GetLatestPrunedOffsetsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetLatestPrunedOffsetsResponse
            {
                ParticipantPrunedUpToInclusive = 17L,
                AllDivulgedContractsPrunedUpToInclusive = 9L,
            }));

        var pruned = await CreateClient().GetLatestPrunedOffsetsAsync(cancellationToken: TestContext.Current.CancellationToken);

        pruned.ParticipantPrunedUpToInclusive.Should().Be(LedgerOffset.At(17));
        pruned.AllDivulgedContractsPrunedUpToInclusive.Should().Be(LedgerOffset.At(9));
    }

    [Fact]
    public async Task GetContractAsync_scopes_lookup_to_the_submitter_parties_and_decodes_the_payload()
    {
        GetContractRequest? captured = null;
        _contractService
            .GetContractAsync(Arg.Do<GetContractRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetContractResponse
            {
                CreatedEvent = CreatedEvent("00abc", offset: 42L, "alice::ns1", "bob::ns1"),
            }));

        var contract = await CreateClient().GetContractAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        captured!.ContractId.Should().Be("00abc");
        captured.QueryingParties.Should().Equal("alice::ns1", "bob::ns1");
        contract.ContractId.Value.Should().Be("00abc");
        contract.Payload.Owner.Should().Be("alice::ns1");
        contract.WitnessParties.Select(p => p.Value).Should().Equal("alice::ns1", "bob::ns1");
    }

    [Fact]
    public async Task GetContractAsync_reads_the_labelled_payload_from_the_event_query_when_the_contract_service_omits_field_labels()
    {
        var unlabelled = CreatedEvent("00abc", offset: 0L, "alice::ns1");
        foreach (var field in unlabelled.CreateArguments.Fields)
        {
            field.Label = string.Empty;
        }

        _contractService
            .GetContractAsync(Arg.Any<GetContractRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetContractResponse { CreatedEvent = unlabelled }));
        GetEventsByContractIdRequest? eventQuery = null;
        _eventQueryService
            .GetEventsByContractIdAsync(Arg.Do<GetEventsByContractIdRequest>(r => eventQuery = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetEventsByContractIdResponse
            {
                Created = new Created { CreatedEvent = CreatedEvent("00abc", 5L, "alice::ns1"), SynchronizerId = "sync::a" },
            }));

        var contract = await CreateClient().GetContractAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        eventQuery!.ContractId.Should().Be("00abc");
        contract.ContractId.Value.Should().Be("00abc");
        contract.Payload.Owner.Should().Be("alice::ns1");
        contract.WitnessParties.Select(p => p.Value).Should().Equal("alice::ns1");
    }

    [Fact]
    public async Task GetContractAsync_rejects_a_contract_of_another_template()
    {
        var foreign = CreatedEvent("00abc", offset: 1L);
        foreign.TemplateId = new ProtoIdentifier { PackageId = "test-pkg", ModuleName = "Sample.Foo", EntityName = "Other" };
        _contractService
            .GetContractAsync(Arg.Any<GetContractRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetContractResponse { CreatedEvent = foreign }));

        var act = async () => await CreateClient().GetContractAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Sample.Foo.Other*");
    }

    [Fact]
    public async Task GetContractAsync_surfaces_a_server_rejection_as_the_rpc_exception()
    {
        _contractService
            .GetContractAsync(Arg.Any<GetContractRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<GetContractResponse>(new RpcException(new Status(StatusCode.NotFound, "CONTRACT_NOT_FOUND"))));

        var act = async () => await CreateClient().GetContractAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_returns_creation_and_archival_with_the_template_filtered_format()
    {
        GetEventsByContractIdRequest? captured = null;
        _eventQueryService
            .GetEventsByContractIdAsync(Arg.Do<GetEventsByContractIdRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetEventsByContractIdResponse
            {
                Created = new Created { CreatedEvent = CreatedEvent("00abc", 5L, "alice::ns1"), SynchronizerId = "sync::a" },
                Archived = new Archived
                {
                    ArchivedEvent = new Com.Daml.Ledger.Api.V2.ArchivedEvent
                    {
                        ContractId = "00abc",
                        TemplateId = FooBarTemplate,
                        Offset = 8L,
                        WitnessParties = { "alice::ns1" },
                    },
                    SynchronizerId = "sync::a",
                },
            }));

        var lifecycle = await CreateClient().GetEventsByContractIdAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        captured!.ContractId.Should().Be("00abc");
        captured.EventFormat.FiltersByParty.Keys.Should().BeEquivalentTo("alice::ns1", "bob::ns1");
        captured.EventFormat.FiltersByParty["alice::ns1"].Cumulative.Should().ContainSingle()
            .Which.TemplateFilter.TemplateId.EntityName.Should().Contain("FooBar");
        lifecycle.Created!.Offset.Should().Be(LedgerOffset.At(5));
        lifecycle.Created.SynchronizerId.Value.Should().Be("sync::a");
        lifecycle.Created.Payload.Owner.Should().Be("alice::ns1");
        lifecycle.Archived!.Offset.Should().Be(LedgerOffset.At(8));
        lifecycle.Archived.ContractId.Value.Should().Be("00abc");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_leaves_archival_empty_for_a_live_contract()
    {
        _eventQueryService
            .GetEventsByContractIdAsync(Arg.Any<GetEventsByContractIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetEventsByContractIdResponse
            {
                Created = new Created { CreatedEvent = CreatedEvent("00abc", 5L, "alice::ns1"), SynchronizerId = "sync::a" },
            }));

        var lifecycle = await CreateClient().GetEventsByContractIdAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created.Should().NotBeNull();
        lifecycle.Archived.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_sends_paging_arguments_and_projects_entries_and_token()
    {
        GetActiveContractsPageRequest? captured = null;
        _stateService
            .GetActiveContractsPageAsync(Arg.Do<GetActiveContractsPageRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetActiveContractsPageResponse
            {
                ActiveContracts =
                {
                    new GetActiveContractsResponse
                    {
                        ActiveContract = new ActiveContract
                        {
                            CreatedEvent = CreatedEvent("00abc", 5L, "alice::ns1"),
                            SynchronizerId = "sync::a",
                        },
                    },
                },
                ActiveAtOffset = 30L,
                NextPageToken = ByteString.CopyFrom([1, 2, 3]),
            }));

        var page = await CreateClient().GetActiveContractsPageAsync<FooBar>(
            Submitter,
            activeAtOffset: LedgerOffset.At(30),
            maxPageSize: 50,
            pageToken: new LedgerPageToken("AQID"),
            cancellationToken: TestContext.Current.CancellationToken);

        captured!.ActiveAtOffset.Should().Be(30L);
        captured.MaxPageSize.Should().Be(50);
        captured.PageToken.ToBase64().Should().Be("AQID");
        captured.EventFormat.FiltersByParty.Keys.Should().BeEquivalentTo("alice::ns1", "bob::ns1");
        page.ActiveAtOffset.Should().Be(LedgerOffset.At(30));
        page.NextPageToken!.Value.Should().Be("AQID");
        page.Entries.Should().ContainSingle().Which
            .Should().BeOfType<AcsSnapshotEntry<FooBar>.Created>().Which.ContractId.Value.Should().Be("00abc");
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_omits_optional_arguments_and_reports_the_last_page_without_a_token()
    {
        GetActiveContractsPageRequest? captured = null;
        _stateService
            .GetActiveContractsPageAsync(Arg.Do<GetActiveContractsPageRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetActiveContractsPageResponse { ActiveAtOffset = 12L }));

        var page = await CreateClient().GetActiveContractsPageAsync<FooBar>(
            Submitter, cancellationToken: TestContext.Current.CancellationToken);

        captured!.HasActiveAtOffset.Should().BeFalse();
        captured.HasMaxPageSize.Should().BeFalse();
        captured.HasPageToken.Should().BeFalse();
        page.NextPageToken.Should().BeNull();
        page.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_at_ledger_begin_answers_an_empty_page_without_calling_the_ledger()
    {
        var page = await CreateClient().GetActiveContractsPageAsync<FooBar>(
            Submitter, LedgerOffset.Begin, cancellationToken: TestContext.Current.CancellationToken);

        page.Entries.Should().BeEmpty();
        page.ActiveAtOffset.Should().Be(LedgerOffset.Begin);
        page.NextPageToken.Should().BeNull();
        _ = _stateService.DidNotReceive().GetActiveContractsPageAsync(
            Arg.Any<GetActiveContractsPageRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUpdatesPageAsync_sends_paging_arguments_and_projects_transactions_and_token()
    {
        GetUpdatesPageRequest? captured = null;
        _updateService
            .GetUpdatesPageAsync(Arg.Do<GetUpdatesPageRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetUpdatesPageResponse
            {
                Updates =
                {
                    new GetUpdateResponse { Transaction = new Transaction { UpdateId = "u-1", Offset = 21L, CommandId = "cmd-1" } },
                },
                LowestPageOffsetExclusive = 20L,
                HighestPageOffsetInclusive = 21L,
                NextPageToken = ByteString.CopyFrom([4, 5, 6]),
            }));

        var page = await CreateClient().GetUpdatesPageAsync(
            Submitter,
            beginExclusive: LedgerOffset.At(20),
            endInclusive: LedgerOffset.At(99),
            maxPageSize: 10,
            descendingOrder: true,
            pageToken: new LedgerPageToken("AQID"),
            cancellationToken: TestContext.Current.CancellationToken);

        captured!.BeginOffsetExclusive.Should().Be(20L);
        captured.EndOffsetInclusive.Should().Be(99L);
        captured.MaxPageSize.Should().Be(10);
        captured.DescendingOrder.Should().BeTrue();
        captured.PageToken.ToBase64().Should().Be("AQID");
        captured.UpdateFormat.IncludeTransactions.EventFormat.FiltersByParty.Keys.Should().BeEquivalentTo("alice::ns1", "bob::ns1");
        page.LowestPageOffsetExclusive.Should().Be(LedgerOffset.At(20));
        page.HighestPageOffsetInclusive.Should().Be(LedgerOffset.At(21));
        page.NextPageToken!.Value.Should().Be("BAUG");
        page.Updates.Should().ContainSingle().Which.UpdateId.Should().Be("u-1");
    }

    [Fact]
    public async Task GetUpdatesPageAsync_omits_optional_arguments_by_default()
    {
        GetUpdatesPageRequest? captured = null;
        _updateService
            .GetUpdatesPageAsync(Arg.Do<GetUpdatesPageRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetUpdatesPageResponse()));

        var page = await CreateClient().GetUpdatesPageAsync(Submitter, cancellationToken: TestContext.Current.CancellationToken);

        captured!.HasBeginOffsetExclusive.Should().BeFalse();
        captured.HasEndOffsetInclusive.Should().BeFalse();
        captured.HasMaxPageSize.Should().BeFalse();
        captured.HasPageToken.Should().BeFalse();
        captured.DescendingOrder.Should().BeFalse();
        page.Updates.Should().BeEmpty();
        page.NextPageToken.Should().BeNull();
    }

    [Fact]
    public async Task GetUpdatesPageAsync_rejects_a_non_transaction_update()
    {
        _updateService
            .GetUpdatesPageAsync(Arg.Any<GetUpdatesPageRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new GetUpdatesPageResponse
            {
                Updates = { new GetUpdateResponse { Reassignment = new Reassignment { UpdateId = "r-1" } } },
            }));

        var act = async () => await CreateClient().GetUpdatesPageAsync(Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Reassignment*");
    }

    [Fact]
    public async Task GetCompletionsAsync_requests_the_given_parties_across_users_and_yields_completions()
    {
        GetCompletionsRequest? captured = null;
        var reader = new FakeStreamReader<CompletionStreamResponse>(
        [
            new CompletionStreamResponse { Completion = new Com.Daml.Ledger.Api.V2.Completion { CommandId = "c1", UpdateId = "u1" } },
            new CompletionStreamResponse { OffsetCheckpoint = new OffsetCheckpoint { Offset = 5L } },
        ]);
        _completionService
            .GetCompletions(Arg.Do<GetCompletionsRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new AsyncServerStreamingCall<CompletionStreamResponse>(
                reader, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var events = new List<CompletionStreamEvent>();
        await foreach (var item in CreateClient().GetCompletionsAsync(
            [Alice, Bob], LedgerOffset.At(3), TestContext.Current.CancellationToken))
        {
            events.Add(item);
        }

        captured!.Parties.Should().Equal("alice::ns1", "bob::ns1");
        captured.BeginExclusive.Should().Be(3L);
        events[0].Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Which.UpdateId.Should().Be("u1");
        events[1].Should().BeOfType<CompletionStreamEvent.Checkpoint>().Which.Offset.Should().Be(LedgerOffset.At(5));
    }

    [Fact]
    public async Task GetCompletionsAsync_surfaces_a_stream_fault_in_band()
    {
        var reader = new FakeStreamReader<CompletionStreamResponse>(
            Array.Empty<CompletionStreamResponse>(), new RpcException(new Status(StatusCode.Unavailable, "down")));
        _completionService
            .GetCompletions(Arg.Any<GetCompletionsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new AsyncServerStreamingCall<CompletionStreamResponse>(
                reader, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var events = new List<CompletionStreamEvent>();
        await foreach (var item in CreateClient().GetCompletionsAsync([Alice], cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(item);
        }

        events.Should().ContainSingle().Which.Should().BeOfType<CompletionStreamEvent.StreamError>();
    }

    [Fact]
    public async Task GetCompletionsAsync_rejects_a_null_party_list()
    {
        var act = async () =>
        {
            await foreach (var _ in CreateClient().GetCompletionsAsync(null!, cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        };

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("parties");
    }

    private static ProtoCreatedEvent CreatedEvent(string contractId, long offset, params string[] witnesses)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = contractId,
            TemplateId = FooBarTemplate,
            CreateArguments = LedgerClientTestFixtures.OwnerArgumentsFor("alice::ns1"),
            Offset = offset,
        };
        created.WitnessParties.AddRange(witnesses);
        return created;
    }

    private static AsyncUnaryCall<TResponse> Unary<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<TResponse> Faulted<TResponse>(RpcException exception) =>
        new(
            Task.FromException<TResponse>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => new Metadata(),
            () => { });
}
