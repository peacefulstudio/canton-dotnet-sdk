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

    /// <summary>
    /// Whether a <c>400</c> answer's <c>detail</c> is the participant's error message, and whether the handler logs
    /// the ledger failure with its message. Off by default, because the participant's message can name parties and
    /// contracts: a <c>400</c> then carries a fixed detail per error category plus the Daml error id, and the
    /// logged exception keeps only its type and stack trace. Turn it on in development only.
    /// </summary>
    public bool IncludeLedgerErrorMessages { get; set; }
}
