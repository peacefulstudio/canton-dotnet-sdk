// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Canton.Ledger.Testing.Localnet;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcRichTypesRoundTripParityTests : RichTypesRoundTripParityTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

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
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
        ServiceProvider? services = null;
        try
        {
            await fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken).ConfigureAwait(false);
            var party = await fixture.AllocatePartyAsync(
                "grpc-richtypes-parity", cancellationToken: cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(party.PartyId, cancellationToken).ConfigureAwait(false);
            var counterparty = await fixture.AllocatePartyAsync(
                "grpc-richtypes-parity-counterparty", cancellationToken: cancellationToken).ConfigureAwait(false);
            await actAsRights.GrantAsync(counterparty.PartyId, cancellationToken).ConfigureAwait(false);

            services = new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddLedgerClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                })
                .BuildServiceProvider();

            var client = services.GetRequiredService<ICantonLedgerClient>();
            return new CapabilityLane<RichTypesSession>(
                new RichTypesSession(client, new Party(party.PartyId), new Party(counterparty.PartyId),
                    (contractId, reader, token) => ReadDisclosedContractAsync(fixture, grpcAddress, contractId, reader, token)),
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
        LocalnetFixture fixture, string grpcAddress, string contractId, Party reader, CancellationToken cancellationToken)
    {
        var withBlob = new Filters();
        withBlob.Cumulative.Add(new CumulativeFilter { WildcardFilter = new WildcardFilter { IncludeCreatedEventBlob = true } });
        var request = new GetEventsByContractIdRequest
        {
            ContractId = contractId,
            EventFormat = new EventFormat { Verbose = true },
        };
        request.EventFormat.FiltersByParty[reader.Value] = withBlob;
        var accessToken = await fixture.TokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var headers = new Metadata { { "Authorization", $"Bearer {accessToken}" } };

        using var channel = GrpcChannel.ForAddress(grpcAddress);
        var response = await new EventQueryService.EventQueryServiceClient(channel)
            .GetEventsByContractIdAsync(request, headers, cancellationToken: cancellationToken);

        var created = response.Created.CreatedEvent;
        return new RuntimeCommands.DisclosedContract(
            contractId,
            new RuntimeIdentifier(created.TemplateId.PackageId, created.TemplateId.ModuleName, created.TemplateId.EntityName),
            created.CreatedEventBlob.ToByteArray());
    }
}
