// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2.Admin;
using Daml.Runtime.Data;
using Google.Protobuf;

namespace Canton.Ledger.Grpc.Client;

internal sealed partial class AdminClient
{
    /// <inheritdoc />
    public Task<ExternalPartyTopology> GenerateExternalPartyTopologyAsync(
        ExternalPartyTopologyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PublicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PartyIdHint);

        var wireRequest = new GenerateExternalPartyTopologyRequest
        {
            Synchronizer = request.SynchronizerId.Value,
            PartyHint = request.PartyIdHint,
            PublicKey = GrpcExternalSigningMapper.ToWire(request.PublicKey),
            LocalParticipantObservationOnly = request.LocalParticipantObservationOnly,
            ConfirmationThreshold = request.ConfirmationThreshold,
        };
        wireRequest.OtherConfirmingParticipantUids.AddRange(request.OtherConfirmingParticipantUids ?? []);
        wireRequest.ObservingParticipantUids.AddRange(request.ObservingParticipantUids ?? []);

        return _invoker.InvokeTracedAsync<AdminClient, GenerateExternalPartyTopologyResponse, ExternalPartyTopology>(
            LedgerCallKind.Read,
            ActivitySource,
            PartyManagementService.Descriptor,
            "GenerateExternalPartyTopology",
            (headers, deadline, token) => _partyService.GenerateExternalPartyTopologyAsync(wireRequest, headers, deadline, token),
            response => MalformedResponse.Decoding(response, generated => new ExternalPartyTopology(
                new Party(generated.PartyId),
                generated.PublicKeyFingerprint,
                generated.TopologyTransactions.Select(transaction => transaction.Memory).ToList(),
                generated.MultiHash.Memory)),
            cancellationToken,
            configureActivity: activity => activity.SetPartyOrContractTag(_options, LedgerActivityTagNames.CantonPartyIdHint, request.PartyIdHint));
    }

    /// <inheritdoc />
    public Task<Party> AllocateExternalPartyAsync(
        ExternalPartyAllocation allocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(allocation.OnboardingTransactions);
        ArgumentNullException.ThrowIfNull(allocation.MultiHashSignatures);

        var wireRequest = new AllocateExternalPartyRequest
        {
            Synchronizer = allocation.SynchronizerId.Value,
            IdentityProviderId = allocation.IdentityProviderId ?? string.Empty,
            UserId = allocation.UserId ?? string.Empty,
        };
        if (allocation.WaitForAllocation is { } waitForAllocation)
            wireRequest.WaitForAllocation = waitForAllocation;
        wireRequest.OnboardingTransactions.AddRange(allocation.OnboardingTransactions.Select(ToWireSignedTransaction));
        wireRequest.MultiHashSignatures.AddRange(allocation.MultiHashSignatures.Select(GrpcExternalSigningMapper.ToWire));

        return _invoker.InvokeTracedAsync<AdminClient, AllocateExternalPartyResponse, Party>(
            LedgerCallKind.EffectAppliedWrite,
            ActivitySource,
            PartyManagementService.Descriptor,
            "AllocateExternalParty",
            (headers, deadline, token) => _partyService.AllocateExternalPartyAsync(wireRequest, headers, deadline, token),
            response => MalformedResponse.Decoding(response, allocated => new Party(allocated.PartyId)),
            cancellationToken,
            replayable: false);
    }

    private static AllocateExternalPartyRequest.Types.SignedTransaction ToWireSignedTransaction(
        SignedTopologyTransaction transaction)
    {
        var wire = new AllocateExternalPartyRequest.Types.SignedTransaction
        {
            Transaction = ByteString.CopyFrom(transaction.Transaction.Span),
        };
        wire.Signatures.AddRange(transaction.Signatures.Select(GrpcExternalSigningMapper.ToWire));
        return wire;
    }
}
