// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Streams;
using Daml.Runtime;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Streams;

public class StreamOffsetsTests
{
    [Theory]
    [InlineData(null, 80L, 80L)]
    [InlineData(0L, 80L, 80L)]
    [InlineData(71L, 80L, 71L)]
    public void EventOrUpdate_takes_the_event_offset_unless_it_is_absent_and_then_the_update_offset(
        long? eventOffset, long updateOffset, long expected)
    {
        StreamOffsets.EventOrUpdate(eventOffset, updateOffset).Should().Be(expected);
    }

    [Fact]
    public void TryAcsEntryResume_takes_the_snapshot_offset_when_the_unassignment_offset_is_absent()
    {
        var resolved = StreamOffsets.TryAcsEntryResume(WireOffset.FromProto(0L), 77L, out var resume);

        resolved.Should().BeTrue();
        resume.Should().Be(77L);
    }

    [Fact]
    public void TryAcsEntryResume_takes_the_unassignment_offset_over_the_snapshot_offset()
    {
        var resolved = StreamOffsets.TryAcsEntryResume(WireOffset.FromProto(43L), 77L, out var resume);

        resolved.Should().BeTrue();
        resume.Should().Be(43L);
    }

    [Fact]
    public void TryAcsEntryResume_refuses_a_malformed_unassignment_offset_instead_of_falling_back_to_the_snapshot_offset()
    {
        StreamOffsets.TryAcsEntryResume(WireOffset.FromProto(-1L), 77L, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAcsCreatedResume_takes_the_entry_resume_offset_when_the_created_offset_is_absent()
    {
        var resolved = StreamOffsets.TryAcsCreatedResume(WireOffset.FromText(null), 43L, out var resume);

        resolved.Should().BeTrue();
        resume.Should().Be(43L);
    }

    [Fact]
    public void TryAcsCreatedResume_takes_the_created_offset_over_the_entry_resume_offset()
    {
        var resolved = StreamOffsets.TryAcsCreatedResume(WireOffset.FromText("42"), 43L, out var resume);

        resolved.Should().BeTrue();
        resume.Should().Be(42L);
    }

    [Fact]
    public void TryAcsCreatedResume_refuses_a_malformed_created_offset()
    {
        StreamOffsets.TryAcsCreatedResume(WireOffset.FromText("-1"), 43L, out _).Should().BeFalse();
    }

    [Fact]
    public void AcsRefusalOffset_is_the_unassignment_offset_when_the_entry_carries_one()
    {
        StreamOffsets.AcsRefusalOffset(WireOffset.FromProto(43L), LedgerOffset.At(77L))
            .Should().Be(LedgerOffset.At(43L));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void AcsRefusalOffset_is_the_snapshot_offset_when_the_unassignment_offset_is_absent_or_malformed(long wire)
    {
        StreamOffsets.AcsRefusalOffset(WireOffset.FromProto(wire), LedgerOffset.At(77L))
            .Should().Be(LedgerOffset.At(77L));
    }

    [Fact]
    public void AcsRefusalOffset_is_no_offset_rather_than_ledger_begin_when_there_is_neither_an_unassignment_nor_a_snapshot_offset()
    {
        StreamOffsets.AcsRefusalOffset(WireOffset.FromProto(0L), snapshotOffset: null).Should().BeNull();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(70L)]
    public void TryRequired_takes_the_offset_of_a_required_field_and_accepts_zero(long wire)
    {
        var resolved = StreamOffsets.TryRequired(WireOffset.FromProto(wire), out var offset);

        resolved.Should().BeTrue();
        offset.Should().Be(wire);
    }

    [Fact]
    public void TryRequired_refuses_a_malformed_offset()
    {
        StreamOffsets.TryRequired(WireOffset.FromProto(-1L), out _).Should().BeFalse();
    }

    [Fact]
    public void TryRequired_refuses_a_required_offset_the_wire_left_out()
    {
        StreamOffsets.TryRequired(WireOffset.FromRequiredText(null), out _).Should().BeFalse();
    }
}
