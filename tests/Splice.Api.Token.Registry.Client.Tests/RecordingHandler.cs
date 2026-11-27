// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;

namespace Splice.Api.Token.Registry.Client.Tests;

internal sealed class RecordingHandler(HttpStatusCode status, string responseJson) : HttpMessageHandler
{
    public HttpMethod? Method { get; private set; }

    public Uri? RequestUri { get; private set; }

    public string? RequestBody { get; private set; }

    public string? RequestContentType { get; private set; }

    public bool TransportSawCancellation { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TransportSawCancellation = cancellationToken.IsCancellationRequested;
        Method = request.Method;
        RequestUri = request.RequestUri;
        RequestContentType = request.Content?.Headers.ContentType?.ToString();
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
    }
}
