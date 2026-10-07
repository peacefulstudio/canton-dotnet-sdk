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

public sealed class NonTerminatingStreamConformanceTests
{
    private const string SnapshotContract = "SubscribeActiveAsync must terminate with a terminal Checkpoint";
    private const string InterfaceSnapshotContract =
        "interface SubscribeActiveAsync must terminate with a terminal Checkpoint";
    private const string BoundedSubscriptionContract = "A bounded SubscribeAsync (toOffset 3) must complete";
    private const string BoundedEffectsContract = "A bounded SubscribeLedgerEffectsAsync (toOffset 3) must complete";
    private const string InterfaceBoundedSubscriptionContract =
        "A bounded interface SubscribeAsync (toOffset 3) must complete";
    private const string InterfaceBoundedEffectsContract =
        "A bounded interface SubscribeLedgerEffectsAsync (toOffset 3) must complete";

    [Fact]
    public async Task Unclassified_row_check_fails_with_a_TimeoutException_when_the_snapshot_never_terminates() =>
        await AssertBoundedFailure(
            kit => kit.Active_snapshot_surfaces_unclassifiable_rows_as_Unclassified(), SnapshotContract);

    [Fact]
    public async Task Terminal_checkpoint_check_fails_with_a_TimeoutException_when_the_snapshot_never_terminates() =>
        await AssertBoundedFailure(
            kit => kit.Active_snapshot_ends_with_a_terminal_Checkpoint(), SnapshotContract);

    [Fact]
    public async Task Seeded_rows_check_fails_with_a_TimeoutException_when_the_snapshot_never_terminates() =>
        await AssertBoundedFailure(
            kit => kit.Active_snapshot_yields_seeded_rows_before_the_checkpoint(), SnapshotContract);

    [Fact]
    public async Task Empty_snapshot_check_fails_with_a_TimeoutException_when_the_snapshot_never_terminates() =>
        await AssertBoundedFailure(
            kit => kit.Empty_active_snapshot_still_ends_with_a_terminal_Checkpoint(), SnapshotContract);

    [Fact]
    public async Task Interface_unclassified_row_check_fails_with_a_TimeoutException_when_the_interface_snapshot_never_terminates() =>
        await AssertInterfaceBoundedFailure(
            kit => kit.Interface_active_snapshot_surfaces_unclassifiable_rows_as_Unclassified(),
            InterfaceSnapshotContract);

    [Fact]
    public async Task Interface_terminal_checkpoint_check_fails_with_a_TimeoutException_when_the_interface_snapshot_never_terminates() =>
        await AssertInterfaceBoundedFailure(
            kit => kit.Interface_active_snapshot_ends_with_a_terminal_Checkpoint(), InterfaceSnapshotContract);

    [Fact]
    public async Task Interface_seeded_rows_check_fails_with_a_TimeoutException_when_the_interface_snapshot_never_terminates() =>
        await AssertInterfaceBoundedFailure(
            kit => kit.Interface_active_snapshot_yields_seeded_rows_before_the_checkpoint(),
            InterfaceSnapshotContract);

    [Fact]
    public async Task Interface_empty_snapshot_check_fails_with_a_TimeoutException_when_the_interface_snapshot_never_terminates() =>
        await AssertInterfaceBoundedFailure(
            kit => kit.Interface_empty_active_snapshot_still_ends_with_a_terminal_Checkpoint(),
            InterfaceSnapshotContract);

    [Fact]
    public async Task Exclusive_fromOffset_check_fails_with_a_TimeoutException_when_toOffset_is_ignored() =>
        await AssertBoundedFailure(
            kit => kit.Subscribing_from_an_offset_excludes_the_event_at_that_offset(), BoundedSubscriptionContract);

    [Fact]
    public async Task Inclusive_toOffset_check_fails_with_a_TimeoutException_when_toOffset_is_ignored() =>
        await AssertBoundedFailure(
            kit => kit.Bounded_subscription_delivers_the_event_at_toOffset_then_completes(),
            BoundedSubscriptionContract);

    [Fact]
    public async Task Acs_delta_shape_check_fails_with_a_TimeoutException_when_toOffset_is_ignored() =>
        await AssertBoundedFailure(
            kit => kit.Acs_delta_subscription_never_yields_Exercised(), BoundedSubscriptionContract);

    [Fact]
    public async Task Ledger_effects_shape_check_fails_with_a_TimeoutException_when_toOffset_is_ignored() =>
        await AssertBoundedFailure(
            kit => kit.Ledger_effects_subscription_never_yields_Archived(), BoundedEffectsContract);

    [Fact]
    public async Task Interface_exclusive_fromOffset_check_fails_with_a_TimeoutException_when_the_interface_toOffset_is_ignored() =>
        await AssertInterfaceBoundedFailure(
            kit => kit.Interface_subscribing_from_an_offset_excludes_the_event_at_that_offset(),
            InterfaceBoundedSubscriptionContract);

    [Fact]
    public async Task Interface_inclusive_toOffset_check_fails_with_a_TimeoutException_when_the_interface_toOffset_is_ignored() =>
        await AssertInterfaceBoundedFailure(
            kit => kit.Interface_bounded_subscription_delivers_the_event_at_toOffset_then_completes(),
            InterfaceBoundedSubscriptionContract);

    [Fact]
    public async Task Interface_acs_delta_shape_check_fails_with_a_TimeoutException_when_the_interface_toOffset_is_ignored() =>
        await AssertInterfaceBoundedFailure(
            kit => kit.Interface_acs_delta_subscription_never_yields_Exercised(),
            InterfaceBoundedSubscriptionContract);

    [Fact]
    public async Task Interface_ledger_effects_shape_check_fails_with_a_TimeoutException_when_the_interface_toOffset_is_ignored() =>
        await AssertInterfaceBoundedFailure(
            kit => kit.Interface_ledger_effects_subscription_never_yields_Archived(),
            InterfaceBoundedEffectsContract);

    [Fact]
    public async Task Snapshot_check_passes_when_an_asynchronously_yielding_transport_terminates_in_budget() =>
        await AssertPasses(kit => kit.Active_snapshot_ends_with_a_terminal_Checkpoint());

    [Fact]
    public async Task Empty_snapshot_check_passes_when_an_asynchronously_yielding_transport_terminates_in_budget() =>
        await AssertPasses(kit => kit.Empty_active_snapshot_still_ends_with_a_terminal_Checkpoint());

    [Fact]
    public async Task Interface_snapshot_check_passes_when_an_asynchronously_yielding_transport_terminates_in_budget() =>
        await AssertPasses(kit => kit.Interface_active_snapshot_ends_with_a_terminal_Checkpoint());

    [Fact]
    public async Task Interface_empty_snapshot_check_passes_when_an_asynchronously_yielding_transport_terminates_in_budget() =>
        await AssertPasses(kit => kit.Interface_empty_active_snapshot_still_ends_with_a_terminal_Checkpoint());

    [Fact]
    public async Task Bounded_subscription_check_passes_when_an_asynchronously_yielding_transport_honours_toOffset() =>
        await AssertPasses(kit => kit.Bounded_subscription_delivers_the_event_at_toOffset_then_completes());

    [Fact]
    public async Task Ledger_effects_check_passes_when_an_asynchronously_yielding_transport_honours_toOffset() =>
        await AssertPasses(kit => kit.Ledger_effects_subscription_never_yields_Archived());

    [Fact]
    public async Task Interface_bounded_subscription_check_passes_when_an_asynchronously_yielding_transport_honours_toOffset() =>
        await AssertPasses(kit => kit.Interface_bounded_subscription_delivers_the_event_at_toOffset_then_completes());

    [Fact]
    public async Task Interface_ledger_effects_check_passes_when_an_asynchronously_yielding_transport_honours_toOffset() =>
        await AssertPasses(kit => kit.Interface_ledger_effects_subscription_never_yields_Archived());

    private static async Task AssertBoundedFailure(
        Func<LedgerClientConformanceTests<ConformanceProbe>, Task> check, string expectedContract)
    {
        var kit = new NonTerminatingKit();

        var run = await Record.ExceptionAsync(() => check(kit));

        run.Should().BeOfType<TimeoutException>(
            "a transport that never terminates must fail the kit's check within its stream budget, "
            + "not hang the adopter's run");
        run!.Message.Should().Contain(expectedContract);
    }

    private static async Task AssertInterfaceBoundedFailure(
        Func<LedgerClientConformanceTests<ConformanceProbe>, Task> check, string expectedContract)
    {
        var kit = new NonTerminatingInterfaceKit();

        var run = await Record.ExceptionAsync(() => check(kit));

        run.Should().BeOfType<TimeoutException>(
            "an interface snapshot that never terminates must fail the kit's check within its stream budget, "
            + "not hang the adopter's run, while the template family conforms");
        run!.Message.Should().Contain(expectedContract);
    }

    private static async Task AssertPasses(
        Func<LedgerClientConformanceTests<ConformanceProbe>, Task> check)
    {
        var kit = new AsynchronouslyTerminatingKit();

        var run = await Record.ExceptionAsync(() => check(kit));

        run.Should().BeNull(
            "the stream budget must not fire against a transport whose MoveNextAsync completes "
            + "asynchronously but within the budget, and every item it yielded must be collected");
    }

    private sealed class NonTerminatingKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() =>
            new StreamBudgetFakeClient(templateTerminates: false, interfaceTerminates: true);

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(200);
    }

    private sealed class NonTerminatingInterfaceKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() =>
            new StreamBudgetFakeClient(templateTerminates: true, interfaceTerminates: false);

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(200);
    }

    private sealed class AsynchronouslyTerminatingKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() =>
            new StreamBudgetFakeClient(templateTerminates: true, interfaceTerminates: true);

        protected override SubmitterInfo Reader { get; } = new Party("alice");
    }

    private sealed class StreamBudgetFakeClient : ILedgerClient
    {
        private static readonly LedgerOffset LedgerEnd = LedgerOffset.At(3);

        private static readonly TimeSpan YieldPause = TimeSpan.FromMilliseconds(1);

        private readonly bool _templateTerminates;

        private readonly bool _interfaceTerminates;

        public StreamBudgetFakeClient(bool templateTerminates, bool interfaceTerminates)
        {
            _templateTerminates = templateTerminates;
            _interfaceTerminates = interfaceTerminates;
        }

        public async IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            bool includeDisclosure = false,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T>
        {
            if (!_templateTerminates)
            {
                while (true)
                {
                    await Task.Delay(YieldPause, CancellationToken.None);
                    yield return SnapshotRow<T>();
                }
            }

            var effective = activeAtOffset ?? LedgerEnd;

            if (effective.Value >= 1)
            {
                await Task.Delay(YieldPause, cancellationToken);
                yield return SnapshotRow<T>();
            }

            if (effective.Value >= 3)
            {
                await Task.Delay(YieldPause, cancellationToken);
                yield return new AcsSnapshotEntry<T>.Unclassified(
                    LedgerOffset.At(3), UnclassifiedKind.Unknown, "UNMAPPED");
            }

            await Task.Delay(YieldPause, cancellationToken);
            yield return new AcsSnapshotEntry<T>.Checkpoint(new StakeholderResume(effective));
        }

        public async IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            bool includeDisclosure = false,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView>
        {
            if (!_interfaceTerminates)
            {
                while (true)
                {
                    await Task.Delay(YieldPause, CancellationToken.None);
                    yield return InterfaceSnapshotRow<TInterface, TView>();
                }
            }

            var effective = activeAtOffset ?? LedgerEnd;

            if (effective.Value >= 1)
            {
                await Task.Delay(YieldPause, cancellationToken);
                yield return InterfaceSnapshotRow<TInterface, TView>();
            }

            if (effective.Value >= 3)
            {
                await Task.Delay(YieldPause, cancellationToken);
                yield return new InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified(
                    LedgerOffset.At(3), UnclassifiedKind.Unknown, "UNMAPPED");
            }

            await Task.Delay(YieldPause, cancellationToken);
            yield return new InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint(new StakeholderResume(effective));
        }

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> =>
            Stream(
                _templateTerminates,
                () => new ContractStreamEvent<T>.Checkpoint(LedgerOffset.Begin),
                SeededStream<T>(),
                fromOffset,
                toOffset,
                cancellationToken);

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> =>
            Stream(
                _templateTerminates,
                () => new ContractStreamEvent<T>.Checkpoint(LedgerOffset.Begin),
                SeededEffectsStream<T>(),
                fromOffset,
                toOffset,
                cancellationToken);

        public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> =>
            Stream(
                _interfaceTerminates,
                () => new InterfaceStreamEvent<TInterface, TView>.Checkpoint(LedgerOffset.Begin),
                SeededInterfaceStream<TInterface, TView>(),
                fromOffset,
                toOffset,
                cancellationToken);

        public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> =>
            Stream(
                _interfaceTerminates,
                () => new InterfaceStreamEvent<TInterface, TView>.Checkpoint(LedgerOffset.Begin),
                SeededInterfaceEffectsStream<TInterface, TView>(),
                fromOffset,
                toOffset,
                cancellationToken);

        private static async IAsyncEnumerable<TEvent> Stream<TEvent>(
            bool terminates,
            Func<TEvent> neverEndingItem,
            IEnumerable<(long Offset, TEvent Event)> seeded,
            LedgerOffset? fromOffset,
            LedgerOffset? toOffset,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!terminates)
            {
                while (true)
                {
                    await Task.Delay(YieldPause, CancellationToken.None);
                    yield return neverEndingItem();
                }
            }

            var lower = (fromOffset ?? LedgerOffset.Begin).Value;

            foreach (var (offset, evt) in seeded)
            {
                if (offset <= lower)
                {
                    continue;
                }

                if (toOffset is { } upper && offset > upper.Value)
                {
                    yield break;
                }

                await Task.Delay(YieldPause, cancellationToken);
                yield return evt;
            }
        }

        private static InterfaceAcsSnapshotEntry<TInterface, TView>.Created InterfaceSnapshotRow<TInterface, TView>()
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> =>
            new(new ContractId<TInterface>("c1"), TView.FromRecord(new ConformanceProbeView(42.5m).ToRecord()), null,
                LedgerOffset.At(1), new SynchronizerId("sync"), [new Party("alice")]);

        private static AcsSnapshotEntry<T>.Created SnapshotRow<T>()
            where T : ITemplate, IDamlRecord<T> =>
            new(new ContractId<T>("c1"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(1),
                new SynchronizerId("sync"), [new Party("alice")]);

        private static IEnumerable<(long Offset, ContractStreamEvent<T> Event)> SeededStream<T>()
            where T : ITemplate, IDamlRecord<T>
        {
            yield return (1, new ContractStreamEvent<T>.Created(
                new ContractId<T>("c1"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(1),
                new SynchronizerId("sync"), [new Party("alice")]));
            yield return (2, new ContractStreamEvent<T>.Archived(
                new ContractId<T>("c2"), LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]));
            yield return (3, new ContractStreamEvent<T>.Unclassified(
                LedgerOffset.At(3), UnclassifiedKind.Unknown, "UNMAPPED"));
        }

        private static IEnumerable<(long Offset, ContractStreamEvent<T> Event)> SeededEffectsStream<T>()
            where T : ITemplate, IDamlRecord<T>
        {
            yield return (1, new ContractStreamEvent<T>.Created(
                new ContractId<T>("c1"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(1),
                new SynchronizerId("sync"), [new Party("alice")]));
            yield return (2, new ContractStreamEvent<T>.Exercised(
                new ContractId<T>("c1"), new ChoiceName("Archive"), DamlUnit.Instance, DamlUnit.Instance, true,
                LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]));
        }

        private static IEnumerable<(long Offset, InterfaceStreamEvent<TInterface, TView> Event)> SeededInterfaceStream<TInterface, TView>()
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView>
        {
            yield return (1, new InterfaceStreamEvent<TInterface, TView>.Created(
                new ContractId<TInterface>("c1"), TView.FromRecord(new ConformanceProbeView(42.5m).ToRecord()), null,
                LedgerOffset.At(1), new SynchronizerId("sync"), [new Party("alice")]));
            yield return (2, new InterfaceStreamEvent<TInterface, TView>.Archived(
                new ContractId<TInterface>("c2"), LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]));
            yield return (3, new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                LedgerOffset.At(3), UnclassifiedKind.Unknown, "UNMAPPED"));
        }

        private static IEnumerable<(long Offset, InterfaceStreamEvent<TInterface, TView> Event)> SeededInterfaceEffectsStream<TInterface, TView>()
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView>
        {
            yield return (1, new InterfaceStreamEvent<TInterface, TView>.Created(
                new ContractId<TInterface>("c1"), TView.FromRecord(new ConformanceProbeView(42.5m).ToRecord()), null,
                LedgerOffset.At(1), new SynchronizerId("sync"), [new Party("alice")]));
            yield return (2, new InterfaceStreamEvent<TInterface, TView>.Exercised(
                new ContractId<TInterface>("c1"), new ChoiceName("Archive"), DamlUnit.Instance, DamlUnit.Instance, true,
                LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]));
        }

        public Task<LedgerOffset> GetLedgerEndAsync(
            TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(LedgerEnd);

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

        public void Dispose()
        {
        }
    }
}
