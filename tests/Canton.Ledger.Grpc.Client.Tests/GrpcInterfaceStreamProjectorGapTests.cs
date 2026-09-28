// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcInterfaceStreamProjectorGapTests
{
    private static readonly ProtoIdentifier MatchingInterfaceId =
        new() { PackageId = "iface-pkg", ModuleName = "Token.Api", EntityName = "IHolding" };

    private static ProtoCreatedEvent DecodableMatchingCreatedEvent(string contractId, long offset) => new()
    {
        ContractId = contractId,
        TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
        CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
        Offset = offset,
        InterfaceViews =
        {
            new InterfaceView
            {
                InterfaceId = MatchingInterfaceId,
                ViewStatus = new Google.Rpc.Status { Code = 0 },
                ViewValue = new Com.Daml.Ledger.Api.V2.Record
                {
                    Fields = { new RecordField { Label = "amount", Value = new ProtoValue { Text = "view-value" } } },
                },
            },
        },
    };

    [Fact]
    public void ProjectTransactionEvents_yields_Unclassified_for_an_event_carrying_no_recognized_variant()
    {
        var transaction = new Transaction { Offset = 50L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event());

        var events = GrpcInterfaceStreamProjector.ProjectTransactionEvents<InterfaceMarker, InterfaceMarkerView>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(50L));
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().Be(Event.EventOneofCase.None.ToString());
    }

    [Fact]
    public void ProjectReassignmentEvents_Assigned_reports_Unclassified_without_a_RawKind_when_the_created_event_is_missing()
    {
        var reassignment = new Reassignment { Offset = 60L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent { Source = "sync-src", Target = "sync-tgt" },
        });

        var events = GrpcInterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(60L));
        unclassified.Kind.Should().Be(UnclassifiedKind.AssignedEvent);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void ProjectReassignmentEvents_Assigned_is_refused_when_the_created_event_does_not_implement_the_interface_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00other",
            TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = 65L,
        };
        var reassignment = new Reassignment { Offset = 65L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent { Source = "sync-src", Target = "sync-tgt", CreatedEvent = created },
        });

        var events = GrpcInterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(65L));
        unclassified.Kind.Should().Be(UnclassifiedKind.AssignedEvent);
    }

    [Fact]
    public void ProjectReassignmentEvents_Unassigned_reports_MissingSynchronizerId_when_the_target_synchronizer_is_missing()
    {
        var reassignment = new Reassignment { Offset = 70L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Unassigned = new UnassignedEvent
            {
                ContractId = "00holding",
                TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
                Source = "sync-src",
                Target = string.Empty,
                Offset = 71L,
            },
        });

        var events = GrpcInterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(71L));
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    [Fact]
    public void ProjectReassignmentEvents_yields_Unclassified_for_a_reassignment_event_carrying_no_recognized_variant()
    {
        var reassignment = new Reassignment { Offset = 80L };
        reassignment.Events.Add(new ReassignmentEvent());

        var events = GrpcInterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(80L));
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().Be(ReassignmentEvent.EventOneofCase.None.ToString());
    }

    [Fact]
    public void ProjectActiveContractEntry_reports_an_entry_without_a_created_event_at_the_snapshot_offset()
    {
        var response = new GetActiveContractsResponse();

        var events = GrpcInterfaceStreamProjector
            .ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response, logger: null, LedgerOffset.At(90L))
            .ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(90L));
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().Be(GetActiveContractsResponse.ContractEntryOneofCase.None.ToString());
    }

    [Fact]
    public void ProjectActiveContractEntry_IncompleteUnassigned_reports_MissingSynchronizerId_when_the_target_is_missing()
    {
        var response = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = DecodableMatchingCreatedEvent("00holding", offset: 100L),
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = "00holding",
                    Source = "sync-src",
                    Target = string.Empty,
                    Offset = 101L,
                },
            },
        };

        var events = GrpcInterfaceStreamProjector.ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response).ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created>();
        var unclassified = events[1].Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(101L));
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    [Fact]
    public void ProjectActiveContractEntry_keeps_the_snapshot_running_when_an_incomplete_unassigned_carries_no_contract_id()
    {
        var response = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = DecodableMatchingCreatedEvent("00holding", offset: 42L),
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = string.Empty,
                    Source = "sync-1",
                    Target = "sync-2",
                    Offset = 43L,
                    ReassignmentId = "reassignment-1",
                    ReassignmentCounter = 7UL,
                },
            },
        };
        var loggerFactory = new CapturingLoggerFactory();

        var events = GrpcInterfaceStreamProjector
            .ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response, loggerFactory.CreateLogger("test"))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created>();
        var unclassified = events[1].Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(43L));
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }
}
