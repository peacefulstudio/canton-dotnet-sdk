// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using Google.Protobuf;
using Crypto = Com.Digitalasset.Canton.Crypto.V30;
using HostingState = Com.Digitalasset.Canton.Protocol.V31.PartyToParticipant.Types.HostingParticipant.Types.HostingState;
using ParticipantPermission = Com.Digitalasset.Canton.Protocol.V30.Enums.Types.ParticipantPermission;
using TopologyChangeOp = Com.Digitalasset.Canton.Protocol.V30.Enums.Types.TopologyChangeOp;
using TopologyV30 = Com.Digitalasset.Canton.Protocol.V30;
using TopologyV31 = Com.Digitalasset.Canton.Protocol.V31;
using Versioned = Com.Digitalasset.Canton.Version.V1;

namespace Canton.Ledger.Kernel.Wire;

internal static class ExternalPartyTopologyVerifier
{
    private const int TopologyProtoVersion30 = 30;
    private const int TopologyProtoVersion31 = 31;
    private const uint OnboardingSerial = 1;
    private const uint PartySigningThreshold = 1;
    private const string PartyIdSeparator = "::";

    private static readonly MessageParser<Versioned.UntypedVersionedMessage> StrictWrapperParser =
        Versioned.UntypedVersionedMessage.Parser.WithDiscardUnknownFields(true);

    private static readonly MessageParser<TopologyV30.TopologyTransaction> StrictV30TransactionParser =
        TopologyV30.TopologyTransaction.Parser.WithDiscardUnknownFields(true);

    private static readonly MessageParser<TopologyV31.TopologyTransaction> StrictV31TransactionParser =
        TopologyV31.TopologyTransaction.Parser.WithDiscardUnknownFields(true);

    internal static ExternalPartyTopology Verify(ExternalPartyTopologyRequest request, ExternalPartyTopology topology)
    {
        ExternalPartyIdentity.Verify(request.PublicKey, topology);
        RequirePartyIdOfHint(request.PartyIdHint, topology);

        var mapping = SingleOnboardingMapping(topology.TopologyTransactions);
        RequireMappedParty(mapping, topology.Party.Value);
        RequirePartySigningKey(mapping, request.PublicKey, topology.PublicKeyFingerprint);
        RequireHosting(mapping, request);
        RequireMultiHash(topology);
        return topology;
    }

    internal static Party ExpectedPartyOf(ExternalPartyAllocation allocation)
    {
        try
        {
            var transactions = allocation.OnboardingTransactions.Select(signed => signed.Transaction).ToList();
            var mapping = SingleOnboardingMapping(transactions);
            return DerivedParty(mapping);
        }
        catch (MalformedResponseException notAnOnboarding)
        {
            throw new ArgumentException(
                "The allocation's onboarding transactions are not the onboarding topology of an external party: "
                + notAnOnboarding.Detail,
                nameof(allocation),
                notAnOnboarding);
        }
    }

    private static Party DerivedParty(PartyToParticipantMapping mapping)
    {
        var signingKeys = mapping.PartySigningKeys
            ?? throw Mismatch("the PartyToParticipant mapping carries no party signing keys.");
        if (signingKeys.Keys.Count != 1)
        {
            throw Mismatch($"the PartyToParticipant mapping carries {signingKeys.Keys.Count} party signing keys, not one.");
        }

        var separatorIndex = mapping.Party.LastIndexOf(PartyIdSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            throw Mismatch($"the PartyToParticipant mapping is for party '{mapping.Party}', which has no namespace.");
        }

        var derived = $"{mapping.Party[..separatorIndex]}{PartyIdSeparator}{FingerprintOf(signingKeys.Keys[0])}";
        return string.Equals(mapping.Party, derived, StringComparison.Ordinal)
            ? new Party(derived)
            : throw Mismatch(
                $"the PartyToParticipant mapping is for party '{mapping.Party}', which is not '{derived}', "
                + "the hint in the namespace of its signing key.");
    }

    private static void RequirePartyIdOfHint(string partyIdHint, ExternalPartyTopology topology)
    {
        var expected = $"{partyIdHint}::{topology.PublicKeyFingerprint}";
        if (!string.Equals(topology.Party.Value, expected, StringComparison.Ordinal))
        {
            throw Mismatch(
                $"the party id '{topology.Party.Value}' is not '{expected}', the requested hint in the key's namespace.");
        }
    }

    private static PartyToParticipantMapping SingleOnboardingMapping(IReadOnlyList<ReadOnlyMemory<byte>> transactions) =>
        transactions.Count == 1
            ? DecodeOnboardingMapping(transactions[0])
            : throw Mismatch(
                "expected exactly one topology transaction, a PartyToParticipant mapping, "
                + $"but got {transactions.Count}.");

    private static PartyToParticipantMapping DecodeOnboardingMapping(ReadOnlyMemory<byte> serialized)
    {
        var wrapper = DecodeWrapper(serialized);
        return wrapper.Version switch
        {
            TopologyProtoVersion30 => OnboardingMappingOf(DecodeTransaction(StrictV30TransactionParser, wrapper)),
            TopologyProtoVersion31 => OnboardingMappingOf(DecodeTransaction(StrictV31TransactionParser, wrapper)),
            _ => throw Mismatch(
                $"the topology transaction is wrapped as proto version {wrapper.Version}, "
                + $"not {TopologyProtoVersion30} or {TopologyProtoVersion31}."),
        };
    }

    private static Versioned.UntypedVersionedMessage DecodeWrapper(ReadOnlyMemory<byte> serialized)
    {
        Versioned.UntypedVersionedMessage wrapper;
        try
        {
            wrapper = StrictWrapperParser.ParseFrom(serialized.Span);
        }
        catch (InvalidProtocolBufferException undecodable)
        {
            throw new MalformedResponseException("the topology transaction could not be decoded.", undecodable);
        }

        if (wrapper.WrapperCase != Versioned.UntypedVersionedMessage.WrapperOneofCase.Data)
        {
            throw Mismatch("the topology transaction could not be decoded.");
        }

        return serialized.Span.SequenceEqual(wrapper.ToByteArray())
            ? wrapper
            : throw UndecodedContent();
    }

    private static TTransaction DecodeTransaction<TTransaction>(
        MessageParser<TTransaction> parser, Versioned.UntypedVersionedMessage wrapper)
        where TTransaction : IMessage<TTransaction>
    {
        TTransaction transaction;
        try
        {
            transaction = parser.ParseFrom(wrapper.Data);
        }
        catch (InvalidProtocolBufferException undecodable)
        {
            throw new MalformedResponseException("the topology transaction could not be decoded.", undecodable);
        }

        return wrapper.Data.Span.SequenceEqual(transaction.ToByteArray())
            ? transaction
            : throw UndecodedContent();
    }

    private static MalformedResponseException UndecodedContent() =>
        Mismatch("the topology transaction carries content this client does not decode.");

    private static PartyToParticipantMapping OnboardingMappingOf(TopologyV30.TopologyTransaction transaction)
    {
        RequireOnboardingChange(transaction.Operation, transaction.Serial);
        var mappingCase = transaction.Mapping?.MappingCase ?? TopologyV30.TopologyMapping.MappingOneofCase.None;
        if (mappingCase != TopologyV30.TopologyMapping.MappingOneofCase.PartyToParticipant)
        {
            throw NotPartyToParticipant(mappingCase.ToString());
        }

        var mapping = transaction.Mapping!.PartyToParticipant;
        return new PartyToParticipantMapping(
            mapping.Party,
            mapping.Threshold,
            [.. mapping.Participants.Select(participant => new HostingParticipant(
                participant.ParticipantUid,
                participant.Permission,
                participant.Onboarding is null ? HostingState.Active : HostingState.Onboarding))],
            mapping.PartySigningKeys);
    }

    private static PartyToParticipantMapping OnboardingMappingOf(TopologyV31.TopologyTransaction transaction)
    {
        RequireOnboardingChange(transaction.Operation, transaction.Serial);
        var mappingCase = transaction.Mapping?.MappingCase ?? TopologyV31.TopologyMapping.MappingOneofCase.None;
        if (mappingCase != TopologyV31.TopologyMapping.MappingOneofCase.PartyToParticipant)
        {
            throw NotPartyToParticipant(mappingCase.ToString());
        }

        var mapping = transaction.Mapping!.PartyToParticipant;
        return new PartyToParticipantMapping(
            mapping.Party,
            mapping.Threshold,
            [.. mapping.Participants.Select(participant => new HostingParticipant(
                participant.ParticipantUid, participant.Permission, participant.State))],
            mapping.PartySigningKeys);
    }

    private static void RequireOnboardingChange(TopologyChangeOp operation, uint serial)
    {
        if (operation != TopologyChangeOp.AddReplace)
        {
            throw Mismatch($"the topology transaction's operation is {operation}, not AddReplace.");
        }

        if (serial != OnboardingSerial)
        {
            throw Mismatch($"the topology transaction's serial is {serial}, not {OnboardingSerial}.");
        }
    }

    private static MalformedResponseException NotPartyToParticipant(string mappingCase) =>
        Mismatch($"the topology transaction maps {mappingCase}, not PartyToParticipant.");

    private static void RequireMappedParty(PartyToParticipantMapping mapping, string party)
    {
        if (!string.Equals(mapping.Party, party, StringComparison.Ordinal))
        {
            throw Mismatch($"the PartyToParticipant mapping is for party '{mapping.Party}', not '{party}'.");
        }
    }

    private static void RequirePartySigningKey(
        PartyToParticipantMapping mapping, SigningPublicKey requestedKey, string requestedFingerprint)
    {
        var signingKeys = mapping.PartySigningKeys
            ?? throw Mismatch("the PartyToParticipant mapping carries no party signing keys.");

        if (signingKeys.Keys.Count == 1 && (int)signingKeys.Keys[0].KeySpec != (int)requestedKey.KeySpec)
        {
            throw Mismatch(
                $"the party signing key has key spec {signingKeys.Keys[0].KeySpec}, not the requested {requestedKey.KeySpec}.");
        }

        var fingerprints = signingKeys.Keys.Select(FingerprintOf).ToList();
        if (fingerprints.Count != 1 || !string.Equals(fingerprints[0], requestedFingerprint, StringComparison.Ordinal))
        {
            throw Mismatch(
                $"the party signing keys are [{string.Join(", ", fingerprints)}], "
                + $"not the requested key {requestedFingerprint} alone.");
        }

        if (signingKeys.Threshold != PartySigningThreshold)
        {
            throw Mismatch($"the party signing threshold is {signingKeys.Threshold}, not {PartySigningThreshold}.");
        }
    }

    private static string FingerprintOf(Crypto.SigningPublicKey key)
    {
        var format = key.Format switch
        {
            Crypto.CryptoKeyFormat.Der => PublicKeyFormat.Der,
            Crypto.CryptoKeyFormat.Raw => PublicKeyFormat.Raw,
            Crypto.CryptoKeyFormat.DerX509SubjectPublicKeyInfo => PublicKeyFormat.DerX509SubjectPublicKeyInfo,
            _ => throw Mismatch($"a party signing key has format {key.Format}, which is not a public key format."),
        };

        try
        {
            return ExternalPartyIdentity.FingerprintOf(
                new SigningPublicKey(format, key.PublicKey.Memory, (SigningKeySpec)key.KeySpec));
        }
        catch (ArgumentException unfingerprintable)
        {
            throw new MalformedResponseException(
                $"a party signing key cannot be fingerprinted: {unfingerprintable.Message}", unfingerprintable);
        }
    }

    private static void RequireHosting(PartyToParticipantMapping mapping, ExternalPartyTopologyRequest request)
    {
        var confirming = request.OtherConfirmingParticipantUids ?? [];
        var observing = request.ObservingParticipantUids ?? [];
        var requested = confirming
            .Select(uid => (Uid: uid, Permission: ParticipantPermission.Confirmation))
            .Concat(observing.Select(uid => (Uid: uid, Permission: ParticipantPermission.Observation)))
            .ToDictionary(participant => participant.Uid, participant => participant.Permission, StringComparer.Ordinal);

        RequireEachHostedOnce(mapping.Participants);
        RequireGeneratingParticipant(mapping.Participants, requested, request.LocalParticipantObservationOnly);
        foreach (var (uid, permission) in requested)
        {
            var hosting = mapping.Participants.FirstOrDefault(participant => participant.ParticipantUid == uid)
                ?? throw Mismatch($"requested participant {uid} does not host the party.");
            if (hosting.Permission != permission)
            {
                throw Mismatch($"participant {uid} hosts the party with {hosting.Permission} permission, not {permission}.");
            }
        }

        var availableConfirmers = (request.LocalParticipantObservationOnly ? 0 : 1) + confirming.Count;
        var threshold = request.ConfirmationThreshold == 0 ? (uint)availableConfirmers : request.ConfirmationThreshold;
        if (mapping.Threshold != threshold)
        {
            throw Mismatch($"the confirmation threshold is {mapping.Threshold}, not {threshold}.");
        }
    }

    private static void RequireEachHostedOnce(IReadOnlyList<HostingParticipant> participants)
    {
        foreach (var participant in participants)
        {
            RequireActive(participant);
        }

        var repeated = participants
            .GroupBy(participant => participant.ParticipantUid, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (repeated is not null)
        {
            throw Mismatch($"participant {repeated.Key} hosts the party more than once.");
        }
    }

    private static void RequireActive(HostingParticipant participant)
    {
        switch (participant.State)
        {
            case HostingState.Active:
                return;
            case HostingState.Onboarding:
                throw Mismatch($"participant {participant.ParticipantUid} is marked as onboarding the party.");
            case HostingState.Nominated or HostingState.Freezing:
                throw Mismatch(
                    $"participant {participant.ParticipantUid} is {participant.State}, so it does not host the party yet.");
            default:
                throw Mismatch($"participant {participant.ParticipantUid} carries no hosting state.");
        }
    }

    private static void RequireGeneratingParticipant(
        IReadOnlyList<HostingParticipant> participants,
        Dictionary<string, ParticipantPermission> requested,
        bool observationOnly)
    {
        var unrequested = participants.Where(participant => !requested.ContainsKey(participant.ParticipantUid)).ToList();
        if (unrequested.Count != 1)
        {
            throw Mismatch(
                $"the party is hosted on {unrequested.Count} participants the request did not name "
                + $"({string.Join(", ", unrequested.Select(participant => participant.ParticipantUid))}), "
                + "not the generating participant alone.");
        }

        var generating = unrequested[0];
        var permission = observationOnly
            ? ParticipantPermission.Observation
            : ParticipantPermission.Confirmation;
        if (generating.Permission != permission)
        {
            throw Mismatch(
                $"the generating participant {generating.ParticipantUid} hosts the party with "
                + $"{generating.Permission} permission, not {permission}.");
        }
    }

    private static void RequireMultiHash(ExternalPartyTopology topology)
    {
        var recomputed = TopologyMultiHash.Of(topology.TopologyTransactions);
        if (!topology.MultiHash.Span.SequenceEqual(recomputed))
        {
            throw Mismatch(
                $"the multi-hash {Convert.ToHexStringLower(topology.MultiHash.Span)} is not "
                + $"{Convert.ToHexStringLower(recomputed)}, the hash of the topology transactions returned.");
        }
    }

    private static MalformedResponseException Mismatch(string detail) => new(detail);

    private sealed record PartyToParticipantMapping(
        string Party,
        uint Threshold,
        IReadOnlyList<HostingParticipant> Participants,
        Crypto.SigningKeysWithThreshold? PartySigningKeys);

    private sealed record HostingParticipant(string ParticipantUid, ParticipantPermission Permission, HostingState State);
}
