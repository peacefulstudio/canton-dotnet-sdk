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

    private sealed class InterfaceGracefullyCancellingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new InterfaceGracefullyCancellingFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(200);
    }

    private sealed class InterfaceGracefullyCancellingFakeClient : ConformingFakeClient
    {
        public override async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);
                yield return new InterfaceStreamEvent<TInterface, TView>.Checkpoint(LedgerOffset.Begin);
            }
        }
    }

    private sealed class GracefullyCancellingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new GracefullyCancellingFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(200);
    }

    private sealed class GracefullyCancellingFakeClient : ILedgerClient
    {
        public async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);
                yield return new ContractStreamEvent<T>.Checkpoint(LedgerOffset.Begin);
            }
        }

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> =>
            throw new NotSupportedException();

        public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            bool includeDisclosure = false,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> =>
            throw new NotSupportedException();

        public Task<LedgerOffset> GetLedgerEndAsync(
            TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
            CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            throw new NotSupportedException();

        public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> =>
            throw new NotSupportedException();

        public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> =>
            throw new NotSupportedException();

        public IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            bool includeDisclosure = false,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
