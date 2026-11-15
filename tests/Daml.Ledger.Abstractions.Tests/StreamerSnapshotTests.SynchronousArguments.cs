// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public sealed partial class StreamerSnapshotTests
{
    private static readonly ILedgerStreamer NullStreamer = null!;

    [Fact]
    public void SnapshotAsync_throws_synchronously_for_a_null_streamer()
    {
        Action act = () => _ = NullStreamer.SnapshotAsync<Probe>(Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("streamer");
    }

    [Fact]
    public void SnapshotActiveAsync_throws_synchronously_for_a_null_streamer()
    {
        Action act = () => _ = NullStreamer.SnapshotActiveAsync<Probe>(Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("streamer");
    }

    [Fact]
    public void SnapshotAsync_with_a_key_throws_synchronously_for_a_null_streamer()
    {
        Action act = () => _ = NullStreamer.SnapshotAsync(Probe.Key, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("streamer");
    }

    [Fact]
    public void SnapshotAsync_with_a_key_throws_synchronously_for_a_null_key()
    {
        Action act = () => _ = new FakeStreamer().SnapshotAsync<Probe, Party>(null!, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("key");
    }

    [Fact]
    public void SnapshotActiveAsync_with_a_key_throws_synchronously_for_a_null_streamer()
    {
        Action act = () => _ = NullStreamer.SnapshotActiveAsync(Probe.Key, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("streamer");
    }

    [Fact]
    public void SnapshotActiveAsync_with_a_key_throws_synchronously_for_a_null_key()
    {
        Action act = () => _ = new FakeStreamer().SnapshotActiveAsync<Probe, Party>(null!, Alice);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("key");
    }
}
