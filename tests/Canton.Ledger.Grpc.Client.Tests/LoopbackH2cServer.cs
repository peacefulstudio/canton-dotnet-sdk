// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Canton.Ledger.Grpc.Client.Tests;

/// <summary>
/// A real loopback HTTP/2-cleartext (h2c) server backing the gRPC traceparent propagation test.
/// A stub <see cref="HttpMessageHandler"/> cannot observe header injection, because that
/// injection happens inside <see cref="System.Net.Http.SocketsHttpHandler"/> itself, below any
/// handler a test could substitute — and <c>Grpc.Net.Client</c> requires a genuine HTTP/2
/// connection, which only a real server provides.
/// </summary>
internal sealed class LoopbackH2cServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly TaskCompletionSource<string?> _captured =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private LoopbackH2cServer(WebApplication app, string grpcAddress)
    {
        _app = app;
        GrpcAddress = grpcAddress;
    }

    internal string GrpcAddress { get; }

    internal Task<string?> CapturedTraceparent => _captured.Task;

    internal static async Task<LoopbackH2cServer> StartAsync()
    {
        var port = GetFreeTcpPort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(port, listenOptions => listenOptions.Protocols = HttpProtocols.Http2));

        var app = builder.Build();
        var server = new LoopbackH2cServer(app, $"http://127.0.0.1:{port}");

        app.Run(context =>
        {
            server._captured.TrySetResult(context.Request.Headers["traceparent"]);
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await app.StartAsync().ConfigureAwait(false);
        return server;
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

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
