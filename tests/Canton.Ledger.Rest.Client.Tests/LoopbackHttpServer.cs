// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// A real loopback HTTP/1.1 server backing the traceparent propagation tests. A stub
/// <see cref="HttpMessageHandler"/> cannot observe header injection, because that injection
/// happens inside <see cref="System.Net.Http.SocketsHttpHandler"/> itself, below any handler a
/// test could substitute.
/// </summary>
internal sealed class LoopbackHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly TaskCompletionSource<string?> _captured =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal LoopbackHttpServer()
    {
        var port = GetFreeTcpPort();
        HttpAddress = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add($"{HttpAddress}/");
        _listener.Start();
        _ = AcceptOnceAsync();
    }

    internal string HttpAddress { get; }

    internal Task<string?> CapturedTraceparent => _captured.Task;

    private async Task AcceptOnceAsync()
    {
        try
        {
            var context = await _listener.GetContextAsync().ConfigureAwait(false);
            _captured.TrySetResult(context.Request.Headers["traceparent"]);

            var body = Encoding.UTF8.GetBytes("""{"offset":"1"}""");
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
        {
            _captured.TrySetResult(null);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Dispose() => _listener.Close();
}
