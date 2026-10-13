// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Data;
using PreparedTransaction = Com.Daml.Ledger.Api.V2.Interactive.PreparedTransaction;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>The onboarding and signing steps the external-signing parity suites share.</summary>
internal static class ExternalSigningSteps
{
    private const string GlobalSynchronizerAlias = "global";

    internal static async Task<(ExternalPartyTopology Generated, Party Allocated)> OnboardAsync(
        ExternalSigningCapability capability,
        ExternalSigningScenario scenario,
        ECDsa key,
        CancellationToken cancellationToken)
    {
        var synchronizer = await ResolveSynchronizerAsync(capability.Client, cancellationToken);
        var publicKey = new SigningPublicKey(
            PublicKeyFormat.DerX509SubjectPublicKeyInfo, key.ExportSubjectPublicKeyInfo(), SigningKeySpec.EcP256);

        var generated = await capability.Admin.GenerateExternalPartyTopologyAsync(
            new ExternalPartyTopologyRequest(synchronizer, scenario.PartyHint, publicKey), cancellationToken);
        var allocated = await capability.Admin.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(
                synchronizer,
                generated.TopologyTransactions
                    .Select(transaction => new SignedTopologyTransaction(transaction, []))
                    .ToList(),
                [Sign(key, generated.MultiHash, generated.PublicKeyFingerprint)]),
            cancellationToken);

        return (generated, allocated);
    }

    internal static LedgerSignature Sign(ECDsa key, ReadOnlyMemory<byte> hash, string fingerprint) => new(
        SignatureFormat.Der,
        key.SignData(hash.Span, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
        fingerprint,
        SigningAlgorithm.EcDsaSha256);

    internal static string RecomputedHashHex(PreparedSubmission prepared) =>
        Convert.ToHexStringLower(PreparedTransactionHasher.Hash(
            PreparedTransaction.Parser.ParseFrom(prepared.PreparedTransaction.Span), prepared.HashingSchemeVersion));

    private static async Task<SynchronizerId> ResolveSynchronizerAsync(
        ICantonLedgerClient client, CancellationToken cancellationToken)
    {
        var connected = await client.GetConnectedSynchronizersAsync(cancellationToken: cancellationToken);
        var chosen = connected.FirstOrDefault(
                synchronizer => synchronizer.SynchronizerAlias == GlobalSynchronizerAlias)
            ?? connected[0];
        return new SynchronizerId(chosen.SynchronizerId);
    }
}
