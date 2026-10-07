// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using SignatureFormat = Canton.Ledger.Abstractions.SignatureFormat;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientUndecodableResponseTests : IDisposable
{
    private static readonly string NegativeOffsetDetail =
        "value ('-1') must be a non-negative value. (Parameter 'value')" + Environment.NewLine + "Actual value was -1.";

    private static readonly Party Alice = new("alice::ns1");
    private static readonly RuntimeCommands.SubmitterInfo Submitter = new(Alice);

    private static readonly Com.Daml.Ledger.Api.V2.Identifier FooBarTemplate = new()
    {
        PackageId = "test-pkg",
        ModuleName = "Sample.Foo",
        EntityName = "FooBar",
    };

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly ContractService.ContractServiceClient _contractService;
    private readonly EventQueryService.EventQueryServiceClient _eventQueryService;
    private readonly Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient _interactiveService;

    public LedgerClientUndecodableResponseTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _contractService = Substitute.ForPartsOf<ContractService.ContractServiceClient>(callInvoker);
        _eventQueryService = Substitute.ForPartsOf<EventQueryService.EventQueryServiceClient>(callInvoker);
        _interactiveService =
            Substitute.ForPartsOf<Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(Substitute.For<CallInvoker>()),
        new StaticTokenProvider("test-token"),
        contractService: _contractService,
        eventQueryService: _eventQueryService,
        interactiveSubmissionService: _interactiveService);

    [Fact]
    public async Task GetLedgerEndAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_offset_is_negative()
    {
        _stateService
            .GetLedgerEndAsync(Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetLedgerEndResponse { Offset = -1 }));

        var act = () => CreateClient().GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, NegativeOffsetDetail);
    }

    [Fact]
    public async Task GetLatestPrunedOffsetsAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_an_offset_is_negative()
    {
        _stateService
            .GetLatestPrunedOffsetsAsync(Arg.Any<GetLatestPrunedOffsetsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetLatestPrunedOffsetsResponse { ParticipantPrunedUpToInclusive = -1 }));

        var act = () => CreateClient().GetLatestPrunedOffsetsAsync(cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, NegativeOffsetDetail);
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_snapshot_offset_is_negative()
    {
        _stateService
            .GetActiveContractsPageAsync(Arg.Any<GetActiveContractsPageRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetActiveContractsPageResponse { ActiveAtOffset = -1 }));

        var act = () => CreateClient().GetActiveContractsPageAsync<FooBar>(Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, NegativeOffsetDetail);
    }

    [Fact]
    public async Task GetUpdateByIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_transaction_offset_is_negative()
    {
        _updateService
            .GetUpdateByIdAsync(Arg.Any<GetUpdateByIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetUpdateResponse { Transaction = new Transaction { UpdateId = "u-1", Offset = -1 } }));

        var act = () => CreateClient().GetUpdateByIdAsync("u-1", Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, $"the transaction at id u-1 could not be decoded: {NegativeOffsetDetail}");
    }

    [Fact]
    public async Task GetContractAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_response_has_no_created_event()
    {
        _contractService
            .GetContractAsync(Arg.Any<GetContractRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetContractResponse()));

        var act = () => CreateClient().GetContractAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(
            act,
            CommitState.NotCommitted,
            "the GetContract response has no created_event, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_raises_an_undecodable_body_failure_that_committed_when_the_completion_offset_is_negative()
    {
        _commandService
            .SubmitAndWaitAsync(Arg.Any<SubmitAndWaitRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new SubmitAndWaitResponse { UpdateId = "u-1", CompletionOffset = -1 }));

        var act = () => CreateClient().SubmitAndWaitAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.Committed, NegativeOffsetDetail);
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_raises_an_undecodable_body_failure_that_committed_when_the_completion_offset_is_negative()
    {
        _interactiveService
            .ExecuteSubmissionAndWaitAsync(
                Arg.Any<Interactive.ExecuteSubmissionAndWaitRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.ExecuteSubmissionAndWaitResponse { UpdateId = "u-1", CompletionOffset = -1 }));

        var act = () => CreateClient().ExecuteSubmissionAndWaitAsync(Signed(), cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.Committed, NegativeOffsetDetail);
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_raises_an_undecodable_body_failure_that_committed_when_the_transaction_is_undecodable()
    {
        _interactiveService
            .ExecuteSubmissionAndWaitForTransactionAsync(
                Arg.Any<Interactive.ExecuteSubmissionAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.ExecuteSubmissionAndWaitForTransactionResponse
            {
                Transaction = new Transaction { UpdateId = "u-1", Offset = -1 },
            }));

        var act = () => CreateClient().ExecuteSubmissionAndWaitForTransactionAsync(
            Signed(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(
            act, CommitState.Committed, $"the transaction at submission sub-1 could not be decoded: {NegativeOffsetDetail}");
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_raises_an_undecodable_body_failure_that_committed_when_the_response_has_no_transaction()
    {
        _interactiveService
            .ExecuteSubmissionAndWaitForTransactionAsync(
                Arg.Any<Interactive.ExecuteSubmissionAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.ExecuteSubmissionAndWaitForTransactionResponse()));

        var act = () => CreateClient().ExecuteSubmissionAndWaitForTransactionAsync(
            Signed(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(
            act,
            CommitState.Committed,
            "the ExecuteSubmissionAndWaitForTransaction response has no transaction, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_created_offset_is_negative()
    {
        StubEventsByContractId(new GetEventsByContractIdResponse
        {
            Created = new Created { CreatedEvent = CreatedEvent("00abc", offset: -1), SynchronizerId = "sync::a" },
        });

        var act = () => CreateClient().GetEventsByContractIdAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, NegativeOffsetDetail);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_archived_offset_is_negative()
    {
        StubEventsByContractId(new GetEventsByContractIdResponse
        {
            Archived = new Archived
            {
                ArchivedEvent = new Com.Daml.Ledger.Api.V2.ArchivedEvent
                {
                    ContractId = "00abc",
                    TemplateId = FooBarTemplate,
                    Offset = -1,
                },
                SynchronizerId = "sync::a",
            },
        });

        var act = () => CreateClient().GetEventsByContractIdAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, NegativeOffsetDetail);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_response_has_no_prepared_transaction()
    {
        _interactiveService
            .PrepareSubmissionAsync(Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.PrepareSubmissionResponse()));

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(
            act,
            CommitState.NotCommitted,
            "the participant prepared a submission without a prepared transaction, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_response_has_no_synchronizer_id()
    {
        _interactiveService
            .GetPreferredPackagesAsync(Arg.Any<Interactive.GetPreferredPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.GetPreferredPackagesResponse()));

        var act = () => CreateClient().GetPreferredPackagesAsync([], cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(
            act,
            CommitState.NotCommitted,
            "the participant returned a package preference without a synchronizer id, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_preference_has_no_synchronizer_id()
    {
        _interactiveService
            .GetPreferredPackageVersionAsync(Arg.Any<Interactive.GetPreferredPackageVersionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.GetPreferredPackageVersionResponse
            {
                PackagePreference = new Interactive.PackagePreference
                {
                    PackageReference = new Com.Daml.Ledger.Api.V2.PackageReference { PackageId = "pkg-1", PackageName = "pkg", PackageVersion = "1.0.0" },
                },
            }));

        var act = () => CreateClient().GetPreferredPackageVersionAsync([Alice], "pkg", cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(
            act,
            CommitState.NotCommitted,
            "the participant returned a package preference without a synchronizer id, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_preference_has_no_package_reference()
    {
        _interactiveService
            .GetPreferredPackageVersionAsync(Arg.Any<Interactive.GetPreferredPackageVersionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.GetPreferredPackageVersionResponse
            {
                PackagePreference = new Interactive.PackagePreference { SynchronizerId = "sync::a" },
            }));

        var act = () => CreateClient().GetPreferredPackageVersionAsync([Alice], "pkg", cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(
            act,
            CommitState.NotCommitted,
            "the package preference has no package_reference, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_cost_exceeds_the_signed_range()
    {
        _interactiveService
            .PrepareSubmissionAsync(Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.PrepareSubmissionResponse
            {
                PreparedTransaction = new Interactive.PreparedTransaction(),
                CostEstimation = new Interactive.CostEstimation { TotalTrafficCostEstimation = ulong.MaxValue },
            }));

        var act = () => CreateClient().EstimateTrafficCostAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(
            act,
            CommitState.NotCommitted,
            "the participant reports a total traffic cost of 18446744073709551615 bytes, which exceeds the supported maximum of 9223372036854775807.");
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_estimation_timestamp_is_out_of_range()
    {
        StubPrepareSubmission(new Interactive.CostEstimation
        {
            EstimationTimestamp = new Google.Protobuf.WellKnownTypes.Timestamp { Seconds = long.MaxValue },
        });

        var act = () => CreateClient().EstimateTrafficCostAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, "Timestamp contains invalid values: Seconds={Seconds}; Nanos={Nanos}");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_estimation_timestamp_is_out_of_range()
    {
        StubPrepareSubmission(new Interactive.CostEstimation
        {
            EstimationTimestamp = new Google.Protobuf.WellKnownTypes.Timestamp { Seconds = long.MaxValue },
        });

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, "Timestamp contains invalid values: Seconds={Seconds}; Nanos={Nanos}");
    }

    [Fact]
    public async Task GetContractAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_required_payload_field_is_absent()
    {
        StubGetContract(Created("00abc", new Com.Daml.Ledger.Api.V2.Record
        {
            Fields = { new RecordField { Label = "other", Value = new Com.Daml.Ledger.Api.V2.Value { Party = "alice::ns1" } } },
        }));

        var act = () => CreateClient().GetContractAsync(new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, "Required field 'owner' not found in record.");
    }

    [Fact]
    public async Task GetContractAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_payload_field_has_the_wrong_kind()
    {
        StubGetContract(Created("00abc", new Com.Daml.Ledger.Api.V2.Record
        {
            Fields = { new RecordField { Label = "owner", Value = new Com.Daml.Ledger.Api.V2.Value { Text = "alice" } } },
        }));

        var act = () => CreateClient().GetContractAsync(new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, "Cannot cast DamlText to DamlParty");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_required_payload_field_is_absent()
    {
        StubEventsByContractId(new GetEventsByContractIdResponse
        {
            Created = new Created
            {
                CreatedEvent = new Com.Daml.Ledger.Api.V2.CreatedEvent
                {
                    ContractId = "00abc",
                    TemplateId = FooBarTemplate,
                    CreateArguments = new Com.Daml.Ledger.Api.V2.Record
                    {
                        Fields = { new RecordField { Label = "other", Value = new Com.Daml.Ledger.Api.V2.Value { Party = "alice::ns1" } } },
                    },
                    Offset = 5,
                },
                SynchronizerId = "sync::a",
            },
        });

        var act = () => CreateClient().GetEventsByContractIdAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, "Required field 'owner' not found in record.");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_an_interface_view_lacks_a_required_field()
    {
        var created = new Com.Daml.Ledger.Api.V2.CreatedEvent
        {
            ContractId = "00abc",
            TemplateId = new Com.Daml.Ledger.Api.V2.Identifier { PackageId = "impl-pkg", ModuleName = "Impl.Module", EntityName = "Impl" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = 5,
        };
        created.InterfaceViews.Add(new InterfaceView
        {
            InterfaceId = new Com.Daml.Ledger.Api.V2.Identifier { PackageId = "view-pkg", ModuleName = "Mismatched.Token", EntityName = "IMismatched" },
            ViewStatus = new Google.Rpc.Status { Code = 0 },
            ViewValue = new Com.Daml.Ledger.Api.V2.Record(),
        });
        StubEventsByContractId(new GetEventsByContractIdResponse
        {
            Created = new Created { CreatedEvent = created, SynchronizerId = "sync::a" },
        });

        var act = () => CreateClient().GetEventsByContractIdAsync(
            new ContractId<MismatchedKindMarker>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, CommitState.NotCommitted, "Required field 'owner' not found in record.");
    }

    [Fact]
    public async Task GetContractAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_created_event_has_an_empty_contract_id()
    {
        StubGetContract(Created("", LedgerClientTestFixtures.OwnerArgumentsFor("alice::ns1")));

        var act = () => CreateClient().GetContractAsync(new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodableContractId(act);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_created_event_has_a_whitespace_contract_id()
    {
        StubEventsByContractId(new GetEventsByContractIdResponse
        {
            Created = new Created { CreatedEvent = CreatedEvent("  ", offset: 5), SynchronizerId = "sync::a" },
        });

        var act = () => CreateClient().GetEventsByContractIdAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodableContractId(act);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_archived_event_has_an_empty_contract_id()
    {
        StubEventsByContractId(new GetEventsByContractIdResponse
        {
            Archived = new Archived
            {
                ArchivedEvent = new Com.Daml.Ledger.Api.V2.ArchivedEvent { ContractId = "", TemplateId = FooBarTemplate, Offset = 5 },
                SynchronizerId = "sync::a",
            },
        });

        var act = () => CreateClient().GetEventsByContractIdAsync(
            new ContractId<FooBar>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodableContractId(act);
    }

    private void StubPrepareSubmission(Interactive.CostEstimation estimation) =>
        _interactiveService
            .PrepareSubmissionAsync(Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new Interactive.PrepareSubmissionResponse
            {
                PreparedTransaction = new Interactive.PreparedTransaction(),
                CostEstimation = estimation,
            }));

    private void StubGetContract(Com.Daml.Ledger.Api.V2.CreatedEvent created) =>
        _contractService
            .GetContractAsync(Arg.Any<GetContractRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetContractResponse { CreatedEvent = created }));

    private static Com.Daml.Ledger.Api.V2.CreatedEvent Created(string contractId, Com.Daml.Ledger.Api.V2.Record arguments) =>
        new() { ContractId = contractId, TemplateId = FooBarTemplate, CreateArguments = arguments, Offset = 5 };

    private void StubEventsByContractId(GetEventsByContractIdResponse response) =>
        _eventQueryService
            .GetEventsByContractIdAsync(Arg.Any<GetEventsByContractIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(response));

    private static Com.Daml.Ledger.Api.V2.CreatedEvent CreatedEvent(string contractId, long offset) =>
        new()
        {
            ContractId = contractId,
            TemplateId = FooBarTemplate,
            CreateArguments = LedgerClientTestFixtures.OwnerArgumentsFor("alice::ns1"),
            Offset = offset,
        };

    private static async Task AssertUndecodableContractId<T>(Func<Task<T>> act)
    {
        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<ArgumentException>();
    }

    private static async Task AssertUndecodable<T>(Func<Task<T>> act, CommitState commitState, string detail)
    {
        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(commitState);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(detail);
        thrown.Message.Should().Be($"Malformed response from ledger: {detail}");
    }

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("test-cmd"));

    private static SignedSubmission Signed() =>
        new(
            new PreparedSubmission(
                new Interactive.PreparedTransaction { Metadata = new Interactive.Metadata { TransactionUuid = "uuid-1" } }
                    .ToByteArray(),
                new byte[] { 9, 9 },
                HashingSchemeVersion.V3,
                null,
                null),
            [
                new PartySignatures(
                    Alice,
                    [new LedgerSignature(SignatureFormat.Der, new byte[] { 1, 2, 3 }, "fp-1", SigningAlgorithm.EcDsaSha256)]),
            ],
            "sub-1",
            null,
            null);

    private static AsyncUnaryCall<T> Answered<T>(T response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
