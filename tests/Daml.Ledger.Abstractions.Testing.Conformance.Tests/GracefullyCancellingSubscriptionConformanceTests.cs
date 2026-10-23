// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class GracefullyCancellingSubscriptionConformanceTests
{
    [Fact]
    public async Task Cancellation_test_fails_when_the_transport_ends_the_stream_gracefully_instead_of_throwing()
    {
        var kit = new GracefullyCancellingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Cancelling_a_live_subscription_throws_OperationCanceledException());

        run.Should().NotBeNull(
            "a transport that ends the stream gracefully on cancel must fail the kit's test: "
            + "a caller draining into a list would otherwise get a silently partial result");
        run!.Message.Should().Contain(nameof(OperationCanceledException));
        run.Message.Should().Contain("no exception was thrown");
    }

    [Fact]
    public async Task Cancellation_test_rejects_a_graceful_end_without_falling_back_on_the_timeout_path()
    {
        var kit = new GracefullyCancellingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Cancelling_a_live_subscription_throws_OperationCanceledException());

        run!.Message.Should().NotContain(
            nameof(TimeoutException),
            "the graceful end must be caught by the OperationCanceledException assertion itself, "
            + "not by the stream budget that already covers a transport ignoring the token");
    }

    [Fact]
    public async Task Interface_cancellation_test_fails_when_the_transport_ends_the_stream_gracefully_instead_of_throwing()
    {
        var kit = new InterfaceGracefullyCancellingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Interface_cancelling_a_live_subscription_throws_OperationCanceledException());

        run.Should().NotBeNull(
            "an interface subscription that ends gracefully on cancel must fail the kit's test: "
            + "a caller draining into a list would otherwise get a silently partial result");
        run!.Message.Should().Contain(nameof(OperationCanceledException));
        run.Message.Should().Contain("no exception was thrown");
    }

    [Fact]
    public async Task Interface_cancellation_test_rejects_a_graceful_end_without_falling_back_on_the_timeout_path()
    {
        var kit = new InterfaceGracefullyCancellingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Interface_cancelling_a_live_subscription_throws_OperationCanceledException());

        run!.Message.Should().NotContain(
            nameof(TimeoutException),
            "the graceful end must be caught by the OperationCanceledException assertion itself, "
            + "not by the stream budget that already covers a transport ignoring the token");
    }

    private static async Task<bool> DelayUnlessCancelled(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private sealed class InterfaceGracefullyCancellingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new InterfaceGracefullyCancellingFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");
    }

    private sealed class InterfaceGracefullyCancellingFakeClient()
        : DelegatingLedgerClient(SeededShippedFake.Create())
    {
        public override async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (await DelayUnlessCancelled(cancellationToken))
            {
                yield return new InterfaceStreamEvent<TInterface, TView>.Checkpoint(LedgerOffset.Begin);
            }
        }
    }

    private sealed class GracefullyCancellingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new GracefullyCancellingFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");
    }

    private sealed class GracefullyCancellingFakeClient : NotSupportedLedgerClient
    {
        public override async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (await DelayUnlessCancelled(cancellationToken))
            {
                yield return new ContractStreamEvent<T>.Checkpoint(LedgerOffset.Begin);
            }
        }
    }
}
