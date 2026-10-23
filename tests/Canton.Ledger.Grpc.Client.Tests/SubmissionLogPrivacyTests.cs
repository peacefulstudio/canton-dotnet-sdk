// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class SubmissionLogPrivacyTests : IDisposable
{
    private const string ContractId = "00contract-that-names-a-party";

    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly CapturingLoggerFactory _loggerFactory = new();
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public void Dispose() => _loggerFactory.Dispose();

    private SubmissionClient ClientAnswering(CallInvoker invoker) =>
        new(
            new LedgerCallInvoker(_options, _tokenProvider),
            new CommandService.CommandServiceClient(invoker),
            new CommandSubmissionService.CommandSubmissionServiceClient(invoker),
            new GrpcCommandBuilder(_options),
            _options,
            new Logger<LedgerClient>(_loggerFactory),
            (_, _, _, _) => throw new InvalidOperationException("point read not expected"));

    private IEnumerable<string> MessagesAtOrAbove(LogLevel level) =>
        _loggerFactory.Records.Where(r => r.Level >= level).Select(r => r.Message);

    private static RuntimeCommands.ExerciseCommand Exercise() =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<LedgerClientTests.TestTemplate>(ContractId),
            new RuntimeCommands.ChoiceName("Archive"),
            DamlUnit.Instance);

    private static RuntimeCommands.SubmitterInfo Submitter() => new(new HashSet<Party> { ActAs });

    private static CannedResponseCallInvoker SuccessfulExercise()
    {
        var transaction = new Transaction { UpdateId = "u-1", Offset = 1L };
        transaction.Events.Add(new Event
        {
            Exercised = new Com.Daml.Ledger.Api.V2.ExercisedEvent
            {
                ContractId = ContractId,
                TemplateId = new Com.Daml.Ledger.Api.V2.Identifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Archive",
                ChoiceArgument = new Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                ExerciseResult = new Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            },
        });
        return CannedResponseCallInvoker.Answering(new SubmitAndWaitForTransactionResponse { Transaction = transaction });
    }

    private IEnumerable<string> DebugChoiceMessages() =>
        _loggerFactory.Records
            .Where(r => r.Level == LogLevel.Debug && (r.Message.StartsWith("Exercising choice", StringComparison.Ordinal) || r.Message.StartsWith("Choice exercised", StringComparison.Ordinal)))
            .Select(r => r.Message);

    [Fact]
    public async Task A_successful_exercise_logs_no_contract_id_by_default()
    {
        var client = ClientAnswering(SuccessfulExercise());

        var outcome = await client.TryExerciseAsync<object>(
            Exercise(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<Daml.Runtime.Outcomes.ExerciseOutcome<object>.One>();
        _loggerFactory.Records.Should().NotContain(r => r.Message.Contains(ContractId, StringComparison.Ordinal));
        _loggerFactory.Records.Should().Contain(r =>
            r.Level == LogLevel.Information && r.Message == "Choice exercised: Archive");
        DebugChoiceMessages().Should().Equal("Exercising choice Archive");
    }

    [Fact]
    public async Task A_successful_exercise_logs_its_contract_id_below_Information_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        var client = ClientAnswering(SuccessfulExercise());

        await client.TryExerciseAsync<object>(
            Exercise(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        MessagesAtOrAbove(LogLevel.Information).Should().NotContain(m => m.Contains(ContractId, StringComparison.Ordinal));
        DebugChoiceMessages().Should().Equal(
            $"Exercising choice Archive on {ContractId}",
            $"Choice exercised: Archive on {ContractId}");
    }

    [Fact]
    public async Task A_failed_exercise_logs_its_contract_id_only_below_Information()
    {
        var client = ClientAnswering(new RejectingCallInvoker(new RpcException(new Status(StatusCode.Unavailable, "down"))));

        await client.TryExerciseAsync<object>(
            Exercise(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        MessagesAtOrAbove(LogLevel.Information).Should().NotContain(m => m.Contains(ContractId, StringComparison.Ordinal));
        _loggerFactory.Records.Should().Contain(r =>
            r.Level == LogLevel.Warning && r.Message == "Failed to exercise choice Archive");
    }

    [Fact]
    public async Task A_failed_submit_logs_only_the_status_code_by_default()
    {
        var detail = $"party alice::1220{Guid.NewGuid():N} is unknown";
        var client = ClientAnswering(new RejectingCallInvoker(new RpcException(new Status(StatusCode.Unavailable, detail))));

        await client.TrySubmitAndWaitForTransactionAsync(
            RuntimeCommands.CommandsSubmission
                .Single(Exercise())
                .WithActAs(ActAs),
            cancellationToken: TestContext.Current.CancellationToken);

        MessagesAtOrAbove(LogLevel.Information).Should().NotContain(m => m.Contains(detail, StringComparison.Ordinal));
        _loggerFactory.Records.Should().Contain(r =>
            r.Level == LogLevel.Warning && r.Message == "Submit failed: Unavailable");
    }

    [Fact]
    public async Task A_failed_submit_logs_the_status_detail_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        var detail = $"party alice::1220{Guid.NewGuid():N} is unknown";
        var client = ClientAnswering(new RejectingCallInvoker(new RpcException(new Status(StatusCode.Unavailable, detail))));

        await client.TrySubmitAndWaitForTransactionAsync(
            RuntimeCommands.CommandsSubmission
                .Single(Exercise())
                .WithActAs(ActAs),
            cancellationToken: TestContext.Current.CancellationToken);

        _loggerFactory.Records.Should().Contain(r =>
            r.Level == LogLevel.Warning && r.Message == $"Submit failed: Unavailable — {detail}");
    }
}
