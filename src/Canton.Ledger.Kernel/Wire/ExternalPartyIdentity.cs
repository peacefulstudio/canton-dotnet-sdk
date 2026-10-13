// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Buffers.Binary;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;

namespace Canton.Ledger.Kernel.Wire;

internal static class ExternalPartyIdentity
{
    private const int PublicKeyFingerprintHashPurpose = 12;
    private const string Sha256MultihashPrefix = "1220";
    private const string PartyIdSeparator = "::";

    private static readonly byte[] Ed25519SubjectPublicKeyInfoPrefix =
        Convert.FromHexString("302a300506032b6570032100");

    private const int Ed25519RawKeyLength = 32;

    internal static string FingerprintOf(SigningPublicKey key)
    {
        var keyBytes = FingerprintInput(key);
        var purposePrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(purposePrefix, PublicKeyFingerprintHashPurpose);
        var digest = SHA256.HashData([.. purposePrefix, .. keyBytes]);
        return Sha256MultihashPrefix + Convert.ToHexStringLower(digest);
    }

    internal static ExternalPartyTopology Verify(SigningPublicKey requestedKey, ExternalPartyTopology topology)
    {
        var expected = FingerprintOfOrMalformed(requestedKey);
        if (!string.Equals(topology.PublicKeyFingerprint, expected, StringComparison.Ordinal))
        {
            throw new MalformedResponseException(
                $"the participant returned public key fingerprint '{topology.PublicKeyFingerprint}', "
                + $"but the key sent hashes to '{expected}'.");
        }

        if (!string.Equals(NamespaceOf(topology.Party.Value), expected, StringComparison.Ordinal))
        {
            throw new MalformedResponseException(
                $"the participant returned party id '{topology.Party.Value}', "
                + $"which is not in the namespace '{expected}' of the key sent.");
        }

        return topology;
    }

    internal static Party VerifyAllocated(Party expected, Party allocated) =>
        allocated == expected
            ? allocated
            : throw MalformedResponse.WithDetail(
                $"the participant allocated party '{allocated.Value}', but the onboarding topology was generated for "
                + $"'{expected.Value}'.");

    private static string FingerprintOfOrMalformed(SigningPublicKey key)
    {
        try
        {
            return FingerprintOf(key);
        }
        catch (ArgumentException unfingerprintable)
        {
            throw MalformedResponse.WithDetail(unfingerprintable.Message, unfingerprintable);
        }
    }

    private static ReadOnlySpan<byte> FingerprintInput(SigningPublicKey key)
    {
        var keyData = key.KeyData.Span;
        if (key.Format != PublicKeyFormat.DerX509SubjectPublicKeyInfo || key.KeySpec != SigningKeySpec.EcCurve25519)
        {
            return keyData;
        }

        var isEd25519Spki = keyData.Length == Ed25519SubjectPublicKeyInfoPrefix.Length + Ed25519RawKeyLength
            && keyData[..Ed25519SubjectPublicKeyInfoPrefix.Length].SequenceEqual(Ed25519SubjectPublicKeyInfoPrefix);
        return isEd25519Spki
            ? keyData[Ed25519SubjectPublicKeyInfoPrefix.Length..]
            : throw new ArgumentException(
                "The Ed25519 key is not an X.509 SubjectPublicKeyInfo holding a 32-byte Ed25519 key.", nameof(key));
    }

    private static string NamespaceOf(string partyId)
    {
        var separatorIndex = partyId.LastIndexOf(PartyIdSeparator, StringComparison.Ordinal);
        return separatorIndex < 0 ? string.Empty : partyId[(separatorIndex + PartyIdSeparator.Length)..];
    }
}
