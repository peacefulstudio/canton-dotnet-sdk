// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class InterfaceLedgerEventsTests
{
    private static readonly Party Bob = new("bob");

    [Fact]
    public void InterfaceLedgerEvents_Created_wraps_the_InterfaceAcsSnapshotEntry_Created_variant()
    {
        var view = new DemoHoldingView(42.5m);

        var created = InterfaceLedgerEvents.Created<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid1"), view, key: null, LedgerOffset.At(1), (SynchronizerId)"sync1", [Bob]);

        var entry = created.Should().BeOfType<InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Created>().Subject;
        entry.ContractId.Should().Be(new ContractId<IDemoHoldingView>("cid1"));
        entry.Payload.Should().BeSameAs(view);
        entry.Key.Should().BeNull();
        entry.Offset.Should().Be(LedgerOffset.At(1));
        entry.SynchronizerId.Should().Be((SynchronizerId)"sync1");
        entry.WitnessParties.Should().ContainSingle().Which.Should().Be(Bob);
    }

    [Fact]
    public void InterfaceLedgerEvents_Checkpoint_wraps_the_offset_in_a_StakeholderResume()
    {
        var checkpoint = InterfaceLedgerEvents.Checkpoint<IDemoHoldingView, DemoHoldingView>(LedgerOffset.At(9));

        checkpoint.Should().BeOfType<InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Checkpoint>()
            .Which.Resume.Offset.Should().Be(LedgerOffset.At(9));
    }

    [Fact]
    public void InterfaceLedgerEvents_StreamError_defaults_category_errorId_and_source_exception_to_null()
    {
        var error = InterfaceLedgerEvents.StreamError<IDemoHoldingView, DemoHoldingView>(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "boom");

        var entry = error.Should().BeOfType<InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.StreamError>().Subject;
        entry.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        entry.Message.Should().Be("boom");
        entry.Category.Should().BeNull();
        entry.ErrorId.Should().BeNull();
        entry.SourceException.Should().BeNull();
    }

    [Fact]
    public void InterfaceLedgerEvents_StreamError_carries_the_category_errorId_and_source_exception()
    {
        var source = new InvalidOperationException("transport died");

        var error = InterfaceLedgerEvents.StreamError<IDemoHoldingView, DemoHoldingView>(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION",
            source);

        var entry = error.Should().BeOfType<InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.StreamError>().Subject;
        entry.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        entry.ErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
        entry.SourceException.Should().BeSameAs(source);
    }

    [Fact]
    public void InterfaceLedgerEvents_Unclassified_wraps_the_InterfaceAcsSnapshotEntry_Unclassified_variant()
    {
        var unclassified = InterfaceLedgerEvents.Unclassified<IDemoHoldingView, DemoHoldingView>(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, "unmapped-row");

        var entry = unclassified.Should().BeOfType<InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Unclassified>().Subject;
        entry.Offset.Should().Be(LedgerOffset.At(7));
        entry.Kind.Should().Be(UnclassifiedKind.Unknown);
        entry.RawKind.Should().Be("unmapped-row");
    }

    [Fact]
    public void InterfaceLedgerEvents_Unclassified_accepts_a_null_offset_and_an_enumerated_kind_without_a_raw_kind()
    {
        var unclassified = InterfaceLedgerEvents.Unclassified<IDemoHoldingView, DemoHoldingView>(
            null, UnclassifiedKind.InterfaceViewUnavailable);

        var entry = unclassified.Should().BeOfType<InterfaceAcsSnapshotEntry<IDemoHoldingView, DemoHoldingView>.Unclassified>().Subject;
        entry.Offset.Should().BeNull();
        entry.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
        entry.RawKind.Should().BeNull();
    }
}
