// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

internal interface IPqsFilterVisitor<out T>
{
    T Visit(PqsAll filter);

    T Visit(PqsAny filter);

    T Visit(PqsNot filter);

    T Visit(PqsCompare filter);

    T Visit(PqsOptionalPresence filter);

    T Visit(PqsNotNull filter);

    T Visit(PqsListAny filter);

    T Visit(PqsListAll filter);
}

internal interface IPqsPathVisitor<out T>
{
    T Visit(PqsScopeRoot path);

    T Visit(PqsField path);

    T Visit(PqsFirstElement path);

    T Visit(PqsSomeCast path);

    T Visit(PqsConstructorCast path);

    T Visit(PqsMapEntry path);

    T Visit(PqsDefaulted path);
}
