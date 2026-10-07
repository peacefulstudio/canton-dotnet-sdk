// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Streams;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Streams;

public class InterfaceSnapshotEntryArmsTests
{
    private static readonly ContractId<InterfaceMarker> Id = new("00cid");
    private static readonly InterfaceMarkerView View = new("12.5");
    private static readonly ContractKey Key = new(new DamlText("key-1"));
    private static readonly SynchronizerId Synchronizer = new("sync::one");
    private static readonly SynchronizerId Other = new("sync::two");
    private static readonly EquatableArray<Party> Witnesses = EquatableArray.Create([(Party)"alice", (Party)"bob"]);

    [Fact]
    public void From_a_created_event_keeps_every_field_and_attaches_the_disclosure()
    {
        var disclosure = new DisclosedContract("00cid", new Identifier("pkg", "Mod", "Ent"), new byte[] { 1, 2, 3 });
        var created = new InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created(
            Id, View, Key, LedgerOffset.At(7), Synchronizer, Witnesses);

        var entry = InterfaceSnapshotEntryArms<InterfaceMarker, InterfaceMarkerView>.From(created, disclosure);

        var snapshotCreated = entry
            .Should().BeOfType<InterfaceAcsSnapshotEntry<InterfaceMarker, InterfaceMarkerView>.Created>().Subject;
        snapshotCreated.ContractId.Should().Be(Id);
        snapshotCreated.Payload.Should().Be(View);
        snapshotCreated.Key.Should().Be(Key);
        snapshotCreated.Offset.Should().Be(LedgerOffset.At(7));
        snapshotCreated.SynchronizerId.Should().Be(Synchronizer);
        snapshotCreated.WitnessParties.Should().Equal((Party)"alice", (Party)"bob");
        snapshotCreated.Disclosure.Should().Be(disclosure);
    }

    [Fact]
    public void From_a_created_event_without_a_disclosure_carries_none()
    {
        var created = new InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created(
            Id, View, null, LedgerOffset.At(7), Synchronizer, Witnesses);

        var entry = InterfaceSnapshotEntryArms<InterfaceMarker, InterfaceMarkerView>.From(created, disclosure: null);

        entry.Should().BeOfType<InterfaceAcsSnapshotEntry<InterfaceMarker, InterfaceMarkerView>.Created>()
            .Which.Disclosure.Should().BeNull();
    }

    [Fact]
    public void From_an_unassigned_event_downgrades_to_an_unclassified_unassigned_entry_at_its_offset()
    {
        var unassigned = new InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unassigned(
            Id, LedgerOffset.At(9), Synchronizer, Other, "reassign-1", 2, Witnesses);

        var entry = InterfaceSnapshotEntryArms<InterfaceMarker, InterfaceMarkerView>.From(unassigned, disclosure: null);

        var unclassified = entry
            .Should().BeOfType<InterfaceAcsSnapshotEntry<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(9));
        unclassified.Kind.Should().Be(UnclassifiedKind.UnassignedEvent);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void From_an_unclassified_event_keeps_its_offset_kind_and_raw_kind()
    {
        var unclassified = new InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified(
            LedgerOffset.At(12), UnclassifiedKind.Unknown, "Mystery");

        var entry = InterfaceSnapshotEntryArms<InterfaceMarker, InterfaceMarkerView>.From(unclassified, disclosure: null);

        entry.Should().Be(new InterfaceAcsSnapshotEntry<InterfaceMarker, InterfaceMarkerView>.Unclassified(
            LedgerOffset.At(12), UnclassifiedKind.Unknown, "Mystery"));
    }

    [Fact]
    public void From_an_unclassified_event_with_an_enumerated_kind_keeps_that_kind()
    {
        var unclassified = new InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified(
            LedgerOffset.At(12), UnclassifiedKind.InterfaceViewUnavailable);

        var entry = InterfaceSnapshotEntryArms<InterfaceMarker, InterfaceMarkerView>.From(unclassified, disclosure: null);

        entry.Should().Be(new InterfaceAcsSnapshotEntry<InterfaceMarker, InterfaceMarkerView>.Unclassified(
            LedgerOffset.At(12), UnclassifiedKind.InterfaceViewUnavailable));
    }

    [Fact]
    public void From_an_event_the_snapshot_never_produces_throws_naming_the_variant()
    {
        var archived = new InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Archived(
            Id, LedgerOffset.At(8), Synchronizer, Witnesses);

        var act = () => InterfaceSnapshotEntryArms<InterfaceMarker, InterfaceMarkerView>.From(archived, disclosure: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Active-contract snapshot produced an unexpected entry variant: Archived");
    }
}
