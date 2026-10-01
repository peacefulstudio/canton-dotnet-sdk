// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// How the bytes of a <see cref="LedgerSignature"/> are laid out. Numeric values equal the Ledger
/// API wire values, so a value this SDK version does not name survives a round trip unchanged.
/// </summary>
public enum SignatureFormat
{
    /// <summary>The raw signature bytes.</summary>
    Raw = 1,

    /// <summary>An ASN.1 DER-encoded signature.</summary>
    Der = 2,

    /// <summary>The <c>r</c> and <c>s</c> values concatenated.</summary>
    Concat = 3,

    /// <summary>A symbolic signature, for testing only.</summary>
    Symbolic = 10000,
}

/// <summary>
/// The algorithm a <see cref="LedgerSignature"/> was produced with. Numeric values equal the Ledger
/// API wire values.
/// </summary>
public enum SigningAlgorithm
{
    /// <summary>Ed25519.</summary>
    Ed25519 = 1,

    /// <summary>ECDSA over SHA-256.</summary>
    EcDsaSha256 = 2,

    /// <summary>ECDSA over SHA-384.</summary>
    EcDsaSha384 = 3,
}

/// <summary>
/// The key specification of a <see cref="SigningPublicKey"/>. Numeric values equal the Ledger API
/// wire values.
/// </summary>
public enum SigningKeySpec
{
    /// <summary>Curve25519.</summary>
    EcCurve25519 = 1,

    /// <summary>NIST P-256.</summary>
    EcP256 = 2,

    /// <summary>NIST P-384.</summary>
    EcP384 = 3,

    /// <summary>secp256k1.</summary>
    EcSecp256k1 = 4,
}

/// <summary>
/// The serialization of a <see cref="SigningPublicKey"/>'s key data. Numeric values equal the
/// Ledger API wire values.
/// </summary>
public enum PublicKeyFormat
{
    /// <summary>ASN.1 DER.</summary>
    Der = 1,

    /// <summary>The raw key bytes.</summary>
    Raw = 2,

    /// <summary>An X.509 <c>SubjectPublicKeyInfo</c> in DER.</summary>
    DerX509SubjectPublicKeyInfo = 3,
}

/// <summary>
/// A signature over a hash the caller obtained from the participant, produced with a key the
/// caller holds. The SDK never signs and never sees the private key.
/// </summary>
/// <remarks>
/// Equality compares <see cref="Bytes"/> by buffer identity, not by content.
/// </remarks>
/// <param name="Format">The layout of <paramref name="Bytes"/>.</param>
/// <param name="Bytes">The signature bytes.</param>
/// <param name="SignedBy">The fingerprint of the key that produced the signature.</param>
/// <param name="Algorithm">The algorithm that produced the signature.</param>
public sealed record LedgerSignature(
    SignatureFormat Format,
    ReadOnlyMemory<byte> Bytes,
    string SignedBy,
    SigningAlgorithm Algorithm);

/// <summary>
/// The public half of a signing key an external party is controlled by.
/// </summary>
/// <remarks>
/// Equality compares <see cref="KeyData"/> by buffer identity, not by content.
/// </remarks>
/// <param name="Format">The serialization of <paramref name="KeyData"/>.</param>
/// <param name="KeyData">The serialized public key.</param>
/// <param name="KeySpec">The key specification.</param>
public sealed record SigningPublicKey(
    PublicKeyFormat Format,
    ReadOnlyMemory<byte> KeyData,
    SigningKeySpec KeySpec);
