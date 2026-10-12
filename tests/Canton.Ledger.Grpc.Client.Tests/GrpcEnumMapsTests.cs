// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Xunit;
using Wire = Com.Daml.Ledger.Api.V2;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcEnumMapsTests
{
    [Theory]
    [InlineData(HashingSchemeVersion.V2, 2)]
    [InlineData(HashingSchemeVersion.V3, 3)]
    [InlineData(HashingSchemeVersion.V4, 4)]
    public void HashingSchemeVersions_encode_each_public_member_to_its_proto_number(
        HashingSchemeVersion member, int protoNumber)
    {
        ((int)GrpcEnumMaps.HashingSchemeVersions.ToWire(member, "version")).Should().Be(protoNumber);
    }

    [Theory]
    [InlineData(SignatureFormat.Raw, 1)]
    [InlineData(SignatureFormat.Der, 2)]
    [InlineData(SignatureFormat.Concat, 3)]
    [InlineData(SignatureFormat.Symbolic, 10000)]
    public void SignatureFormats_encode_each_public_member_to_its_proto_number(SignatureFormat member, int protoNumber)
    {
        ((int)GrpcEnumMaps.SignatureFormats.ToWire(member, "format")).Should().Be(protoNumber);
    }

    [Theory]
    [InlineData(SigningAlgorithm.Ed25519, 1)]
    [InlineData(SigningAlgorithm.EcDsaSha256, 2)]
    [InlineData(SigningAlgorithm.EcDsaSha384, 3)]
    [InlineData(SigningAlgorithm.MlDsa65, 4)]
    public void SigningAlgorithms_encode_each_public_member_to_its_proto_number(SigningAlgorithm member, int protoNumber)
    {
        ((int)GrpcEnumMaps.SigningAlgorithms.ToWire(member, "algorithm")).Should().Be(protoNumber);
    }

    [Theory]
    [InlineData(SigningKeySpec.EcCurve25519, 1)]
    [InlineData(SigningKeySpec.EcP256, 2)]
    [InlineData(SigningKeySpec.EcP384, 3)]
    [InlineData(SigningKeySpec.EcSecp256k1, 4)]
    [InlineData(SigningKeySpec.MlDsa65, 5)]
    public void SigningKeySpecs_encode_each_public_member_to_its_proto_number(SigningKeySpec member, int protoNumber)
    {
        ((int)GrpcEnumMaps.SigningKeySpecs.ToWire(member, "keySpec")).Should().Be(protoNumber);
    }

    [Theory]
    [InlineData(PublicKeyFormat.Der, 1)]
    [InlineData(PublicKeyFormat.Raw, 2)]
    [InlineData(PublicKeyFormat.DerX509SubjectPublicKeyInfo, 3)]
    public void PublicKeyFormats_encode_each_public_member_to_its_proto_number(PublicKeyFormat member, int protoNumber)
    {
        ((int)GrpcEnumMaps.PublicKeyFormats.ToWire(member, "format")).Should().Be(protoNumber);
    }

    [Theory]
    [InlineData(2, HashingSchemeVersion.V2)]
    [InlineData(3, HashingSchemeVersion.V3)]
    [InlineData(4, HashingSchemeVersion.V4)]
    public void HashingSchemeVersions_decode_each_proto_number_to_its_public_member(
        int protoNumber, HashingSchemeVersion expected)
    {
        GrpcEnumMaps.HashingSchemeVersions.FromWire((Wire.Interactive.HashingSchemeVersion)protoNumber).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(99)]
    public void HashingSchemeVersions_decode_refuses_a_proto_number_it_does_not_know(int protoNumber)
    {
        var act = () => GrpcEnumMaps.HashingSchemeVersions.FromWire((Wire.Interactive.HashingSchemeVersion)protoNumber);

        act.Should().Throw<MalformedResponseException>().WithMessage($"*HashingSchemeVersion value {protoNumber}*");
    }

    [Theory]
    [InlineData(0, SynchronizerPermissionLevel.Unspecified)]
    [InlineData(1, SynchronizerPermissionLevel.Submission)]
    [InlineData(2, SynchronizerPermissionLevel.Confirmation)]
    [InlineData(3, SynchronizerPermissionLevel.Observation)]
    public void SynchronizerPermissions_decode_each_proto_number_to_its_public_member(
        int protoNumber, SynchronizerPermissionLevel expected)
    {
        GrpcEnumMaps.SynchronizerPermissions.FromWire((Wire.ParticipantPermission)protoNumber).Should().Be(expected);
    }

    [Fact]
    public void SynchronizerPermissions_decode_refuses_a_proto_number_it_does_not_know()
    {
        var act = () => GrpcEnumMaps.SynchronizerPermissions.FromWire((Wire.ParticipantPermission)99);

        act.Should().Throw<MalformedResponseException>().WithMessage("*ParticipantPermission value 99*");
    }

    [Theory]
    [InlineData(0, PackageStatus.Unspecified)]
    [InlineData(1, PackageStatus.Registered)]
    public void PackageStatuses_decode_each_proto_number_to_its_public_member(int protoNumber, PackageStatus expected)
    {
        GrpcEnumMaps.PackageStatuses.FromWire((Wire.PackageStatus)protoNumber).Should().Be(expected);
    }

    [Fact]
    public void PackageStatuses_decode_refuses_a_proto_number_it_does_not_know()
    {
        var act = () => GrpcEnumMaps.PackageStatuses.FromWire((Wire.PackageStatus)2);

        act.Should().Throw<MalformedResponseException>().WithMessage("*PackageStatus value 2*");
    }

    [Fact]
    public void HashFunctions_decode_proto_number_0_to_Sha256()
    {
        GrpcEnumMaps.HashFunctions.FromWire((Wire.HashFunction)0).Should().Be(HashFunction.Sha256);
    }

    [Fact]
    public void HashFunctions_decode_refuses_a_proto_number_it_does_not_know()
    {
        var act = () => GrpcEnumMaps.HashFunctions.FromWire((Wire.HashFunction)1);

        act.Should().Throw<MalformedResponseException>().WithMessage("*HashFunction value 1*");
    }

    [Theory]
    [InlineData(1, CommandState.Pending)]
    [InlineData(2, CommandState.Succeeded)]
    [InlineData(3, CommandState.Failed)]
    public void ReportedCommandStates_decode_each_proto_number_to_its_public_member(int protoNumber, CommandState expected)
    {
        GrpcEnumMaps.ReportedCommandStates.FromWire((Wire.Admin.CommandState)protoNumber).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(99)]
    public void ReportedCommandStates_decode_refuses_the_query_wildcard_and_any_proto_number_it_does_not_know(int protoNumber)
    {
        var act = () => GrpcEnumMaps.ReportedCommandStates.FromWire((Wire.Admin.CommandState)protoNumber);

        act.Should().Throw<MalformedResponseException>().WithMessage($"*CommandState value {protoNumber}*");
    }

    [Fact]
    public void HashingSchemeVersions_encode_refuses_an_undefined_member_naming_the_parameter()
    {
        var act = () => GrpcEnumMaps.HashingSchemeVersions.ToWire((HashingSchemeVersion)99, "version");

        act.Should().Throw<ArgumentException>().WithParameterName("version").WithMessage("*HashingSchemeVersion value 99*");
    }

    [Fact]
    public void Every_public_member_of_each_external_signing_enum_has_a_proto_number()
    {
        var encodeEveryMember = () =>
        {
            foreach (var member in Enum.GetValues<HashingSchemeVersion>()) GrpcEnumMaps.HashingSchemeVersions.ToWire(member, "p");
            foreach (var member in Enum.GetValues<SignatureFormat>()) GrpcEnumMaps.SignatureFormats.ToWire(member, "p");
            foreach (var member in Enum.GetValues<SigningAlgorithm>()) GrpcEnumMaps.SigningAlgorithms.ToWire(member, "p");
            foreach (var member in Enum.GetValues<SigningKeySpec>()) GrpcEnumMaps.SigningKeySpecs.ToWire(member, "p");
            foreach (var member in Enum.GetValues<PublicKeyFormat>()) GrpcEnumMaps.PublicKeyFormats.ToWire(member, "p");
        };

        encodeEveryMember.Should().NotThrow();
    }
}
