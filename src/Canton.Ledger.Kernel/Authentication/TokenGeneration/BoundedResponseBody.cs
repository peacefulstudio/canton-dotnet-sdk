// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Authentication.TokenGeneration;

internal readonly record struct BoundedResponseBody(byte[] Bytes, bool ExceededLimit)
{
    internal static async Task<BoundedResponseBody> ReadAsync(HttpContent content, int limitBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limitBytes)
        {
            return new BoundedResponseBody([], ExceededLimit: true);
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        var buffer = new byte[limitBytes + 1];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled > limitBytes
            ? new BoundedResponseBody(buffer[..limitBytes], ExceededLimit: true)
            : new BoundedResponseBody(buffer[..filled], ExceededLimit: false);
    }
}
