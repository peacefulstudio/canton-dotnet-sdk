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
            Format = GrpcEnumMaps.SignatureFormats.ToWire(signature.Format, nameof(signature)),
            Signature_ = ByteString.CopyFrom(signature.Bytes.Span),
            SignedBy = signature.SignedBy,
            SigningAlgorithmSpec = GrpcEnumMaps.SigningAlgorithms.ToWire(signature.Algorithm, nameof(signature)),
        };

    internal static Wire.SigningPublicKey ToWire(SigningPublicKey key) =>
        new()
        {
            Format = GrpcEnumMaps.PublicKeyFormats.ToWire(key.Format, nameof(key)),
            KeyData = ByteString.CopyFrom(key.KeyData.Span),
            KeySpec = GrpcEnumMaps.SigningKeySpecs.ToWire(key.KeySpec, nameof(key)),
        };

    internal static PackageReference FromWire(Wire.PackageReference reference) =>
        new(reference.PackageId, reference.PackageName, reference.PackageVersion);
}
