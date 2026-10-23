// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Grpc.Core;
using Canton.Ledger.Testing.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class SubmissionClientTests
{
    private static readonly Party ActAs = new("party::alice");
    private static readonly RuntimeCommands.CommandId TestCommandId = new("test-cmd");
    private static readonly ProtoIdentifier TestTemplateId = new()
    {
        PackageId = "pkg",
        ModuleName = "Module",
        EntityName = "Template",
    };

    private readonly LedgerClientOptions _options;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _commandSubmissionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public SubmissionClientTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _commandSubmissionService = Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
    }

    private SubmissionClient CreateClient(
        Func<long, RuntimeCommands.SubmitterInfo, IReadOnlyList<DamlTypeDescriptor>, CancellationToken, Task<Transaction>>? readTransactionAtOffset = null,
        ILogger? logger = null) =>
        new(
            new LedgerCallInvoker(_options, _tokenProvider),
            _commandService,
            _commandSubmissionService,
            new GrpcCommandBuilder(_options),
            _options,
            logger ?? NullLogger<LedgerClient>.Instance,
            readTransactionAtOffset ?? ((_, _, _, _) => throw new InvalidOperationException("point read not expected")));

    private static RuntimeCommands.CommandsSubmission Create() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(ActAs)
            .WithCommandId(TestCommandId);

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_resolves_a_retried_duplicate_through_the_injected_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));

        var resolved = new Transaction { UpdateId = "u-original", CommandId = "test-cmd", Offset = 42L };
        long? readOffset = null;
        var client = CreateClient(readTransactionAtOffset: (offset, _, _, _) =>
        {
            readOffset = offset;
            return Task.FromResult(resolved);
        });

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "a duplicate on a retried attempt proves the first attempt committed").Subject;
        success.Result.UpdateId.Should().Be("u-original");
        readOffset.Should().Be(42L,
            "the completion_offset carried on the duplicate rejection is resolved via the injected point read");
    }

    private async Task<(string Category, LogLevel Level, string Message, Exception? Exception)> WarningOfAnUnprojectableRetriedDuplicate()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        using var logs = new CapturingLoggerFactory();
        var client = CreateClient(
            readTransactionAtOffset: (_, _, _, _) => throw new InvalidOperationException("cannot read a transaction naming party::alice"),
            logger: new Logger<LedgerClient>(logs));

        await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        return logs.Records.Single(r => r.Level == LogLevel.Warning && r.Message.Contains("could not be projected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_logs_an_unprojectable_retried_duplicate_without_its_failure_message_by_default()
    {
        var warning = await WarningOfAnUnprojectableRetriedDuplicate();

        warning.Message.Should().Be(
            "Retried submission test-cmd was rejected as DUPLICATE_COMMAND — the original submission succeeded — but the update at completion offset 42 could not be projected as a transaction; surfacing the DUPLICATE_COMMAND error");
        warning.Exception.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_logs_the_failure_message_of_an_unprojectable_retried_duplicate_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        var warning = await WarningOfAnUnprojectableRetriedDuplicate();

        warning.Message.Should().Be(
            "Retried submission test-cmd was rejected as DUPLICATE_COMMAND — the original submission succeeded — but the update at completion offset 42 could not be projected as a transaction (cannot read a transaction naming party::alice); surfacing the DUPLICATE_COMMAND error");
        warning.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_a_first_attempt_duplicate_without_touching_the_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        var pointReadInvoked = false;
        var client = CreateClient(readTransactionAtOffset: (_, _, _, _) =>
        {
            pointReadInvoked = true;
            return Task.FromException<Transaction>(new InvalidOperationException("unexpected"));
        });

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        pointReadInvoked.Should().BeFalse("a first-attempt duplicate is a genuine caller error, not a lost success");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_a_retried_duplicate_the_participant_did_not_accept_without_touching_the_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(LedgerClientTestFixtures.MakeDamlRpcException(
                "DUPLICATE_COMMAND",
                "duplicate",
                "InvalidGivenCurrentSystemStateResourceExists",
                StatusCode.AlreadyExists,
                new Dictionary<string, string> { ["accepted"] = "false", ["completion_offset"] = "42" })));
        var pointReadInvoked = false;
        var client = CreateClient(readTransactionAtOffset: (_, _, _, _) =>
        {
            pointReadInvoked = true;
            return Task.FromException<Transaction>(new InvalidOperationException("unexpected"));
        });

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        pointReadInvoked.Should().BeFalse(
            "a duplicate whose accepted metadata is \"false\" does not prove the first attempt committed");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_resolves_a_retried_duplicate_through_the_injected_tree_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));

        var resolved = new Transaction { UpdateId = "u-original", CommandId = "test-cmd", Offset = 42L };
        long? readOffset = null;
        var client = CreateClient(readTransactionAtOffset: (offset, _, _, _) =>
        {
            readOffset = offset;
            return Task.FromResult(resolved);
        });

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            Create(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.One>(
            "a duplicate on a retried attempt proves the first attempt committed").Subject;
        success.Result.UpdateId.Should().Be("u-original");
        readOffset.Should().Be(42L,
            "the tree path resolves the completion_offset through the same point read");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_keeps_a_retried_duplicate_as_a_DamlError_when_the_transaction_at_the_offset_is_another_commands()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        var client = CreateClient(readTransactionAtOffset: (_, _, _, _) =>
            Task.FromResult(new Transaction { UpdateId = "u-other", CommandId = "another-cmd", Offset = 42L }));

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            Create(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_counts_a_consuming_exercise_as_an_archive()
    {
        var loggerFactory = new CapturingLoggerFactory();
        StubSubmitAndWaitForTransaction(Completed(LedgerEffectsTransaction()));

        var client = CreateClient(logger: new Logger<LedgerClient>(loggerFactory));
        await client.TrySubmitAndWaitForTransactionTreeAsync(
            Create(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        loggerFactory.Records.Should().Contain(
            r => r.Message.Contains("Created: 1, Archived: 1", StringComparison.Ordinal),
            "a ledger-effects transaction reports consumption as a consuming exercise, never as an ArchivedEvent");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_counts_an_archived_event_as_an_archive()
    {
        var loggerFactory = new CapturingLoggerFactory();
        StubSubmitAndWaitForTransaction(Completed(AcsDeltaTransaction()));

        var client = CreateClient(logger: new Logger<LedgerClient>(loggerFactory));
        await client.TrySubmitAndWaitForTransactionAsync(
            Create(), cancellationToken: TestContext.Current.CancellationToken);

        loggerFactory.Records.Should().Contain(
            r => r.Message.Contains("Created: 1, Archived: 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_requests_the_ledger_effects_shape_filtered_to_every_submitting_party()
    {
        StubSubmitAndWaitForTransaction(Completed(LedgerEffectsTransaction()));
        var client = CreateClient();

        await client.TrySubmitAndWaitForTransactionAsync(
            Create().WithReadAs(new Party("party::bob")), cancellationToken: TestContext.Current.CancellationToken);

        var format = SentRequest().TransactionFormat;
        format.TransactionShape.Should().Be(TransactionShape.LedgerEffects);
        format.EventFormat.Verbose.Should().BeTrue();
        format.EventFormat.FiltersByParty.Keys.Should().BeEquivalentTo("party::alice", "party::bob");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_surfaces_the_exercised_events_of_a_nonconsuming_choice()
    {
        StubSubmitAndWaitForTransaction(Completed(LedgerEffectsTransaction()));
        var client = CreateClient();

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result;
        result.ExercisedEvents.Select(e => e.ChoiceName.Value).Should().Equal("Consume", "Peek");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_reports_only_the_events_the_wire_flags_as_acs_delta()
    {
        StubSubmitAndWaitForTransaction(Completed(LedgerEffectsTransaction()));
        var client = CreateClient();

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result;
        result.CreatedContracts.Select(c => c.ContractId).Should().Equal("00created");
        result.ArchivedContractIds.Should().Equal("00target");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_resolves_a_retried_duplicate_to_the_acs_delta_meaning()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        var pointRead = LedgerEffectsTransactionCreatingAndConsuming("00transient");
        var client = CreateClient(readTransactionAtOffset: (_, _, _, _) => Task.FromResult(pointRead));

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result;
        result.CreatedContracts.Should().BeEmpty();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_omits_the_events_the_wire_does_not_flag_as_acs_delta()
    {
        StubSubmitAndWaitForTransaction(Completed(WitnessedTransaction()));
        var client = CreateClient();

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result;
        result.CreatedContracts.Select(c => c.ContractId).Should().Equal("00held");
        result.ArchivedContractIds.Should().Equal("00earlier");
    }

    private static readonly DamlTypeDescriptor GadgetInterface = new(
        new RuntimeIdentifier("gadget-pkg-id", "Acme.Gadget", "IGadget"), DamlTypeKind.Interface, "gadget-package");

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_asks_for_the_requested_interface_views_beside_the_wildcard()
    {
        StubSubmitAndWaitForTransaction(Completed(LedgerEffectsTransaction()));
        var client = CreateClient();

        await client.TrySubmitAndWaitForTransactionAsync(
            Create() with { InterfaceViewRequests = [GadgetInterface] },
            cancellationToken: TestContext.Current.CancellationToken);

        var cumulative = SentRequest().TransactionFormat.EventFormat.FiltersByParty["party::alice"].Cumulative;
        cumulative.Should().HaveCount(2);
        cumulative[0].WildcardFilter.Should().NotBeNull();
        cumulative[1].InterfaceFilter.InterfaceId.PackageId.Should().Be("#gadget-package");
        cumulative[1].InterfaceFilter.IncludeInterfaceView.Should().BeTrue();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_records_the_requested_interface_on_the_result()
    {
        StubSubmitAndWaitForTransaction(Completed(LedgerEffectsTransaction()));
        var client = CreateClient();

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create() with { InterfaceViewRequests = [GadgetInterface] },
            cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result;
        result.RequestedInterfaces.Should().Equal(new RuntimeIdentifier("gadget-pkg-id", "Acme.Gadget", "IGadget"));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_records_no_requested_interface_without_a_request()
    {
        StubSubmitAndWaitForTransaction(Completed(LedgerEffectsTransaction()));
        var client = CreateClient();

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result
            .RequestedInterfaces.Should().BeEmpty();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_reads_a_retried_duplicate_with_the_requested_interface_views()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        IReadOnlyList<DamlTypeDescriptor>? readInterfaces = null;
        var client = CreateClient(readTransactionAtOffset: (_, _, interfaces, _) =>
        {
            readInterfaces = interfaces;
            return Task.FromResult(LedgerEffectsTransactionCreatingAndConsuming("00transient"));
        });

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create() with { InterfaceViewRequests = [GadgetInterface] },
            cancellationToken: TestContext.Current.CancellationToken);

        readInterfaces.Should().Equal(GadgetInterface);
        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result
            .RequestedInterfaces.Should().Equal(new RuntimeIdentifier("gadget-pkg-id", "Acme.Gadget", "IGadget"));
    }

    private SubmitAndWaitForTransactionRequest SentRequest() =>
        _commandService.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(CommandService.CommandServiceClient.SubmitAndWaitForTransactionAsync))
            .Select(call => (SubmitAndWaitForTransactionRequest)call.GetArguments()[0]!)
            .Last();

    private static Transaction LedgerEffectsTransactionCreatingAndConsuming(string contractId)
    {
        var transaction = new Transaction { UpdateId = "u-effects", CommandId = "test-cmd", Offset = 42L };
        transaction.Events.Add(Created(nodeId: 0, contractId));
        transaction.Events.Add(ExercisedOn(nodeId: 1, contractId, "Consume", consuming: true));
        return transaction;
    }

    private static RuntimeCommands.SubmitterInfo Submitter() => new(new HashSet<Party> { ActAs });

    private static Transaction LedgerEffectsTransaction()
    {
        var transaction = new Transaction { UpdateId = "u-effects", Offset = 42L };
        transaction.Events.Add(Exercised(nodeId: 0, lastDescendantNodeId: 1, "Consume", consuming: true, acsDelta: true));
        transaction.Events.Add(Created(nodeId: 1, "00created", acsDelta: true));
        transaction.Events.Add(Exercised(nodeId: 2, lastDescendantNodeId: 2, "Peek", consuming: false));
        return transaction;
    }

    private static Transaction WitnessedTransaction()
    {
        var transaction = new Transaction { UpdateId = "u-witnessed", Offset = 42L };
        transaction.Events.Add(Created(nodeId: 0, "00witnessed"));
        transaction.Events.Add(Created(nodeId: 1, "00held", acsDelta: true));
        transaction.Events.Add(ExercisedOn(nodeId: 2, "00witnessed-target", "Retire", consuming: true));
        transaction.Events.Add(ExercisedOn(nodeId: 3, "00earlier", "Retire", consuming: true, acsDelta: true));
        return transaction;
    }

    private static Transaction AcsDeltaTransaction()
    {
        var transaction = new Transaction { UpdateId = "u-delta", Offset = 42L };
        transaction.Events.Add(Created(nodeId: 0, "00created"));
        transaction.Events.Add(new Event
        {
            Archived = new ProtoArchivedEvent
            {
                NodeId = 1,
                ContractId = "00archived",
                TemplateId = TestTemplateId,
            },
        });
        return transaction;
    }

    private static Event Created(int nodeId, string contractId, bool acsDelta = false) => new()
    {
        Created = new ProtoCreatedEvent
        {
            AcsDelta = acsDelta,
            NodeId = nodeId,
            ContractId = contractId,
            TemplateId = TestTemplateId,
            CreateArguments = new ProtoRecord(),
        },
    };

    private static Event Exercised(
        int nodeId, int lastDescendantNodeId, string choice, bool consuming, bool acsDelta = false) =>
        ExercisedOn(nodeId, "00target", choice, consuming, lastDescendantNodeId, acsDelta);

    private static Event ExercisedOn(
        int nodeId, string contractId, string choice, bool consuming, int? lastDescendantNodeId = null, bool acsDelta = false) => new()
    {
        Exercised = new ProtoExercisedEvent
        {
            AcsDelta = acsDelta,
            NodeId = nodeId,
            LastDescendantNodeId = lastDescendantNodeId ?? nodeId,
            ContractId = contractId,
            TemplateId = TestTemplateId,
            Choice = choice,
            ChoiceArgument = new Com.Daml.Ledger.Api.V2.Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            ExerciseResult = new Com.Daml.Ledger.Api.V2.Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            Consuming = consuming,
        },
    };

    private static AsyncUnaryCall<SubmitAndWaitForTransactionResponse> Completed(Transaction transaction) =>
        new(
            Task.FromResult(new SubmitAndWaitForTransactionResponse { Transaction = transaction }),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private void StubSubmitAndWaitForTransaction(params AsyncUnaryCall<SubmitAndWaitForTransactionResponse>[] calls) =>
        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Any<SubmitAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(calls[0], calls[1..]);

    private static RpcException Unavailable() => new(new Status(StatusCode.Unavailable, "transient down"));

    private static RpcException DuplicateCommand(long? completionOffset) =>
        LedgerClientTestFixtures.MakeDamlRpcException(
            "DUPLICATE_COMMAND",
            "duplicate",
            "InvalidGivenCurrentSystemStateResourceExists",
            StatusCode.AlreadyExists,
            completionOffset is { } offset
                ? new Dictionary<string, string> { ["completion_offset"] = offset.ToString(CultureInfo.InvariantCulture) }
                : null);

    private static AsyncUnaryCall<T> Faulted<T>(RpcException exception) =>
        new(
            Task.FromException<T>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });
}
