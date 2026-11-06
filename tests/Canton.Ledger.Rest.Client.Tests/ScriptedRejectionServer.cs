// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// A real loopback socket that drains one request and then plays a scripted raw HTTP/1.1 response
/// onto it. A stub <see cref="HttpMessageHandler"/> cannot reproduce a chunked or cut-off body: only
/// <see cref="SocketsHttpHandler"/> enforces that a response stream, once consumed, cannot be read again.
/// The handler it creates never aborts the wait for the response headers, so a deadline can only elapse
/// once the scripted response has started, however late a loaded host delivers it.
/// </summary>
internal sealed class ScriptedRejectionServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopped = new();
    private readonly Func<Stream, CancellationToken, Task> _script;

    internal ScriptedRejectionServer(Func<Stream, CancellationToken, Task> script)
    {
        _script = script;
        _listener.Start();
        _ = AcceptAsync();
    }

    internal Uri Address => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");

    internal HttpMessageHandler CreateHandler() =>
        new RestActivityHandler
        {
            InnerHandler = new RedirectingHandler(Address, new HeadersAwaitedHandler(new SocketsHttpHandler(), _stopped.Token)),
        };

    internal static Func<Stream, CancellationToken, Task> ChunkedBody(HttpStatusCode status, params string[] chunks) =>
        async (stream, token) =>
        {
            await WriteAsync(
                stream,
                $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n",
                token).ConfigureAwait(false);
            foreach (var chunk in chunks)
            {
                await WriteAsync(stream, $"{Encoding.UTF8.GetByteCount(chunk):x}\r\n{chunk}\r\n", token).ConfigureAwait(false);
            }

            await WriteAsync(stream, "0\r\n\r\n", token).ConfigureAwait(false);
        };

    internal static Func<Stream, CancellationToken, Task> CutOffBody(
        HttpStatusCode status, string partialBody, int declaredLength, Func<CancellationToken, Task>? afterPartial = null) =>
        async (stream, token) =>
        {
            await WriteAsync(
                stream,
                $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: application/json\r\nContent-Length: {declaredLength}\r\nConnection: close\r\n\r\n{partialBody}",
                token).ConfigureAwait(false);
            if (afterPartial is not null)
            {
                await afterPartial(token).ConfigureAwait(false);
            }
        };

    internal static Func<Stream, CancellationToken, Task> StagedBody(
        HttpStatusCode status, int declaredLength, params (TimeSpan Delay, string Text)[] stages) =>
        async (stream, token) =>
        {
            var head = $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: application/json\r\nContent-Length: {declaredLength}\r\nConnection: close\r\n\r\n";
            for (var index = 0; index < stages.Length; index++)
            {
                await Task.Delay(stages[index].Delay, token).ConfigureAwait(false);
                await WriteAsync(stream, (index == 0 ? head : string.Empty) + stages[index].Text, token).ConfigureAwait(false);
            }
        };

    private static async Task WriteAsync(Stream stream, string text, CancellationToken token)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stopped.IsCancellationRequested)
            {
                var connection = await _listener.AcceptTcpClientAsync(_stopped.Token).ConfigureAwait(false);
                _ = AnswerAsync(connection);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    private async Task AnswerAsync(TcpClient connection)
    {
        try
        {
            using (connection)
            {
                var stream = connection.GetStream();
                await DrainRequestAsync(stream).ConfigureAwait(false);
                await _script(stream, _stopped.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException)
        {
        }
    }

    private async Task DrainRequestAsync(Stream stream)
    {
        var received = new List<byte>();
        var buffer = new byte[8192];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var count = await stream.ReadAsync(buffer, _stopped.Token).ConfigureAwait(false);
            if (count == 0)
            {
                return;
            }

            received.AddRange(buffer.AsSpan(0, count).ToArray());
            headerEnd = Encoding.ASCII.GetString([.. received]).IndexOf("\r\n\r\n", StringComparison.Ordinal);
        }

        var head = Encoding.ASCII.GetString([.. received], 0, headerEnd);
        var declaredBody = head.Split("\r\n")
            .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Select(line => int.Parse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture))
            .FirstOrDefault();
        var bodyReceived = received.Count - (headerEnd + 4);
        while (bodyReceived < declaredBody)
        {
            var count = await stream.ReadAsync(buffer, _stopped.Token).ConfigureAwait(false);
            if (count == 0)
            {
                return;
            }

            bodyReceived += count;
        }
    }

    public void Dispose()
    {
        _stopped.Cancel();
        _listener.Stop();
        _stopped.Dispose();
    }

    private sealed class HeadersAwaitedHandler(HttpMessageHandler inner, CancellationToken serverStopped)
        : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            base.SendAsync(request, serverStopped);
    }

    private sealed class RedirectingHandler(Uri address, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(address, request.RequestUri!.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
