// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Canton.Ledger.Rest.Client.Tests;

internal static class LoopbackRequestReader
{
    private static readonly byte[] HeadEnd = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] LineEnd = "\r\n"u8.ToArray();

    internal static async Task<bool> ReadWholeRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var received = new List<byte>();
        var buffer = new byte[8192];
        while (!IsWholeRequest(CollectionsMarshal.AsSpan(received)))
        {
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return false;
            }

            received.AddRange(buffer.AsSpan(0, count));
        }

        return true;
    }

    private static bool IsWholeRequest(ReadOnlySpan<byte> request)
    {
        var headEnd = request.IndexOf(HeadEnd);
        if (headEnd < 0)
        {
            return false;
        }

        var head = Encoding.ASCII.GetString(request[..headEnd]);
        var body = request[(headEnd + HeadEnd.Length)..];
        if (HeaderValue(head, "Transfer-Encoding") is { } encoding
            && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            return EndsWithLastChunk(body);
        }

        var declaredLength = HeaderValue(head, "Content-Length") is { } length
            ? int.Parse(length, CultureInfo.InvariantCulture)
            : 0;
        return body.Length >= declaredLength;
    }

    private static string? HeaderValue(string head, string name) =>
        head.Split("\r\n")
            .Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault();

    private static bool EndsWithLastChunk(ReadOnlySpan<byte> body)
    {
        while (true)
        {
            var sizeLineEnd = body.IndexOf(LineEnd);
            if (sizeLineEnd < 0)
            {
                return false;
            }

            var sizeText = Encoding.ASCII.GetString(body[..sizeLineEnd]).Split(';', 2)[0].Trim();
            var size = int.Parse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            body = body[(sizeLineEnd + LineEnd.Length)..];
            if (size == 0)
            {
                return body.StartsWith(LineEnd);
            }

            if (body.Length < size + LineEnd.Length)
            {
                return false;
            }

            body = body[(size + LineEnd.Length)..];
        }
    }
}
