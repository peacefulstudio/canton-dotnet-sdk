// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class ConnectionCountingListenerTests
{
    [Fact]
    public async Task DisposeAsync_straight_after_an_accepted_connection_never_throws()
    {
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var listener = new ConnectionCountingListener();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, TestContext.Current.CancellationToken);
            while (listener.AcceptedConnections == 0)
            {
                await Task.Yield();
            }

            var dispose = async () => await listener.DisposeAsync();

            await dispose.Should().NotThrowAsync($"iteration {iteration} disposed the listener right after its first accept");
        }
    }
}
