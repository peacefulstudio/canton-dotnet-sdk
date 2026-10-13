// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Data;
using Google.Protobuf;
using Xunit;
using Crypto = Com.Digitalasset.Canton.Crypto.V30;
using TopologyV30 = Com.Digitalasset.Canton.Protocol.V30;
using Versioned = Com.Digitalasset.Canton.Version.V1;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class ExternalPartyTopologyVerifierTests
{
    private const string Fingerprint = "1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa";
    private const string Ed25519SpkiHex =
        "302a300506032b6570032100d91c148881d551576e971cc3f0d2ad3e621a681a8c7abd40f74ac38e892524f6";

    private const string GoldenTransactionHex =
        "0ab701080110011ab0014aad010a4b616c6963653a3a313232303230353035376533333163633839323964643231376532663865"
        + "363366353033623730383137373364653630643031666234363833393730306263356361616110011a1f0a1b5041523a3a70617274"
        + "69636970616e74313a3a31323230616161611002323b0a3710041a2c302a300506032b6570032100d91c148881d551576e971cc3f0"
        + "d2ad3e621a681a8c7abd40f74ac38e892524f62a0301050430011001101e";

    private const string GoldenMultiHashHex = "122039f67adabee1599026d3801cd11293cc150ec9a58d39667624ec7c5796c62611";

    private static SigningPublicKey Ed25519Key() =>
        new(PublicKeyFormat.DerX509SubjectPublicKeyInfo, Convert.FromHexString(Ed25519SpkiHex), SigningKeySpec.EcCurve25519);

    private static ExternalPartyTopologyRequest Request() =>
        new(new SynchronizerId("global-domain::1220bbbb"), "alice", Ed25519Key());

    private static ExternalPartyTopology TopologyOf(params byte[][] transactions) =>
        new(
            new Party("alice::" + Fingerprint),
            Fingerprint,
            [.. transactions.Select(transaction => (ReadOnlyMemory<byte>)transaction)],
            TopologyMultiHash.Of([.. transactions.Select(transaction => (ReadOnlyMemory<byte>)transaction)]));

    private static ExternalPartyTopology GoldenTopology() =>
        new(
            new Party("alice::" + Fingerprint),
            Fingerprint,
            [Convert.FromHexString(GoldenTransactionHex)],
            Convert.FromHexString(GoldenMultiHashHex));

    private static TopologyV30.TopologyTransaction GoldenTransaction() =>
        TopologyV30.TopologyTransaction.Parser.ParseFrom(
            Versioned.UntypedVersionedMessage.Parser.ParseFrom(Convert.FromHexString(GoldenTransactionHex)).Data);

    private static byte[] Serialized(TopologyV30.TopologyTransaction transaction, int version = 30) =>
        new Versioned.UntypedVersionedMessage { Data = transaction.ToByteString(), Version = version }.ToByteArray();

    private static byte[] Tampered(Action<TopologyV30.TopologyTransaction> tamper)
    {
        var transaction = GoldenTransaction();
        tamper(transaction);
        return Serialized(transaction);
    }

    private static TopologyV30.PartyToParticipant.Types.HostingParticipant Hosting(
        string uid, TopologyV30.Enums.Types.ParticipantPermission permission) =>
        new() { ParticipantUid = uid, Permission = permission };

    private static string RefusalOf(ExternalPartyTopologyRequest request, ExternalPartyTopology topology)
    {
        var act = () => ExternalPartyTopologyVerifier.Verify(request, topology);
        return act.Should().Throw<MalformedResponseException>().Which.Detail;
    }

    [Fact]
    public void Verify_accepts_the_single_party_to_participant_transaction_Canton_generates_for_the_request()
    {
        var topology = GoldenTopology();

        ExternalPartyTopologyVerifier.Verify(Request(), topology).Should().BeSameAs(topology);
    }

    [Fact]
    public void Verify_decodes_the_golden_transaction_as_Canton_3_6_1_lays_it_out()
    {
        var transaction = GoldenTransaction();

        transaction.Operation.Should().Be(TopologyV30.Enums.Types.TopologyChangeOp.AddReplace);
        transaction.Serial.Should().Be(1u);
        var mapping = transaction.Mapping.PartyToParticipant;
        mapping.Party.Should().Be("alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa");
        mapping.Threshold.Should().Be(1u);
        mapping.Participants.Should().ContainSingle().Which.ParticipantUid.Should().Be("PAR::participant1::1220aaaa");
        mapping.PartySigningKeys.Threshold.Should().Be(1u);
        mapping.PartySigningKeys.Keys.Should().ContainSingle().Which.KeySpec.Should().Be(Crypto.SigningKeySpec.EcCurve25519);
    }

    [Fact]
    public void Verify_refuses_a_multi_hash_that_does_not_cover_the_transactions()
    {
        var topology = GoldenTopology() with { MultiHash = Convert.FromHexString("1220" + new string('0', 64)) };

        RefusalOf(Request(), topology).Should().Be(
            "the multi-hash 12200000000000000000000000000000000000000000000000000000000000000000 is not "
            + "122039f67adabee1599026d3801cd11293cc150ec9a58d39667624ec7c5796c62611, the hash of the topology transactions returned.");
    }

    [Fact]
    public void Verify_refuses_an_extra_transaction_delegating_the_namespace_to_another_key()
    {
        var delegation = new TopologyV30.TopologyTransaction
        {
            Operation = TopologyV30.Enums.Types.TopologyChangeOp.AddReplace,
            Serial = 1,
            Mapping = new TopologyV30.TopologyMapping
            {
                NamespaceDelegation = new TopologyV30.NamespaceDelegation
                {
                    Namespace = Fingerprint,
                    TargetKey = new Crypto.SigningPublicKey
                    {
                        Format = Crypto.CryptoKeyFormat.Raw,
                        PublicKey = ByteString.CopyFrom(new byte[32]),
                        KeySpec = Crypto.SigningKeySpec.EcCurve25519,
                    },
                    CanSignAllMappings = new TopologyV30.NamespaceDelegation.Types.CanSignAllMappings(),
                },
            },
        };

        RefusalOf(Request(), TopologyOf(Convert.FromHexString(GoldenTransactionHex), Serialized(delegation)))
            .Should().Be("expected exactly one topology transaction, a PartyToParticipant mapping, but got 2.");
    }

    [Fact]
    public void Verify_refuses_a_mapping_other_than_party_to_participant()
    {
        var transaction = Tampered(tx => tx.Mapping = new TopologyV30.TopologyMapping
        {
            PartyHostingLimits = new TopologyV30.PartyHostingLimits { Party = "alice::" + Fingerprint },
        });

        RefusalOf(Request(), TopologyOf(transaction))
            .Should().Be("the topology transaction maps PartyHostingLimits, not PartyToParticipant.");
    }

    [Fact]
    public void Verify_refuses_a_removal()
    {
        RefusalOf(Request(), TopologyOf(Tampered(tx => tx.Operation = TopologyV30.Enums.Types.TopologyChangeOp.Remove)))
            .Should().Be("the topology transaction's operation is Remove, not AddReplace.");
    }

    [Fact]
    public void Verify_refuses_a_serial_other_than_one()
    {
        RefusalOf(Request(), TopologyOf(Tampered(tx => tx.Serial = 2)))
            .Should().Be("the topology transaction's serial is 2, not 1.");
    }

    [Fact]
    public void Verify_refuses_a_mapping_for_another_party()
    {
        RefusalOf(Request(), TopologyOf(Tampered(tx => tx.Mapping.PartyToParticipant.Party = "bob::" + Fingerprint)))
            .Should().Be(
                "the PartyToParticipant mapping is for party 'bob::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa', "
                + "not 'alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa'.");
    }

    [Fact]
    public void Verify_refuses_a_party_id_whose_name_is_not_the_requested_hint()
    {
        var topology = GoldenTopology() with { Party = new Party("mallory::" + Fingerprint) };

        RefusalOf(Request(), topology).Should().Be(
            "the party id 'mallory::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa' is not "
            + "'alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa', the requested hint in the key's namespace.");
    }

    [Fact]
    public void Verify_refuses_a_mapping_without_party_signing_keys()
    {
        RefusalOf(Request(), TopologyOf(Tampered(tx => tx.Mapping.PartyToParticipant.PartySigningKeys = null)))
            .Should().Be("the PartyToParticipant mapping carries no party signing keys.");
    }

    [Fact]
    public void Verify_refuses_a_signing_key_other_than_the_requested_one()
    {
        var transaction = Tampered(tx =>
            tx.Mapping.PartyToParticipant.PartySigningKeys.Keys[0].PublicKey =
                ByteString.CopyFrom(Convert.FromHexString("302a300506032b6570032100" + new string('0', 64))));

        RefusalOf(Request(), TopologyOf(transaction)).Should().Be(
            "the party signing keys are [1220ea618da83b6c6b2c4557ffa17d722045169f52b8f50f3b31fc867e266de7e53d], "
            + "not the requested key 1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa alone.");
    }

    [Fact]
    public void Verify_refuses_a_second_signing_key()
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.PartySigningKeys.Keys.Add(new Crypto.SigningPublicKey
        {
            Format = Crypto.CryptoKeyFormat.Raw,
            PublicKey = ByteString.CopyFrom(new byte[32]),
            KeySpec = Crypto.SigningKeySpec.EcCurve25519,
        }));

        RefusalOf(Request(), TopologyOf(transaction)).Should().Be(
            "the party signing keys are [1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa, "
            + "1220ea618da83b6c6b2c4557ffa17d722045169f52b8f50f3b31fc867e266de7e53d], "
            + "not the requested key 1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa alone.");
    }

    [Fact]
    public void Verify_refuses_a_signing_key_spec_other_than_the_requested_one()
    {
        var transaction = Tampered(tx =>
            tx.Mapping.PartyToParticipant.PartySigningKeys.Keys[0].KeySpec = Crypto.SigningKeySpec.EcP256);

        RefusalOf(Request(), TopologyOf(transaction))
            .Should().Be("the party signing key has key spec EcP256, not the requested EcCurve25519.");
    }

    [Fact]
    public void Verify_refuses_a_signing_threshold_other_than_one()
    {
        RefusalOf(Request(), TopologyOf(Tampered(tx => tx.Mapping.PartyToParticipant.PartySigningKeys.Threshold = 2)))
            .Should().Be("the party signing threshold is 2, not 1.");
    }

    [Fact]
    public void Verify_refuses_a_lower_confirmation_threshold()
    {
        var request = Request() with { OtherConfirmingParticipantUids = ["PAR::participant2::1220cccc"] };
        var transaction = Tampered(tx =>
        {
            tx.Mapping.PartyToParticipant.Participants.Add(
                Hosting("PAR::participant2::1220cccc", TopologyV30.Enums.Types.ParticipantPermission.Confirmation));
            tx.Mapping.PartyToParticipant.Threshold = 1;
        });

        RefusalOf(request, TopologyOf(transaction)).Should().Be("the confirmation threshold is 1, not 2.");
    }

    [Fact]
    public void Verify_refuses_an_extra_confirming_participant()
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Participants.Add(
            Hosting("PAR::mallory::1220dddd", TopologyV30.Enums.Types.ParticipantPermission.Confirmation)));

        RefusalOf(Request(), TopologyOf(transaction)).Should().Be(
            "the party is hosted on 2 participants the request did not name (PAR::participant1::1220aaaa, PAR::mallory::1220dddd), "
            + "not the generating participant alone.");
    }

    [Fact]
    public void Verify_refuses_submission_permission_for_the_generating_participant()
    {
        var transaction = Tampered(tx =>
            tx.Mapping.PartyToParticipant.Participants[0].Permission = TopologyV30.Enums.Types.ParticipantPermission.Submission);

        RefusalOf(Request(), TopologyOf(transaction)).Should().Be(
            "the generating participant PAR::participant1::1220aaaa hosts the party with Submission permission, not Confirmation.");
    }

    [Fact]
    public void Verify_refuses_confirmation_for_a_generating_participant_asked_to_only_observe()
    {
        var request = Request() with
        {
            LocalParticipantObservationOnly = true,
            OtherConfirmingParticipantUids = ["PAR::participant2::1220cccc"],
        };
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Participants.Add(
            Hosting("PAR::participant2::1220cccc", TopologyV30.Enums.Types.ParticipantPermission.Confirmation)));

        RefusalOf(request, TopologyOf(transaction)).Should().Be(
            "the generating participant PAR::participant1::1220aaaa hosts the party with Confirmation permission, not Observation.");
    }

    [Fact]
    public void Verify_refuses_observation_for_a_requested_confirming_participant()
    {
        var request = Request() with { OtherConfirmingParticipantUids = ["PAR::participant2::1220cccc"] };
        var transaction = Tampered(tx =>
        {
            tx.Mapping.PartyToParticipant.Participants.Add(
                Hosting("PAR::participant2::1220cccc", TopologyV30.Enums.Types.ParticipantPermission.Observation));
            tx.Mapping.PartyToParticipant.Threshold = 2;
        });

        RefusalOf(request, TopologyOf(transaction)).Should().Be(
            "participant PAR::participant2::1220cccc hosts the party with Observation permission, not Confirmation.");
    }

    [Fact]
    public void Verify_refuses_a_missing_requested_observing_participant()
    {
        var request = Request() with { ObservingParticipantUids = ["PAR::participant3::1220eeee"] };

        RefusalOf(request, GoldenTopology()).Should().Be(
            "requested participant PAR::participant3::1220eeee does not host the party.");
    }

    [Fact]
    public void Verify_refuses_a_participant_listed_twice()
    {
        var request = Request() with { ObservingParticipantUids = ["PAR::participant3::1220eeee"] };
        var transaction = Tampered(tx =>
        {
            tx.Mapping.PartyToParticipant.Participants.Add(
                Hosting("PAR::participant3::1220eeee", TopologyV30.Enums.Types.ParticipantPermission.Observation));
            tx.Mapping.PartyToParticipant.Participants.Add(
                Hosting("PAR::participant3::1220eeee", TopologyV30.Enums.Types.ParticipantPermission.Observation));
        });

        RefusalOf(request, TopologyOf(transaction))
            .Should().Be("participant PAR::participant3::1220eeee hosts the party more than once.");
    }

    [Fact]
    public void Verify_refuses_a_participant_marked_as_onboarding()
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Participants[0].Onboarding =
            new TopologyV30.PartyToParticipant.Types.HostingParticipant.Types.Onboarding());

        RefusalOf(Request(), TopologyOf(transaction))
            .Should().Be("participant PAR::participant1::1220aaaa is marked as onboarding the party.");
    }

    [Fact]
    public void Verify_accepts_every_requested_participant_with_its_permission_and_the_requested_threshold()
    {
        var request = Request() with
        {
            OtherConfirmingParticipantUids = ["PAR::participant2::1220cccc"],
            ObservingParticipantUids = ["PAR::participant3::1220eeee"],
            ConfirmationThreshold = 1,
        };
        var topology = TopologyOf(Tampered(tx =>
        {
            tx.Mapping.PartyToParticipant.Participants.Add(
                Hosting("PAR::participant2::1220cccc", TopologyV30.Enums.Types.ParticipantPermission.Confirmation));
            tx.Mapping.PartyToParticipant.Participants.Add(
                Hosting("PAR::participant3::1220eeee", TopologyV30.Enums.Types.ParticipantPermission.Observation));
        }));

        ExternalPartyTopologyVerifier.Verify(request, topology).Should().BeSameAs(topology);
    }

    [Fact]
    public void Verify_defaults_the_threshold_to_the_other_confirmers_when_the_generating_participant_only_observes()
    {
        var request = Request() with
        {
            LocalParticipantObservationOnly = true,
            OtherConfirmingParticipantUids = ["PAR::participant2::1220cccc"],
        };
        var topology = TopologyOf(Tampered(tx =>
        {
            tx.Mapping.PartyToParticipant.Participants[0].Permission = TopologyV30.Enums.Types.ParticipantPermission.Observation;
            tx.Mapping.PartyToParticipant.Participants.Add(
                Hosting("PAR::participant2::1220cccc", TopologyV30.Enums.Types.ParticipantPermission.Confirmation));
        }));

        ExternalPartyTopologyVerifier.Verify(request, topology).Should().BeSameAs(topology);
    }

    [Fact]
    public void Verify_refuses_a_transaction_versioned_for_another_protocol()
    {
        RefusalOf(Request(), TopologyOf(Serialized(GoldenTransaction(), version: 31)))
            .Should().Be("the topology transaction is wrapped as proto version 31, not 30.");
    }

    [Fact]
    public void Verify_refuses_a_transaction_carrying_a_field_Canton_3_6_1_does_not_define()
    {
        var transaction = GoldenTransaction().ToByteArray().Concat(new byte[] { 0xA0, 0x06, 0x01 }).ToArray();
        var wrapped = new Versioned.UntypedVersionedMessage { Data = ByteString.CopyFrom(transaction), Version = 30 }.ToByteArray();

        RefusalOf(Request(), TopologyOf(wrapped))
            .Should().Be("the topology transaction carries content this client does not decode.");
    }

    [Fact]
    public void Verify_refuses_bytes_that_are_not_a_topology_transaction()
    {
        RefusalOf(Request(), TopologyOf([0x0A, 0x05, 0x01]))
            .Should().Be("the topology transaction could not be decoded.");
    }

    [Fact]
    public void Verify_refuses_a_fingerprint_that_is_not_the_one_of_the_key_sent()
    {
        var topology = GoldenTopology() with { PublicKeyFingerprint = "1220abcd" };

        RefusalOf(Request(), topology).Should().Be(
            "the participant returned public key fingerprint '1220abcd', but the key sent hashes to "
            + "'1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa'.");
    }
}
