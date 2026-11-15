// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

public class LedgerCompletionReopenBudgetTests
{
    [Fact]
    public void ReopenWindowBudget_is_two_seconds_when_the_first_window_already_saw_the_completion()
        => LedgerCompletionParityTests.ReopenWindowBudget(firstWindowSawTheCompletion: true)
            .Should().Be(TimeSpan.FromSeconds(2));

    [Fact]
    public void ReopenWindowBudget_is_the_full_drain_when_the_completion_is_still_to_come()
        => LedgerCompletionParityTests.ReopenWindowBudget(firstWindowSawTheCompletion: false)
            .Should().Be(TimeSpan.FromSeconds(30));
}
