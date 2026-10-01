// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The opaque continuation of a paged read: pass the <c>NextPageToken</c> of one page as the
/// <c>pageToken</c> of the next request. The value is the participant's token, base64-encoded,
/// so a token read over one transport is valid on the other.
/// </summary>
public sealed record LedgerPageToken
{
    /// <summary>Wraps a base64-encoded participant page token.</summary>
    /// <param name="value">The base64 form of the token; never empty.</param>
    public LedgerPageToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The base64 form of the participant's page token.</summary>
    public string Value { get; }
}
