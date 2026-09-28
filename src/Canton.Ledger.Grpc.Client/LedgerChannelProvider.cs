// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Grpc.Net.Client;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Grpc.Client;

internal sealed class LedgerChannelProvider(IOptions<LedgerClientOptions> options) : IDisposable
{
    public GrpcChannel Channel { get; } = LedgerGrpcChannel.Create(options.Value);

    public void Dispose() => Channel.Dispose();
}
