// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Google.Protobuf;
using Wire = Com.Daml.Ledger.Api.V2;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcExternalSigningMapper
{
    internal static Wire.Signature ToWire(LedgerSignature signature) =>
        new()
        {
            Format = (Wire.SignatureFormat)(int)signature.Format,
            Signature_ = ByteString.CopyFrom(signature.Bytes.Span),
            SignedBy = signature.SignedBy,
            SigningAlgorithmSpec = (Wire.SigningAlgorithmSpec)(int)signature.Algorithm,
        };

    internal static Wire.SigningPublicKey ToWire(SigningPublicKey key) =>
        new()
        {
            Format = (Wire.CryptoKeyFormat)(int)key.Format,
            KeyData = ByteString.CopyFrom(key.KeyData.Span),
            KeySpec = (Wire.SigningKeySpec)(int)key.KeySpec,
        };

    internal static PackageReference FromWire(Wire.PackageReference reference) =>
        new(reference.PackageId, reference.PackageName, reference.PackageVersion);
}
