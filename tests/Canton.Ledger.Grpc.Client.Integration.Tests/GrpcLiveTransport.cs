// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Canton.Ledger.Testing.LiveConformance;
using Com.Daml.Ledger.Api.V2;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>Reaches the participant over gRPC through the container <see cref="LocalnetLedgerServices"/> builds.</summary>
internal sealed class GrpcLiveTransport(ServiceProvider services) : ILiveTransport
{
    private const string GetActiveContractsMethod = "GetActiveContracts";
    private const int ResponsesKeptBeforeSnapshotFault = 1;

    public ICantonLedgerClient CreateClient() => new LedgerClient(
        services.GetRequiredService<IOptions<LedgerClientOptions>>(),
        services.GetRequiredService<LedgerChannelProvider>(),
        services.GetRequiredService<ITokenProvider>());

    public ICantonLedgerClient CreateFaultingSnapshotClient()
    {
        var options = services.GetRequiredService<IOptions<LedgerClientOptions>>().Value;
        var channel = services.GetRequiredService<LedgerChannelProvider>().Channel;
        var faultingInvoker = channel.CreateCallInvoker()
            .Intercept(new MidStreamUnavailableInterceptor(GetActiveContractsMethod, ResponsesKeptBeforeSnapshotFault));

        return new LedgerClient(
            options,
            channel,
            new CommandService.CommandServiceClient(channel),
            new UpdateService.UpdateServiceClient(channel),
            new StateService.StateServiceClient(faultingInvoker),
            new CommandSubmissionService.CommandSubmissionServiceClient(channel),
            new CommandCompletionService.CommandCompletionServiceClient(channel),
            services.GetRequiredService<ITokenProvider>());
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();
}
