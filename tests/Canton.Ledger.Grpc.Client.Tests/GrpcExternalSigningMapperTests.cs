// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Xunit;
using Wire = Com.Daml.Ledger.Api.V2;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcExternalSigningMapperTests
{
    private static LedgerSignature SignatureWith(
        SignatureFormat format = SignatureFormat.Der,
        byte[]? bytes = null,
        string signedBy = "1220abcd",
        SigningAlgorithm algorithm = SigningAlgorithm.Ed25519) =>
        new(format, bytes ?? new byte[] { 1, 2, 3 }, signedBy, algorithm);

    private static SigningPublicKey KeyWith(
        PublicKeyFormat format = PublicKeyFormat.Raw,
        byte[]? keyData = null,
        SigningKeySpec keySpec = SigningKeySpec.EcCurve25519) =>
        new(format, keyData ?? new byte[] { 9, 8, 7 }, keySpec);

    [Fact]
    public void ToWire_signature_copies_every_field()
    {
        var signature = new LedgerSignature(
            SignatureFormat.Concat, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, "1220fingerprint", SigningAlgorithm.EcDsaSha384);

        var wire = GrpcExternalSigningMapper.ToWire(signature);

        wire.Format.Should().Be(Wire.SignatureFormat.Concat);
        wire.Signature_.ToByteArray().Should().Equal(0xDE, 0xAD, 0xBE, 0xEF);
        wire.SignedBy.Should().Be("1220fingerprint");
        wire.SigningAlgorithmSpec.Should().Be(Wire.SigningAlgorithmSpec.EcDsaSha384);
    }

    [Fact]
    public void ToWire_signature_accepts_empty_bytes_and_empty_signed_by()
    {
        var wire = GrpcExternalSigningMapper.ToWire(SignatureWith(bytes: Array.Empty<byte>(), signedBy: ""));

        wire.Signature_.Length.Should().Be(0);
        wire.SignedBy.Should().BeEmpty();
    }

    [Fact]
    public void ToWire_signature_copies_the_bytes_so_later_mutation_does_not_reach_the_wire()
    {
        var bytes = new byte[] { 1, 2, 3 };

        var wire = GrpcExternalSigningMapper.ToWire(SignatureWith(bytes: bytes));
        bytes[0] = 99;

        wire.Signature_.ToByteArray().Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(SignatureFormat.Raw, 1)]
    [InlineData(SignatureFormat.Der, 2)]
    [InlineData(SignatureFormat.Concat, 3)]
    [InlineData(SignatureFormat.Symbolic, 10000)]
    public void ToWire_signature_maps_each_format_to_its_wire_value(SignatureFormat format, int wireValue)
    {
        var wire = GrpcExternalSigningMapper.ToWire(SignatureWith(format: format));

        ((int)wire.Format).Should().Be(wireValue);
    }

    [Theory]
    [InlineData(SigningAlgorithm.Ed25519, 1)]
    [InlineData(SigningAlgorithm.EcDsaSha256, 2)]
    [InlineData(SigningAlgorithm.EcDsaSha384, 3)]
    [InlineData(SigningAlgorithm.MlDsa65, 4)]
    public void ToWire_signature_maps_each_algorithm_to_its_wire_value(SigningAlgorithm algorithm, int wireValue)
    {
        var wire = GrpcExternalSigningMapper.ToWire(SignatureWith(algorithm: algorithm));

        ((int)wire.SigningAlgorithmSpec).Should().Be(wireValue);
    }

    [Fact]
    public void ToWire_signature_refuses_an_undefined_format_naming_the_signature_parameter()
    {
        var act = () => GrpcExternalSigningMapper.ToWire(SignatureWith(format: (SignatureFormat)77));

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("signature");
    }

    [Fact]
    public void ToWire_signature_refuses_an_undefined_algorithm_naming_the_signature_parameter()
    {
        var act = () => GrpcExternalSigningMapper.ToWire(SignatureWith(algorithm: (SigningAlgorithm)77));

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("signature");
    }

    [Fact]
    public void ToWire_key_copies_every_field()
    {
        var key = new SigningPublicKey(
            PublicKeyFormat.DerX509SubjectPublicKeyInfo, new byte[] { 0x30, 0x2A, 0x30 }, SigningKeySpec.EcP384);

        var wire = GrpcExternalSigningMapper.ToWire(key);

        wire.Format.Should().Be(Wire.CryptoKeyFormat.DerX509SubjectPublicKeyInfo);
        wire.KeyData.ToByteArray().Should().Equal(0x30, 0x2A, 0x30);
        wire.KeySpec.Should().Be(Wire.SigningKeySpec.EcP384);
    }

    [Fact]
    public void ToWire_key_accepts_empty_key_data()
    {
        var wire = GrpcExternalSigningMapper.ToWire(KeyWith(keyData: Array.Empty<byte>()));

        wire.KeyData.Length.Should().Be(0);
    }

    [Fact]
    public void ToWire_key_copies_the_key_data_so_later_mutation_does_not_reach_the_wire()
    {
        var keyData = new byte[] { 9, 8, 7 };

        var wire = GrpcExternalSigningMapper.ToWire(KeyWith(keyData: keyData));
        keyData[0] = 99;

        wire.KeyData.ToByteArray().Should().Equal(9, 8, 7);
    }

    [Theory]
    [InlineData(PublicKeyFormat.Der, 1)]
    [InlineData(PublicKeyFormat.Raw, 2)]
    [InlineData(PublicKeyFormat.DerX509SubjectPublicKeyInfo, 3)]
    public void ToWire_key_maps_each_format_to_its_wire_value(PublicKeyFormat format, int wireValue)
    {
        var wire = GrpcExternalSigningMapper.ToWire(KeyWith(format: format));

        ((int)wire.Format).Should().Be(wireValue);
    }

    [Theory]
    [InlineData(SigningKeySpec.EcCurve25519, 1)]
    [InlineData(SigningKeySpec.EcP256, 2)]
    [InlineData(SigningKeySpec.EcP384, 3)]
    [InlineData(SigningKeySpec.EcSecp256k1, 4)]
    [InlineData(SigningKeySpec.MlDsa65, 5)]
    public void ToWire_key_maps_each_key_spec_to_its_wire_value(SigningKeySpec keySpec, int wireValue)
    {
        var wire = GrpcExternalSigningMapper.ToWire(KeyWith(keySpec: keySpec));

        ((int)wire.KeySpec).Should().Be(wireValue);
    }

    [Fact]
    public void ToWire_key_refuses_an_undefined_format_naming_the_key_parameter()
    {
        var act = () => GrpcExternalSigningMapper.ToWire(KeyWith(format: (PublicKeyFormat)77));

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("key");
    }

    [Fact]
    public void ToWire_key_refuses_an_undefined_key_spec_naming_the_key_parameter()
    {
        var act = () => GrpcExternalSigningMapper.ToWire(KeyWith(keySpec: (SigningKeySpec)77));

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("key");
    }

    [Fact]
    public void FromWire_package_reference_copies_id_name_and_version()
    {
        var wire = new Wire.PackageReference
        {
            PackageId = "ab12cd34",
            PackageName = "splice-amulet",
            PackageVersion = "0.1.14",
        };

        var reference = GrpcExternalSigningMapper.FromWire(wire);

        reference.PackageId.Should().Be("ab12cd34");
        reference.PackageName.Should().Be("splice-amulet");
        reference.PackageVersion.Should().Be("0.1.14");
    }

    [Fact]
    public void FromWire_package_reference_keeps_empty_strings()
    {
        var reference = GrpcExternalSigningMapper.FromWire(new Wire.PackageReference());

        reference.Should().Be(new PackageReference("", "", ""));
    }
}
