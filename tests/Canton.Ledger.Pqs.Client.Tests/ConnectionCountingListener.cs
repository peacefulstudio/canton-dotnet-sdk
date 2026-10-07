// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;

namespace Canton.Ledger.Pqs.Client.Tests;

internal sealed class ConnectionCountingListener : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private int _acceptedConnections;

    public ConnectionCountingListener()
    {
        _listener.Start();
        _acceptLoop = AcceptAndCloseAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int AcceptedConnections => Volatile.Read(ref _acceptedConnections);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _acceptLoop;
        _listener.Stop();
        _stop.Dispose();
    }

    private async Task AcceptAndCloseAsync()
    {
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _acceptedConnections);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }
}
