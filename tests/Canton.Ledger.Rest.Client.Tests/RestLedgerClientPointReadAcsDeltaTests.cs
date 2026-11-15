// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime;
using Daml.Runtime.Data;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientPointReadAcsDeltaTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private static readonly RuntimeCommands.SubmitterInfo AliceSubmitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    private const string ChurnTransactionBody =
        """
        {
          "update": {
            "Transaction": {
              "value": {
                "updateId": "upd-churn",
                "commandId": "cmd-churn",
                "offset": "7",
                "events": [
                  {
                    "ExercisedEvent": {
                      "offset": "7", "nodeId": 0, "lastDescendantNodeId": 4,
                      "contractId": "00factory", "packageName": "pkg-name",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Factory"},
                      "choice": "Churn", "choiceArgument": {}, "exerciseResult": {},
                      "consuming": false, "acsDelta": false,
                      "actingParties": ["party::alice"], "witnessParties": ["party::alice"]
                    }
                  },
                  {
                    "ExercisedEvent": {
                      "offset": "7", "nodeId": 1, "lastDescendantNodeId": 1,
                      "contractId": "00retiring", "packageName": "pkg-name",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Ephemeral"},
                      "choice": "Archive", "choiceArgument": {}, "exerciseResult": {},
                      "consuming": true, "acsDelta": true,
                      "actingParties": ["party::alice"], "witnessParties": ["party::alice"]
                    }
                  },
                  {
                    "CreatedEvent": {
                      "offset": "7", "contractId": "00kept", "nodeId": 2, "packageName": "pkg-name",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Ephemeral"},
                      "createArgument": {"owner": "party::alice"}, "acsDelta": true
                    }
                  },
                  {
                    "CreatedEvent": {
                      "offset": "7", "contractId": "00transient", "nodeId": 3, "packageName": "pkg-name",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Ephemeral"},
                      "createArgument": {"owner": "party::alice"}, "acsDelta": false
                    }
                  },
                  {
                    "ExercisedEvent": {
                      "offset": "7", "nodeId": 4, "lastDescendantNodeId": 4,
                      "contractId": "00transient", "packageName": "pkg-name",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Ephemeral"},
                      "choice": "Archive", "choiceArgument": {}, "exerciseResult": {},
                      "consuming": true, "acsDelta": false,
                      "actingParties": ["party::alice"], "witnessParties": ["party::alice"]
                    }
                  }
                ]
              }
            }
          }
        }
        """;

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_reports_only_the_acsDelta_creates_and_the_archives_of_consuming_exercises()
    {
        var client = ClientWith(ChurnTransactionBody);

        var result = await client.GetUpdateByOffsetAsync(
            LedgerOffset.At(7), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        result.CreatedContracts.Select(created => created.ContractId).Should().Equal("00kept");
        result.ArchivedContractIds.Should().Equal("00retiring");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_reports_only_the_acsDelta_creates_and_the_archives_of_consuming_exercises()
    {
        var client = ClientWith(ChurnTransactionBody);

        var result = await client.GetUpdateByIdAsync(
            "upd-churn", AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        result.CreatedContracts.Select(created => created.ContractId).Should().Equal("00kept");
        result.ArchivedContractIds.Should().Equal("00retiring");
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_still_lists_every_exercise()
    {
        var client = ClientWith(ChurnTransactionBody);

        var result = await client.GetUpdateByOffsetAsync(
            LedgerOffset.At(7), AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        result.ExercisedEvents.Select(exercised => exercised.ContractId)
            .Should().Equal("00factory", "00retiring", "00transient");
    }

    [Fact]
    public async Task GetUpdatesPageAsync_reports_only_the_acsDelta_creates_and_the_archives_of_consuming_exercises()
    {
        var client = ClientWith(
            $$"""{"updates": [{{ChurnTransactionBody}}], "lowestPageOffsetExclusive": "6", "highestPageOffsetInclusive": "7"}""");

        var page = await client.GetUpdatesPageAsync(AliceSubmitter, cancellationToken: TestContext.Current.CancellationToken);

        var result = page.Updates.Should().ContainSingle().Subject;
        result.CreatedContracts.Select(created => created.ContractId).Should().Equal("00kept");
        result.ArchivedContractIds.Should().Equal("00retiring");
    }

    private RestLedgerClient ClientWith(string responseBody)
    {
        var factory = new StubHttpClientFactory(new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, responseBody));
        _factories.Add(factory);
        return new RestLedgerClient(
            factory,
            Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }
}
