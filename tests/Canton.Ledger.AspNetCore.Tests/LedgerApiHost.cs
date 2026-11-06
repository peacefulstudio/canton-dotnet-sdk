// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

internal sealed class LedgerApiHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private LedgerApiHost(WebApplication app, HttpClient client)
    {
        _app = app;
        _client = client;
    }

    internal static async Task<LedgerApiHost> StartAsync(
        Action<WebApplication> mapEndpoints,
        Action<IServiceCollection>? configureServices = null,
        bool optIntoErrorMetadataTags = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCantonLedgerProblemDetails(options => options.EmitErrorMetadataSpanTags = optIntoErrorMetadataTags);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseExceptionHandler();
        mapEndpoints(app);
        await app.StartAsync(TestContext.Current.CancellationToken);

        return new LedgerApiHost(app, app.GetTestClient());
    }

    internal static Task<LedgerApiHost> ThrowingAsync(Exception exception) =>
        StartAsync(app => app.MapGet("/fail", (Func<IResult>)(() => throw exception)));

    internal Task<HttpResponseMessage> GetAsync(string path, string? accept = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null)
            request.Headers.TryAddWithoutValidation("Accept", accept);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    internal async Task<(HttpResponseMessage Response, JsonElement Problem)> GetProblemAsync(string path, string? accept = null)
    {
        var response = await GetAsync(path, accept);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (response, problem);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}
