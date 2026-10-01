// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Streams;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Streams;

public class SynchronizerIdWireFormatTests
{
    [Theory]
    [InlineData("global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5")]
    [InlineData("global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5::35-0")]
    public void Synchronizer_carries_the_wire_id_verbatim_in_both_the_3_4_and_3_5_formats(string wireSynchronizerId)
    {
        var synchronizer = StreamEventClassifier.Synchronizer(wireSynchronizerId);

        synchronizer.Should().NotBeNull();
        synchronizer!.Value.Value.Should().Be(wireSynchronizerId);
    }

    [Theory]
    [InlineData(
        "global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5",
        "app-domain::1220b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f0a")]
    [InlineData(
        "global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5::35-0",
        "app-domain::1220b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f0a::35-0")]
    public void ReassignmentSynchronizers_carries_source_and_target_verbatim_in_both_the_3_4_and_3_5_formats(
        string wireSource,
        string wireTarget)
    {
        var scope = StreamEventClassifier.ReassignmentSynchronizers(wireSource, wireTarget);

        scope.Should().NotBeNull();
        scope!.Value.Source.Value.Should().Be(wireSource);
        scope.Value.Target.Value.Should().Be(wireTarget);
    }

    [Fact]
    public void ReassignmentSynchronizers_carries_a_3_4_source_and_a_3_5_target_verbatim_when_a_reassignment_crosses_formats()
    {
        var scope = StreamEventClassifier.ReassignmentSynchronizers(
            "global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5",
            "app-domain::1220b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f0a::35-0");

        scope!.Value.Source.Value.Should().Be("global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5");
        scope.Value.Target.Value.Should().Be("app-domain::1220b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f0a::35-0");
    }
}
