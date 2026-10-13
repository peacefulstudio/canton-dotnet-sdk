// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcTokenFailureSweepTests : TokenFailureSweepTests
{
    private readonly CannedResponseCallInvoker _participant = new(responseType => (Google.Protobuf.IMessage)Activator.CreateInstance(responseType)!);

    protected override byte[] PreparedTransaction => GrpcClientHarness.SerializedPreparedTransaction;

    protected override int RequestsReceived => _participant.RecordedCalls.Count;

    protected override (ICantonLedgerClient Ledger, IAdminClient Admin) CreateClients(ITokenProvider tokenProvider)
    {
        var clients = GrpcClientHarness.CreateClients(_participant, tokenProvider);
        return (clients.Ledger, clients.Admin);
    }

    protected override string? ServedWithoutAuthentication(string entryPoint) => null;
}
