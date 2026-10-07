// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed class TruncatedBodyServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopped = new();

    private readonly HttpStatusCode _status;

    internal TruncatedBodyServer(HttpStatusCode status = HttpStatusCode.OK)
    {
        _status = status;
        _listener.Start();
        _ = AcceptAsync();
    }

    internal Uri Address => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");

    internal HttpMessageHandler CreateHandler() => new RedirectingHandler(Address, new SocketsHttpHandler());

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
        using (connection)
        {
            var stream = connection.GetStream();
            var buffer = new byte[8192];
            var received = new StringBuilder();
            while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var count = await stream.ReadAsync(buffer, _stopped.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    return;
                }

                received.Append(Encoding.ASCII.GetString(buffer, 0, count));
            }

            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)_status} {_status}\r\nContent-Type: application/json\r\nContent-Length: 100000\r\nConnection: close\r\n\r\n{{\"users\":[");
            await stream.WriteAsync(response, _stopped.Token).ConfigureAwait(false);
            await stream.FlushAsync(_stopped.Token).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _stopped.Cancel();
        _listener.Stop();
        _stopped.Dispose();
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
