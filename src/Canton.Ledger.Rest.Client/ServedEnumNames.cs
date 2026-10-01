// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Globalization;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;

namespace Canton.Ledger.Rest.Client;

internal sealed class ServedEnumNames<TEnum>
    where TEnum : struct, Enum
{
    private readonly string _wireEnum;
    private readonly FrozenDictionary<TEnum, string> _namesByValue;
    private readonly FrozenDictionary<string, TEnum> _valuesByName;

    public ServedEnumNames(string wireEnum, IReadOnlyDictionary<TEnum, string> namesByValue)
    {
        _wireEnum = wireEnum;
        _namesByValue = namesByValue.ToFrozenDictionary();
        _valuesByName = namesByValue.ToFrozenDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);
    }

    public string NameOf(TEnum value, string paramName) =>
        _namesByValue.TryGetValue(value, out var name)
            ? name
            : throw new ArgumentException(
                $"{typeof(TEnum).Name} value {Convert.ToInt64(value, CultureInfo.InvariantCulture)} has no "
                + $"{_wireEnum} name this SDK version knows, and the JSON Ledger API carries {_wireEnum} by name only.",
                paramName);

    public TEnum ValueOf(string? name) =>
        name is not null && _valuesByName.TryGetValue(name, out var value)
            ? value
            : throw MalformedResponse.WithDetail(
                $"The participant sent {_wireEnum} '{name}', which this SDK version does not know.");
}

internal static class ServedEnums
{
    public static readonly ServedEnumNames<HashingSchemeVersion> HashingSchemeVersions = new(
        "HashingSchemeVersion",
        new Dictionary<HashingSchemeVersion, string>
        {
            [HashingSchemeVersion.V2] = "HASHING_SCHEME_VERSION_V2",
            [HashingSchemeVersion.V3] = "HASHING_SCHEME_VERSION_V3",
        });

    public static readonly ServedEnumNames<SignatureFormat> SignatureFormats = new(
        "SignatureFormat",
        new Dictionary<SignatureFormat, string>
        {
            [SignatureFormat.Raw] = "SIGNATURE_FORMAT_RAW",
            [SignatureFormat.Der] = "SIGNATURE_FORMAT_DER",
            [SignatureFormat.Concat] = "SIGNATURE_FORMAT_CONCAT",
            [SignatureFormat.Symbolic] = "SIGNATURE_FORMAT_SYMBOLIC",
        });

    public static readonly ServedEnumNames<SigningAlgorithm> SigningAlgorithms = new(
        "SigningAlgorithmSpec",
        new Dictionary<SigningAlgorithm, string>
        {
            [SigningAlgorithm.Ed25519] = "SIGNING_ALGORITHM_SPEC_ED25519",
            [SigningAlgorithm.EcDsaSha256] = "SIGNING_ALGORITHM_SPEC_EC_DSA_SHA_256",
            [SigningAlgorithm.EcDsaSha384] = "SIGNING_ALGORITHM_SPEC_EC_DSA_SHA_384",
        });

    public static readonly ServedEnumNames<SigningKeySpec> SigningKeySpecs = new(
        "SigningKeySpec",
        new Dictionary<SigningKeySpec, string>
        {
            [SigningKeySpec.EcCurve25519] = "SIGNING_KEY_SPEC_EC_CURVE25519",
            [SigningKeySpec.EcP256] = "SIGNING_KEY_SPEC_EC_P256",
            [SigningKeySpec.EcP384] = "SIGNING_KEY_SPEC_EC_P384",
            [SigningKeySpec.EcSecp256k1] = "SIGNING_KEY_SPEC_EC_SECP256K1",
        });

    public static readonly ServedEnumNames<PublicKeyFormat> PublicKeyFormats = new(
        "CryptoKeyFormat",
        new Dictionary<PublicKeyFormat, string>
        {
            [PublicKeyFormat.Der] = "CRYPTO_KEY_FORMAT_DER",
            [PublicKeyFormat.Raw] = "CRYPTO_KEY_FORMAT_RAW",
            [PublicKeyFormat.DerX509SubjectPublicKeyInfo] = "CRYPTO_KEY_FORMAT_DER_X509_SUBJECT_PUBLIC_KEY_INFO",
        });
}
