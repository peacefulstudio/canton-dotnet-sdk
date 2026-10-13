// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;
using RestClientRegistration = Canton.Ledger.Rest.Client.ServiceCollectionExtensions;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerRetriedWriteCommitStateParityTests : LedgerRetriedWriteCommitStateParityTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    protected override Task<CapabilityLane<RetriedWriteLane>> OpenRetryingAsync(CancellationToken cancellationToken)
    {
        var tap = new LostResponseTap();
        return LiveLane.OpenAsync(
            SkipMessage,
            fixture =>
            {
                var jsonAddress = fixture.Endpoints.JsonLedgerApi.ToString();
                var services = new ServiceCollection()
                    .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                    .AddRestLedgerRawApis(options =>
                    {
                        options.HttpAddress = jsonAddress;
                        options.UserId = fixture.ValidatorUserId;
                        options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 1, Delay = TimeSpan.Zero };
                    })
                    .AddRestLedgerClient(options => options.HttpAddress = jsonAddress);
                services.AddHttpClient(RestClientRegistration.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new LostResponseHandler(tap, new SocketsHttpHandler(), ConnectionReset));
                return services.BuildServiceProvider();
            },
            async (context, ct) =>
            {
                await LedgerApiVersionSkewGuard.AssertConformableAsync(
                    context.Services.GetRequiredService<IVersionServiceApi>(), ct).ConfigureAwait(false);
                await context.Fixture.UploadDarAsync(RichTypesDar.Path, ct).ConfigureAwait(false);
                var party = await context.Fixture.AllocatePartyAsync("rest-retried-write", cancellationToken: ct)
                    .ConfigureAwait(false);
                await context.ActAsRights.GrantAsync(party.PartyId, ct).ConfigureAwait(false);
                return new RetriedWriteLane(
                    context.Services.GetRequiredService<ICantonLedgerClient>(),
                    new Party(party.PartyId),
                    tap,
                    TryOperation: "/v2/commands/submit-and-wait-for-transaction",
                    ThrowingOperation: "/v2/commands/submit-and-wait",
                    FireAndForgetOperation: "/v2/commands/async/submit");
            },
            cancellationToken);
    }

    private static HttpResponseMessage ConnectionReset() =>
        throw new HttpRequestException("connection reset while reading the response");
}
