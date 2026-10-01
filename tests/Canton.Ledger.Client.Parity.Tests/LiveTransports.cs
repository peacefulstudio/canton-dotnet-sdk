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
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    internal static LiveTransport Grpc { get; } = new(
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.",
        fixture =>
        {
            var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
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
        LiveTransport transport, PackageLookup lookup, string partyHint, CancellationToken cancellationToken) =>
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
                var packageName = lookup == PackageLookup.UploadedPackage
                    ? Daml.Codegen.Testing.Conformance.RichTypes.Marker.PackageName
                    : $"no-such-package-{Guid.NewGuid():N}";
                return new PreferredPackagesCapability(
                    context.Services.GetRequiredService<ICantonLedgerClient>(),
                    new Party(party.PartyId),
                    packageName);
            },
            cancellationToken);

    internal static Task<CapabilityLane<ExternalSigningCapability>> OpenExternalSigningAsync(
        LiveTransport transport, CancellationToken cancellationToken) =>
        LiveLane.OpenAsync(
            transport.SkipMessage,
            transport.BuildServices,
            async (context, ct) =>
            {
                await transport.VerifyEndpoint(context.Services, ct).ConfigureAwait(false);
                await context.Fixture.UploadDarAsync(RichTypesDar.Path, ct).ConfigureAwait(false);
                return new ExternalSigningCapability(
                    context.Services.GetRequiredService<IAdminClient>(),
                    context.Services.GetRequiredService<ICantonLedgerClient>(),
                    (party, grantCancellation) => context.ActAsRights.GrantAsync(party.Value, grantCancellation));
            },
            cancellationToken);
}
