// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Ledger.Abstractions;

/// <summary>
/// What a drained active-contract-set snapshot does with a row the projector could not classify,
/// such as an interface view the participant did not compute or a payload that did not decode.
/// </summary>
/// <remarks>
/// The policy covers unclassified rows only. A fault, a snapshot that ends without its terminal
/// checkpoint, an expired timeout and caller cancellation fail the same way under both values.
/// </remarks>
public enum UnreadableRows
{
    /// <summary>
    /// Throw <see cref="LedgerOperationException"/> on the first unclassified row, so a returned
    /// snapshot is always complete. The default, and the only behaviour before this policy existed.
    /// </summary>
    Fail = 0,

    /// <summary>
    /// Keep draining and hand each unclassified row back on <see cref="ActiveSnapshot{TRow}.Unreadable"/>,
    /// so the readable rows are returned alongside a record of what could not be read.
    /// </summary>
    Return,
}
