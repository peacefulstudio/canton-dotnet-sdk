// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientCommandBatchTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");

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
        return new RestLedgerClient(factory, Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            UserId = "test-user",
        }));
    }

    private const string MixedBatchTransaction =
        """
        {
          "transaction": {
            "updateId": "upd-batch",
            "commandId": "cmd-batch",
            "offset": "9",
            "events": [
              {
                "CreatedEvent": {
                  "offset": "9", "nodeId": 0, "acsDelta": true, "contractId": "00asset-first",
                  "packageName": "richtypes-h07285b495f9e",
                  "templateId": {"packageId": "3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "moduleName": "RichTypes", "entityName": "Asset"},
                  "createArgument": {"issuer": "party::alice", "amount": "10.0"}
                }
              },
              {
                "ExercisedEvent": {
                  "offset": "9", "nodeId": 1, "lastDescendantNodeId": 2, "contractId": "00marker",
                  "packageName": "richtypes-h07285b495f9e",
                  "templateId": {"packageId": "3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "moduleName": "RichTypes", "entityName": "Marker"},
                  "choice": "Mint", "choiceArgument": {"amount": "5.0"}, "actingParties": ["party::alice"],
                  "consuming": false, "witnessParties": ["party::alice"], "exerciseResult": "00asset-minted"
                }
              },
              {
                "CreatedEvent": {
                  "offset": "9", "nodeId": 2, "acsDelta": true, "contractId": "00asset-minted",
                  "packageName": "richtypes-h07285b495f9e",
                  "templateId": {"packageId": "3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "moduleName": "RichTypes", "entityName": "Asset"},
                  "createArgument": {"issuer": "party::alice", "amount": "5.0"}
                }
              },
              {
                "CreatedEvent": {
                  "offset": "9", "nodeId": 3, "acsDelta": true, "contractId": "00asset-second",
                  "packageName": "richtypes-h07285b495f9e",
                  "templateId": {"packageId": "3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "moduleName": "RichTypes", "entityName": "Asset"},
                  "createArgument": {"issuer": "party::alice", "amount": "20.0"}
                }
              },
              {
                "ExercisedEvent": {
                  "offset": "9", "nodeId": 4, "lastDescendantNodeId": 4, "contractId": "00marker-old",
                  "packageName": "richtypes-h07285b495f9e",
                  "templateId": {"packageId": "3acb9f11503a1c56db40eb057c7f630247329f66bde28d381d31955a1b334a2c", "moduleName": "RichTypes", "entityName": "Marker"},
                  "choice": "Archive", "choiceArgument": {}, "actingParties": ["party::alice"],
                  "consuming": true, "witnessParties": ["party::alice"], "exerciseResult": {}
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task TrySubmitBatchAsync_posts_every_command_in_one_request_and_reads_each_result_from_its_root_node()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, MixedBatchTransaction);
        var batch = new CommandBatch();
        var firstAsset = batch.AddCreate(new Asset(Alice, 10m));
        var minted = batch.Add(Marker.ChoiceMint, new ContractId<Marker>("00marker"), new Marker.Mint(5m));
        var secondAsset = batch.AddCreate(new Asset(Alice, 20m));
        var archived = batch.Add(Marker.ChoiceArchive, new ContractId<Marker>("00marker-old"), DamlUnit.Instance);

        var outcome = await ClientWith(transport).TrySubmitBatchAsync(
            batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        var commands = body.RootElement.GetProperty("commands").GetProperty("commands");
        commands.EnumerateArray().Select(command => command.EnumerateObject().Single().Name).Should().Equal(
            "CreateCommand", "ExerciseCommand", "CreateCommand", "ExerciseCommand");
        commands[1].GetProperty("ExerciseCommand").GetProperty("choice").GetString().Should().Be("Mint");
        commands[3].GetProperty("ExerciseCommand").GetProperty("contractId").GetString().Should().Be("00marker-old");
        var results = outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.One>().Subject.Result;
        results.Get(firstAsset).Should().Be(new ContractId<Asset>("00asset-first"));
        results.Get(minted).Should().Be(new ContractId<Asset>("00asset-minted"));
        results.Get(secondAsset).Should().Be(new ContractId<Asset>("00asset-second"));
        results.Get(archived).Should().Be(DamlUnit.Instance);
        outcome.Operation!.Label.Should().Be("Asset.Create ×2, Marker.Mint, Marker.Archive");
    }

    [Fact]
    public async Task TrySubmitBatchAsync_rejected_by_a_command_partway_through_names_every_command()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.NotFound,
            """
            {
              "code": "CONTRACT_NOT_FOUND",
              "cause": "Contract could not be found with id 00marker-old",
              "context": {"category": "11"},
              "errorCategory": 11
            }
            """);
        var batch = new CommandBatch();
        batch.AddCreate(new Asset(Alice, 10m));
        batch.Add(Marker.ChoiceArchive, new ContractId<Marker>("00marker-old"), DamlUnit.Instance);
        batch.AddCreate(new Asset(Alice, 20m));

        var outcome = await ClientWith(transport).TrySubmitBatchAsync(
            batch, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<BatchResult>.DamlError>().Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        var act = () => outcome.OneOrThrow();
        act.Should().Throw<LedgerOperationException>()
            .Which.Operation.Should().Be("Asset.Create ×2, Marker.Archive");
    }
}
