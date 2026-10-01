// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Data;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestAdminClient
{
    private const string GenerateExternalPartyTopologyPath = "/v2/parties/external/generate-topology";
    private const string AllocateExternalPartyPath = "/v2/parties/external/allocate";

    /// <inheritdoc />
    /// <remarks>Sent as <c>POST /v2/parties/external/generate-topology</c>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The public key carries a format or key specification this SDK version has no JSON Ledger API name for.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The confirmation threshold exceeds the <see cref="int.MaxValue"/> the JSON Ledger API accepts.
    /// </exception>
    public Task<ExternalPartyTopology> GenerateExternalPartyTopologyAsync(
        ExternalPartyTopologyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.ConfirmationThreshold, (uint)int.MaxValue, nameof(request));

        var body = new ServedGenerateExternalPartyTopologyRequest(
            request.SynchronizerId.Value,
            request.PartyIdHint,
            RestExternalSigningConversions.ToServed(request.PublicKey, nameof(request)),
            request.LocalParticipantObservationOnly ? true : null,
            request.OtherConfirmingParticipantUids is { Count: > 0 } confirming ? confirming : null,
            request.ConfirmationThreshold > 0 ? (int)request.ConfirmationThreshold : null,
            request.ObservingParticipantUids is { Count: > 0 } observing ? observing : null);

        return TracedAsync(
            nameof(GenerateExternalPartyTopologyAsync),
            () => _calls.SendAsync<Raw.GenerateExternalPartyTopologyResponse, ExternalPartyTopology>(
                new RestCall(
                    HttpMethod.Post, GenerateExternalPartyTopologyPath, body,
                    MissingBody("external party topology"), MalformedBody("external party topology")),
                ToExternalPartyTopology,
                timeout: null,
                cancellationToken));
    }

    /// <inheritdoc />
    /// <remarks>Sent as <c>POST /v2/parties/external/allocate</c>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="allocation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A signature carries a format or algorithm this SDK version has no JSON Ledger API name for.
    /// </exception>
    public Task<Party> AllocateExternalPartyAsync(
        ExternalPartyAllocation allocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        var body = new ServedAllocateExternalPartyRequest(
            allocation.SynchronizerId.Value,
            [
                .. allocation.OnboardingTransactions.Select(signed => new ServedSignedTransaction(
                    Convert.ToBase64String(signed.Transaction.Span),
                    RestExternalSigningConversions.ToServedOrOmitted(signed.Signatures, nameof(allocation)))),
            ],
            RestExternalSigningConversions.ToServedOrOmitted(allocation.MultiHashSignatures, nameof(allocation)),
            allocation.IdentityProviderId,
            allocation.WaitForAllocation,
            allocation.UserId);

        return TracedAsync(
            nameof(AllocateExternalPartyAsync),
            () => _calls.SendAsync<Raw.AllocateExternalPartyResponse, Party>(
                Mutate(HttpMethod.Post, AllocateExternalPartyPath, body, "allocated external party"),
                response => string.IsNullOrEmpty(response.PartyId)
                    ? throw MalformedResponse.MissingRequiredField("The response carries no partyId")
                    : new Party(response.PartyId),
                timeout: null,
                cancellationToken));
    }

    private static ExternalPartyTopology ToExternalPartyTopology(Raw.GenerateExternalPartyTopologyResponse response) =>
        new(
            string.IsNullOrEmpty(response.PartyId)
                ? throw MalformedResponse.MissingRequiredField("The response carries no partyId")
                : new Party(response.PartyId),
            response.PublicKeyFingerprint
                ?? throw MalformedResponse.MissingRequiredField("The response carries no publicKeyFingerprint"),
            [
                .. (response.TopologyTransactions
                    ?? throw MalformedResponse.MissingRequiredField("The response carries no topologyTransactions"))
                    .Select(transaction => RestExternalSigningConversions.FromBase64(transaction, "topology transaction")),
            ],
            RestExternalSigningConversions.FromBase64(response.MultiHash, "multiHash"));
}
