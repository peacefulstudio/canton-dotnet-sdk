// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// One page of an active-contract snapshot, as returned by
/// <c>ICantonLedgerClient.GetActiveContractsPageAsync</c>.
/// </summary>
/// <param name="Entries">The snapshot entries on this page, projected like the entries of a subscribed snapshot.</param>
/// <param name="ActiveAtOffset">The offset the snapshot is computed at; pass it back on every following page request.</param>
/// <param name="NextPageToken">The token of the next page, or <see langword="null"/> on the last page.</param>
public sealed record AcsPage<T>(
    IReadOnlyList<AcsSnapshotEntry<T>> Entries,
    LedgerOffset ActiveAtOffset,
    LedgerPageToken? NextPageToken)
    where T : ITemplate, IDamlRecord<T>;
