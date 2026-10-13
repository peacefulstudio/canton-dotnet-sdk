// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

internal static class ResponseBodyCap
{
    public static async Task<HttpContent> CapAsync(
        HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw new ResponseBodyTooLargeException(maxBytes);
        }

        var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new StreamContent(new CappedReadStream(source, maxBytes))
        {
            Headers = { ContentType = content.Headers.ContentType },
        };
    }

    public static async Task<byte[]> ReadBytesAsync(
        HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        using var capped = await CapAsync(content, maxBytes, cancellationToken).ConfigureAwait(false);
        return await capped.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> ReadTextAsync(
        HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        using var capped = await CapAsync(content, maxBytes, cancellationToken).ConfigureAwait(false);
        return await capped.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
