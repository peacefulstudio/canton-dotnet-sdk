// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class InterfaceEventsTests
{
    private static readonly Party Bob = new("bob");
    private static readonly ContractId<IDemoHoldingView> Cid = new("cid1");
    private static readonly SynchronizerId Source = (SynchronizerId)"src";
    private static readonly SynchronizerId Target = (SynchronizerId)"tgt";

    [Fact]
    public void InterfaceEvents_Created_wraps_the_InterfaceStreamEvent_Created_variant()
    {
        var view = new DemoHoldingView(42.5m);

        var created = InterfaceEvents.Created<IDemoHoldingView, DemoHoldingView>(
            Cid, view, key: null, LedgerOffset.At(1), (SynchronizerId)"sync1", [Bob]);

        var variant = created.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.Created>().Subject;
        variant.ContractId.Should().Be(Cid);
        variant.Payload.Should().BeSameAs(view);
        variant.Key.Should().BeNull();
        variant.Offset.Should().Be(LedgerOffset.At(1));
        variant.SynchronizerId.Should().Be((SynchronizerId)"sync1");
        variant.WitnessParties.Should().ContainSingle().Which.Should().Be(Bob);
    }

    [Fact]
    public void InterfaceEvents_Archived_wraps_the_InterfaceStreamEvent_Archived_variant()
    {
        var archived = InterfaceEvents.Archived<IDemoHoldingView, DemoHoldingView>(
            Cid, LedgerOffset.At(2), (SynchronizerId)"sync1", [Bob]);

        var variant = archived.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.Archived>().Subject;
        variant.ContractId.Should().Be(Cid);
        variant.Offset.Should().Be(LedgerOffset.At(2));
        variant.SynchronizerId.Should().Be((SynchronizerId)"sync1");
        variant.WitnessParties.Should().ContainSingle().Which.Should().Be(Bob);
    }

    [Fact]
    public void InterfaceEvents_Exercised_wraps_the_InterfaceStreamEvent_Exercised_variant()
    {
        var exercised = InterfaceEvents.Exercised<IDemoHoldingView, DemoHoldingView>(
            Cid,
            new ChoiceName("Transfer"),
            new DamlText("arg"),
            new DamlText("res"),
            consuming: true,
            LedgerOffset.At(3),
            (SynchronizerId)"sync1",
            [Bob]);

        var variant = exercised.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.Exercised>().Subject;
        variant.ContractId.Should().Be(Cid);
        variant.ChoiceName.Should().Be(new ChoiceName("Transfer"));
        variant.ChoiceArgument.Should().Be(new DamlText("arg"));
        variant.ExerciseResult.Should().Be(new DamlText("res"));
        variant.Consuming.Should().BeTrue();
        variant.Offset.Should().Be(LedgerOffset.At(3));
        variant.SynchronizerId.Should().Be((SynchronizerId)"sync1");
        variant.WitnessParties.Should().ContainSingle().Which.Should().Be(Bob);
    }

    [Fact]
    public void InterfaceEvents_Assigned_wraps_the_InterfaceStreamEvent_Assigned_variant()
    {
        var view = new DemoHoldingView(7m);

        var assigned = InterfaceEvents.Assigned<IDemoHoldingView, DemoHoldingView>(
            Cid, view, key: null, LedgerOffset.At(4), Source, Target, "rid-1", 5L, [Bob]);

        var variant = assigned.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.Assigned>().Subject;
        variant.ContractId.Should().Be(Cid);
        variant.Payload.Should().BeSameAs(view);
        variant.Key.Should().BeNull();
        variant.Offset.Should().Be(LedgerOffset.At(4));
        variant.Source.Should().Be(Source);
        variant.Target.Should().Be(Target);
        variant.ReassignmentId.Should().Be("rid-1");
        variant.ReassignmentCounter.Should().Be(5L);
        variant.WitnessParties.Should().ContainSingle().Which.Should().Be(Bob);
    }

    [Fact]
    public void InterfaceEvents_Unassigned_wraps_the_InterfaceStreamEvent_Unassigned_variant()
    {
        var unassigned = InterfaceEvents.Unassigned<IDemoHoldingView, DemoHoldingView>(
            Cid, LedgerOffset.At(5), Source, Target, "rid-2", 6L, [Bob]);

        var variant = unassigned.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.Unassigned>().Subject;
        variant.ContractId.Should().Be(Cid);
        variant.Offset.Should().Be(LedgerOffset.At(5));
        variant.Source.Should().Be(Source);
        variant.Target.Should().Be(Target);
        variant.ReassignmentId.Should().Be("rid-2");
        variant.ReassignmentCounter.Should().Be(6L);
        variant.WitnessParties.Should().ContainSingle().Which.Should().Be(Bob);
    }

    [Fact]
    public void InterfaceEvents_Checkpoint_wraps_the_InterfaceStreamEvent_Checkpoint_variant()
    {
        var checkpoint = InterfaceEvents.Checkpoint<IDemoHoldingView, DemoHoldingView>(LedgerOffset.At(9));

        checkpoint.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.Checkpoint>()
            .Which.Offset.Should().Be(LedgerOffset.At(9));
    }

    [Fact]
    public void InterfaceEvents_StreamError_defaults_category_errorId_and_source_exception_to_null()
    {
        var error = InterfaceEvents.StreamError<IDemoHoldingView, DemoHoldingView>(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "boom");

        var variant = error.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.StreamError>().Subject;
        variant.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        variant.Message.Should().Be("boom");
        variant.Category.Should().BeNull();
        variant.ErrorId.Should().BeNull();
        variant.SourceException.Should().BeNull();
    }

    [Fact]
    public void InterfaceEvents_StreamError_carries_the_category_errorId_and_source_exception()
    {
        var source = new InvalidOperationException("transport died");

        var error = InterfaceEvents.StreamError<IDemoHoldingView, DemoHoldingView>(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION",
            source);

        var variant = error.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.StreamError>().Subject;
        variant.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        variant.ErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
        variant.SourceException.Should().BeSameAs(source);
    }

    [Fact]
    public void InterfaceEvents_Unclassified_wraps_the_InterfaceStreamEvent_Unclassified_variant()
    {
        var unclassified = InterfaceEvents.Unclassified<IDemoHoldingView, DemoHoldingView>(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, "unmapped-event");

        var variant = unclassified.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.Unclassified>().Subject;
        variant.Offset.Should().Be(LedgerOffset.At(7));
        variant.Kind.Should().Be(UnclassifiedKind.Unknown);
        variant.RawKind.Should().Be("unmapped-event");
    }

    [Fact]
    public void InterfaceEvents_Unclassified_accepts_a_null_offset_and_an_enumerated_kind_without_a_raw_kind()
    {
        var unclassified = InterfaceEvents.Unclassified<IDemoHoldingView, DemoHoldingView>(
            null, UnclassifiedKind.InterfaceViewUnavailable);

        var variant = unclassified.Should().BeOfType<InterfaceStreamEvent<IDemoHoldingView, DemoHoldingView>.Unclassified>().Subject;
        variant.Offset.Should().BeNull();
        variant.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
        variant.RawKind.Should().BeNull();
    }
}
