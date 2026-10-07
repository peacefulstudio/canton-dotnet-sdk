// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text.Json;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using AwesomeAssertions;
using Xunit;
using InterfaceEvent = Daml.Runtime.Streams.InterfaceStreamEvent<
    Canton.Ledger.Testing.Helpers.InterfaceMarker, Canton.Ledger.Testing.Helpers.InterfaceMarkerView>;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestContractStreamProjectorParityTests : ContractStreamProjectorParityTests
{
    private OmittedOffsetWire omittedOffsetWire = OmittedOffsetWire.Absent;

    private enum OmittedOffsetWire
    {
        Absent,
        Zero,
    }

    protected override async Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectActiveContractEntryAsync(
        ActiveContractScenario scenario)
    {
        var response = await ActiveContractsResponseAsync(scenario);

        return RestContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response, logger: null, SnapshotOffsetOf(scenario)).ToList();
    }

    protected override async Task<IReadOnlyList<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>>> ProjectActiveContractEntryAsInterfaceAsync(
        ActiveContractScenario scenario)
    {
        var response = await ActiveContractsResponseAsync(scenario);

        return RestInterfaceStreamProjector.ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response, logger: null, SnapshotOffsetOf(scenario)).ToList();
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectTransactionEventsAsync(
        TransactionEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected = RestContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(UpdateFrom(TransactionJson(scenario)).Transaction).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectReassignmentEventsAsync(
        ReassignmentEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected = RestContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(UpdateFrom(ReassignmentJson(scenario)).Reassignment).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<InterfaceEvent>> ProjectTransactionEventsAsInterfaceAsync(
        TransactionEventScenario scenario, ILogger logger)
    {
        IReadOnlyList<InterfaceEvent> projected = RestInterfaceStreamProjector
            .ProjectTransactionEvents<InterfaceMarker, InterfaceMarkerView>(
                UpdateFrom(TransactionJson(scenario)).Transaction, logger).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<InterfaceEvent>> ProjectReassignmentEventsAsInterfaceAsync(
        ReassignmentEventScenario scenario, ILogger logger)
    {
        IReadOnlyList<InterfaceEvent> projected = RestInterfaceStreamProjector
            .ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(
                UpdateFrom(ReassignmentJson(scenario)).Reassignment, logger).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<InterfaceKindTemplateMarker>>> ProjectTransactionEventsAsInterfaceKindTemplateAsync(
        TransactionEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<InterfaceKindTemplateMarker>> projected = RestContractStreamProjector
            .ProjectTransactionEvents<InterfaceKindTemplateMarker>(UpdateFrom(TransactionJson(scenario)).Transaction).ToList();
        return Task.FromResult(projected);
    }

    [Fact]
    public async Task ProjectTransactionEvents_reports_a_Created_with_a_wire_event_offset_of_zero_at_the_transactions_offset()
    {
        omittedOffsetWire = OmittedOffsetWire.Zero;

        var projected = await ProjectTransactionEventsAsync(
            new TransactionEventScenario { Event = TransactionEventShape.Created, OmitEventOffset = true });

        projected.Should().ContainSingle().Which.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
            .Which.Offset.Should().Be(Daml.Runtime.LedgerOffset.At(70L));
    }

    [Fact]
    public async Task ProjectReassignmentEvents_reports_an_Unassigned_with_a_wire_event_offset_of_zero_at_the_reassignments_offset()
    {
        omittedOffsetWire = OmittedOffsetWire.Zero;

        var projected = await ProjectReassignmentEventsAsync(
            new ReassignmentEventScenario { Event = ReassignmentEventShape.Unassigned, OmitEventOffset = true });

        projected.Should().ContainSingle().Which.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>()
            .Which.Offset.Should().Be(Daml.Runtime.LedgerOffset.At(80L));
    }

    [Fact]
    public async Task ProjectActiveContractEntry_reports_a_Created_with_a_wire_event_offset_of_zero_at_the_snapshot_offset()
    {
        omittedOffsetWire = OmittedOffsetWire.Zero;

        var projected = await ProjectActiveContractEntryAsync(new ActiveContractScenario
        {
            Entry = ActiveContractEntry.Active,
            OmitCreatedEventOffset = true,
            SnapshotOffset = 77L,
        });

        projected.Should().ContainSingle().Which.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
            .Which.Offset.Should().Be(Daml.Runtime.LedgerOffset.At(77L));
    }

    private static Daml.Runtime.LedgerOffset? SnapshotOffsetOf(ActiveContractScenario scenario) =>
        scenario.SnapshotOffset is { } offset ? Daml.Runtime.LedgerOffset.At(offset) : null;

    private static Update UpdateFrom(string json) =>
        JsonSerializer.Deserialize<GetUpdatesResponse>(json, RestRefitSettings.SerializerOptions)!.Update;

    private string TransactionJson(TransactionEventScenario scenario)
    {
        var trailing = scenario.FollowedByMatchingCreated
            ? $", {{\"CreatedEvent\": {CreatedEventJson(ActiveContractScenario.MatchingEntityName, TransactionEventScenario.TrailingEventOffset, omitTemplateId: false, scenario.InterfaceView)}}}"
            : string.Empty;

        return $$"""
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "{{Wire(TransactionEventScenario.TransactionOffset)}}",
                    {{OptionalField("synchronizerId", scenario.Synchronizer)}}
                    "events": [{{TransactionEventJson(scenario)}}{{trailing}}]
                  }
                }
              }
            }
            """;
    }

    private string TransactionEventJson(TransactionEventScenario scenario)
    {
        long? eventOffset = scenario.OmitEventOffset ? null : TransactionEventScenario.EventOffset;
        return scenario.Event switch
        {
            TransactionEventShape.Created =>
                $$"""
                {"CreatedEvent": {{CreatedEventJson(scenario.EntityName, eventOffset, scenario.OmitTemplateId, scenario.InterfaceView, scenario.OmitCreateArguments)}}}
                """,
            TransactionEventShape.Archived =>
                $$"""
                {
                  "ArchivedEvent": {
                    {{OffsetField(eventOffset)}}
                    "nodeId": 0,
                    "contractId": "{{ActiveContractScenario.ContractId}}",
                    {{TemplateIdField(scenario.EntityName, scenario.OmitTemplateId)}}
                    {{ImplementedInterfacesField(scenario.ImplementsSubscribedInterface)}}
                    {{WitnessPartiesField}}
                  }
                }
                """,
            TransactionEventShape.Exercised =>
                $$"""
                {
                  "ExercisedEvent": {
                    {{OffsetField(eventOffset)}}
                    "nodeId": 0,
                    "contractId": "{{ActiveContractScenario.ContractId}}",
                    {{TemplateIdField(scenario.EntityName, scenario.OmitTemplateId)}}
                    {{ImplementedInterfacesField(scenario.ImplementsSubscribedInterface)}}
                    "choice": "{{TransactionEventScenario.ChoiceName}}",
                    "choiceArgument": "{{TransactionEventScenario.ChoiceArgumentValue}}",
                    "exerciseResult": "{{TransactionEventScenario.ExerciseResultValue}}",
                    "consuming": true,
                    {{WitnessPartiesField}}
                  }
                }
                """,
            TransactionEventShape.Empty => "{}",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private string ReassignmentJson(ReassignmentEventScenario scenario)
    {
        var trailing = scenario.FollowedByMatchingUnassigned
            ? ", " + UnassignedEventJson(
                new ReassignmentEventScenario { Event = ReassignmentEventShape.Unassigned },
                ReassignmentEventScenario.TrailingEventOffset)
            : string.Empty;

        return $$"""
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "{{Wire(ReassignmentEventScenario.ReassignmentOffset)}}",
                    "events": [{{ReassignmentEventJson(scenario)}}{{trailing}}]
                  }
                }
              }
            }
            """;
    }

    private string ReassignmentEventJson(ReassignmentEventScenario scenario)
    {
        long? eventOffset = scenario.OmitEventOffset ? null : ReassignmentEventScenario.EventOffset;
        return scenario.Event switch
        {
            ReassignmentEventShape.Assigned =>
                $$"""
                {
                  "JsAssignmentEvent": {
                    {{OptionalField("source", scenario.Source)}}
                    {{OptionalField("target", scenario.Target)}}
                    "reassignmentId": "{{ReassignmentEventScenario.ReassignmentId}}",
                    "reassignmentCounter": "{{Wire(ReassignmentEventScenario.ReassignmentCounter)}}"
                    {{(scenario.OmitCreatedEvent
                        ? string.Empty
                        : ", \"createdEvent\": " + CreatedEventJson(
                            scenario.EntityName,
                            eventOffset,
                            scenario.OmitTemplateId,
                            scenario.InterfaceView,
                            scenario.OmitCreateArguments))}}
                  }
                }
                """,
            ReassignmentEventShape.Unassigned => UnassignedEventJson(scenario, eventOffset),
            ReassignmentEventShape.Empty => "{}",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private string UnassignedEventJson(ReassignmentEventScenario scenario, long? offset) =>
        $$"""
        {
          "JsUnassignedEvent": {
            "value": {
              {{OptionalField("source", scenario.Source)}}
              {{OptionalField("target", scenario.Target)}}
              "contractId": "{{ActiveContractScenario.ContractId}}",
              {{TemplateIdField(scenario.EntityName, scenario.OmitTemplateId)}}
              "reassignmentId": "{{ReassignmentEventScenario.ReassignmentId}}",
              "reassignmentCounter": "{{Wire(ReassignmentEventScenario.ReassignmentCounter)}}",
              {{OffsetField(offset)}}
              {{WitnessPartiesField}}
            }
          }
        }
        """;

    private string CreatedEventJson(
        string entityName,
        long? offset,
        bool omitTemplateId,
        InterfaceViewRendering interfaceView = InterfaceViewRendering.None,
        bool omitCreateArguments = false) =>
        $$"""
        {
          {{OffsetField(offset)}}
          "nodeId": 0,
          "contractId": "{{ActiveContractScenario.ContractId}}",
          {{TemplateIdField(entityName, omitTemplateId)}}
          {{(omitCreateArguments ? string.Empty : $"\"createArgument\": {PayloadRecordJson(ActiveContractScenario.CreateArgumentValue)},")}}
          "contractKey": "{{ActiveContractScenario.ContractKeyParty}}",
          "contractKeyHash": "{{ActiveContractScenario.ContractKeyHashBase64}}",
          {{InterfaceViewsField(interfaceView)}}
          {{WitnessPartiesField}}
        }
        """;

    private static string WitnessPartiesField =>
        $"\"witnessParties\": [\"{ActiveContractScenario.FirstWitnessParty}\", \"{ActiveContractScenario.SecondWitnessParty}\"]";

    private static string ImplementedInterfacesField(bool implementsSubscribedInterface) =>
        implementsSubscribedInterface ? $"\"implementedInterfaces\": [{SubscribedInterfaceIdJson}]," : string.Empty;

    private static string TemplateIdField(string entityName, bool omitTemplateId) =>
        omitTemplateId
            ? string.Empty
            : $$"""
              "templateId": {
                "packageId": "tmpl-pkg",
                "moduleName": "{{ActiveContractScenario.ModuleName}}",
                "entityName": "{{entityName}}"
              },
              """;

    private string OffsetField(long? offset) =>
        offset is { } value
            ? $"\"offset\": \"{Wire(value)}\","
            : omittedOffsetWire == OmittedOffsetWire.Zero ? "\"offset\": \"0\"," : string.Empty;

    private static string Wire(long value) => value.ToString(CultureInfo.InvariantCulture);

    private async Task<GetActiveContractsResponse> ActiveContractsResponseAsync(ActiveContractScenario scenario)
    {
        var (api, transport) = RestApiFactory.Build<IStateServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, BuildResponseJson(scenario));
        return await api.GetActiveContracts(new GetActiveContractsRequest(), TestContext.Current.CancellationToken);
    }

    private string BuildResponseJson(ActiveContractScenario scenario)
    {
        var createdEntry = scenario.OmitCreatedEvent
            ? string.Empty
            : $"\"createdEvent\": {CreatedEventJson(scenario)},";

        return scenario.Entry switch
        {
            ActiveContractEntry.Active =>
                $$"""
                {
                  "contractEntry": {
                    "JsActiveContract": {
                      {{createdEntry}}
                      {{OptionalField("synchronizerId", scenario.Synchronizer)}}
                      "reassignmentCounter": "0"
                    }
                  }
                }
                """,
            ActiveContractEntry.IncompleteUnassigned =>
                $$"""
                {
                  "contractEntry": {
                    "JsIncompleteUnassigned": {
                      {{createdEntry}}
                      "unassignedEvent": {
                        {{OptionalField("contractId", scenario.OmitUnassignedContractId ? null : ActiveContractScenario.ContractId)}}
                        {{OptionalField("source", scenario.Synchronizer)}}
                        "target": "{{ActiveContractScenario.CounterpartSynchronizerId}}",
                        {{OffsetField(scenario.OmitUnassignedEventOffset ? null : ActiveContractScenario.UnassignedOffset)}}
                        "reassignmentId": "{{ActiveContractScenario.ReassignmentId}}",
                        "reassignmentCounter": "{{ActiveContractScenario.ReassignmentCounter.ToString(CultureInfo.InvariantCulture)}}"
                      }
                    }
                  }
                }
                """,
            ActiveContractEntry.IncompleteAssigned =>
                $$"""
                {
                  "contractEntry": {
                    "JsIncompleteAssigned": {
                      "assignedEvent": {
                        {{createdEntry}}
                        "source": "{{ActiveContractScenario.CounterpartSynchronizerId}}",
                        {{OptionalField("target", scenario.Synchronizer)}}
                        "reassignmentCounter": "0"
                      }
                    }
                  }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static string OptionalField(string name, string? value) =>
        value is null ? string.Empty : $"\"{name}\": \"{value}\",";

    private string CreatedEventJson(ActiveContractScenario scenario) =>
        $$"""
        {
          {{OffsetField(scenario.OmitCreatedEventOffset ? null : ActiveContractScenario.CreatedOffset)}}
          "nodeId": 0,
          "contractId": "{{ActiveContractScenario.ContractId}}",
          "templateId": {
            "packageId": "tmpl-pkg",
            "moduleName": "{{ActiveContractScenario.ModuleName}}",
            "entityName": "{{scenario.EntityName}}"
          },
          "createArgument": {{PayloadRecordJson(ActiveContractScenario.CreateArgumentValue)}},
          {{InterfaceViewsField(scenario.InterfaceView)}}
          "witnessParties": []
        }
        """;

    private static string InterfaceViewsField(InterfaceViewRendering rendering) =>
        InterfaceViewJson(rendering) is { } view ? $"\"interfaceViews\": [{view}]," : string.Empty;

    private static string? InterfaceViewJson(InterfaceViewRendering rendering) => rendering switch
    {
        InterfaceViewRendering.None => null,
        InterfaceViewRendering.Computed =>
            $$"""
            {
              "interfaceId": {{SubscribedInterfaceIdJson}},
              "viewStatus": {{ViewStatusJson(ActiveContractScenario.ComputedViewStatusCode)}},
              "viewValue": {{PayloadRecordJson(ActiveContractScenario.InterfaceViewValue)}}
            }
            """,
        InterfaceViewRendering.ComputationFailed =>
            $$"""
            {
              "interfaceId": {{SubscribedInterfaceIdJson}},
              "viewStatus": {{ViewStatusJson(ActiveContractScenario.FailedViewStatusCode)}}
            }
            """,
        InterfaceViewRendering.ValueOmitted =>
            $$"""
            {
              "interfaceId": {{SubscribedInterfaceIdJson}},
              "viewStatus": {{ViewStatusJson(ActiveContractScenario.ComputedViewStatusCode)}}
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(rendering)),
    };

    private static string SubscribedInterfaceIdJson =>
        $$"""
        {
          "packageId": "{{ActiveContractScenario.InterfacePackageId}}",
          "moduleName": "{{ActiveContractScenario.InterfaceModuleName}}",
          "entityName": "{{ActiveContractScenario.InterfaceEntityName}}"
        }
        """;

    private static string ViewStatusJson(int code) =>
        $$"""
        {
          "code": {{code.ToString(CultureInfo.InvariantCulture)}},
          "message": ""
        }
        """;

    private static string PayloadRecordJson(string value) =>
        $$"""
        {
          "{{ActiveContractScenario.OwnerFieldName}}": "{{ActiveContractScenario.OwnerParty}}",
          "{{ActiveContractScenario.PayloadFieldName}}": "{{value}}"
        }
        """;
}
