// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientTypedReadsTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");
    private static readonly Party Bob = new("party::bob");
    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party> { Bob });

    private const string CreatedEventJson =
        """
        {"offset": "42", "contractId": "00abc", "nodeId": 0,
         "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "TypedReadTemplate"},
         "createArgument": {"owner": "party::alice"}, "witnessParties": ["party::alice", "party::bob"]}
        """;

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private sealed record TypedReadTemplate : ITemplate, IDamlRecord<TypedReadTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "TypedReadTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", DamlLfJsonDecoders.ReadParty));

        public static TypedReadTemplate FromRecord(DamlRecord record) => new();
    }

    private sealed record CountRequiringTemplate : ITemplate, IDamlRecord<CountRequiringTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "TypedReadTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", DamlLfJsonDecoders.ReadParty));

        public static CountRequiringTemplate FromRecord(DamlRecord record)
        {
            record.GetTypeParameterField("count", value => value, absentReadsAs: null);
            return new();
        }
    }

    private sealed record CountRequiringInterfaceMarker : ITemplate, IDamlRecord<CountRequiringInterfaceMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-api";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => DamlRecord.Create();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", DamlLfJsonDecoders.ReadParty));

        public static CountRequiringInterfaceMarker FromRecord(DamlRecord record)
        {
            record.GetTypeParameterField("count", value => value, absentReadsAs: null);
            return new();
        }
    }

    private sealed record IHolding : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-api";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private RestLedgerClient ClientWith(RecordingHttpHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestLedgerClient(factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }

    [Fact]
    public async Task GetLatestPrunedOffsetsAsync_reads_both_offsets_from_the_participant()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"participantPrunedUpToInclusive": "17", "allDivulgedContractsPrunedUpToInclusive": "9"}""");

        var pruned = await ClientWith(transport).GetLatestPrunedOffsetsAsync(cancellationToken: TestContext.Current.CancellationToken);

        pruned.ParticipantPrunedUpToInclusive.Should().Be(LedgerOffset.At(17));
        pruned.AllDivulgedContractsPrunedUpToInclusive.Should().Be(LedgerOffset.At(9));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/state/latest-pruned-offsets");
    }

    [Fact]
    public async Task GetLatestPrunedOffsetsAsync_reads_absent_offsets_as_zero_on_an_unpruned_participant()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");

        var pruned = await ClientWith(transport).GetLatestPrunedOffsetsAsync(cancellationToken: TestContext.Current.CancellationToken);

        pruned.ParticipantPrunedUpToInclusive.Should().Be(LedgerOffset.At(0));
        pruned.AllDivulgedContractsPrunedUpToInclusive.Should().Be(LedgerOffset.At(0));
    }

    [Fact]
    public async Task GetLatestPrunedOffsetsAsync_reads_one_absent_offset_as_zero_and_keeps_the_other()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"participantPrunedUpToInclusive": "17"}""");

        var pruned = await ClientWith(transport).GetLatestPrunedOffsetsAsync(cancellationToken: TestContext.Current.CancellationToken);

        pruned.ParticipantPrunedUpToInclusive.Should().Be(LedgerOffset.At(17));
        pruned.AllDivulgedContractsPrunedUpToInclusive.Should().Be(LedgerOffset.At(0));
    }

    [Fact]
    public async Task GetContractAsync_posts_the_contract_id_and_querying_parties_and_decodes_the_contract()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, $$$"""{"createdEvent": {{{CreatedEventJson}}}}""");

        var contract = await ClientWith(transport).GetContractAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/contracts/contract-by-id");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("contractId").GetString().Should().Be("00abc");
        body.RootElement.GetProperty("queryingParties").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo("party::alice", "party::bob");
        contract.ContractId.Value.Should().Be("00abc");
        contract.WitnessParties.Select(p => p.Value).Should().Equal("party::alice", "party::bob");
    }

    [Fact]
    public async Task GetContractAsync_rejects_a_contract_of_another_template()
    {
        var foreign = CreatedEventJson.Replace("TypedReadTemplate", "Other");
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, $$$"""{"createdEvent": {{{foreign}}}}""");

        var act = () => ClientWith(transport).GetContractAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Module.Other*");
    }

    [Fact]
    public async Task GetContractAsync_surfaces_a_server_rejection_as_a_ledger_operation_exception()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.NotFound, """{"code": "CONTRACT_NOT_FOUND", "cause": "no such contract"}""");

        var act = () => ClientWith(transport).GetContractAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
    }

    [Fact]
    public async Task GetContractAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_required_payload_field_is_absent()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, $$$"""{"createdEvent": {{{CreatedEventJson}}}}""");

        var act = () => ClientWith(transport).GetContractAsync(
            new ContractId<CountRequiringTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, "Required field 'count' not found in record.");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_required_payload_field_is_absent()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$$"""{"created": {"createdEvent": {{{CreatedEventJson}}}, "synchronizerId": "sync::a"}}""");

        var act = () => ClientWith(transport).GetEventsByContractIdAsync(
            new ContractId<CountRequiringTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, "Required field 'count' not found in record.");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_an_interface_view_lacks_a_required_field()
    {
        var createdWithView = CreatedEventJson.Replace(
            "\"witnessParties\"",
            """
            "interfaceViews": [{"interfaceId": {"packageId": "iface-pkg", "moduleName": "Token.Api", "entityName": "IHolding"},
                                "viewStatus": {"code": 0, "message": ""}, "viewValue": {"owner": "party::alice"}}],
            "witnessParties"
            """);
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$$"""{"created": {"createdEvent": {{{createdWithView}}}, "synchronizerId": "sync::a"}}""");

        var act = () => ClientWith(transport).GetEventsByContractIdAsync(
            new ContractId<CountRequiringInterfaceMarker>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodable(act, "Required field 'count' not found in record.");
    }

    [Fact]
    public async Task GetContractAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_created_event_has_an_empty_contract_id()
    {
        var emptyIdCreated = CreatedEventJson.Replace("\"contractId\": \"00abc\"", "\"contractId\": \"\"");
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, $$$"""{"createdEvent": {{{emptyIdCreated}}}}""");

        var act = () => ClientWith(transport).GetContractAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodableContractId(act);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_created_event_has_an_empty_contract_id()
    {
        var emptyIdCreated = CreatedEventJson.Replace("\"contractId\": \"00abc\"", "\"contractId\": \"\"");
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$$"""{"created": {"createdEvent": {{{emptyIdCreated}}}, "synchronizerId": "sync::a"}}""");

        var act = () => ClientWith(transport).GetEventsByContractIdAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodableContractId(act);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_archived_event_has_an_empty_contract_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {"archived": {"archivedEvent": {"offset": "50", "contractId": "",
                "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "TypedReadTemplate"},
                "witnessParties": ["party::alice"]}, "synchronizerId": "sync::a"}}
            """);

        var act = () => ClientWith(transport).GetEventsByContractIdAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodableContractId(act);
    }

    private static async Task AssertUndecodableContractId<T>(Func<Task<T>> act)
    {
        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<ArgumentException>();
    }

    private static async Task AssertUndecodable<T>(Func<Task<T>> act, string detail)
    {
        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(detail);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_returns_creation_and_archival()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            $$$"""
            {"created": {"createdEvent": {{{CreatedEventJson}}}, "synchronizerId": "sync::a"},
             "archived": {"archivedEvent": {"offset": "50", "contractId": "00abc",
                "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "TypedReadTemplate"},
                "witnessParties": ["party::alice"]}, "synchronizerId": "sync::a"}}
            """);

        var lifecycle = await ClientWith(transport).GetEventsByContractIdAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/events/events-by-contract-id");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("contractId").GetString().Should().Be("00abc");
        body.RootElement.GetProperty("eventFormat").GetProperty("filtersByParty").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("party::alice", "party::bob");
        lifecycle.Created!.Offset.Should().Be(LedgerOffset.At(42));
        lifecycle.Created.SynchronizerId.Value.Should().Be("sync::a");
        lifecycle.Archived!.Offset.Should().Be(LedgerOffset.At(50));
        lifecycle.Archived.WitnessParties.Select(p => p.Value).Should().Equal("party::alice");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_leaves_archival_empty_for_a_live_contract()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$$"""{"created": {"createdEvent": {{{CreatedEventJson}}}, "synchronizerId": "sync::a"}}""");

        var lifecycle = await ClientWith(transport).GetEventsByContractIdAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created.Should().NotBeNull();
        lifecycle.Archived.Should().BeNull();
    }

    private const string DisclosedCreatedJson =
        """
        {"created": {"createdEvent": {"offset": "42", "contractId": "00abc", "nodeId": 0,
           "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "TypedReadTemplate"},
           "createdEventBlob": "AQID", "witnessParties": ["party::alice"]}, "synchronizerId": "sync::a"}}
        """;

    [Fact]
    public async Task GetDisclosureAsync_asks_for_the_created_event_blob_through_a_wildcard_filter_for_every_submitter_party()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, DisclosedCreatedJson);

        await ClientWith(transport).GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/events/events-by-contract-id");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("contractId").GetString().Should().Be("00abc");
        var filtersByParty = body.RootElement.GetProperty("eventFormat").GetProperty("filtersByParty");
        filtersByParty.EnumerateObject().Select(party => party.Name).Should().BeEquivalentTo("party::alice", "party::bob");
        foreach (var party in filtersByParty.EnumerateObject())
        {
            party.Value.GetRawText().Should().Be(
                """{"cumulative":[{"identifierFilter":{"WildcardFilter":{"value":{"includeCreatedEventBlob":true}}}}]}""");
        }
    }

    [Fact]
    public async Task GetDisclosureAsync_returns_the_contract_id_template_id_and_blob_of_a_visible_template_leaving_the_synchronizer_unset()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, DisclosedCreatedJson);

        var disclosure = await ClientWith(transport).GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        disclosure.Should().NotBeNull();
        disclosure!.ContractId.Should().Be("00abc");
        disclosure.TemplateId.Should().Be(new RuntimeIdentifier("pkg", "Module", "TypedReadTemplate"));
        disclosure.CreatedEventBlob.ToArray().Should().Equal(1, 2, 3);
        disclosure.SynchronizerId.Should().BeNull();
    }

    [Fact]
    public async Task GetDisclosureAsync_serves_a_contract_reached_through_an_interface_type_parameter()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, DisclosedCreatedJson);

        var disclosure = await ClientWith(transport).GetDisclosureAsync(
            new ContractId<IHolding>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        disclosure!.ContractId.Should().Be("00abc");
        disclosure.TemplateId.EntityName.Should().Be("TypedReadTemplate");
        transport.LastRequestBody.Should().Contain("WildcardFilter").And.NotContain("InterfaceFilter");
    }

    [Fact]
    public async Task GetDisclosureAsync_returns_null_for_an_archived_contract()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {"created": {"createdEvent": {"offset": "42", "contractId": "00abc", "nodeId": 0,
               "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "TypedReadTemplate"},
               "createdEventBlob": "AQID", "witnessParties": ["party::alice"]}, "synchronizerId": "sync::a"},
             "archived": {"archivedEvent": {"offset": "50", "contractId": "00abc",
               "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "TypedReadTemplate"},
               "witnessParties": ["party::alice"]}, "synchronizerId": "sync::a"}}
            """);

        var disclosure = await ClientWith(transport).GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        disclosure.Should().BeNull();
    }

    [Fact]
    public async Task GetDisclosureAsync_returns_null_when_the_participant_answers_without_events()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");

        var disclosure = await ClientWith(transport).GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        disclosure.Should().BeNull();
    }

    [Fact]
    public async Task GetDisclosureAsync_returns_null_when_the_participant_answers_NOT_FOUND()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.NotFound, """{"code": "CONTRACT_EVENTS_NOT_FOUND", "cause": "no such contract", "grpcCodeValue": 5, "errorCategory": 11, "context": {}}""");

        var disclosure = await ClientWith(transport).GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        disclosure.Should().BeNull();
    }

    [Fact]
    public async Task GetDisclosureAsync_surfaces_a_bare_404_without_a_structured_body()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.NotFound, "The requested resource could not be found.");

        var act = () => ClientWith(transport).GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
    }

    [Fact]
    public async Task GetDisclosureAsync_lets_other_participant_rejections_surface()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.Forbidden, """{"code": "PERMISSION_DENIED", "cause": "denied"}""");

        var act = () => ClientWith(transport).GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
    }

    [Fact]
    public async Task GetDisclosureAsync_per_call_timeout_ends_a_request_the_participant_never_answers()
    {
        var client = ClientWith(new RecordingHttpHandler().WithNoAnswerUntilCancelled());

        var act = () => client.GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>()).WithMessage("*50*deadline*");
    }

    [Fact]
    public async Task GetDisclosureAsync_leaves_a_request_alone_when_no_per_call_timeout_is_supplied()
    {
        var client = ClientWith(new RecordingHttpHandler().WithNoAnswerUntilCancelled());
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => client.GetDisclosureAsync(
            new ContractId<TypedReadTemplate>("00abc"), Submitter, timeout: null, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_sends_paging_arguments_and_projects_entries_and_token()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            $$$$"""
            {"activeContracts": [{"contractEntry": {"JsActiveContract": {"createdEvent": {{{{CreatedEventJson}}}}, "synchronizerId": "sync::a"}}}],
             "activeAtOffset": "30", "nextPageToken": "AQID"}
            """);

        var page = await ClientWith(transport).GetActiveContractsPageAsync<TypedReadTemplate>(
            Submitter,
            activeAtOffset: LedgerOffset.At(30),
            maxPageSize: 50,
            pageToken: new LedgerPageToken("BAUG"),
            cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/state/active-contracts-page");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("activeAtOffset").GetString().Should().Be("30");
        body.RootElement.GetProperty("maxPageSize").GetInt32().Should().Be(50);
        body.RootElement.GetProperty("pageToken").GetString().Should().Be("BAUG");
        page.ActiveAtOffset.Should().Be(LedgerOffset.At(30));
        page.NextPageToken!.Value.Should().Be("AQID");
        page.Entries.Should().ContainSingle().Which
            .Should().BeOfType<AcsSnapshotEntry<TypedReadTemplate>.Created>().Which.ContractId.Value.Should().Be("00abc");
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_Created_entry_carries_a_Disclosure_built_from_the_created_event_blob()
    {
        var createdWithBlob = CreatedEventJson.Replace("\"nodeId\": 0,", "\"nodeId\": 0, \"createdEventBlob\": \"CgsM\",");
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            $$$$"""
            {"activeContracts": [{"contractEntry": {"JsActiveContract": {"createdEvent": {{{{createdWithBlob}}}}, "synchronizerId": "sync::a"}}}],
             "activeAtOffset": "30"}
            """);

        var page = await ClientWith(transport).GetActiveContractsPageAsync<TypedReadTemplate>(
            Submitter,
            includeDisclosure: true,
            cancellationToken: TestContext.Current.CancellationToken);

        page.Entries.Should().ContainSingle().Which
            .Should().BeOfType<AcsSnapshotEntry<TypedReadTemplate>.Created>()
            .Which.Disclosure.Should().Be(new RuntimeCommands.DisclosedContract(
                "00abc",
                new RuntimeIdentifier("pkg", "Module", "TypedReadTemplate"),
                new byte[] { 0x0A, 0x0B, 0x0C }) { SynchronizerId = new SynchronizerId("sync::a") });
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_omits_optional_arguments_and_reports_the_last_page_without_a_token()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"activeContracts": [], "activeAtOffset": "12", "nextPageToken": ""}""");

        var page = await ClientWith(transport).GetActiveContractsPageAsync<TypedReadTemplate>(
            Submitter, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.TryGetProperty("activeAtOffset", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("maxPageSize", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("pageToken", out _).Should().BeFalse();
        page.NextPageToken.Should().BeNull();
        page.ActiveAtOffset.Should().Be(LedgerOffset.At(12));
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_answers_the_empty_snapshot_of_ledger_begin_without_asking_the_participant()
    {
        var transport = new RecordingHttpHandler();

        var page = await ClientWith(transport).GetActiveContractsPageAsync<TypedReadTemplate>(
            Submitter, activeAtOffset: LedgerOffset.Begin, cancellationToken: TestContext.Current.CancellationToken);

        page.Entries.Should().BeEmpty();
        page.ActiveAtOffset.Should().Be(LedgerOffset.Begin);
        page.NextPageToken.Should().BeNull();
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUpdatesPageAsync_sends_paging_arguments_and_projects_transactions_and_token()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {"updates": [{"update": {"Transaction": {"value": {"updateId": "upd-1", "commandId": "cmd-1", "offset": "21", "events": []}}}}],
             "lowestPageOffsetExclusive": "20", "highestPageOffsetInclusive": "21", "nextPageToken": "AQID"}
            """);

        var page = await ClientWith(transport).GetUpdatesPageAsync(
            Submitter,
            beginExclusive: LedgerOffset.At(20),
            endInclusive: LedgerOffset.At(99),
            maxPageSize: 10,
            descendingOrder: true,
            pageToken: new LedgerPageToken("BAUG"),
            cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/updates/get-updates-page");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("beginOffsetExclusive").GetString().Should().Be("20");
        body.RootElement.GetProperty("endOffsetInclusive").GetString().Should().Be("99");
        body.RootElement.GetProperty("maxPageSize").GetInt32().Should().Be(10);
        body.RootElement.GetProperty("descendingOrder").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("pageToken").GetString().Should().Be("BAUG");
        page.LowestPageOffsetExclusive.Should().Be(LedgerOffset.At(20));
        page.HighestPageOffsetInclusive.Should().Be(LedgerOffset.At(21));
        page.NextPageToken!.Value.Should().Be("AQID");
        page.Updates.Should().ContainSingle().Which.UpdateId.Should().Be("upd-1");
    }

    [Fact]
    public async Task GetUpdatesPageAsync_rejects_a_non_transaction_update()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {"updates": [{"update": {"Reassignment": {"value": {"offset": "7", "events": []}}}}],
             "lowestPageOffsetExclusive": "6", "highestPageOffsetInclusive": "7"}
            """);

        var act = () => ClientWith(transport).GetUpdatesPageAsync(Submitter, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Reassignment*");
    }
}
