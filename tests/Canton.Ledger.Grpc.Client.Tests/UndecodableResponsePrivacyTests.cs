// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection("LedgerClient global ActivitySource")]
public sealed class UndecodableResponsePrivacyTests : IDisposable
{
    private const string ContractId = "00contract-that-names-a-party";

    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
    private readonly CapturingLoggerFactory _loggerFactory = new();

    public void Dispose() => _loggerFactory.Dispose();

    private SubmissionClient ClientAnswering(CannedResponseCallInvoker invoker) =>
        new(
            new LedgerCallInvoker(_options, new StaticTokenProvider("test-token")),
            new CommandService.CommandServiceClient(invoker),
            new CommandSubmissionService.CommandSubmissionServiceClient(invoker),
            new GrpcCommandBuilder(_options),
            _options,
            new Logger<LedgerClient>(_loggerFactory),
            (_, _, _, _) => throw new InvalidOperationException("point read not expected"));

    private static CannedResponseCallInvoker ExerciseWithoutResult()
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
            },
        });
        return CannedResponseCallInvoker.Answering(new SubmitAndWaitForTransactionResponse { Transaction = transaction });
    }

    private static RuntimeCommands.ExerciseCommand Exercise() =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<LedgerClientTests.TestTemplate>(ContractId),
            new RuntimeCommands.ChoiceName("Archive"),
            DamlUnit.Instance);

    private static RuntimeCommands.SubmitterInfo Submitter() => new(new HashSet<Party> { new("party::alice") });

    private (string Message, Exception? Exception) UndecodableErrorLog() =>
        _loggerFactory.Records
            .Where(r => r.Level == LogLevel.Error && r.Message.StartsWith("SubmitAndWaitForTransaction succeeded", StringComparison.Ordinal))
            .Select(r => (r.Message, r.Exception))
            .Single();

    [Fact]
    public async Task An_undecodable_transaction_logs_the_exception_type_without_its_contract_id_by_default()
    {
        var client = ClientAnswering(ExerciseWithoutResult());

        await client.TryExerciseAsync<object>(Exercise(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        UndecodableErrorLog().Exception!.Message.Should().Be("Canton.Ledger.Abstractions.MalformedResponseException");
        _loggerFactory.Records.Should().NotContain(r => r.Exception != null && r.Exception.ToString().Contains(ContractId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_undecodable_transaction_logs_the_original_exception_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        var client = ClientAnswering(ExerciseWithoutResult());

        await client.TryExerciseAsync<object>(Exercise(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        UndecodableErrorLog().Exception.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Contain($"ExercisedEvent for contract '{ContractId}' has no exercise_result");
    }

    [Fact]
    public async Task An_undecodable_transaction_sets_the_span_status_to_the_error_type_by_default()
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);
        var client = ClientAnswering(ExerciseWithoutResult());

        await client.TryExerciseAsync<object>(Exercise(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("CommittedUndecodable");
    }

    [Fact]
    public async Task An_undecodable_transaction_puts_its_message_on_the_span_status_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);
        var client = ClientAnswering(ExerciseWithoutResult());

        await client.TryExerciseAsync<object>(Exercise(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        capture.Activities.Should().ContainSingle().Subject.StatusDescription.Should().Be(
            $"The command committed, but its transaction could not be decoded: Malformed response from ledger: ExercisedEvent for contract '{ContractId}' has no exercise_result, though the Ledger API marks the field as required.");
    }

    private async Task SubmitReassignmentTheLedgerAnswersWithNothing()
    {
        var client = ClientAnswering(CannedResponseCallInvoker.Answering(new SubmitAndWaitForReassignmentResponse()));
        var submission = ReassignmentSubmission.Of(
            new Canton.Ledger.Abstractions.UnassignCommand(ContractId, new SynchronizerId("sync::source"), new SynchronizerId("sync::target")),
            new Party("party::alice"));

        await client.TrySubmitAndWaitForReassignmentAsync<LedgerClientTests.TestTemplate>(
            submission, cancellationToken: TestContext.Current.CancellationToken);
    }

    private Exception? ReassignmentUndecodableLoggedException() =>
        _loggerFactory.Records
            .Where(r => r.Level == LogLevel.Error && r.Message.StartsWith("SubmitAndWaitForReassignment succeeded", StringComparison.Ordinal))
            .Select(r => r.Exception)
            .Single();

    [Fact]
    public async Task An_undecodable_reassignment_logs_the_exception_type_without_its_message_by_default()
    {
        await SubmitReassignmentTheLedgerAnswersWithNothing();

        ReassignmentUndecodableLoggedException()!.Message.Should().Be("System.NullReferenceException");
    }

    [Fact]
    public async Task An_undecodable_reassignment_logs_the_original_exception_when_party_and_contract_tags_are_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await SubmitReassignmentTheLedgerAnswersWithNothing();

        ReassignmentUndecodableLoggedException().Should().BeOfType<NullReferenceException>();
    }
}
