// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Microsoft.Extensions.Options;
using Xunit;
using WireGetUpdateResponse = Canton.Ledger.Rest.Client.Raw.GetUpdateResponse;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;
using WireUpdate = Canton.Ledger.Rest.Client.Raw.Update;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestMalformedResponseSweepTests : MalformedResponseSweepTests, IDisposable
{
    private const string ContractCreatedEventTemplate =
        """
        {"offset": "42", "contractId": "00abc", "nodeId": 0,
         "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "SweepTemplate"},
         "createArgument": {"owner": "party::alice"}, "witnessParties": ["party::alice"]}
        """;

    [ModuleInitializer]
    internal static void RegisterHandWrittenTemplates() =>
        GeneratedTypeReaders.ForRecord<ThrowingReaderTemplate>();

    private static readonly byte[] PreparedTransaction = [0x0A, 0x03];

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    protected override IReadOnlyDictionary<(string EntryPoint, MalformedField Field), string> KnownDefects { get; } =
        new Dictionary<(string EntryPoint, MalformedField Field), string>();

    protected override string? NotExpressibleOnTheWire(string entryPoint, MalformedField field) =>
        field is not MalformedField.BodyUnreadable ? null : entryPoint switch
        {
            "DeleteUserAsync(3)" or "UpdateUserIdentityProviderIdAsync(4)" or "DeleteIdentityProviderConfigAsync(2)"
                or "UploadDarAsync(3)" or "UploadDarAsync(4)" =>
                "the participant answers this write with an empty body, which the client never reads",
            "ValidateDarAsync(2)" or "ValidateDarAsync(3)" =>
                "the participant answers a validation with an empty body, which the client never reads",
            "SubmitAsync(3)" or "SubmitReassignmentAsync(3)" or "ExecuteSubmissionAsync(3)" =>
                "the participant answers an accepted-only submission with an empty body, which the client never reads",
            "SetTimeAsync(3)" or "GetTimeAsync(1)" =>
                "the participant serves no time route over the JSON Ledger API, so the client raises NotSupportedException before any request",
            "GetCommandStatusAsync(4)" or "ListKnownPackagesAsync(1)" or "PruneAsync(4)" or "UpdatePartyIdentityProviderIdAsync(4)" =>
                "the participant serves no route for this member over the JSON Ledger API, so the client raises NotSupportedException before any request",
            _ => null,
        };

    protected override Task InvokeAgainstMalformed(string entryPoint, MalformedField field)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, BodyMalformedIn(entryPoint, field));
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        var ledger = new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
        var admin = new RestAdminClient(factory);

        return EntryPointInvocations.Invoke<SweepTemplate>(
            ledger, admin, entryPoint, PreparedTransaction, TestContext.Current.CancellationToken);
    }

    protected override Task<ExerciseOutcome<TransactionResult>> TrySubmitTransactionMalformedIn(MalformedField field) =>
        TrySubmit($$"""{"transaction": {{TransactionMalformedIn(field)}}}""");

    protected override Task<ExerciseOutcome<TransactionResult>> TrySubmitWithoutTransaction() => TrySubmit("{}");

    protected override Exception EscapingPointRead(Exception decodeFailure)
    {
        var response = new WireGetUpdateResponse
        {
            Update = new WireUpdate { Transaction = new WireTransaction { UpdateId = "u-1", Offset = "42" } },
        };

        return Record.Exception(() => RestLedgerClient.ProjectPointRead<int>(
            response, "offset 42", _ => throw decodeFailure))!;
    }

    [Fact]
    public async Task A_create_argument_reader_that_raises_something_other_than_a_decode_failure_is_reported_as_CommittedUndecodable()
    {
        var outcome = await TrySubmit(
            $$$"""
            {"transaction": {"updateId": "u-1", "commandId": "cmd-1", "offset": "42", "events": [
              {"CreatedEvent": {
                "offset": "42", "contractId": "00aa", "nodeId": 0, "acsDelta": true,
                "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "ThrowingReaderTemplate"},
                "createArgument": {"owner": "party::alice"},
                "witnessParties": ["party::alice"], "signatories": ["party::alice"], "observers": []}}]}}
            """);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("u-1");
        undecodable.SourceException.Should().BeOfType<KeyNotFoundException>();
    }

    private Task<ExerciseOutcome<TransactionResult>> TrySubmit(string responseBody)
    {
        var factory = new StubHttpClientFactory(new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, responseBody));
        _factories.Add(factory);
        var ledger = new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));

        return ledger.TrySubmitAndWaitForTransactionAsync(
            EntryPointInvocations.Submission(), cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string BodyMalformedIn(string entryPoint, MalformedField field) => (entryPoint, field) switch
    {
        (_, MalformedField.BodyUnreadable) => "{ not json",

        ("GetLedgerEndAsync(2)", MalformedField.OffsetNegative) => """{"offset": "-1"}""",
        ("GetLedgerEndAsync(2)", MalformedField.OffsetNonNumeric) => """{"offset": "not-an-offset"}""",
        ("GetLatestPrunedOffsetsAsync(2)", MalformedField.OffsetNegative) =>
            """{"participantPrunedUpToInclusive": "-1"}""",
        ("GetLatestPrunedOffsetsAsync(2)", MalformedField.OffsetNonNumeric) =>
            """{"participantPrunedUpToInclusive": "not-an-offset"}""",
        ("GetActiveContractsPageAsync(7)", MalformedField.OffsetNegative) =>
            """{"activeContracts": [], "activeAtOffset": "-1"}""",
        ("SubmitAndWaitAsync(4)" or "ExecuteSubmissionAndWaitAsync(3)", MalformedField.OffsetNegative) =>
            """{"updateId": "u-1", "completionOffset": "-1"}""",

        ("GetUpdateByOffsetAsync(4)" or "GetUpdateByIdAsync(4)" or "GetUpdateTreeByOffsetAsync(4)", _) =>
            UpdateResponseOf(TransactionMalformedIn(field)),
        ("ExecuteSubmissionAndWaitForTransactionAsync(4)", MalformedField.ResultMissing) => "{}",
        ("ExecuteSubmissionAndWaitForTransactionAsync(4)", _) =>
            $$"""{"transaction": {{TransactionMalformedIn(field)}}}""",

        ("GetContractAsync(4)", MalformedField.ResultMissing) => "{}",
        ("GetContractAsync(4)", _) => $$"""{"createdEvent": {{CreatedEventMalformedIn(field)}}}""",
        ("GetEventsByContractIdAsync(4)", MalformedField.ArchivedContractIdEmpty) =>
            ArchivedEvent(contractId: string.Empty, offset: "50"),
        ("GetEventsByContractIdAsync(4)", MalformedField.ArchivedOffsetNegative) =>
            ArchivedEvent(contractId: "00abc", offset: "-1"),
        ("GetEventsByContractIdAsync(4)", _) =>
            $$$"""{"created": {"createdEvent": {{{CreatedEventMalformedIn(field)}}}, "synchronizerId": "sync::a"}}""",

        ("PrepareSubmissionAsync(3)", MalformedField.ResultMissing) =>
            """{"preparedTransactionHash": "3q2+7w==", "hashingSchemeVersion": "HASHING_SCHEME_VERSION_V2"}""",
        ("PrepareSubmissionAsync(3)" or "EstimateTrafficCostAsync(3)", MalformedField.TimestampOutOfRange) =>
            PreparedWithCostEstimation("""{"estimationTimestamp": "99999-01-01T00:00:00Z"}"""),
        ("EstimateTrafficCostAsync(3)", MalformedField.CostOutOfRange) =>
            PreparedWithCostEstimation("""{"totalTrafficCostEstimation": "9223372036854775808"}"""),

        ("GetPreferredPackagesAsync(5)", MalformedField.SynchronizerIdMissing) => """{"packageReferences": []}""",
        ("GetPreferredPackageVersionAsync(6)", MalformedField.SynchronizerIdMissing) =>
            """{"packagePreference": {"packageReference": {"packageId": "pkg-1", "packageName": "pkg", "packageVersion": "1.0.0"}}}""",
        ("GetPreferredPackageVersionAsync(6)", MalformedField.PackageReferenceMissing) =>
            """{"packagePreference": {"synchronizerId": "sync::a"}}""",

        _ => throw new NotSupportedException($"No REST body is built for {entryPoint} with {field}."),
    };

    private static string TransactionMalformedIn(MalformedField field)
    {
        var commandId = field == MalformedField.CommandIdWhitespace ? "   " : "cmd-1";
        var offset = field == MalformedField.OffsetNonNumeric ? "not-an-offset"
            : field == MalformedField.OffsetNegative ? "-1"
            : "42";
        var events = field == MalformedField.ActingPartyEmpty
            ? """
              [{"ExercisedEvent": {
                "nodeId": 0, "contractId": "00aa",
                "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "SweepTemplate"},
                "choice": "Accept", "choiceArgument": {}, "exerciseResult": {},
                "actingParties": [""], "lastDescendantNodeId": 0}}]
              """
            : "[]";

        return $$"""
            {"updateId": "u-1", "commandId": "{{commandId}}", "offset": "{{offset}}", "events": {{events}}}
            """;
    }

    private static string CreatedEventMalformedIn(MalformedField field)
    {
        var contractId = field switch
        {
            MalformedField.ContractIdEmpty => """ "contractId": "", """,
            MalformedField.ContractIdWhitespace => """ "contractId": "  ", """,
            MalformedField.ContractIdMissing => string.Empty,
            _ => """ "contractId": "00abc", """,
        };
        var templateId = field == MalformedField.TemplateIdMissing
            ? string.Empty
            : """ "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "SweepTemplate"}, """;
        var createArgument = field switch
        {
            MalformedField.CreateArgumentFieldMissing => """{"other": "party::alice"}""",
            MalformedField.CreateArgumentWrongShape => """{"owner": 5}""",
            _ => """{"owner": "party::alice"}""",
        };
        var offset = field == MalformedField.OffsetNegative ? "-1" : "42";

        return $$"""
            {"offset": "{{offset}}",{{contractId}}"nodeId": 0,{{templateId}}
             "createArgument": {{createArgument}}, "witnessParties": ["party::alice"]}
            """;
    }

    private static string UpdateResponseOf(string transaction) =>
        $$$$"""
        {"update": {"Transaction": {"value": {{{{transaction}}}}
        }}}
        """;

    private static string ArchivedEvent(string contractId, string offset) =>
        $$$"""
        {"archived": {"archivedEvent": {"offset": "{{{offset}}}", "contractId": "{{{contractId}}}",
            "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "SweepTemplate"},
            "witnessParties": ["party::alice"]}, "synchronizerId": "sync::a"}}
        """;

    private static string PreparedWithCostEstimation(string costEstimation) =>
        $$"""
        {"preparedTransaction": "CgM=", "preparedTransactionHash": "3q2+7w==",
         "hashingSchemeVersion": "HASHING_SCHEME_VERSION_V2", "costEstimation": {{costEstimation}}}
        """;

    private sealed record SweepTemplate : ITemplate, IDamlRecord<SweepTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "SweepTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", DamlLfJsonDecoders.ReadParty));

        public static SweepTemplate FromRecord(DamlRecord record) => new();
    }

    private sealed record ThrowingReaderTemplate : ITemplate, IDamlRecord<ThrowingReaderTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "ThrowingReaderTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new KeyNotFoundException("the reader looked up a key that is not there");

        public static ThrowingReaderTemplate FromRecord(DamlRecord record) => new();
    }
}
