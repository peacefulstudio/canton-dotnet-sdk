// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Streams;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Streams;

public class StreamEventClassifierTests
{
    private static readonly SynchronizerId Synchronizer = new("sync::fingerprint::3");

    [Theory]
    [InlineData(UnclassifiedKind.CreatedEvent)]
    [InlineData(UnclassifiedKind.ArchivedEvent)]
    [InlineData(UnclassifiedKind.ExercisedEvent)]
    [InlineData(UnclassifiedKind.AssignedEvent)]
    [InlineData(UnclassifiedKind.UnassignedEvent)]
    public void TryAdmit_refuses_with_the_wire_shapes_own_kind_when_the_marker_does_not_match(UnclassifiedKind unmatchedKind)
    {
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            4711L, MatchesMarker: false, Synchronizer, unmatchedKind);

        var admitted = StreamEventClassifier.TryAdmit(decoded, out _, out var refusal);

        admitted.Should().BeFalse();
        refusal.Kind.Should().Be(unmatchedKind);
        refusal.Offset.Should().Be(LedgerOffset.At(4711L));
    }

    [Fact]
    public void TryAdmit_refuses_with_MissingSynchronizerId_when_the_marker_matches_but_no_synchronizer_is_carried()
    {
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            4711L, MatchesMarker: true, SynchronizerScope: null, UnclassifiedKind.CreatedEvent);

        var admitted = StreamEventClassifier.TryAdmit(decoded, out _, out var refusal);

        admitted.Should().BeFalse();
        refusal.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        refusal.Offset.Should().Be(LedgerOffset.At(4711L));
    }

    [Fact]
    public void TryAdmit_reports_a_marker_mismatch_ahead_of_a_missing_synchronizer_id()
    {
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            4711L, MatchesMarker: false, SynchronizerScope: null, UnclassifiedKind.ArchivedEvent);

        StreamEventClassifier.TryAdmit(decoded, out _, out var refusal);

        refusal.Kind.Should().Be(UnclassifiedKind.ArchivedEvent);
    }

    [Fact]
    public void TryAdmit_hands_back_the_synchronizer_it_validated_so_no_caller_can_skip_the_rule()
    {
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            4711L, MatchesMarker: true, Synchronizer, UnclassifiedKind.CreatedEvent);

        var admitted = StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal);

        admitted.Should().BeTrue();
        refusal.Should().Be(default(StreamEntryRefusal));
        scope.Should().Be(new SynchronizerId("sync::fingerprint::3"));
    }

    [Fact]
    public void TryAdmit_applies_the_same_rule_to_a_reassignments_source_and_target_pair()
    {
        var scope = new ReassignmentScope(new SynchronizerId("source"), new SynchronizerId("target"));
        var decoded = new DecodedStreamEvent<ReassignmentScope>(
            4711L, MatchesMarker: true, scope, UnclassifiedKind.AssignedEvent);

        var admitted = StreamEventClassifier.TryAdmit(decoded, out var validated, out _);

        admitted.Should().BeTrue();
        validated.Should().Be(new ReassignmentScope(new SynchronizerId("source"), new SynchronizerId("target")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Synchronizer_is_absent_when_the_participant_carried_no_usable_id(string? wireSynchronizerId)
    {
        StreamEventClassifier.Synchronizer(wireSynchronizerId).Should().BeNull();
    }

    [Fact]
    public void Synchronizer_wraps_a_populated_id()
    {
        StreamEventClassifier.Synchronizer("sync::fingerprint::3").Should().Be(Synchronizer);
    }

    [Theory]
    [InlineData(null, "target")]
    [InlineData("source", null)]
    [InlineData("", "target")]
    [InlineData("source", "   ")]
    public void ReassignmentSynchronizers_are_absent_when_either_end_is_missing(string? source, string? target)
    {
        StreamEventClassifier.ReassignmentSynchronizers(source, target).Should().BeNull();
    }

    [Fact]
    public void ReassignmentSynchronizers_carry_both_ends_when_the_participant_populated_both()
    {
        StreamEventClassifier.ReassignmentSynchronizers("source", "target")
            .Should().Be(new ReassignmentScope(new SynchronizerId("source"), new SynchronizerId("target")));
    }

    [Fact]
    public void DecodeFailure_refuses_with_DecodeFailure_at_the_offset_it_is_given()
    {
        var refusal = StreamEventClassifier.DecodeFailure(
            "StreamMarker", 4711L, logger: null, new InvalidOperationException("poison payload"));

        refusal.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        refusal.Offset.Should().Be(LedgerOffset.At(4711L));
    }

    [Fact]
    public void DecodeFailure_warns_once_naming_the_offset_the_subscribed_type_and_the_cause()
    {
        using var loggerFactory = new CapturingLoggerFactory();
        var cause = new InvalidOperationException("poison payload");

        StreamEventClassifier.DecodeFailure("StreamMarker", 4711L, loggerFactory.CreateLogger("stream"), cause);

        var record = loggerFactory.Records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Be(
            "Could not decode event at offset 4711 on the StreamMarker stream — surfaced as Unclassified (decode-failure)");
        record.Exception.Should().BeSameAs(cause);
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TaskCanceledException))]
    public void IsNotCancellation_never_swallows_a_cancellation(Type cancellation)
    {
        StreamEventClassifier.IsNotCancellation((Exception)Activator.CreateInstance(cancellation)!)
            .Should().BeFalse();
    }

    [Fact]
    public void IsNotCancellation_covers_any_other_exception_so_no_event_is_dropped()
    {
        StreamEventClassifier.IsNotCancellation(new FormatException()).Should().BeTrue();
    }

    [Fact]
    public void UndecodableCheckpoint_ends_the_stream_with_an_UndecodableBody_fault_naming_the_cause()
    {
        var cause = WireOffset.FromProto(-1L).ToMalformedResponse("checkpoint offset");

        var fault = StreamEventClassifier.UndecodableCheckpoint("StreamMarker", logger: null, cause);

        fault.Status.Should().Be(new TransportStatus.UndecodableBody());
        fault.Message.Should().Be(
            "Could not decode the offset checkpoint on the update stream: checkpoint offset '-1' is not a valid ledger offset");
        fault.SourceException.Should().BeSameAs(cause);
        fault.Category.Should().BeNull();
        fault.ErrorId.Should().BeNull();
    }

    [Fact]
    public void UndecodableCheckpoint_warns_once_naming_the_subscribed_type_and_the_cause()
    {
        using var loggerFactory = new CapturingLoggerFactory();
        var cause = WireOffset.FromProto(-1L).ToMalformedResponse("checkpoint offset");

        StreamEventClassifier.UndecodableCheckpoint("StreamMarker", loggerFactory.CreateLogger("stream"), cause);

        var record = loggerFactory.Records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Be(
            "Could not decode the offset checkpoint on the StreamMarker update stream — ended with StreamError (undecodable-body)");
        record.Exception.Should().BeSameAs(cause);
    }

    [Fact]
    public void UndecodableUpdateOffset_refuses_with_DecodeFailure_carrying_no_offset()
    {
        var cause = WireOffset.FromProto(-1L).ToMalformedResponse("transaction offset");

        var refusal = StreamEventClassifier.UndecodableUpdateOffset("StreamMarker", logger: null, cause);

        refusal.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        refusal.Offset.Should().BeNull();
    }

    [Fact]
    public void UndecodableUpdateOffset_warns_once_that_the_refusal_is_no_resume_point_and_names_the_cause()
    {
        using var loggerFactory = new CapturingLoggerFactory();
        var cause = WireOffset.FromProto(-1L).ToMalformedResponse("transaction offset");

        StreamEventClassifier.UndecodableUpdateOffset("StreamMarker", loggerFactory.CreateLogger("stream"), cause);

        var record = loggerFactory.Records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Be(
            "Could not decode the offset of an update on the StreamMarker stream — surfaced as Unclassified (decode-failure) carrying no offset, so a consumer must not persist this event as a resume point");
        record.Exception.Should().BeSameAs(cause);
    }
}
