// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The hash a participant returned for a prepared transaction is not the hash of that transaction:
/// the client recomputed it from <see cref="PreparedSubmission.PreparedTransaction"/> under the
/// Canton external-signing hashing algorithm and got a different value. A signature over the
/// participant's hash would authorize something other than the transaction it returned, so the
/// client refuses the prepared submission before any signer sees it.
/// </summary>
/// <remarks>
/// <c>PrepareSubmissionAsync</c> raises it on either transport as the
/// <see cref="Exception.InnerException"/> of a <c>LedgerOperationException</c> whose <c>Status</c> is
/// <c>UndecodableBody</c> and whose <c>CommitState</c> is <c>NotCommitted</c>: preparing commits nothing.
/// A participant that is buggy or compromised is the usual cause.
/// </remarks>
public sealed class PreparedHashMismatchException : InvalidOperationException
{
    /// <summary>Creates an exception describing a prepared-transaction hash that does not match its transaction.</summary>
    /// <param name="hashingSchemeVersion">The scheme the participant reported, and the client recomputed the hash under.</param>
    /// <param name="reportedHash">The hash the participant returned.</param>
    /// <param name="recomputedHash">The hash the client recomputed from the prepared transaction.</param>
    public PreparedHashMismatchException(
        HashingSchemeVersion hashingSchemeVersion,
        ReadOnlyMemory<byte> reportedHash,
        ReadOnlyMemory<byte> recomputedHash)
        : base(
            $"The participant's prepared-transaction hash {Convert.ToHexStringLower(reportedHash.Span)} does not match "
            + $"{Convert.ToHexStringLower(recomputedHash.Span)}, the hash of the prepared transaction it returned under "
            + $"hashing scheme {hashingSchemeVersion}.")
    {
        HashingSchemeVersion = hashingSchemeVersion;
        ReportedHash = reportedHash;
        RecomputedHash = recomputedHash;
    }

    /// <summary>The scheme the participant reported, and the client recomputed the hash under.</summary>
    public HashingSchemeVersion HashingSchemeVersion { get; }

    /// <summary>The hash the participant returned.</summary>
    public ReadOnlyMemory<byte> ReportedHash { get; }

    /// <summary>The hash the client recomputed from the prepared transaction.</summary>
    public ReadOnlyMemory<byte> RecomputedHash { get; }
}
