// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Pins how the JSON Ledger API client treats a template whose package registered no binding: nothing
/// scans for types, so the create payload degrades to the raw JSON and a submission that committed the
/// template still reports its created contract with the undecoded payload.
/// </summary>
public sealed class RestUnregisteredTemplateDecodeTests
{
    private const string UnregisteredTemplateId = "unregistered-pkg:Unregistered.Module:UnregisteredTemplate";

    private static readonly RuntimeIdentifier UnregisteredTemplate =
        new("unregistered-pkg", "Unregistered.Module", "UnregisteredTemplate");

    [Fact]
    public void CreatePayloadOf_carries_the_create_argument_of_a_template_whose_package_registers_no_binding()
    {
        var createArgument = new WireRecord();
        createArgument.AdditionalProperties["idiomatic"] = """{"sender": "party::alice"}""";
        var created = new WireCreatedEvent
        {
            ContractId = "00unregistered",
            TemplateId = new WireIdentifier
            {
                PackageId = UnregisteredTemplate.PackageId,
                ModuleName = UnregisteredTemplate.ModuleName,
                EntityName = UnregisteredTemplate.EntityName,
            },
            CreateArgument = createArgument,
        };

        var payload = RestPayloadDecoder.CreatePayloadOf(created, UnregisteredTemplate);

        payload.Undecoded.Should().Be(new DamlUndecodedJson("""{"sender": "party::alice"}"""));
        payload.Arguments.Should().Be(new DamlRecord(UnregisteredTemplate, []));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_carries_the_created_payload_when_the_created_template_is_a_template_that_registers_no_binding()
    {
        var response = $$$"""
            {"transaction": {"updateId": "update-1", "commandId": "cmd-1", "offset": "42", "events": [
              {"CreatedEvent": {
                "offset": "42", "contractId": "00unregistered", "nodeId": 0, "acsDelta": true,
                "templateId": "{{{UnregisteredTemplateId}}}",
                "createArgument": {"sender": "party::alice"},
                "witnessParties": ["party::alice"], "signatories": ["party::alice"], "observers": []}}]}}
            """;
        using var factory = new FixedResponseHttpClientFactory(response);
        var client = new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
        var submission = CommandsSubmission
            .Single(new CreateCommand(UnregisteredTemplate, new DamlRecord(null, [])))
            .WithActAs((Party)"party::alice");

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var transaction = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Which.Result;
        transaction.UpdateId.Should().Be("update-1");
        var created = transaction.CreatedContracts.Should().ContainSingle().Subject;
        created.ContractId.Should().Be("00unregistered");
        created.UndecodedPayload.Should().Be(new DamlUndecodedJson("""{"sender": "party::alice"}"""));
    }

    private sealed class FixedResponseHttpClientFactory(string responseBody) : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client = new(new FixedResponseHandler(responseBody), disposeHandler: true)
        {
            BaseAddress = new Uri("http://localhost:7575"),
        };

        public HttpClient CreateClient(string name) => _client;

        public void Dispose() => _client.Dispose();
    }

    private sealed class FixedResponseHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
    }
}
