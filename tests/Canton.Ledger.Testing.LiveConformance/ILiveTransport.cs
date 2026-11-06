// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Testing.LiveConformance;

internal interface ILiveTransport : IAsyncDisposable
{
    ICantonLedgerClient CreateClient();
    ICantonLedgerClient CreateFaultingSnapshotClient();
}
