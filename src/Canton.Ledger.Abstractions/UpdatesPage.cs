// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// One page of ledger updates, as returned by <c>ICantonLedgerClient.GetUpdatesPageAsync</c>.
/// </summary>
/// <param name="Updates">The transactions on this page, in the requested order.</param>
/// <param name="LowestPageOffsetExclusive">The exclusive lower bound of the offsets this page covers.</param>
/// <param name="HighestPageOffsetInclusive">The inclusive upper bound of the offsets this page covers.</param>
/// <param name="NextPageToken">The token of the next page, or <see langword="null"/> on the last page.</param>
public sealed record UpdatesPage(
    IReadOnlyList<TransactionResult> Updates,
    LedgerOffset LowestPageOffsetExclusive,
    LedgerOffset HighestPageOffsetInclusive,
    LedgerPageToken? NextPageToken);
