// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

internal sealed record LiveTransport(
    string SkipMessage,
    Func<LocalnetFixture, ServiceProvider> BuildServices,
    Func<ServiceProvider, CancellationToken, Task> VerifyEndpoint);

internal static class LiveTransports
{
    internal static LiveTransport Grpc { get; } = new(
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.",
        fixture =>
        {
            var grpcAddress = LocalnetLedgerServices.GrpcAddress;
            return new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddAdminClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                })
                .AddLedgerClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                })
                .BuildServiceProvider();
        },
        (_, _) => Task.CompletedTask);

    internal static LiveTransport Rest { get; } = new(
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.",
        fixture => new ServiceCollection()
            .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
            .AddRestLedgerRawApis(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
            .AddRestLedgerClient(options => options.HttpAddress = fixture.Endpoints.JsonLedgerApi.ToString())
            .BuildServiceProvider(),
        (services, cancellationToken) => LedgerApiVersionSkewGuard.AssertConformableAsync(
            services.GetRequiredService<IVersionServiceApi>(), cancellationToken));

    internal static Task<CapabilityLane<PreferredPackagesCapability>> OpenPreferredPackagesAsync(
        LiveTransport transport, string partyHint, CancellationToken cancellationToken) =>
        LiveLane.OpenAsync(
            transport.SkipMessage,
            transport.BuildServices,
            async (context, ct) =>
            {
                await transport.VerifyEndpoint(context.Services, ct).ConfigureAwait(false);
                await context.Fixture.UploadDarAsync(RichTypesDar.Path, ct).ConfigureAwait(false);
                var party = await context.Fixture.AllocatePartyAsync(partyHint, cancellationToken: ct)
                    .ConfigureAwait(false);
                await context.ActAsRights.GrantAsync(party.PartyId, ct).ConfigureAwait(false);
                return new PreferredPackagesCapability(
                    context.Services.GetRequiredService<ICantonLedgerClient>(),
                    new Party(party.PartyId),
                    Daml.Codegen.Testing.Conformance.RichTypes.Marker.PackageName);
            },
            cancellationToken);

    internal static Task<CapabilityLane<ExternalSigningCapability>> OpenExternalSigningAsync(
        LiveTransport transport, CancellationToken cancellationToken) =>
        OpenExternalSigningWithDarAsync(transport, RichTypesDar.Path, cancellationToken);

    internal static Task<CapabilityLane<ExternalSigningCapability>> OpenKeyedExternalSigningAsync(
        LiveTransport transport, CancellationToken cancellationToken) =>
        OpenExternalSigningWithDarAsync(transport, ContractKeysDar.Path, cancellationToken);

    internal static Task<CapabilityLane<ContractKeyCapability>> OpenContractKeysAsync(
        LiveTransport transport,
        Func<LiveLaneContext, Func<Party, string, CancellationToken, Task<string?>>> wireKeyHashReader,
        CancellationToken cancellationToken) =>
        LiveLane.OpenAsync(
            transport.SkipMessage,
            transport.BuildServices,
            async (context, ct) =>
            {
                await transport.VerifyEndpoint(context.Services, ct).ConfigureAwait(false);
                await context.Fixture.UploadDarAsync(ContractKeysDar.Path, ct).ConfigureAwait(false);
                var party = await context.Fixture.AllocatePartyAsync("contract-key-parity", cancellationToken: ct)
                    .ConfigureAwait(false);
                await context.ActAsRights.GrantAsync(party.PartyId, ct).ConfigureAwait(false);
                return new ContractKeyCapability(
                    context.Services.GetRequiredService<ICantonLedgerClient>(),
                    new Party(party.PartyId),
                    wireKeyHashReader(context));
            },
            cancellationToken);

    private static Task<CapabilityLane<ExternalSigningCapability>> OpenExternalSigningWithDarAsync(
        LiveTransport transport, string darPath, CancellationToken cancellationToken) =>
        LiveLane.OpenAsync(
            transport.SkipMessage,
            transport.BuildServices,
            async (context, ct) =>
            {
                await transport.VerifyEndpoint(context.Services, ct).ConfigureAwait(false);
                await context.Fixture.UploadDarAsync(darPath, ct).ConfigureAwait(false);
                return new ExternalSigningCapability(
                    context.Services.GetRequiredService<IAdminClient>(),
                    context.Services.GetRequiredService<ICantonLedgerClient>(),
                    (party, grantCancellation) => context.ActAsRights.GrantAsync(party.Value, grantCancellation));
            },
            cancellationToken);
}
