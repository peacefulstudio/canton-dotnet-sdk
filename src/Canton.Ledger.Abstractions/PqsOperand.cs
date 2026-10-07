// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

internal sealed class PqsOperand
{
    public PqsOperand(PqsLeafKind kind, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Kind = kind;
        Value = value;
    }

    public PqsLeafKind Kind { get; }

    public object Value { get; }
}

internal enum PqsLeafKind
{
    Text,
    Int64,
    Numeric,
    Bool,
    Date,
    Time,
    Party,
    ContractId,
    Enum,
}
