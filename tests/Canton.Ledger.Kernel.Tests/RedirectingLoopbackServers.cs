// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Canton.Ledger.Kernel.Tests;

/// <summary>
/// Two real loopback HTTP/1.1 listeners: the first answers every request with a
/// <c>307 Temporary Redirect</c> to the second, which records every request body it receives.
/// Redirect following happens inside <see cref="SocketsHttpHandler"/>, below any handler a test
/// could substitute, so only a real socket can observe whether a request body is re-sent.
/// </summary>
internal sealed class RedirectingLoopbackServers : IDisposable
{
    private readonly HttpListener _redirector = new();
    private readonly HttpListener _target = new();
    private readonly ConcurrentQueue<string> _targetBodies = new();

    internal RedirectingLoopbackServers()
    {
        RedirectorAddress = $"http://127.0.0.1:{GetFreeTcpPort()}";
        TargetAddress = $"http://127.0.0.1:{GetFreeTcpPort()}";
        _redirector.Prefixes.Add($"{RedirectorAddress}/");
        _target.Prefixes.Add($"{TargetAddress}/");
        _redirector.Start();
        _target.Start();
        _ = ServeAsync(_redirector, RedirectAsync);
        _ = ServeAsync(_target, RecordAsync);
    }

    internal string RedirectorAddress { get; }

    internal string TargetAddress { get; }

    internal IReadOnlyCollection<string> TargetRequestBodies => _targetBodies;

    private async Task RedirectAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        await reader.ReadToEndAsync().ConfigureAwait(false);
        context.Response.StatusCode = (int)HttpStatusCode.TemporaryRedirect;
        context.Response.RedirectLocation = $"{TargetAddress}{context.Request.Url!.PathAndQuery}";
        context.Response.Close();
    }

    private async Task RecordAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        _targetBodies.Enqueue(await reader.ReadToEndAsync().ConfigureAwait(false));
        var body = Encoding.UTF8.GetBytes("""{"access_token":"leaked","expires_in":3600,"token_type":"Bearer"}""");
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        context.Response.Close();
    }

    private static async Task ServeAsync(HttpListener listener, Func<HttpListenerContext, Task> respond)
    {
        try
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                await respond(context).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
        {
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

    public void Dispose()
    {
        _redirector.Close();
        _target.Close();
    }
}
