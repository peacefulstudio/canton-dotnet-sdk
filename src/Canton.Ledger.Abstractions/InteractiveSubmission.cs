// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The version of the scheme the participant hashed a prepared transaction with. Numeric values
/// equal the Ledger API wire values, so a version this SDK version does not name survives a round
/// trip from <see cref="PreparedSubmission"/> to <see cref="SignedSubmission"/> unchanged.
/// </summary>
public enum HashingSchemeVersion
{
    /// <summary>Version 2.</summary>
    V2 = 2,

    /// <summary>Version 3.</summary>
    V3 = 3,
}

/// <summary>
/// A submission the participant has interpreted and hashed but not executed, ready for its
/// <see cref="Hash"/> to be signed by the parties that must authorize it.
/// </summary>
/// <remarks>
/// Equality compares the byte fields by buffer identity, not by content.
/// </remarks>
/// <param name="PreparedTransaction">
/// The serialized prepared transaction, opaque to the SDK. Hand it back unchanged in a
/// <see cref="SignedSubmission"/>; a caller that does not trust the preparing participant must
/// decode and show it to the signer before signing.
/// </param>
/// <param name="Hash">The hash to sign.</param>
/// <param name="HashingSchemeVersion">The scheme <paramref name="Hash"/> was computed with.</param>
/// <param name="HashingDetails">
/// A human-readable account of how the hash was computed, or <see langword="null"/> when the
/// participant sent none.
/// </param>
/// <param name="CostEstimate">
/// The participant's traffic-cost estimate, or <see langword="null"/> when it sent none.
/// </param>
public sealed record PreparedSubmission(
    ReadOnlyMemory<byte> PreparedTransaction,
    ReadOnlyMemory<byte> Hash,
    HashingSchemeVersion HashingSchemeVersion,
    string? HashingDetails,
    TrafficCostEstimate? CostEstimate);

/// <summary>
/// The signatures one party contributes to a <see cref="SignedSubmission"/>.
/// </summary>
/// <param name="Party">The signing party.</param>
/// <param name="Signatures">The party's signatures over <see cref="PreparedSubmission.Hash"/>.</param>
public sealed record PartySignatures(Party Party, IReadOnlyList<LedgerSignature> Signatures);

/// <summary>
/// A <see cref="PreparedSubmission"/> together with the signatures that authorize it, ready to
/// execute.
/// </summary>
/// <param name="Prepared">The submission the participant prepared and the parties signed.</param>
/// <param name="PartySignatures">The signatures, per party.</param>
/// <param name="SubmissionId">
/// A unique identifier of this execution, which the participant echoes on the completion. It is the
/// deduplication key for a retry of the same execution.
/// </param>
/// <param name="DeduplicationPeriod">
/// The period within which a repeat of the same submission is rejected, or <see langword="null"/>
/// for the participant's default.
/// </param>
/// <param name="MinLedgerTime">
/// The earliest ledger time the transaction may be assigned, or <see langword="null"/> for none.
/// </param>
public sealed record SignedSubmission(
    PreparedSubmission Prepared,
    IReadOnlyList<PartySignatures> PartySignatures,
    string SubmissionId,
    DeduplicationPeriod? DeduplicationPeriod = null,
    MinLedgerTime? MinLedgerTime = null);

/// <summary>
/// The outcome of an executed submission the participant waited on.
/// </summary>
/// <param name="UpdateId">The id of the update the submission produced.</param>
/// <param name="CompletionOffset">The offset at which the submission completed.</param>
public sealed record ExecutedSubmission(string UpdateId, LedgerOffset CompletionOffset);

/// <summary>
/// A package identified by id, name and version.
/// </summary>
/// <param name="PackageId">The id of the package.</param>
/// <param name="PackageName">The name of the package.</param>
/// <param name="PackageVersion">The version of the package.</param>
public sealed record PackageReference(string PackageId, string PackageName, string PackageVersion);

/// <summary>
/// A package name that every participant hosting <paramref name="Parties"/> must have vetted.
/// </summary>
/// <param name="Parties">The parties whose hosting participants must have vetted the package.</param>
/// <param name="PackageName">The package name to resolve a preferred package for.</param>
public sealed record PackageVettingRequirement(IReadOnlyList<Party> Parties, string PackageName);

/// <summary>
/// The packages the participant prefers for a set of <see cref="PackageVettingRequirement"/>s.
/// </summary>
/// <param name="PackageReferences">One preferred package per requirement.</param>
/// <param name="SynchronizerId">The synchronizer the preference was resolved against.</param>
public sealed record PreferredPackages(
    IReadOnlyList<PackageReference> PackageReferences,
    SynchronizerId SynchronizerId);

/// <summary>
/// The preferred version of one package.
/// </summary>
/// <param name="Package">The preferred package.</param>
/// <param name="SynchronizerId">The synchronizer the preference was resolved against.</param>
public sealed record PackagePreference(PackageReference Package, SynchronizerId SynchronizerId);
