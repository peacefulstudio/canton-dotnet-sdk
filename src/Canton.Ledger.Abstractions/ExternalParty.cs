// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// What the participant needs to generate the topology transactions that onboard an external party.
/// </summary>
/// <param name="SynchronizerId">The synchronizer the party is onboarded on.</param>
/// <param name="PartyIdHint">The party's name; the participant appends the key fingerprint as the namespace.</param>
/// <param name="PublicKey">The public half of the key that controls the party.</param>
/// <param name="LocalParticipantObservationOnly">
/// Whether the local participant hosts the party with observation permission only.
/// </param>
/// <param name="OtherConfirmingParticipantUids">
/// Unique ids of further participants that host the party with confirmation permission.
/// </param>
/// <param name="ConfirmationThreshold">
/// How many confirming participants must confirm a transaction, or zero for the participant's default.
/// </param>
/// <param name="ObservingParticipantUids">
/// Unique ids of further participants that host the party with observation permission.
/// </param>
public sealed record ExternalPartyTopologyRequest(
    SynchronizerId SynchronizerId,
    string PartyIdHint,
    SigningPublicKey PublicKey,
    bool LocalParticipantObservationOnly = false,
    IReadOnlyList<string>? OtherConfirmingParticipantUids = null,
    uint ConfirmationThreshold = 0,
    IReadOnlyList<string>? ObservingParticipantUids = null);

/// <summary>
/// The topology transactions that onboard an external party, and the hash to sign over them.
/// </summary>
/// <remarks>
/// Equality compares the byte fields by buffer identity, not by content.
/// </remarks>
/// <param name="Party">The id the party will have once onboarded.</param>
/// <param name="PublicKeyFingerprint">The fingerprint of the party's public key.</param>
/// <param name="TopologyTransactions">The serialized topology transactions, opaque to the SDK.</param>
/// <param name="MultiHash">
/// The hash covering every transaction in <paramref name="TopologyTransactions"/>; signing it
/// authorizes them all at once.
/// </param>
public sealed record ExternalPartyTopology(
    Party Party,
    string PublicKeyFingerprint,
    IReadOnlyList<ReadOnlyMemory<byte>> TopologyTransactions,
    ReadOnlyMemory<byte> MultiHash);

/// <summary>
/// A topology transaction with the signatures that authorize it.
/// </summary>
/// <remarks>
/// Equality compares <see cref="Transaction"/> by buffer identity, not by content.
/// </remarks>
/// <param name="Transaction">The serialized topology transaction.</param>
/// <param name="Signatures">Signatures over this transaction alone; empty when a multi-hash signature covers it.</param>
public sealed record SignedTopologyTransaction(
    ReadOnlyMemory<byte> Transaction,
    IReadOnlyList<LedgerSignature> Signatures);

/// <summary>
/// The signed topology that allocates an external party.
/// </summary>
/// <param name="SynchronizerId">The synchronizer to allocate the party on.</param>
/// <param name="OnboardingTransactions">The topology transactions, each with its own signatures if any.</param>
/// <param name="MultiHashSignatures">
/// Signatures over <see cref="ExternalPartyTopology.MultiHash"/>, covering every onboarding transaction.
/// </param>
/// <param name="WaitForAllocation">
/// Whether to return only once the party is visible on the ledger; <see langword="null"/> for the
/// participant's default, which waits.
/// </param>
/// <param name="IdentityProviderId">
/// The identity provider the party belongs to, or <see langword="null"/> for the default one.
/// </param>
/// <param name="UserId">
/// The user who receives act-as rights over the new party, or <see langword="null"/> for no user.
/// </param>
public sealed record ExternalPartyAllocation(
    SynchronizerId SynchronizerId,
    IReadOnlyList<SignedTopologyTransaction> OnboardingTransactions,
    IReadOnlyList<LedgerSignature> MultiHashSignatures,
    bool? WaitForAllocation = null,
    string? IdentityProviderId = null,
    string? UserId = null);
