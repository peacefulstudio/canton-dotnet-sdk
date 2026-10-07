// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Streams;
using Google.Rpc;
using Microsoft.Extensions.Logging;
using Xunit;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using InterfaceEvent = Daml.Runtime.Streams.InterfaceStreamEvent<
    Canton.Ledger.Testing.Helpers.InterfaceMarker, Canton.Ledger.Testing.Helpers.InterfaceMarkerView>;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcContractStreamProjectorParityTests : ContractStreamProjectorParityTests
{
    private const long UnsetOffset = 0L;

    private long omittedCreatedEventOffset = UnsetOffset;

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectActiveContractEntryAsync(
        ActiveContractScenario scenario)
    {
        var response = BuildResponse(scenario);
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected =
            GrpcContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response, logger: null, SnapshotOffsetOf(scenario)).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>>> ProjectActiveContractEntryAsInterfaceAsync(
        ActiveContractScenario scenario)
    {
        var response = BuildResponse(scenario);
        IReadOnlyList<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>> projected =
            GrpcInterfaceStreamProjector.ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response, logger: null, SnapshotOffsetOf(scenario)).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectTransactionEventsAsync(
        TransactionEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected = GrpcContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(BuildTransaction(scenario)).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectReassignmentEventsAsync(
        ReassignmentEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<TemplateMarker>> projected = GrpcContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(BuildReassignment(scenario)).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<InterfaceEvent>> ProjectTransactionEventsAsInterfaceAsync(
        TransactionEventScenario scenario, ILogger logger)
    {
        IReadOnlyList<InterfaceEvent> projected = GrpcInterfaceStreamProjector
            .ProjectTransactionEvents<InterfaceMarker, InterfaceMarkerView>(BuildTransaction(scenario), logger).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<InterfaceEvent>> ProjectReassignmentEventsAsInterfaceAsync(
        ReassignmentEventScenario scenario, ILogger logger)
    {
        IReadOnlyList<InterfaceEvent> projected = GrpcInterfaceStreamProjector
            .ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(BuildReassignment(scenario), logger).ToList();
        return Task.FromResult(projected);
    }

    protected override Task<IReadOnlyList<ContractStreamEvent<InterfaceKindTemplateMarker>>> ProjectTransactionEventsAsInterfaceKindTemplateAsync(
        TransactionEventScenario scenario)
    {
        IReadOnlyList<ContractStreamEvent<InterfaceKindTemplateMarker>> projected = GrpcContractStreamProjector
            .ProjectTransactionEvents<InterfaceKindTemplateMarker>(BuildTransaction(scenario)).ToList();
        return Task.FromResult(projected);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntry_reports_a_Created_with_a_negative_event_offset_as_a_DecodeFailure_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        omittedCreatedEventOffset = -1L;

        var projected = await ProjectActiveContractEntryAsync(new ActiveContractScenario
        {
            Entry = entry,
            OmitCreatedEventOffset = true,
            SnapshotOffset = 77L,
        });

        var failure = projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        failure.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        failure.Offset.Should().Be(Daml.Runtime.LedgerOffset.At(expectedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntryAsInterface_reports_a_Created_with_a_negative_event_offset_as_a_DecodeFailure_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        omittedCreatedEventOffset = -1L;

        var projected = await ProjectActiveContractEntryAsInterfaceAsync(new ActiveContractScenario
        {
            Entry = entry,
            OmitCreatedEventOffset = true,
            SnapshotOffset = 77L,
            InterfaceView = InterfaceViewRendering.Computed,
        });

        var failure = projected[0].Should().BeOfType<InterfaceEvent.Unclassified>().Subject;
        failure.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        failure.Offset.Should().Be(Daml.Runtime.LedgerOffset.At(expectedOffset));
    }

    private static Daml.Runtime.LedgerOffset? SnapshotOffsetOf(ActiveContractScenario scenario) =>
        scenario.SnapshotOffset is { } offset ? Daml.Runtime.LedgerOffset.At(offset) : null;

    private static Transaction BuildTransaction(TransactionEventScenario scenario)
    {
        var transaction = new Transaction
        {
            Offset = TransactionEventScenario.TransactionOffset,
            SynchronizerId = scenario.Synchronizer ?? string.Empty,
        };
        transaction.Events.Add(BuildTransactionEvent(scenario));
        if (scenario.FollowedByMatchingCreated)
        {
            transaction.Events.Add(new Event
            {
                Created = MatchingCreatedEvent(
                    ActiveContractScenario.MatchingEntityName,
                    TransactionEventScenario.TrailingEventOffset,
                    interfaceView: scenario.InterfaceView),
            });
        }
        return transaction;
    }

    private static Event BuildTransactionEvent(TransactionEventScenario scenario)
    {
        var templateId = scenario.OmitTemplateId ? null : TemplateIdFor(scenario.EntityName);
        var eventOffset = scenario.OmitEventOffset ? UnsetOffset : TransactionEventScenario.EventOffset;
        return scenario.Event switch
        {
            TransactionEventShape.Created => new Event
            {
                Created = MatchingCreatedEvent(
                    scenario.EntityName,
                    eventOffset,
                    scenario.OmitTemplateId,
                    scenario.InterfaceView,
                    scenario.OmitCreateArguments),
            },
            TransactionEventShape.Archived => new Event
            {
                Archived = WithImplementedInterfaces(
                    new ProtoArchivedEvent
                    {
                        ContractId = ActiveContractScenario.ContractId,
                        TemplateId = templateId,
                        Offset = eventOffset,
                    },
                    scenario.ImplementsSubscribedInterface),
            },
            TransactionEventShape.Exercised => new Event
            {
                Exercised = WithImplementedInterfaces(
                    new ProtoExercisedEvent
                    {
                        ContractId = ActiveContractScenario.ContractId,
                        TemplateId = templateId,
                        Choice = TransactionEventScenario.ChoiceName,
                        ChoiceArgument = new ProtoValue { Text = TransactionEventScenario.ChoiceArgumentValue },
                        ExerciseResult = new ProtoValue { Text = TransactionEventScenario.ExerciseResultValue },
                        Consuming = true,
                        Offset = eventOffset,
                    },
                    scenario.ImplementsSubscribedInterface),
            },
            TransactionEventShape.Empty => new Event(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static ProtoArchivedEvent WithImplementedInterfaces(ProtoArchivedEvent archived, bool implementsSubscribedInterface)
    {
        archived.WitnessParties.AddRange(WitnessParties);
        if (implementsSubscribedInterface)
        {
            archived.ImplementedInterfaces.Add(SubscribedInterfaceId);
        }
        return archived;
    }

    private static ProtoExercisedEvent WithImplementedInterfaces(ProtoExercisedEvent exercised, bool implementsSubscribedInterface)
    {
        exercised.WitnessParties.AddRange(WitnessParties);
        if (implementsSubscribedInterface)
        {
            exercised.ImplementedInterfaces.Add(SubscribedInterfaceId);
        }
        return exercised;
    }

    private static string[] WitnessParties =>
        [ActiveContractScenario.FirstWitnessParty, ActiveContractScenario.SecondWitnessParty];

    private static Reassignment BuildReassignment(ReassignmentEventScenario scenario)
    {
        var reassignment = new Reassignment { Offset = ReassignmentEventScenario.ReassignmentOffset };
        reassignment.Events.Add(BuildReassignmentEvent(scenario));
        if (scenario.FollowedByMatchingUnassigned)
        {
            reassignment.Events.Add(new ReassignmentEvent
            {
                Unassigned = BuildUnassignedEvent(
                    new ReassignmentEventScenario { Event = ReassignmentEventShape.Unassigned },
                    ReassignmentEventScenario.TrailingEventOffset),
            });
        }
        return reassignment;
    }

    private static ReassignmentEvent BuildReassignmentEvent(ReassignmentEventScenario scenario)
    {
        var eventOffset = scenario.OmitEventOffset ? UnsetOffset : ReassignmentEventScenario.EventOffset;
        return scenario.Event switch
        {
            ReassignmentEventShape.Assigned => new ReassignmentEvent
            {
                Assigned = new AssignedEvent
                {
                    Source = scenario.Source ?? string.Empty,
                    Target = scenario.Target ?? string.Empty,
                    ReassignmentId = ReassignmentEventScenario.ReassignmentId,
                    ReassignmentCounter = (ulong)ReassignmentEventScenario.ReassignmentCounter,
                    CreatedEvent = scenario.OmitCreatedEvent
                        ? null
                        : MatchingCreatedEvent(
                            scenario.EntityName,
                            eventOffset,
                            scenario.OmitTemplateId,
                            scenario.InterfaceView,
                            scenario.OmitCreateArguments),
                },
            },
            ReassignmentEventShape.Unassigned => new ReassignmentEvent
            {
                Unassigned = BuildUnassignedEvent(scenario, eventOffset),
            },
            ReassignmentEventShape.Empty => new ReassignmentEvent(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static UnassignedEvent BuildUnassignedEvent(ReassignmentEventScenario scenario, long offset)
    {
        var unassigned = new UnassignedEvent
        {
            ContractId = ActiveContractScenario.ContractId,
            TemplateId = scenario.OmitTemplateId ? null : TemplateIdFor(scenario.EntityName),
            Source = scenario.Source ?? string.Empty,
            Target = scenario.Target ?? string.Empty,
            Offset = offset,
            ReassignmentId = ReassignmentEventScenario.ReassignmentId,
            ReassignmentCounter = (ulong)ReassignmentEventScenario.ReassignmentCounter,
        };
        unassigned.WitnessParties.AddRange(WitnessParties);
        return unassigned;
    }

    private static ProtoCreatedEvent MatchingCreatedEvent(
        string entityName,
        long offset,
        bool omitTemplateId = false,
        InterfaceViewRendering interfaceView = InterfaceViewRendering.None,
        bool omitCreateArguments = false)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = ActiveContractScenario.ContractId,
            TemplateId = omitTemplateId ? null : TemplateIdFor(entityName),
            CreateArguments = omitCreateArguments ? null : PayloadRecord(ActiveContractScenario.CreateArgumentValue),
            ContractKey = new ProtoValue { Party = ActiveContractScenario.ContractKeyParty },
            ContractKeyHash = Google.Protobuf.ByteString.FromBase64(ActiveContractScenario.ContractKeyHashBase64),
            Offset = offset,
        };
        created.WitnessParties.AddRange(WitnessParties);
        if (BuildInterfaceView(interfaceView) is { } view)
        {
            created.InterfaceViews.Add(view);
        }
        return created;
    }

    private static ProtoIdentifier TemplateIdFor(string entityName) => new()
    {
        PackageId = "tmpl-pkg",
        ModuleName = ActiveContractScenario.ModuleName,
        EntityName = entityName,
    };

    private GetActiveContractsResponse BuildResponse(ActiveContractScenario scenario)
    {
        var created = scenario.OmitCreatedEvent ? null : BuildCreatedEvent(scenario);
        return scenario.Entry switch
        {
            ActiveContractEntry.Active => new GetActiveContractsResponse
            {
                ActiveContract = new ActiveContract
                {
                    CreatedEvent = created,
                    SynchronizerId = scenario.Synchronizer ?? string.Empty,
                },
            },
            ActiveContractEntry.IncompleteUnassigned => new GetActiveContractsResponse
            {
                IncompleteUnassigned = new IncompleteUnassigned
                {
                    CreatedEvent = created,
                    UnassignedEvent = new UnassignedEvent
                    {
                        ContractId = scenario.OmitUnassignedContractId ? string.Empty : ActiveContractScenario.ContractId,
                        Source = scenario.Synchronizer ?? string.Empty,
                        Target = ActiveContractScenario.CounterpartSynchronizerId,
                        Offset = scenario.OmitUnassignedEventOffset ? UnsetOffset : ActiveContractScenario.UnassignedOffset,
                        ReassignmentId = ActiveContractScenario.ReassignmentId,
                        ReassignmentCounter = (ulong)ActiveContractScenario.ReassignmentCounter,
                    },
                },
            },
            ActiveContractEntry.IncompleteAssigned => new GetActiveContractsResponse
            {
                IncompleteAssigned = new IncompleteAssigned
                {
                    AssignedEvent = new AssignedEvent
                    {
                        CreatedEvent = created,
                        Source = ActiveContractScenario.CounterpartSynchronizerId,
                        Target = scenario.Synchronizer ?? string.Empty,
                    },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private ProtoCreatedEvent BuildCreatedEvent(ActiveContractScenario scenario)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = ActiveContractScenario.ContractId,
            TemplateId = new ProtoIdentifier
            {
                PackageId = "tmpl-pkg",
                ModuleName = ActiveContractScenario.ModuleName,
                EntityName = scenario.EntityName,
            },
            CreateArguments = PayloadRecord(ActiveContractScenario.CreateArgumentValue),
            Offset = scenario.OmitCreatedEventOffset ? omittedCreatedEventOffset : ActiveContractScenario.CreatedOffset,
        };
        if (BuildInterfaceView(scenario.InterfaceView) is { } interfaceView)
        {
            created.InterfaceViews.Add(interfaceView);
        }
        return created;
    }

    private static InterfaceView? BuildInterfaceView(InterfaceViewRendering rendering) => rendering switch
    {
        InterfaceViewRendering.None => null,
        InterfaceViewRendering.Computed => new InterfaceView
        {
            InterfaceId = SubscribedInterfaceId,
            ViewStatus = new Status { Code = ActiveContractScenario.ComputedViewStatusCode },
            ViewValue = PayloadRecord(ActiveContractScenario.InterfaceViewValue),
        },
        InterfaceViewRendering.ComputationFailed => new InterfaceView
        {
            InterfaceId = SubscribedInterfaceId,
            ViewStatus = new Status { Code = ActiveContractScenario.FailedViewStatusCode },
        },
        InterfaceViewRendering.ValueOmitted => new InterfaceView
        {
            InterfaceId = SubscribedInterfaceId,
            ViewStatus = new Status { Code = ActiveContractScenario.ComputedViewStatusCode },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(rendering)),
    };

    private static ProtoIdentifier SubscribedInterfaceId => new()
    {
        PackageId = ActiveContractScenario.InterfacePackageId,
        ModuleName = ActiveContractScenario.InterfaceModuleName,
        EntityName = ActiveContractScenario.InterfaceEntityName,
    };

    private static ProtoRecord PayloadRecord(string value) => new()
    {
        Fields =
        {
            new RecordField
            {
                Label = ActiveContractScenario.OwnerFieldName,
                Value = new ProtoValue { Party = ActiveContractScenario.OwnerParty },
            },
            new RecordField
            {
                Label = ActiveContractScenario.PayloadFieldName,
                Value = new ProtoValue { Text = value },
            },
        },
    };
}
