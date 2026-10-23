// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client.Integration.Tests;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerContractKeyRoundTripParityTests : LedgerContractKeyRoundTripParityTests
{
    protected override Task<CapabilityLane<ContractKeyCapability>> OpenContractKeysAsync(
        CancellationToken cancellationToken) =>
        LiveTransports.OpenContractKeysAsync(
            LiveTransports.Grpc,
            context => (reader, contractId, token) => ReadWireKeyHashAsync(
                context.Fixture, reader, contractId, token),
            cancellationToken);

    private static async Task<string?> ReadWireKeyHashAsync(
        LocalnetFixture fixture, Party reader, string contractId, CancellationToken cancellationToken)
    {
        var request = new GetEventsByContractIdRequest
        {
            ContractId = contractId,
            EventFormat = new EventFormat { Verbose = true },
        };
        request.EventFormat.FiltersByParty.Add(reader.Value, new Filters());
        var accessToken = await fixture.TokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var headers = new Metadata { { "Authorization", $"Bearer {accessToken}" } };

        using var channel = GrpcChannel.ForAddress(LocalnetLedgerServices.GrpcAddress);
        var response = await new EventQueryService.EventQueryServiceClient(channel)
            .GetEventsByContractIdAsync(request, headers, cancellationToken: cancellationToken);

        var createdEvent = response.Created?.CreatedEvent;
        if (createdEvent is null)
        {
            Assert.Fail(
                $"EventQueryService returned no created event for {contractId} as read by {reader.Value}, so "
                + "there is no wire contract key hash to compare the projected one against");
            return null;
        }

        var wireHash = createdEvent.ContractKeyHash;
        return wireHash.IsEmpty ? null : Convert.ToBase64String(wireHash.Span);
    }
}
