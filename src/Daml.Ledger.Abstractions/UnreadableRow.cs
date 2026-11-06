// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// A snapshot row the projector could not classify, returned instead of thrown under
/// <see cref="UnreadableRows.Return"/>.
/// </summary>
/// <param name="Kind">Why the row could not be projected.</param>
/// <param name="RawKind">
/// The transport's raw descriptor of the row; set only when <paramref name="Kind"/> is
/// <see cref="UnclassifiedKind.Unknown"/>, <c>null</c> otherwise.
/// </param>
/// <param name="Offset">The offset the row was reported at, or <c>null</c> when the transport reported none.</param>
public sealed record UnreadableRow(UnclassifiedKind Kind, string? RawKind, LedgerOffset? Offset);
