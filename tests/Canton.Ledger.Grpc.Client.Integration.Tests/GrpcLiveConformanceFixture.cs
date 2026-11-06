// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.LiveConformance;
using Peaceful.Canton.Localnet.Testing;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

public sealed class GrpcLiveConformanceFixture : LiveConformanceFixture
{
    internal override ILiveTransport OpenTransport(LocalnetFixture localnet) =>
        new GrpcLiveTransport(LocalnetLedgerServices.ForValidator(localnet, localnet.ValidatorUserId));
}
