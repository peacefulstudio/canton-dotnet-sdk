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
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class HangingSubscriptionConformanceTests
{
    [Fact]
    public async Task Cancellation_test_fails_with_a_TimeoutException_when_the_transport_ignores_the_token()
    {
        var kit = new HangingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Cancelling_a_live_subscription_throws_OperationCanceledException());

        run.Should().NotBeNull(
            "a transport that ignores the cancellation token must fail the kit's test, not hang the run");
        run!.Message.Should().Contain(nameof(TimeoutException));
        run.Message.Should().Contain("a cancelled live subscription must throw OperationCanceledException");
    }

    [Fact]
    public async Task Cancellation_test_fails_with_a_TimeoutException_when_the_transport_drops_the_callers_token()
    {
        var kit = new TokenDroppingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Cancelling_a_live_subscription_throws_OperationCanceledException());

        run.Should().NotBeNull(
            "a transport whose guard method drops the caller's token must fail the kit's test: "
            + "the kit must not hand the caller's token to the stream's enumerator on the transport's behalf");
        run!.Message.Should().Contain(nameof(TimeoutException));
        run.Message.Should().Contain("a cancelled live subscription must throw OperationCanceledException");
    }

    [Fact]
    public async Task Interface_cancellation_test_fails_with_a_TimeoutException_when_the_transport_ignores_the_token()
    {
        var kit = new InterfaceHangingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Interface_cancelling_a_live_subscription_throws_OperationCanceledException());

        run.Should().NotBeNull(
            "an interface subscription that ignores the cancellation token must fail the kit's test, not hang the run");
        run!.Message.Should().Contain(nameof(TimeoutException));
        run.Message.Should().Contain(
            "a cancelled live interface subscription must throw OperationCanceledException");
    }

    [Fact]
    public async Task Interface_cancellation_test_fails_with_a_TimeoutException_when_the_transport_drops_the_callers_token()
    {
        var kit = new InterfaceTokenDroppingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Interface_cancelling_a_live_subscription_throws_OperationCanceledException());

        run.Should().NotBeNull(
            "an interface read whose guard method drops the caller's token must fail the kit's test: "
            + "the kit must not hand the caller's token to the stream's enumerator on the transport's behalf");
        run!.Message.Should().Contain(nameof(TimeoutException));
        run.Message.Should().Contain(
            "a cancelled live interface subscription must throw OperationCanceledException");
    }

    private sealed class HangingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new TokenIgnoringFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(200);
    }

    private sealed class TokenDroppingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new TokenDroppingFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(300);
    }

    private sealed class InterfaceHangingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new InterfaceTokenIgnoringFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(200);
    }

    private sealed class InterfaceTokenDroppingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new InterfaceTokenDroppingFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(300);
    }

    private sealed class InterfaceTokenDroppingFakeClient : ConformingFakeClient
    {
        public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default) =>
            Checkpoints<TInterface, TView>(CancellationToken.None);

        private static async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> Checkpoints<TInterface, TView>(
            [EnumeratorCancellation] CancellationToken cancellationToken)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
                yield return new InterfaceStreamEvent<TInterface, TView>.Checkpoint(LedgerOffset.Begin);
            }
        }
    }

    private sealed class InterfaceTokenIgnoringFakeClient : ConformingFakeClient
    {
        public override async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);
                yield return new InterfaceStreamEvent<TInterface, TView>.Checkpoint(LedgerOffset.Begin);
            }
        }
    }

    private sealed class TokenDroppingFakeClient : NotSupportedLedgerClient
    {
        public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default) =>
            Checkpoints<T>(CancellationToken.None);

        private static async IAsyncEnumerable<ContractStreamEvent<T>> Checkpoints<T>(
            [EnumeratorCancellation] CancellationToken cancellationToken)
            where T : ITemplate, IDamlRecord<T>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
                yield return new ContractStreamEvent<T>.Checkpoint(LedgerOffset.Begin);
            }
        }
    }

    private sealed class TokenIgnoringFakeClient : NotSupportedLedgerClient
    {
        public override async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);
                yield return new ContractStreamEvent<T>.Checkpoint(LedgerOffset.Begin);
            }
        }
    }
}
