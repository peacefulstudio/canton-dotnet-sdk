// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Wire;

internal enum LedgerCallKind
{
    Read,
    AcceptedOnlyWrite,
    EffectAppliedWrite,
}
