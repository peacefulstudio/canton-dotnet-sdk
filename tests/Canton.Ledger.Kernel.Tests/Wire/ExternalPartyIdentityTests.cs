// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class ExternalPartyIdentityTests
{
    private const string DocumentedRawEd25519KeyBase64 = "2RwUiIHVUVdulxzD8NKtPmIaaBqMer1A90rDjoklJPY=";
    private const string DocumentedEd25519Fingerprint = "1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa";
    private const string Ed25519SpkiPrefixHex = "302a300506032b6570032100";

    private static SigningPublicKey RawEd25519Key() =>
        new(PublicKeyFormat.Raw, Convert.FromBase64String(DocumentedRawEd25519KeyBase64), SigningKeySpec.EcCurve25519);

    private static SigningPublicKey SpkiEd25519Key() =>
        new(
            PublicKeyFormat.DerX509SubjectPublicKeyInfo,
            Convert.FromHexString(Ed25519SpkiPrefixHex).Concat(Convert.FromBase64String(DocumentedRawEd25519KeyBase64)).ToArray(),
            SigningKeySpec.EcCurve25519);

    private static ExternalPartyTopology Topology(string party, string fingerprint) =>
        new(new Party(party), fingerprint, [new byte[] { 1 }], new byte[] { 2 });

    [Fact]
    public void FingerprintOf_a_raw_Ed25519_key_matches_the_value_Canton_documents()
    {
        ExternalPartyIdentity.FingerprintOf(RawEd25519Key()).Should().Be(DocumentedEd25519Fingerprint);
    }

    [Fact]
    public void FingerprintOf_an_Ed25519_SubjectPublicKeyInfo_hashes_the_raw_key_inside_it()
    {
        ExternalPartyIdentity.FingerprintOf(SpkiEd25519Key()).Should().Be(DocumentedEd25519Fingerprint);
    }

    [Fact]
    public void FingerprintOf_a_P256_SubjectPublicKeyInfo_hashes_the_bytes_as_given()
    {
        var key = new SigningPublicKey(PublicKeyFormat.DerX509SubjectPublicKeyInfo, new byte[] { 0x30, 0x59 }, SigningKeySpec.EcP256);

        ExternalPartyIdentity.FingerprintOf(key)
            .Should().Be("1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472");
    }

    [Fact]
    public void FingerprintOf_an_Ed25519_SubjectPublicKeyInfo_that_is_not_an_Ed25519_key_is_rejected()
    {
        var key = new SigningPublicKey(PublicKeyFormat.DerX509SubjectPublicKeyInfo, new byte[] { 4, 5, 6 }, SigningKeySpec.EcCurve25519);

        var act = () => ExternalPartyIdentity.FingerprintOf(key);

        act.Should().Throw<ArgumentException>().WithMessage("*SubjectPublicKeyInfo*");
    }

    [Fact]
    public void Verify_accepts_a_party_in_the_namespace_of_the_fingerprint_of_the_key_sent()
    {
        var topology = Topology("alice::" + DocumentedEd25519Fingerprint, DocumentedEd25519Fingerprint);

        ExternalPartyIdentity.Verify(SpkiEd25519Key(), topology).Should().BeSameAs(topology);
    }

    [Fact]
    public void Verify_rejects_a_fingerprint_that_is_not_the_one_of_the_key_sent()
    {
        var topology = Topology("alice::1220abcd", "1220abcd");

        var act = () => ExternalPartyIdentity.Verify(SpkiEd25519Key(), topology);

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Be(
            "the participant returned public key fingerprint '1220abcd', but the key sent hashes to "
            + "'" + DocumentedEd25519Fingerprint + "'.");
    }

    [Fact]
    public void Verify_rejects_a_party_id_in_another_namespace_than_the_key_sent()
    {
        var topology = Topology("alice::1220abcd", DocumentedEd25519Fingerprint);

        var act = () => ExternalPartyIdentity.Verify(SpkiEd25519Key(), topology);

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Be(
            "the participant returned party id 'alice::1220abcd', which is not in the namespace "
            + "'" + DocumentedEd25519Fingerprint + "' of the key sent.");
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("alice::")]
    [InlineData("::" + DocumentedEd25519Fingerprint + "x")]
    public void Verify_rejects_a_party_id_without_the_namespace_of_the_key_sent(string party)
    {
        var topology = Topology(party, DocumentedEd25519Fingerprint);

        var act = () => ExternalPartyIdentity.Verify(SpkiEd25519Key(), topology);

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Contain("is not in the namespace");
    }

    [Fact]
    public void Verify_reports_a_key_that_cannot_be_fingerprinted_as_a_malformed_response()
    {
        var key = new SigningPublicKey(PublicKeyFormat.DerX509SubjectPublicKeyInfo, new byte[] { 4, 5, 6 }, SigningKeySpec.EcCurve25519);

        var act = () => ExternalPartyIdentity.Verify(key, Topology("alice::fp", "fp"));

        act.Should().Throw<MalformedResponseException>().Which.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public void VerifyAllocated_returns_the_allocated_party_when_it_is_the_expected_one()
    {
        ExternalPartyIdentity.VerifyAllocated(
                new Party("alice::" + DocumentedEd25519Fingerprint), new Party("alice::" + DocumentedEd25519Fingerprint))
            .Should().Be(new Party("alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa"));
    }

    [Theory]
    [InlineData("bob::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa")]
    [InlineData("alice::1220abcd")]
    [InlineData("ALICE::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa")]
    public void VerifyAllocated_rejects_a_party_other_than_the_expected_one(string allocated)
    {
        var act = () => ExternalPartyIdentity.VerifyAllocated(
            new Party("alice::" + DocumentedEd25519Fingerprint), new Party(allocated));

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Be(
            $"the participant allocated party '{allocated}', but the onboarding topology was generated for "
            + "'alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa'.");
    }
}
