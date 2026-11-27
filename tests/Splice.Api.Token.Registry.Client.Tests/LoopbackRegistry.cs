// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Splice.Api.Token.Registry.Client.Tests;

internal sealed class LoopbackRegistry : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Action<HttpListenerContext> _respond;
    private readonly Task _serving;

    internal LoopbackRegistry(Action<HttpListenerContext> respond)
    {
        _respond = respond;
        BaseAddress = new Uri($"http://127.0.0.1:{FreePort()}/");
        _listener.Prefixes.Add(BaseAddress.ToString());
        _listener.Start();
        _serving = ServeAsync();
    }

    internal Uri BaseAddress { get; }

    internal ConcurrentQueue<string> RequestedPaths { get; } = new();

    public void Dispose()
    {
        _listener.Close();
        _serving.Wait(TimeSpan.FromSeconds(5));
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception closed) when (closed is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            RequestedPaths.Enqueue(context.Request.Url!.AbsolutePath);
            _respond(context);
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
