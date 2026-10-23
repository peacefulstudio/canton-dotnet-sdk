// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Rest.Client.Raw;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerContractKeyRoundTripParityTests : LedgerContractKeyRoundTripParityTests
{
    protected override Task<CapabilityLane<ContractKeyCapability>> OpenContractKeysAsync(
        CancellationToken cancellationToken) =>
        LiveTransports.OpenContractKeysAsync(
            LiveTransports.Rest,
            context => (reader, contractId, token) => ReadWireKeyHashAsync(
                context.Services.GetRequiredService<IEventQueryServiceApi>(), reader, contractId, token),
            cancellationToken);

    private static async Task<string?> ReadWireKeyHashAsync(
        IEventQueryServiceApi eventQuery, Party reader, string contractId, CancellationToken cancellationToken)
    {
        var response = await eventQuery.GetEventsByContractId(
            new GetEventsByContractIdRequest
            {
                ContractId = contractId,
                EventFormat = new EventFormat
                {
                    Verbose = true,
                    FiltersByParty = new Dictionary<string, Filters> { [reader.Value] = new Filters() },
                },
            },
            cancellationToken).ConfigureAwait(false);

        var createdEvent = response.Created?.CreatedEvent;
        if (createdEvent is null)
        {
            Assert.Fail(
                $"the events-by-contract-id endpoint returned no created event for {contractId} as read by "
                + $"{reader.Value}, so there is no wire contract key hash to compare the projected one against");
            return null;
        }

        return string.IsNullOrEmpty(createdEvent.ContractKeyHash) ? null : createdEvent.ContractKeyHash;
    }
}
