// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;

namespace Canton.Ledger.Rest.Client;

internal sealed class FaultedHttpContent : HttpContent
{
    private readonly Exception _failure;

    internal FaultedHttpContent(HttpContentHeaders headers, Exception failure)
    {
        _failure = failure;
        foreach (var header in headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        Task.FromException(_failure);

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        Task.FromException<Stream>(_failure);

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
        throw _failure;

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
