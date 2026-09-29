// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestRichTypesRoundTripParityTests : RichTypesRoundTripParityTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    protected override async Task<CapabilityLane<RichTypesSession>> OpenClientAsync(
        CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        var fixture = LocalnetFixture.FromEnvironment();
        var actAsRights = ActAsRightsLease.ForValidator(fixture);
        ServiceProvider? services = null;
        try
        {
            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddRestLedgerRawApis(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .AddRestLedgerClient(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
                .BuildServiceProvider();

            await LedgerApiVersionSkewGuard.AssertConformableAsync(
                services.GetRequiredService<IVersionServiceApi>(), cancellationToken).ConfigureAwait(false);

            await fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken).ConfigureAwait(false);
            var party = await fixture.AllocatePartyAsync(
                "rest-richtypes-parity", cancellationToken: cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(party.PartyId, cancellationToken).ConfigureAwait(false);
            var counterparty = await fixture.AllocatePartyAsync(
                "rest-richtypes-parity-counterparty", cancellationToken: cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(counterparty.PartyId, cancellationToken).ConfigureAwait(false);

            var client = services.GetRequiredService<ICantonLedgerClient>();
            return new CapabilityLane<RichTypesSession>(
                new RichTypesSession(client, new Party(party.PartyId), new Party(counterparty.PartyId),
                    (contractId, reader, token) => ReadDisclosedContractAsync(
                        services.GetRequiredService<IEventQueryServiceApi>(), contractId, reader, token)),
                async () =>
                {
                    try
                    {
                        await services.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await LaneTeardown.ReleaseAsync(actAsRights, fixture).ConfigureAwait(false);
                    }
                },
                rightsGatedUserId: fixture.ValidatorUserId);
        }
        catch (Exception openFailure)
        {
            await LaneTeardown.ReleaseAsync(openFailure, services, actAsRights, fixture)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<RuntimeCommands.DisclosedContract> ReadDisclosedContractAsync(
        IEventQueryServiceApi eventQuery, string contractId, Party reader, CancellationToken cancellationToken)
    {
        var response = await eventQuery.GetEventsByContractId(
            new GetEventsByContractIdRequest
            {
                ContractId = contractId,
                EventFormat = new EventFormat
                {
                    Verbose = true,
                    FiltersByParty = new Dictionary<string, Filters>
                    {
                        [reader.Value] = new Filters
                        {
                            Cumulative =
                            [
                                new CumulativeFilter
                                {
                                    IdentifierFilter = new IdentifierFilter
                                    {
                                        WildcardFilter = new WildcardFilter { IncludeCreatedEventBlob = true },
                                    },
                                },
                            ],
                        },
                    },
                },
            },
            cancellationToken).ConfigureAwait(false);

        var created = response.Created.CreatedEvent;
        return new RuntimeCommands.DisclosedContract(
            contractId,
            new RuntimeIdentifier(created.TemplateId.PackageId, created.TemplateId.ModuleName, created.TemplateId.EntityName),
            Convert.FromBase64String(created.CreatedEventBlob));
    }
}
