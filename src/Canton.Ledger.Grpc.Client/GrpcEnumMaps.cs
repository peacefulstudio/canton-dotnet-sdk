// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Wire = Com.Daml.Ledger.Api.V2;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcEnumMaps
{
    public static readonly WireEnumMap<HashingSchemeVersion, Wire.Interactive.HashingSchemeVersion> HashingSchemeVersions = new(
        "HashingSchemeVersion",
        new Dictionary<HashingSchemeVersion, Wire.Interactive.HashingSchemeVersion>
        {
            [HashingSchemeVersion.V2] = Wire.Interactive.HashingSchemeVersion.V2,
            [HashingSchemeVersion.V3] = Wire.Interactive.HashingSchemeVersion.V3,
            [HashingSchemeVersion.V4] = Wire.Interactive.HashingSchemeVersion.V4,
        });

    public static readonly WireEnumMap<SignatureFormat, Wire.SignatureFormat> SignatureFormats = new(
        "SignatureFormat",
        new Dictionary<SignatureFormat, Wire.SignatureFormat>
        {
            [SignatureFormat.Raw] = Wire.SignatureFormat.Raw,
            [SignatureFormat.Der] = Wire.SignatureFormat.Der,
            [SignatureFormat.Concat] = Wire.SignatureFormat.Concat,
            [SignatureFormat.Symbolic] = Wire.SignatureFormat.Symbolic,
        });

    public static readonly WireEnumMap<SigningAlgorithm, Wire.SigningAlgorithmSpec> SigningAlgorithms = new(
        "SigningAlgorithmSpec",
        new Dictionary<SigningAlgorithm, Wire.SigningAlgorithmSpec>
        {
            [SigningAlgorithm.Ed25519] = Wire.SigningAlgorithmSpec.Ed25519,
            [SigningAlgorithm.EcDsaSha256] = Wire.SigningAlgorithmSpec.EcDsaSha256,
            [SigningAlgorithm.EcDsaSha384] = Wire.SigningAlgorithmSpec.EcDsaSha384,
            [SigningAlgorithm.MlDsa65] = Wire.SigningAlgorithmSpec.MlDsa65,
        });

    public static readonly WireEnumMap<SigningKeySpec, Wire.SigningKeySpec> SigningKeySpecs = new(
        "SigningKeySpec",
        new Dictionary<SigningKeySpec, Wire.SigningKeySpec>
        {
            [SigningKeySpec.EcCurve25519] = Wire.SigningKeySpec.EcCurve25519,
            [SigningKeySpec.EcP256] = Wire.SigningKeySpec.EcP256,
            [SigningKeySpec.EcP384] = Wire.SigningKeySpec.EcP384,
            [SigningKeySpec.EcSecp256k1] = Wire.SigningKeySpec.EcSecp256K1,
            [SigningKeySpec.MlDsa65] = Wire.SigningKeySpec.MlDsa65,
        });

    public static readonly WireEnumMap<PublicKeyFormat, Wire.CryptoKeyFormat> PublicKeyFormats = new(
        "CryptoKeyFormat",
        new Dictionary<PublicKeyFormat, Wire.CryptoKeyFormat>
        {
            [PublicKeyFormat.Der] = Wire.CryptoKeyFormat.Der,
            [PublicKeyFormat.Raw] = Wire.CryptoKeyFormat.Raw,
            [PublicKeyFormat.DerX509SubjectPublicKeyInfo] = Wire.CryptoKeyFormat.DerX509SubjectPublicKeyInfo,
        });

    public static readonly WireEnumMap<SynchronizerPermissionLevel, Wire.ParticipantPermission> SynchronizerPermissions = new(
        "ParticipantPermission",
        new Dictionary<SynchronizerPermissionLevel, Wire.ParticipantPermission>
        {
            [SynchronizerPermissionLevel.Unspecified] = Wire.ParticipantPermission.Unspecified,
            [SynchronizerPermissionLevel.Submission] = Wire.ParticipantPermission.Submission,
            [SynchronizerPermissionLevel.Confirmation] = Wire.ParticipantPermission.Confirmation,
            [SynchronizerPermissionLevel.Observation] = Wire.ParticipantPermission.Observation,
        });

    public static readonly WireEnumMap<PackageStatus, Wire.PackageStatus> PackageStatuses = new(
        "PackageStatus",
        new Dictionary<PackageStatus, Wire.PackageStatus>
        {
            [PackageStatus.Unspecified] = Wire.PackageStatus.Unspecified,
            [PackageStatus.Registered] = Wire.PackageStatus.Registered,
        });

    public static readonly WireEnumMap<HashFunction, Wire.HashFunction> HashFunctions = new(
        "HashFunction",
        new Dictionary<HashFunction, Wire.HashFunction>
        {
            [HashFunction.Sha256] = Wire.HashFunction.Sha256,
        });

    public static readonly WireEnumMap<CommandState, Wire.Admin.CommandState> ReportedCommandStates = new(
        "CommandState",
        new Dictionary<CommandState, Wire.Admin.CommandState>
        {
            [CommandState.Pending] = Wire.Admin.CommandState.Pending,
            [CommandState.Succeeded] = Wire.Admin.CommandState.Succeeded,
            [CommandState.Failed] = Wire.Admin.CommandState.Failed,
        });
}
