// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class RawCallReplayTests
{
    [Fact]
    public void IsAllowed_is_false_outside_a_scope()
    {
        RawCallReplay.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Allow_makes_IsAllowed_true_until_the_scope_is_disposed()
    {
        var scope = RawCallReplay.Allow();
        var insideScope = RawCallReplay.IsAllowed;
        scope.Dispose();

        insideScope.Should().BeTrue();
        RawCallReplay.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Allow_restores_the_outer_scope_when_a_nested_scope_is_disposed()
    {
        using var outer = RawCallReplay.Allow();
        RawCallReplay.Allow().Dispose();

        RawCallReplay.IsAllowed.Should().BeTrue();
    }
}
