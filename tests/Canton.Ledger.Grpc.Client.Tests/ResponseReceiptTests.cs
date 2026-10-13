// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ResponseReceiptTests
{
    [Fact]
    public void Retried_is_true_once_a_call_records_a_second_attempt()
    {
        var receipt = ResponseReceipt.OpenForCurrentCall();
        ResponseReceipt.AwaitResponse();

        ResponseReceipt.RecordAttempt();
        ResponseReceipt.RecordAttempt();

        receipt.Retried.Should().BeTrue();
        ResponseReceipt.CurrentCallRetried.Should().BeTrue();
    }

    [Fact]
    public void Retried_is_false_for_a_single_attempt()
    {
        var receipt = ResponseReceipt.OpenForCurrentCall();
        ResponseReceipt.AwaitResponse();

        ResponseReceipt.RecordAttempt();

        receipt.Retried.Should().BeFalse();
    }

    [Fact]
    public void AwaitResponse_forgets_the_attempts_of_an_earlier_call_in_the_same_operation()
    {
        var receipt = ResponseReceipt.OpenForCurrentCall();
        ResponseReceipt.AwaitResponse();
        ResponseReceipt.RecordAttempt();
        ResponseReceipt.RecordAttempt();

        ResponseReceipt.AwaitResponse();
        ResponseReceipt.RecordAttempt();

        receipt.Retried.Should().BeFalse();
        ResponseReceipt.CurrentCallRetried.Should().BeFalse();
    }
}
