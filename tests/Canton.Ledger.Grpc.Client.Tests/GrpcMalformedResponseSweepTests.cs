// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Com.Daml.Ledger.Api.V2;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using XunitRecord = Xunit.Record;
using Xunit;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoRecordField = Com.Daml.Ledger.Api.V2.RecordField;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcMalformedResponseSweepTests : MalformedResponseSweepTests
{
    private static readonly ProtoIdentifier FooBarTemplate = new()
    {
        PackageId = "test-pkg",
        ModuleName = "Sample.Foo",
        EntityName = "FooBar",
    };

    protected override IReadOnlyDictionary<(string EntryPoint, MalformedField Field), string> KnownDefects { get; } =
        new Dictionary<(string EntryPoint, MalformedField Field), string>();

    protected override string? NotExpressibleOnTheWire(string entryPoint, MalformedField field) => field switch
    {
        MalformedField.OffsetNonNumeric => "gRPC carries an offset as an int64, which is always a number",
        MalformedField.BodyUnreadable => "a protobuf message that reached the client is always well-formed",
        MalformedField.ContractIdMissing => "an unset proto3 string is the empty string, covered by ContractIdEmpty",
        _ => null,
    };

    protected override Task InvokeAgainstMalformed(string entryPoint, MalformedField field)
    {
        var clients = GrpcClientHarness.CreateClients(CannedResponseCallInvoker.Answering(ResponseMalformedIn(entryPoint, field)));
        return GrpcClientHarness.Invoke(clients, entryPoint);
    }

    protected override Task<ExerciseOutcome<TransactionResult>> TrySubmitTransactionMalformedIn(MalformedField field) =>
        TrySubmit(new SubmitAndWaitForTransactionResponse { Transaction = TransactionMalformedIn(field) });

    protected override Task<ExerciseOutcome<TransactionResult>> TrySubmitWithoutTransaction() =>
        TrySubmit(new SubmitAndWaitForTransactionResponse());

    protected override Exception EscapingPointRead(Exception decodeFailure)
    {
        var response = new GetUpdateResponse { Transaction = TransactionMalformedIn(MalformedField.ContractIdEmpty) };

        return XunitRecord.Exception(() => LedgerClient.ProjectPointRead<int>(
            response, "offset 42", _ => throw decodeFailure))!;
    }

    private static Task<ExerciseOutcome<TransactionResult>> TrySubmit(SubmitAndWaitForTransactionResponse response)
    {
        var clients = GrpcClientHarness.CreateClients(CannedResponseCallInvoker.Answering(response));
        return clients.Ledger.TrySubmitAndWaitForTransactionAsync(
            EntryPointInvocations.Submission(), cancellationToken: TestContext.Current.CancellationToken);
    }

    private static IMessage ResponseMalformedIn(string entryPoint, MalformedField field) => (entryPoint, field) switch
    {
        ("GetLedgerEndAsync(2)", MalformedField.OffsetNegative) => new GetLedgerEndResponse { Offset = -1 },
        ("GetLatestPrunedOffsetsAsync(2)", MalformedField.OffsetNegative) =>
            new GetLatestPrunedOffsetsResponse { ParticipantPrunedUpToInclusive = -1 },
        ("GetActiveContractsPageAsync(7)", MalformedField.OffsetNegative) =>
            new GetActiveContractsPageResponse { ActiveAtOffset = -1 },
        ("SubmitAndWaitAsync(4)", MalformedField.OffsetNegative) =>
            new SubmitAndWaitResponse { UpdateId = "u-1", CompletionOffset = -1 },
        ("ExecuteSubmissionAndWaitAsync(3)", MalformedField.OffsetNegative) =>
            new Interactive.ExecuteSubmissionAndWaitResponse { UpdateId = "u-1", CompletionOffset = -1 },

        ("GetUpdateByOffsetAsync(4)" or "GetUpdateByIdAsync(4)" or "GetUpdateTreeByOffsetAsync(4)", _) =>
            new GetUpdateResponse { Transaction = TransactionMalformedIn(field) },
        ("ExecuteSubmissionAndWaitForTransactionAsync(4)", MalformedField.ResultMissing) =>
            new Interactive.ExecuteSubmissionAndWaitForTransactionResponse(),
        ("ExecuteSubmissionAndWaitForTransactionAsync(4)", _) =>
            new Interactive.ExecuteSubmissionAndWaitForTransactionResponse { Transaction = TransactionMalformedIn(field) },

        ("GetContractAsync(4)", MalformedField.ResultMissing) => new GetContractResponse(),
        ("GetContractAsync(4)", _) => new GetContractResponse { CreatedEvent = CreatedEventMalformedIn(field) },
        ("GetEventsByContractIdAsync(4)", MalformedField.ArchivedContractIdEmpty) =>
            ArchivedWith(contractId: string.Empty, offset: 5),
        ("GetEventsByContractIdAsync(4)", MalformedField.ArchivedOffsetNegative) =>
            ArchivedWith(contractId: "00abc", offset: -1),
        ("GetEventsByContractIdAsync(4)", _) => new GetEventsByContractIdResponse
        {
            Created = new Created { CreatedEvent = CreatedEventMalformedIn(field), SynchronizerId = "sync::a" },
        },

        ("PrepareSubmissionAsync(3)", MalformedField.ResultMissing) => new Interactive.PrepareSubmissionResponse(),
        ("PrepareSubmissionAsync(3)" or "EstimateTrafficCostAsync(3)", MalformedField.TimestampOutOfRange) =>
            PreparedWith(new Interactive.CostEstimation { EstimationTimestamp = new Timestamp { Seconds = long.MaxValue } }),
        ("EstimateTrafficCostAsync(3)", MalformedField.CostOutOfRange) =>
            PreparedWith(new Interactive.CostEstimation { TotalTrafficCostEstimation = ulong.MaxValue }),

        ("GetPreferredPackagesAsync(5)", MalformedField.SynchronizerIdMissing) =>
            new Interactive.GetPreferredPackagesResponse(),
        ("GetPreferredPackageVersionAsync(6)", MalformedField.SynchronizerIdMissing) =>
            PreferenceOf(new Interactive.PackagePreference { PackageReference = PackageReference() }),
        ("GetPreferredPackageVersionAsync(6)", MalformedField.PackageReferenceMissing) =>
            PreferenceOf(new Interactive.PackagePreference { SynchronizerId = "sync::a" }),

        _ => throw new NotSupportedException($"No gRPC response is built for {entryPoint} with {field}."),
    };

    private static Transaction TransactionMalformedIn(MalformedField field)
    {
        var transaction = new Transaction
        {
            UpdateId = "u-1",
            CommandId = field == MalformedField.CommandIdWhitespace ? "   " : "cmd-1",
            Offset = field == MalformedField.OffsetNegative ? -1L : 42L,
        };

        if (field == MalformedField.ActingPartyEmpty)
        {
            var exercised = new ProtoExercisedEvent
            {
                NodeId = 0,
                ContractId = "00aa",
                TemplateId = FooBarTemplate,
                Choice = "Accept",
                ChoiceArgument = new ProtoValue { Unit = new Empty() },
                ExerciseResult = new ProtoValue { Unit = new Empty() },
                LastDescendantNodeId = 0,
            };
            exercised.ActingParties.Add(string.Empty);
            transaction.Events.Add(new Event { Exercised = exercised });
        }

        return transaction;
    }

    private static ProtoCreatedEvent CreatedEventMalformedIn(MalformedField field) => new()
    {
        ContractId = field switch
        {
            MalformedField.ContractIdEmpty => string.Empty,
            MalformedField.ContractIdWhitespace => "  ",
            _ => "00abc",
        },
        TemplateId = field == MalformedField.TemplateIdMissing ? null : FooBarTemplate,
        CreateArguments = field switch
        {
            MalformedField.CreateArgumentFieldMissing => RecordWith("other", new ProtoValue { Party = "alice::ns1" }),
            MalformedField.CreateArgumentWrongShape => RecordWith("owner", new ProtoValue { Text = "alice" }),
            _ => LedgerClientTestFixtures.OwnerArgumentsFor("alice::ns1"),
        },
        Offset = field == MalformedField.OffsetNegative ? -1 : 5,
    };

    private static GetEventsByContractIdResponse ArchivedWith(string contractId, long offset) => new()
    {
        Archived = new Archived
        {
            ArchivedEvent = new Com.Daml.Ledger.Api.V2.ArchivedEvent
            {
                ContractId = contractId,
                TemplateId = FooBarTemplate,
                Offset = offset,
            },
            SynchronizerId = "sync::a",
        },
    };

    private static ProtoRecord RecordWith(string label, ProtoValue value) =>
        new() { Fields = { new ProtoRecordField { Label = label, Value = value } } };

    private static Interactive.PrepareSubmissionResponse PreparedWith(Interactive.CostEstimation estimation) => new()
    {
        PreparedTransaction = new Interactive.PreparedTransaction(),
        CostEstimation = estimation,
    };

    private static Interactive.GetPreferredPackageVersionResponse PreferenceOf(Interactive.PackagePreference preference) =>
        new() { PackagePreference = preference };

    private static PackageReference PackageReference() =>
        new() { PackageId = "pkg-1", PackageName = "pkg", PackageVersion = "1.0.0" };
}
