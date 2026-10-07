// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class LedgerCallKindExtensionsTests
{
    [Theory]
    [InlineData("Read", CommitState.NotCommitted)]
    [InlineData("AcceptedOnlyWrite", CommitState.Unknown)]
    [InlineData("EffectAppliedWrite", CommitState.Unknown)]
    public void NoAnswer_carries_the_commit_state_of_a_call_the_participant_never_answered(
        string kindName, CommitState expected)
    {
        var kind = Enum.Parse<LedgerCallKind>(kindName);
        var cause = new InvalidOperationException("connection refused");

        var exception = kind.NoAnswer("connection refused", new TransportStatus.NoResponse(), cause);

        exception.CommitState.Should().Be(expected);
        exception.Message.Should().Be("connection refused");
        exception.Status.Should().Be(new TransportStatus.NoResponse());
        exception.InnerException.Should().BeSameAs(cause);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
    }

    [Theory]
    [InlineData("Read", CommitState.NotCommitted)]
    [InlineData("AcceptedOnlyWrite", CommitState.Unknown)]
    [InlineData("EffectAppliedWrite", CommitState.Committed)]
    public void UnreadableResponse_carries_the_commit_state_of_a_call_whose_success_response_cannot_be_read(
        string kindName, CommitState expected)
    {
        var kind = Enum.Parse<LedgerCallKind>(kindName);
        var cause = new FormatException("not a transaction");

        var exception = kind.UnreadableResponse("could not be decoded: not a transaction", cause);

        exception.CommitState.Should().Be(expected);
        exception.Message.Should().Be("could not be decoded: not a transaction");
        exception.Status.Should().Be(new TransportStatus.UndecodableBody());
        exception.InnerException.Should().BeSameAs(cause);
        exception.Category.Should().BeNull();
    }

    [Fact]
    public void UnreadableResponse_accepts_a_response_that_has_no_exception_to_blame()
    {
        var exception = LedgerCallKind.EffectAppliedWrite.UnreadableResponse("no body was present", cause: null);

        exception.InnerException.Should().BeNull();
        exception.CommitState.Should().Be(CommitState.Committed);
    }
}
