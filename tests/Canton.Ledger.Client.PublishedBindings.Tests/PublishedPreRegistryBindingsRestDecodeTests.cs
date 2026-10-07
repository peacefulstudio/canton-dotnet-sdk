// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Splice.Wallet.TransferOffer;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;

namespace Canton.Ledger.Client.PublishedBindings.Tests;

/// <summary>
/// Pins how the JSON Ledger API client treats a binding published before generated packages registered
/// themselves: the pinned <c>Splice.Wallet</c> package declares its <c>TransferOffer</c> template but
/// registers nothing, and nothing scans for types, so reading that template over REST is refused with
/// the missing-type exception and a submission that committed it is reported as undecodable. Regenerating
/// the binding with a current codegen is what fixes it.
/// </summary>
public sealed class PublishedPreRegistryBindingsRestDecodeTests
{
    private const string TransferOfferId =
        "d42261314d98cc04bf50bcc58930af98b9f33aeed11339465925de697d3ba66e:Splice.Wallet.TransferOffer:TransferOffer";

    private static readonly RuntimeIdentifier TransferOfferTemplateId = TransferOffer.TemplateId;

    [Fact]
    public void CreatePayloadOf_carries_the_create_argument_of_a_published_template_whose_package_registers_nothing()
    {
        var createArgument = new WireRecord();
        createArgument.AdditionalProperties["idiomatic"] = """{"sender": "party::alice"}""";
        var created = new WireCreatedEvent
        {
            ContractId = "00offer",
            TemplateId = new WireIdentifier
            {
                PackageId = TransferOfferTemplateId.PackageId,
                ModuleName = TransferOfferTemplateId.ModuleName,
                EntityName = TransferOfferTemplateId.EntityName,
            },
            CreateArgument = createArgument,
        };

        var payload = RestPayloadDecoder.CreatePayloadOf(created, TransferOfferTemplateId);

        payload.Undecoded.Should().Be(new DamlUndecodedJson("""{"sender": "party::alice"}"""));
        payload.Arguments.Should().Be(new DamlRecord(TransferOfferTemplateId, []));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_carries_the_created_payload_when_the_created_template_is_a_published_binding_that_registers_nothing()
    {
        var response = $$$"""
            {"transaction": {"updateId": "update-1", "commandId": "cmd-1", "offset": "42", "events": [
              {"CreatedEvent": {
                "offset": "42", "contractId": "00offer", "nodeId": 0, "acsDelta": true,
                "templateId": "{{{TransferOfferId}}}",
                "createArgument": {"sender": "party::alice"},
                "witnessParties": ["party::alice"], "signatories": ["party::alice"], "observers": []}}]}}
            """;
        using var factory = new FixedResponseHttpClientFactory(response);
        var client = new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
        var submission = CommandsSubmission
            .Single(new CreateCommand(TransferOfferTemplateId, new DamlRecord(null, [])))
            .WithActAs((Party)"party::alice");

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var transaction = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Which.Result;
        transaction.UpdateId.Should().Be("update-1");
        var created = transaction.CreatedContracts.Should().ContainSingle().Subject;
        created.ContractId.Should().Be("00offer");
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
