// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestGeneratedTypeDiscoveryParityTests : GeneratedTypeDiscoveryParityTests, IDisposable
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

    protected override async Task<ExerciseOutcome<OptionalTails>> ExerciseAnsweringEchoedTailsAsync(
        RuntimeIdentifier templateId, ChoiceName choice)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ExercisedTransaction(templateId, choice));
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        var client = new RestLedgerClient(factory);
        var command = new ExerciseCommand(templateId, new ContractId<OptionalTails>("00echo"), choice, DamlUnit.Instance);

        return await client.TryExerciseAsync<OptionalTails>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string ExercisedTransaction(RuntimeIdentifier templateId, ChoiceName choice) =>
        $$"""
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "nodeId": 0,
                  "contractId": "00echo",
                  "templateId": {"packageId": "{{templateId.PackageId}}", "moduleName": "{{templateId.ModuleName}}", "entityName": "{{templateId.EntityName}}"},
                  "choice": "{{choice.Value}}",
                  "choiceArgument": {},
                  "actingParties": ["party::alice"],
                  "consuming": false,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": {{EchoedTailsLfJson}}
                }
              }
            ]
          }
        }
        """;
}
