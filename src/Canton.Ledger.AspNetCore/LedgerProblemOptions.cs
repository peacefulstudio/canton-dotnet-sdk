// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.AspNetCore;

/// <summary>Options for the ledger failure mapping registered by <see cref="LedgerProblemServiceCollectionExtensions.AddCantonLedgerProblemDetails"/>.</summary>
public sealed class LedgerProblemOptions
{
    /// <summary>
    /// Whether a ledger failure tags the request span with one <c>daml.error_metadata.&lt;key&gt;</c> attribute per
    /// entry of the participant's structured error metadata. Off by default, because that metadata can name
    /// parties and contracts, which the ledger clients keep off their own spans unless the host opts in
    /// (<c>EmitPartyAndContractSpanTags</c>). <c>daml.error_id</c> and <c>daml.error_category</c> are always
    /// tagged.
    /// </summary>
    public bool EmitErrorMetadataSpanTags { get; set; }
}
