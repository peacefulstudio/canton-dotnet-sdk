// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Google.Rpc;
using System.Text.Json;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

/// <summary>
/// A hand-authored <see cref="ITemplate"/> that reports <see cref="DamlTypeKind.Interface"/> on
/// its <see cref="DamlTypeId"/>. No code generator ever emits this shape — every generated
/// template reports <see cref="DamlTypeKind.Template"/> — but the interface contract does not
/// forbid it, so <c>GrpcContractStreamProjector</c>'s defensive interface-view branch for a
/// template marker is reachable through this legally-constructed type, without reflection or
/// <c>InternalsVisibleTo</c>.
/// </summary>
internal sealed record MismatchedKindMarker(string Owner) : ITemplate, IDamlRecord<MismatchedKindMarker>
{
    public static RuntimeIdentifier TemplateId { get; } = new("mismatched-pkg", "Mismatched.Token", "IMismatched");
    public static string PackageId => "mismatched-pkg";
    public static string PackageName => "mismatched-api";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Interface, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", new DamlParty(Owner)));

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => throw new NotSupportedException();
    public static MismatchedKindMarker FromRecord(DamlRecord record) =>
        new(record.GetRequiredField("owner").As<DamlParty>().Value);
}

public class GrpcContractStreamProjectorInterfaceKindMarkerTests
{
    private static readonly ProtoIdentifier MatchingInterfaceId =
        new() { PackageId = "mismatched-view-pkg", ModuleName = "Mismatched.Token", EntityName = "IMismatched" };

    private static InterfaceView UndecodableMatchingView() => new()
    {
        InterfaceId = MatchingInterfaceId,
        ViewStatus = new Status { Code = 2 },
    };

    private static InterfaceView DecodableMatchingView(string ownerParty) => new()
    {
        InterfaceId = MatchingInterfaceId,
        ViewStatus = new Status { Code = 0 },
        ViewValue = new Com.Daml.Ledger.Api.V2.Record
        {
            Fields = { new RecordField { Label = "owner", Value = new ProtoValue { Party = ownerParty } } },
        },
    };

    [Fact]
    public void ProjectReassignmentEvents_Assigned_reports_InterfaceViewUnavailable_when_the_matching_view_is_undecodable_for_an_interface_kind_template_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00mismatched",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Impl.Module", EntityName = "Impl" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = 15L,
        };
        created.InterfaceViews.Add(UndecodableMatchingView());
        var reassignment = new Reassignment { Offset = 77L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = "sync-src",
                Target = "sync-tgt",
                CreatedEvent = created,
            },
        });

        var events = GrpcContractStreamProjector.ProjectReassignmentEvents<MismatchedKindMarker>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<MismatchedKindMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(15L), "the created event carries its own offset ahead of the reassignment offset");
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public void ProjectTransactionEvents_Created_reports_InterfaceViewUnavailable_when_the_matching_view_is_undecodable_for_an_interface_kind_template_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00mismatched",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Impl.Module", EntityName = "Impl" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = 20L,
        };
        created.InterfaceViews.Add(UndecodableMatchingView());
        var transaction = new Transaction { Offset = 99L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = GrpcContractStreamProjector.ProjectTransactionEvents<MismatchedKindMarker>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<MismatchedKindMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(20L), "the Created path resolves the view before the outer catch could fall back to the transaction offset");
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public void ProjectTransactionEvents_Created_surfaces_a_matching_interface_view_without_create_arguments_as_DecodeFailure_for_an_interface_kind_template_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00mismatched",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Impl.Module", EntityName = "Impl" },
            Offset = 25L,
        };
        created.InterfaceViews.Add(DecodableMatchingView("view-party"));
        var transaction = new Transaction { Offset = 77L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });
        var loggerFactory = new CapturingLoggerFactory();

        var events = GrpcContractStreamProjector
            .ProjectTransactionEvents<MismatchedKindMarker>(transaction, loggerFactory.CreateLogger("test"))
            .ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<MismatchedKindMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
            .Which.Exception.Should().BeOfType<MalformedResponseException>()
            .Which.Message.Should().Be("Malformed response from ledger: CreatedEvent for contract '00mismatched' has no create_arguments, though the Ledger API marks the field as required.");
    }

    [Fact]
    public void ProjectTransactionEvents_Created_decodes_Payload_from_the_matching_interface_view_rather_than_create_arguments_for_an_interface_kind_template_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00mismatched",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Impl.Module", EntityName = "Impl" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = 30L,
        };
        created.InterfaceViews.Add(DecodableMatchingView("view-party"));
        var transaction = new Transaction { Offset = 30L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = GrpcContractStreamProjector.ProjectTransactionEvents<MismatchedKindMarker>(transaction).ToList();

        var typed = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<MismatchedKindMarker>.Created>().Subject;
        typed.Payload.Owner.Should().Be(
            "view-party", "the interface-kind branch decodes the payload from the matching view, not from create_arguments");
    }
}
