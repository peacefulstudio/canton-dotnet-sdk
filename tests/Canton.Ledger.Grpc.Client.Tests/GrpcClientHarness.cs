// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using Com.Daml.Ledger.Api.V2.Testing;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Xunit;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;

namespace Canton.Ledger.Grpc.Client.Tests;

internal sealed record GrpcClients(LedgerClient Ledger, AdminClient Admin);

internal static class GrpcClientHarness
{
    private static readonly GrpcChannel Channel = GrpcChannel.ForAddress("https://localhost:5001");
    private static readonly LedgerClientOptions Options = new() { GrpcAddress = "https://localhost:5001", UserId = "test-user" };

    private static readonly byte[] SerializedPreparedTransaction =
        new Interactive.PreparedTransaction { Metadata = new Interactive.Metadata { TransactionUuid = "uuid-1" } }.ToByteArray();

    internal static Task Invoke(GrpcClients clients, string entryPoint) =>
        EntryPointInvocations.Invoke<FooBar>(
            clients.Ledger, clients.Admin, entryPoint, SerializedPreparedTransaction, TestContext.Current.CancellationToken);

    internal static GrpcClients CreateClients(CallInvoker invoker)
    {
        var tokenProvider = new StaticTokenProvider("test-token");
        var ledger = new LedgerClient(
            Options,
            Channel,
            new CommandService.CommandServiceClient(invoker),
            new UpdateService.UpdateServiceClient(invoker),
            new StateService.StateServiceClient(invoker),
            new CommandSubmissionService.CommandSubmissionServiceClient(invoker),
            new CommandCompletionService.CommandCompletionServiceClient(invoker),
            tokenProvider,
            new VersionService.VersionServiceClient(invoker),
            new Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient(invoker),
            new ContractService.ContractServiceClient(invoker),
            new EventQueryService.EventQueryServiceClient(invoker));
        var admin = new AdminClient(
            Options,
            Channel,
            new PartyManagementService.PartyManagementServiceClient(invoker),
            new UserManagementService.UserManagementServiceClient(invoker),
            tokenProvider,
            new PackageManagementService.PackageManagementServiceClient(invoker),
            new PackageService.PackageServiceClient(invoker),
            commandInspectionService: new CommandInspectionService.CommandInspectionServiceClient(invoker),
            identityProviderConfigService: new IdentityProviderConfigService.IdentityProviderConfigServiceClient(invoker),
            pruningService: new ParticipantPruningService.ParticipantPruningServiceClient(invoker),
            timeService: new TimeService.TimeServiceClient(invoker));
        return new GrpcClients(ledger, admin);
    }
}
