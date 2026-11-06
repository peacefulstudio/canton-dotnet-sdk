// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Google.Protobuf;
using Xunit;
using HostingState = Com.Digitalasset.Canton.Protocol.V31.PartyToParticipant.Types.HostingParticipant.Types.HostingState;
using TopologyV30 = Com.Digitalasset.Canton.Protocol.V30;
using TopologyV31 = Com.Digitalasset.Canton.Protocol.V31;
using Versioned = Com.Digitalasset.Canton.Version.V1;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class ExternalPartyTopologyVerifierV31Tests
{
    private const string GeneratingParticipantUid =
        "participant::12206ae79e0eb6b6bae64a40a8b3b183cf8be36b287bc7750f60742676eb6bb11b56";

    private const string OtherParticipantUid = "PAR::participant3::1220eeee";
    private const string UndecodedContent = "the topology transaction carries content this client does not decode.";

    private const string V30TransactionHex =
        "0ab701080110011ab0014aad010a4b616c6963653a3a313232303230353035376533333163633839323964643231376532663865"
        + "363366353033623730383137373364653630643031666234363833393730306263356361616110011a1f0a1b5041523a3a70617274"
        + "69636970616e74313a3a31323230616161611002323b0a3710041a2c302a300506032b6570032100d91c148881d551576e971cc3f0"
        + "d2ad3e621a681a8c7abd40f74ac38e892524f62a0301050430011001101e";

    private static ExternalPartyTopology TopologyOf(byte[] transaction) =>
        new(
            CapturedV31Topology.Party,
            CapturedV31Topology.PublicKeyFingerprint,
            [transaction],
            TopologyMultiHash.Of([transaction]));

    private static ByteString CapturedData() =>
        Versioned.UntypedVersionedMessage.Parser.ParseFrom(CapturedV31Topology.Transaction).Data;

    private static TopologyV31.TopologyTransaction CapturedTransaction() =>
        TopologyV31.TopologyTransaction.Parser.ParseFrom(CapturedData());

    private static byte[] Wrapped(ByteString data, int version) =>
        new Versioned.UntypedVersionedMessage { Data = data, Version = version }.ToByteArray();

    private static byte[] Tampered(Action<TopologyV31.TopologyTransaction> tamper)
    {
        var transaction = CapturedTransaction();
        tamper(transaction);
        return Wrapped(transaction.ToByteString(), 31);
    }

    private static TopologyV31.PartyToParticipant.Types.HostingParticipant Hosting(
        string uid, TopologyV30.Enums.Types.ParticipantPermission permission, HostingState state) =>
        new() { ParticipantUid = uid, Permission = permission, State = state };

    private static string RefusalOf(ExternalPartyTopologyRequest request, ExternalPartyTopology topology)
    {
        var act = () => ExternalPartyTopologyVerifier.Verify(request, topology);
        return act.Should().Throw<MalformedResponseException>().Which.Detail;
    }

    [Fact]
    public void Verify_accepts_the_v31_transaction_a_protocol_version_37_participant_generated_for_the_request()
    {
        var topology = CapturedV31Topology.Topology();

        ExternalPartyTopologyVerifier.Verify(CapturedV31Topology.Request(), topology).Should().BeSameAs(topology);
    }

    [Fact]
    public void Verify_reads_the_captured_v31_transaction_as_wrapper_version_31_with_one_active_participant_and_one_signing_key()
    {
        var wrapper = Versioned.UntypedVersionedMessage.Parser.ParseFrom(CapturedV31Topology.Transaction);
        var mapping = CapturedTransaction().Mapping.PartyToParticipant;

        wrapper.Version.Should().Be(31);
        mapping.Participants.Should().ContainSingle().Which.ParticipantUid.Should().Be(GeneratingParticipantUid);
        mapping.Participants[0].State.Should().Be(HostingState.Active);
        mapping.PartySigningKeys.Keys.Should().ContainSingle();
    }

    [Fact]
    public void Verify_never_uses_the_v30_schema_for_v31_bytes_because_it_parses_them_without_their_signing_keys()
    {
        var misread = TopologyV30.TopologyTransaction.Parser.ParseFrom(CapturedData());

        misread.Mapping.PartyToParticipant.PartySigningKeys.Should().BeNull();
        RefusalOf(CapturedV31Topology.Request(), TopologyOf(Wrapped(CapturedData(), 30))).Should().Be(UndecodedContent);
    }

    [Fact]
    public void Verify_refuses_v30_bytes_wrapped_as_proto_version_31()
    {
        var v30Data = Versioned.UntypedVersionedMessage.Parser.ParseFrom(Convert.FromHexString(V30TransactionHex)).Data;

        RefusalOf(CapturedV31Topology.Request(), TopologyOf(Wrapped(v30Data, 31))).Should().Be(UndecodedContent);
    }

    [Theory]
    [InlineData(29, "the topology transaction is wrapped as proto version 29, not 30 or 31.")]
    [InlineData(32, "the topology transaction is wrapped as proto version 32, not 30 or 31.")]
    public void Verify_refuses_a_proto_version_other_than_30_or_31(int version, string expected)
    {
        RefusalOf(CapturedV31Topology.Request(), TopologyOf(Wrapped(CapturedData(), version))).Should().Be(expected);
    }

    [Fact]
    public void Verify_hashes_the_captured_v31_transaction_as_the_participant_did()
    {
        Convert.ToHexStringLower(TopologyMultiHash.Of([CapturedV31Topology.Transaction]))
            .Should().Be("1220af3fb25cfa3796c5e68449300a3b7beb7187bc4ba58e8527fdd1180d17c6aa16");
    }

    [Theory]
    [InlineData(1, $"participant {GeneratingParticipantUid} is Nominated, so it does not host the party yet.")]
    [InlineData(2, $"participant {GeneratingParticipantUid} is Freezing, so it does not host the party yet.")]
    [InlineData(3, $"participant {GeneratingParticipantUid} is marked as onboarding the party.")]
    [InlineData(0, $"participant {GeneratingParticipantUid} carries no hosting state.")]
    public void Verify_refuses_a_generating_participant_that_is_not_active(int wireState, string expected)
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Participants[0].State = (HostingState)wireState);

        RefusalOf(CapturedV31Topology.Request(), TopologyOf(transaction)).Should().Be(expected);
    }

    [Fact]
    public void Verify_refuses_a_requested_participant_that_is_only_nominated()
    {
        var request = CapturedV31Topology.Request() with { ObservingParticipantUids = [OtherParticipantUid] };
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Participants.Add(Hosting(
            OtherParticipantUid, TopologyV30.Enums.Types.ParticipantPermission.Observation, HostingState.Nominated)));

        RefusalOf(request, TopologyOf(transaction))
            .Should().Be($"participant {OtherParticipantUid} is Nominated, so it does not host the party yet.");
    }

    [Fact]
    public void Verify_accepts_every_requested_v31_participant_with_its_permission_and_the_requested_threshold()
    {
        const string confirmingUid = "PAR::participant2::1220cccc";
        var request = CapturedV31Topology.Request() with
        {
            OtherConfirmingParticipantUids = [confirmingUid],
            ObservingParticipantUids = [OtherParticipantUid],
            ConfirmationThreshold = 1,
        };
        var topology = TopologyOf(Tampered(tx =>
        {
            tx.Mapping.PartyToParticipant.Participants.Add(Hosting(
                confirmingUid, TopologyV30.Enums.Types.ParticipantPermission.Confirmation, HostingState.Active));
            tx.Mapping.PartyToParticipant.Participants.Add(Hosting(
                OtherParticipantUid, TopologyV30.Enums.Types.ParticipantPermission.Observation, HostingState.Active));
        }));

        ExternalPartyTopologyVerifier.Verify(request, topology).Should().BeSameAs(topology);
    }

    [Fact]
    public void Verify_refuses_a_v31_removal()
    {
        RefusalOf(
                CapturedV31Topology.Request(),
                TopologyOf(Tampered(tx => tx.Operation = TopologyV30.Enums.Types.TopologyChangeOp.Remove)))
            .Should().Be("the topology transaction's operation is Remove, not AddReplace.");
    }

    [Fact]
    public void Verify_refuses_a_v31_serial_other_than_one()
    {
        RefusalOf(CapturedV31Topology.Request(), TopologyOf(Tampered(tx => tx.Serial = 2)))
            .Should().Be("the topology transaction's serial is 2, not 1.");
    }

    [Fact]
    public void Verify_refuses_a_v31_mapping_other_than_party_to_participant()
    {
        var transaction = Tampered(tx => tx.Mapping = new TopologyV31.TopologyMapping
        {
            PartyHostingLimits = new TopologyV30.PartyHostingLimits { Party = CapturedV31Topology.Party.Value },
        });

        RefusalOf(CapturedV31Topology.Request(), TopologyOf(transaction))
            .Should().Be("the topology transaction maps PartyHostingLimits, not PartyToParticipant.");
    }

    [Fact]
    public void Verify_refuses_a_v31_mapping_for_another_party()
    {
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Party = "bob::" + CapturedV31Topology.PublicKeyFingerprint);

        RefusalOf(CapturedV31Topology.Request(), TopologyOf(transaction)).Should().Be(
            "the PartyToParticipant mapping is for party "
            + "'bob::12201a7f9a2e46df4de035ce54bcd90966c503992337feac4ffc223e1335eb287043', "
            + "not 'ext-signing-8e4d21c5c1da451ab3bad771964a0929::12201a7f9a2e46df4de035ce54bcd90966c503992337feac4ffc223e1335eb287043'.");
    }

    [Fact]
    public void Verify_refuses_a_v31_mapping_without_party_signing_keys()
    {
        RefusalOf(CapturedV31Topology.Request(), TopologyOf(Tampered(tx => tx.Mapping.PartyToParticipant.PartySigningKeys = null)))
            .Should().Be("the PartyToParticipant mapping carries no party signing keys.");
    }

    [Fact]
    public void Verify_refuses_a_v31_confirmation_threshold_other_than_the_requested_one()
    {
        RefusalOf(CapturedV31Topology.Request(), TopologyOf(Tampered(tx => tx.Mapping.PartyToParticipant.Threshold = 2)))
            .Should().Be("the confirmation threshold is 2, not 1.");
    }

    [Fact]
    public void Verify_refuses_submission_permission_for_a_v31_generating_participant()
    {
        var transaction = Tampered(tx =>
            tx.Mapping.PartyToParticipant.Participants[0].Permission = TopologyV30.Enums.Types.ParticipantPermission.Submission);

        RefusalOf(CapturedV31Topology.Request(), TopologyOf(transaction)).Should().Be(
            $"the generating participant {GeneratingParticipantUid} hosts the party with Submission permission, not Confirmation.");
    }

    [Fact]
    public void Verify_refuses_a_v31_generating_participant_with_another_uid_alongside_the_named_one()
    {
        var request = CapturedV31Topology.Request() with { ObservingParticipantUids = [OtherParticipantUid] };
        var transaction = Tampered(tx => tx.Mapping.PartyToParticipant.Participants[0].ParticipantUid = OtherParticipantUid);

        RefusalOf(request, TopologyOf(transaction)).Should().Be(
            "the party is hosted on 0 participants the request did not name (), not the generating participant alone.");
    }

    [Fact]
    public void Verify_refuses_a_versioned_wrapper_carrying_a_field_Canton_3_7_1_does_not_define()
    {
        var wrapped = CapturedV31Topology.Transaction.Concat(new byte[] { 0x78, 0x01 }).ToArray();

        RefusalOf(CapturedV31Topology.Request(), TopologyOf(wrapped)).Should().Be(UndecodedContent);
    }

    [Fact]
    public void Verify_refuses_a_v31_transaction_carrying_a_field_Canton_3_7_1_does_not_define()
    {
        var transaction = CapturedTransaction().ToByteArray().Concat(new byte[] { 0xA0, 0x06, 0x01 }).ToArray();

        RefusalOf(CapturedV31Topology.Request(), TopologyOf(Wrapped(ByteString.CopyFrom(transaction), 31)))
            .Should().Be(UndecodedContent);
    }
}
