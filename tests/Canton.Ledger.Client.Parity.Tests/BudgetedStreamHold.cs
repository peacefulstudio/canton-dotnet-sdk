// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Localnet;

namespace Canton.Ledger.Client.Parity.Tests;

internal sealed class BudgetedStreamHold : IDisposable
{
    private readonly StreamHold? _hold;
    private readonly CancellationTokenSource _budget;

    private BudgetedStreamHold(StreamHold? hold, CancellationTokenSource budget)
    {
        _hold = hold;
        _budget = budget;
    }

    internal CancellationToken Token => _budget.Token;

    internal static BudgetedStreamHold StartBudget(StreamHold? hold, TimeSpan budget, CancellationToken cancellationToken)
    {
        var budgetSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetSource.CancelAfter(budget);
        return new BudgetedStreamHold(hold, budgetSource);
    }

    public void Dispose()
    {
        _budget.Dispose();
        _hold?.Dispose();
    }
}
