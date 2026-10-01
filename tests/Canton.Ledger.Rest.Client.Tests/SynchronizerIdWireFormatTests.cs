// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireUpdate = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class SynchronizerIdWireFormatTests : IDisposable
{
    private const string ThreeFourSource = "global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5";
    private const string ThreeFiveSource = "global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5::35-0";
    private const string ThreeFourTarget = "app-domain::1220b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f0a";
    private const string ThreeFiveTarget = "app-domain::1220b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f0a::35-0";

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private sealed record HoldingMarker(DamlRecord Record) : ITemplate, IDamlRecord<HoldingMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "WireFormatHolding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => Record;

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static HoldingMarker FromRecord(DamlRecord record) => new(record);
    }

    private static WireUpdate UpdateFrom(string json) =>
        JsonSerializer.Deserialize<WireUpdate>(json, RestRefitSettings.SerializerOptions)!;

    private static async Task<GetActiveContractsResponse> ActiveContractsResponseFrom(string json)
    {
        var (api, transport) = RestApiFactory.Build<IStateServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, json);
        return await api.GetActiveContracts(new GetActiveContractsRequest(), TestContext.Current.CancellationToken);
    }

    private static string CreatedEventJson(long offset) =>
        $$"""
        {
          "offset": "{{offset}}",
          "contractId": "00holding",
          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "WireFormatHolding"},
          "createArgument": {},
          "witnessParties": ["alice::ns1"]
        }
        """;

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public void ProjectTransactionEvents_carries_the_created_events_synchronizer_id_verbatim(string wireSynchronizerId)
    {
        var transaction = UpdateFrom(
            $$"""
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "7",
                    "synchronizerId": "{{wireSynchronizerId}}",
                    "events": [{"CreatedEvent": {{CreatedEventJson(7)}}}]
                  }
                }
              }
            }
            """).Update.Transaction;

        var projected = RestContractStreamProjector.ProjectTransactionEvents<HoldingMarker>(transaction)
            .Should().ContainSingle().Subject;

        projected.Should().BeOfType<ContractStreamEvent<HoldingMarker>.Created>()
            .Which.SynchronizerId.Value.Should().Be(wireSynchronizerId);
    }

    [Theory]
    [InlineData(ThreeFourSource, ThreeFourTarget)]
    [InlineData(ThreeFiveSource, ThreeFiveTarget)]
    [InlineData(ThreeFourSource, ThreeFiveTarget)]
    public void ProjectReassignmentEvents_carries_the_assigned_events_source_and_target_verbatim(string wireSource, string wireTarget)
    {
        var reassignment = UpdateFrom(
            $$"""
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "20",
                    "events": [
                      {
                        "JsAssignmentEvent": {
                          "source": "{{wireSource}}",
                          "target": "{{wireTarget}}",
                          "reassignmentId": "reassign-1",
                          "reassignmentCounter": "3",
                          "createdEvent": {{CreatedEventJson(20)}}
                        }
                      }
                    ]
                  }
                }
              }
            }
            """).Update.Reassignment;

        var projected = RestContractStreamProjector.ProjectReassignmentEvents<HoldingMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var assigned = projected.Should().BeOfType<ContractStreamEvent<HoldingMarker>.Assigned>().Subject;
        assigned.Source.Value.Should().Be(wireSource);
        assigned.Target.Value.Should().Be(wireTarget);
    }

    [Theory]
    [InlineData(ThreeFourSource, ThreeFourTarget)]
    [InlineData(ThreeFiveSource, ThreeFiveTarget)]
    [InlineData(ThreeFourSource, ThreeFiveTarget)]
    public void ProjectReassignmentEvents_carries_the_unassigned_events_source_and_target_verbatim(string wireSource, string wireTarget)
    {
        var reassignment = UpdateFrom(
            $$"""
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "21",
                    "events": [
                      {
                        "JsUnassignedEvent": {
                          "value": {
                            "source": "{{wireSource}}",
                            "target": "{{wireTarget}}",
                            "contractId": "00holding",
                            "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "WireFormatHolding"},
                            "reassignmentId": "reassign-2",
                            "reassignmentCounter": "4",
                            "offset": "21"
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """).Update.Reassignment;

        var projected = RestContractStreamProjector.ProjectReassignmentEvents<HoldingMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var unassigned = projected.Should().BeOfType<ContractStreamEvent<HoldingMarker>.Unassigned>().Subject;
        unassigned.Source.Value.Should().Be(wireSource);
        unassigned.Target.Value.Should().Be(wireTarget);
    }

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public async Task ProjectActiveContractEntry_carries_an_active_contracts_synchronizer_id_verbatim(string wireSynchronizerId)
    {
        var response = await ActiveContractsResponseFrom(
            $$"""
            {
              "contractEntry": {
                "JsActiveContract": {
                  "createdEvent": {{CreatedEventJson(42)}},
                  "synchronizerId": "{{wireSynchronizerId}}",
                  "reassignmentCounter": "0"
                }
              }
            }
            """);

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<HoldingMarker>(response)
            .Should().ContainSingle().Subject;

        projected.Should().BeOfType<ContractStreamEvent<HoldingMarker>.Created>()
            .Which.SynchronizerId.Value.Should().Be(wireSynchronizerId);
    }

    [Theory]
    [InlineData(ThreeFourSource, ThreeFourTarget)]
    [InlineData(ThreeFiveSource, ThreeFiveTarget)]
    [InlineData(ThreeFourSource, ThreeFiveTarget)]
    public async Task ProjectActiveContractEntry_carries_an_incomplete_unassigned_entrys_source_and_target_verbatim(string wireSource, string wireTarget)
    {
        var response = await ActiveContractsResponseFrom(
            $$"""
            {
              "contractEntry": {
                "JsIncompleteUnassigned": {
                  "createdEvent": {{CreatedEventJson(42)}},
                  "unassignedEvent": {
                    "contractId": "00holding",
                    "source": "{{wireSource}}",
                    "target": "{{wireTarget}}",
                    "offset": "50",
                    "reassignmentId": "reassignment-1",
                    "reassignmentCounter": "7"
                  }
                }
              }
            }
            """);

        var projected = RestContractStreamProjector.ProjectActiveContractEntry<HoldingMarker>(response).ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<HoldingMarker>.Created>()
            .Which.SynchronizerId.Value.Should().Be(wireSource);
        var unassigned = projected[1].Should().BeOfType<ContractStreamEvent<HoldingMarker>.Unassigned>().Subject;
        unassigned.Source.Value.Should().Be(wireSource);
        unassigned.Target.Value.Should().Be(wireTarget);
    }

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public async Task ListVettedPackagesAsync_carries_the_vetting_synchronizer_id_verbatim(string wireSynchronizerId)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            $$"""
            {"vettedPackages": [{"packages": [{"packageId": "1220aa", "packageName": "splice-amulet", "packageVersion": "0.1.14"}],
              "participantId": "participant1::1220ab", "synchronizerId": "{{wireSynchronizerId}}"}],
             "nextPageToken": ""}
            """);
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        IAdminClient client = new RestAdminClient(factory);

        var vetted = await client.ListVettedPackagesAsync(["splice-"], TestContext.Current.CancellationToken);

        vetted.Should().ContainSingle().Which.SynchronizerId.Value.Should().Be(wireSynchronizerId);
    }

    private static RuntimeCommands.CommandsSubmission CreateSubmission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, [])))
            .WithActAs(new Party("party::alice"))
            .WithCommandId(new RuntimeCommands.CommandId("wire-format-cmd"));

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public void BuildCommands_writes_the_submissions_synchronizer_id_verbatim(string synchronizerId)
    {
        var submission = CreateSubmission().WithSynchronizerId(new SynchronizerId(synchronizerId));

        var commands = RestCommandBuilder.BuildCommands(submission, userId: null);

        commands.SynchronizerId.Should().Be(synchronizerId);
    }

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public void BuildCommands_writes_a_disclosed_contracts_synchronizer_id_verbatim(string synchronizerId)
    {
        var submission = CreateSubmission().WithDisclosedContracts(new RuntimeCommands.DisclosedContract(
            "00disclosed", new RuntimeIdentifier("disclosed-pkg", "Disclosed", "Contract"), new byte[] { 0x01 })
        {
            SynchronizerId = new SynchronizerId(synchronizerId),
        });

        var json = JsonSerializer.Serialize(
            RestCommandBuilder.BuildCommands(submission, userId: null), RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("disclosedContracts")[0]
            .GetProperty("synchronizerId").GetString().Should().Be(synchronizerId);
    }
}
