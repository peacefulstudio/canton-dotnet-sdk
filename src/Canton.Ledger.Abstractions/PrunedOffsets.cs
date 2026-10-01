// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The offsets up to which the participant has pruned, as returned by
/// <c>ICantonLedgerClient.GetLatestPrunedOffsetsAsync</c>. <see cref="LedgerOffset.Begin"/> means
/// nothing has been pruned.
/// </summary>
/// <param name="ParticipantPrunedUpToInclusive">The offset up to which the participant has pruned, inclusive.</param>
/// <param name="AllDivulgedContractsPrunedUpToInclusive">The offset up to which all divulged contracts have been pruned, inclusive.</param>
public sealed record PrunedOffsets(
    LedgerOffset ParticipantPrunedUpToInclusive,
    LedgerOffset AllDivulgedContractsPrunedUpToInclusive);
