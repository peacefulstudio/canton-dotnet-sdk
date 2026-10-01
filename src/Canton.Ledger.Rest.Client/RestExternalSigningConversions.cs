// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Rest.Client;

internal static class RestExternalSigningConversions
{
    public static ServedSignature ToServed(LedgerSignature signature, string paramName) =>
        new(
            ServedEnums.SignatureFormats.NameOf(signature.Format, paramName),
            Convert.ToBase64String(signature.Bytes.Span),
            signature.SignedBy,
            ServedEnums.SigningAlgorithms.NameOf(signature.Algorithm, paramName));

    public static IReadOnlyList<ServedSignature>? ToServedOrOmitted(
        IReadOnlyList<LedgerSignature>? signatures, string paramName) =>
        signatures is { Count: > 0 }
            ? [.. signatures.Select(signature => ToServed(signature, paramName))]
            : null;

    public static ServedSigningPublicKey ToServed(SigningPublicKey publicKey, string paramName) =>
        new(
            ServedEnums.PublicKeyFormats.NameOf(publicKey.Format, paramName),
            Convert.ToBase64String(publicKey.KeyData.Span),
            ServedEnums.SigningKeySpecs.NameOf(publicKey.KeySpec, paramName));

    public static ServedPartySignatures ToServed(IReadOnlyList<PartySignatures> partySignatures, string paramName) =>
        new([
            .. partySignatures.Select(party => new ServedSinglePartySignatures(
                party.Party.Value,
                [.. party.Signatures.Select(signature => ToServed(signature, paramName))])),
        ]);

    public static ServedMinLedgerTime ToServed(RuntimeCommands.MinLedgerTime bound) =>
        new(bound.Match(
            absolute: instant => new ServedMinLedgerTimeArm(new ServedValue<DateTimeOffset>(instant), Relative: null),
            relative: delay => new ServedMinLedgerTimeArm(Absolute: null, new ServedValue<ServedDuration>(ToServed(delay)))));

    public static ReadOnlyMemory<byte> FromBase64(string? encoded, string field) =>
        encoded is null
            ? throw MalformedResponse.MissingRequiredField($"The response carries no {field}")
            : Convert.FromBase64String(encoded);

    private static ServedDuration ToServed(TimeSpan delay)
    {
        var (seconds, nanos) = WireDuration.PartsOf(RestWireConversions.ToWireDuration(delay));
        return new ServedDuration(seconds, nanos);
    }
}
