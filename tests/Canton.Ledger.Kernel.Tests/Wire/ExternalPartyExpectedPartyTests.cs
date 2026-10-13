// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Data;
using Google.Protobuf;
using Xunit;
using Crypto = Com.Digitalasset.Canton.Crypto.V30;
using TopologyV30 = Com.Digitalasset.Canton.Protocol.V30;
using Versioned = Com.Digitalasset.Canton.Version.V1;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class ExternalPartyExpectedPartyTests
{
    private const string Prefix = "The allocation's onboarding transactions are not the onboarding topology of an external party: ";

    private static ExternalPartyAllocation AllocationOf(params byte[][] transactions) =>
        new(
            new SynchronizerId("sync::1"),
            [.. transactions.Select(transaction => new SignedTopologyTransaction(transaction, []))],
            []);

    private static byte[] Tampered(Action<TopologyV30.TopologyTransaction> tamper)
    {
        var transaction = TopologyV30.TopologyTransaction.Parser.ParseFrom(
            Versioned.UntypedVersionedMessage.Parser.ParseFrom(GoldenExternalPartyOnboarding.Transaction()).Data);
        tamper(transaction);
        return new Versioned.UntypedVersionedMessage { Data = transaction.ToByteString(), Version = 30 }.ToByteArray();
    }

    private static string RefusalOf(ExternalPartyAllocation allocation)
    {
        var act = () => ExternalPartyTopologyVerifier.ExpectedPartyOf(allocation);
        var thrown = act.Should().Throw<ArgumentException>().Which;
        thrown.ParamName.Should().Be("allocation");
        return thrown.Message.Split(" (Parameter")[0];
    }

    [Fact]
    public void ExpectedPartyOf_derives_the_hint_in_the_namespace_of_the_signing_key()
    {
        var expected = ExternalPartyTopologyVerifier.ExpectedPartyOf(AllocationOf(GoldenExternalPartyOnboarding.Transaction()));

        expected.Should().Be(new Party("alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa"));
    }

    [Fact]
    public void ExpectedPartyOf_refuses_a_party_outside_the_namespace_of_its_signing_key()
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Party = "alice::1220" + new string('b', 64));

        RefusalOf(AllocationOf(transaction)).Should().Be(
            Prefix + "the PartyToParticipant mapping is for party 'alice::1220" + new string('b', 64)
            + "', which is not 'alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa', "
            + "the hint in the namespace of its signing key.");
    }

    [Fact]
    public void ExpectedPartyOf_refuses_a_party_without_a_namespace()
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Party = "alice");

        RefusalOf(AllocationOf(transaction)).Should().Be(
            Prefix + "the PartyToParticipant mapping is for party 'alice', which has no namespace.");
    }

    [Fact]
    public void ExpectedPartyOf_refuses_no_transactions()
    {
        RefusalOf(AllocationOf()).Should().Be(
            Prefix + "expected exactly one topology transaction, a PartyToParticipant mapping, but got 0.");
    }

    [Fact]
    public void ExpectedPartyOf_refuses_a_second_transaction()
    {
        var golden = GoldenExternalPartyOnboarding.Transaction();

        RefusalOf(AllocationOf(golden, golden)).Should().Be(
            Prefix + "expected exactly one topology transaction, a PartyToParticipant mapping, but got 2.");
    }

    [Fact]
    public void ExpectedPartyOf_refuses_undecodable_bytes()
    {
        RefusalOf(AllocationOf(new byte[] { 1, 1 })).Should().StartWith(Prefix + "the topology transaction could not be decoded.");
    }

    [Fact]
    public void ExpectedPartyOf_refuses_a_mapping_other_than_party_to_participant()
    {
        var transaction = Tampered(tx => tx.Mapping = new TopologyV30.TopologyMapping
        {
            NamespaceDelegation = new TopologyV30.NamespaceDelegation { Namespace = "1220aa" },
        });

        RefusalOf(AllocationOf(transaction)).Should().Be(
            Prefix + "the topology transaction maps NamespaceDelegation, not PartyToParticipant.");
    }

    [Fact]
    public void ExpectedPartyOf_refuses_a_mapping_without_party_signing_keys()
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.PartySigningKeys = null);

        RefusalOf(AllocationOf(transaction)).Should().Be(
            Prefix + "the PartyToParticipant mapping carries no party signing keys.");
    }

    [Fact]
    public void ExpectedPartyOf_refuses_a_mapping_with_two_party_signing_keys()
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.PartySigningKeys.Keys.Add(new Crypto.SigningPublicKey
        {
            Format = Crypto.CryptoKeyFormat.Raw,
            PublicKey = ByteString.CopyFrom(new byte[32]),
            KeySpec = Crypto.SigningKeySpec.EcCurve25519,
        }));

        RefusalOf(AllocationOf(transaction)).Should().Be(
            Prefix + "the PartyToParticipant mapping carries 2 party signing keys, not one.");
    }
}
