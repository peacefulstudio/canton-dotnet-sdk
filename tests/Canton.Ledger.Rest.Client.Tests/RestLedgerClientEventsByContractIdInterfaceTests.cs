// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientEventsByContractIdInterfaceTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");
    private static readonly Party Bob = new("party::bob");
    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party> { Bob });

    private const string ViewedInterfaceIdJson =
        """{"packageId": "viewed-pkg", "moduleName": "Token.Api", "entityName": "IViewedHolding"}""";

    private const string CreatedWithViewJson =
        $$$"""
        {"offset": "42", "contractId": "00impl", "nodeId": 0,
         "packageName": "token-impl",
         "templateId": {"packageId": "impl-pkg", "moduleName": "Token.Impl", "entityName": "Asset"},
         "createArgument": {"amount": "999"},
         "interfaceViews": [{"interfaceId": {{{ViewedInterfaceIdJson}}}, "viewStatus": {"code": 0, "message": ""}, "viewValue": {"amount": "42.5"}}],
         "witnessParties": ["party::alice", "party::bob"]}
        """;

    private const string ArchivedJson =
        """
        "archived": {"archivedEvent": {"offset": "50", "contractId": "00impl",
           "packageName": "token-impl",
           "templateId": {"packageId": "impl-pkg", "moduleName": "Token.Impl", "entityName": "Asset"},
           "witnessParties": ["party::alice"]}, "synchronizerId": "sync::a"}
        """;

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private RestLedgerClient ClientWith(RecordingHttpHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestLedgerClient(
            factory,
            Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_returns_the_creation_with_its_decoded_view_and_the_archival_of_an_archived_interface_contract()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            $$$"""{"created": {"createdEvent": {{{CreatedWithViewJson}}}, "synchronizerId": "sync::a"}, {{{ArchivedJson}}}}""");

        var lifecycle = await ClientWith(transport).GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created!.ContractId.Value.Should().Be("00impl");
        lifecycle.Created.Payload.Amount.Should().Be(42.5m);
        lifecycle.Created.Offset.Should().Be(LedgerOffset.At(42));
        lifecycle.Created.SynchronizerId.Value.Should().Be("sync::a");
        lifecycle.Created.WitnessParties.Select(party => party.Value).Should().Equal("party::alice", "party::bob");
        lifecycle.Archived!.ContractId.Value.Should().Be("00impl");
        lifecycle.Archived.Offset.Should().Be(LedgerOffset.At(50));
        lifecycle.Archived.SynchronizerId.Value.Should().Be("sync::a");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_leaves_the_archival_empty_for_an_active_interface_contract()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$$"""{"created": {"createdEvent": {{{CreatedWithViewJson}}}, "synchronizerId": "sync::a"}}""");

        var lifecycle = await ClientWith(transport).GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created.Should().NotBeNull();
        lifecycle.Archived.Should().BeNull();
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_asks_the_participant_for_an_InterfaceFilter_carrying_the_view()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$$"""{"created": {"createdEvent": {{{CreatedWithViewJson}}}, "synchronizerId": "sync::a"}}""");

        _ = await ClientWith(transport).GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/events/events-by-contract-id");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("contractId").GetString().Should().Be("00impl");
        var filtersByParty = body.RootElement.GetProperty("eventFormat").GetProperty("filtersByParty");
        filtersByParty.EnumerateObject().Select(party => party.Name).Should().BeEquivalentTo("party::alice", "party::bob");
        var interfaceFilter = filtersByParty.GetProperty("party::alice").GetProperty("cumulative")[0]
            .GetProperty("identifierFilter").GetProperty("InterfaceFilter").GetProperty("value");
        interfaceFilter.GetProperty("includeInterfaceView").GetBoolean().Should().BeTrue();
        interfaceFilter.GetProperty("interfaceId").GetString().Should().Be("#viewed-token-api:Token.Api:IViewedHolding");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_surfaces_an_unknown_contract_id_as_a_not_committed_ledger_operation_failure()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.NotFound,
            """{"code": "CONTRACT_EVENTS_NOT_FOUND", "cause": "no such contract", "grpcCodeValue": 5, "errorCategory": 11, "context": {}}""");

        var act = () => ClientWith(transport).GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00unknown"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.NotFound));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.ErrorId.Should().Be("CONTRACT_EVENTS_NOT_FOUND");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_refuses_a_creation_whose_interface_view_the_participant_did_not_compute()
    {
        var withoutView = CreatedWithViewJson.Replace("\"viewValue\": {\"amount\": \"42.5\"}", "\"viewValue\": null")
            .Replace("\"code\": 0", "\"code\": 2");
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$$"""{"created": {"createdEvent": {{{withoutView}}}, "synchronizerId": "sync::a"}}""");

        var act = () => ClientWith(transport).GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*00impl*IViewedInterfaceMarker*interface view is unavailable*");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_view_lacks_a_required_field()
    {
        var emptyView = CreatedWithViewJson.Replace("\"viewValue\": {\"amount\": \"42.5\"}", "\"viewValue\": {}");
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$$"""{"created": {"createdEvent": {{{emptyView}}}, "synchronizerId": "sync::a"}}""");

        var act = () => ClientWith(transport).GetEventsByContractIdAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            new ContractId<IViewedInterfaceMarker>("00impl"), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.Detail.Should().Be("Required Daml field 'ViewedInterfaceView.amount' is missing from the JSON object");
    }
}
