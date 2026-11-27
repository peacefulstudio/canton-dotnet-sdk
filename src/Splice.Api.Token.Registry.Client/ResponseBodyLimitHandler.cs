// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Splice.Api.Token.Registry.Client;

internal sealed class ResponseBodyLimitHandler : DelegatingHandler
{
    internal const int MaxResponseBodyBytes = 16 * 1024 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            if (response.Content.Headers.ContentLength > MaxResponseBodyBytes)
                throw new HttpRequestException(
                    $"The registry declared a {response.Content.Headers.ContentLength}-byte response body, above the {MaxResponseBodyBytes}-byte limit.");
            await response.Content.LoadIntoBufferAsync(MaxResponseBodyBytes, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
