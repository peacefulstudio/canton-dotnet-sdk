// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Cuts the body of an active-contracts page response part-way through a row, so a REST client
/// that reads the page meets a body that is not valid JSON. The first <c>rowsKept</c> rows of the
/// page's first array stay whole and the next row is cut in half; a page with no row to cut inside
/// is cut in half instead. Every other response passes through whole.
/// </summary>
internal sealed class TruncatedActiveContractsPageHandler(int rowsKept) : DelegatingHandler
{
    private const string ActiveContractsPagePath = "/v2/state/active-contracts-page";

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.RequestUri?.AbsolutePath.EndsWith(ActiveContractsPagePath, StringComparison.Ordinal) != true)
        {
            return response;
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var truncated = new ByteArrayContent(body, 0, CutOffset(body));
        if (response.Content.Headers.ContentType is { } contentType)
        {
            truncated.Headers.ContentType = contentType;
        }

        response.Content = truncated;
        return response;
    }

    private int CutOffset(byte[] body)
    {
        var reader = new Utf8JsonReader(body);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                return CutOffsetInsideRowOf(ref reader) ?? body.Length / 2;
            }
        }

        return body.Length / 2;
    }

    private int? CutOffsetInsideRowOf(ref Utf8JsonReader reader)
    {
        for (var row = 0; reader.Read() && reader.TokenType != JsonTokenType.EndArray; row++)
        {
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            if (row == rowsKept)
            {
                var end = (int)reader.BytesConsumed;
                return start + (end - start) / 2;
            }
        }

        return null;
    }
}
