// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class GrpcLedgerRetriedWriteCommitStateParityTests : LedgerRetriedWriteCommitStateParityTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    protected override Task<CapabilityLane<RetriedWriteLane>> OpenRetryingAsync(CancellationToken cancellationToken)
    {
        var tap = new LostResponseTap();
        var grpcAddress = LocalnetLedgerServices.GrpcAddress;
        return LiveLane.OpenAsync(
            SkipMessage,
            fixture => new ServiceCollection()
                .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
                .AddLedgerClient(options =>
                {
                    options.GrpcAddress = grpcAddress;
                    options.UserId = fixture.ValidatorUserId;
                    options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 1, Delay = TimeSpan.Zero };
                    options.ConfigureChannel = channel =>
                        channel.HttpHandler = new LostResponseHandler(tap, channel.HttpHandler!, Unavailable);
                })
                .BuildServiceProvider(),
            async (context, ct) =>
            {
                await context.Fixture.UploadDarAsync(RichTypesDar.Path, ct).ConfigureAwait(false);
                var party = await context.Fixture.AllocatePartyAsync("grpc-retried-write", cancellationToken: ct)
                    .ConfigureAwait(false);
                await context.ActAsRights.GrantAsync(party.PartyId, ct).ConfigureAwait(false);
                return new RetriedWriteLane(
                    context.Services.GetRequiredService<ICantonLedgerClient>(),
                    new Party(party.PartyId),
                    tap,
                    TryOperation: "/SubmitAndWaitForTransaction",
                    ThrowingOperation: "/SubmitAndWait",
                    FireAndForgetOperation: "/Submit");
            },
            cancellationToken);
    }

    private static HttpResponseMessage Unavailable()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent([]),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        response.Headers.TryAddWithoutValidation("grpc-status", "14");
        response.Headers.TryAddWithoutValidation("grpc-message", "connection reset");
        return response;
    }
}
