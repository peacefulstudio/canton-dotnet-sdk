// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Wire;

internal abstract record RetriedDuplicateVerdict
{
    private RetriedDuplicateVerdict()
    {
    }

    internal sealed record NotARetriedDuplicate : RetriedDuplicateVerdict;

    internal sealed record Unresolvable : RetriedDuplicateVerdict;

    internal sealed record CommittedAt(long CompletionOffset) : RetriedDuplicateVerdict;
}
