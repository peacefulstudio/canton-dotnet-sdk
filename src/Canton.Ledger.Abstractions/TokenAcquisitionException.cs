// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Says a bearer token could not be obtained before any attempt of a call was sent, so the call was
/// never sent. A token that cannot be fetched for a retry after an earlier attempt went out is not
/// reported this way: that earlier attempt may have been applied, so the call reports that attempt's
/// failure instead. A throwing
/// call on either transport raises it as the <see cref="Exception.InnerException"/> of a
/// <c>LedgerOperationException</c> with <c>CommitState.NotCommitted</c> whatever the call's kind;
/// a <c>Try*</c> call reports it as the <c>SourceException</c> of an <c>InfraError</c>, and a stream
/// as the source of a terminal <c>StreamError</c>.
/// </summary>
/// <remarks>
/// Look for this type in the failure's <see cref="Exception.InnerException"/> chain to tell an
/// authentication failure from a participant failure: the identity provider being down or slow, a
/// token response that cannot be read or is too large, or a token provider that returned a blank
/// token. Its own <see cref="Exception.InnerException"/> is what the token provider raised. Caller
/// cancellation is not a token failure and stays an <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed class TokenAcquisitionException : Exception
{
    /// <summary>Creates an exception describing why no token could be obtained.</summary>
    /// <param name="message">What went wrong, phrased as a sentence.</param>
    /// <param name="innerException">What the token provider raised.</param>
    public TokenAcquisitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
