// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class RetryAbandonedExceptionTests
{
    [Fact]
    public void SentAttemptFailure_is_the_exact_failure_the_exception_was_created_with()
    {
        var failure = new InvalidOperationException("connection reset");

        var abandoned = new RetryAbandonedException(failure);

        abandoned.SentAttemptFailure.Should().BeSameAs(failure);
    }
}
