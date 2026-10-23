// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing.Tests;

/// <summary>
/// Plays a participant whose open-ended subscription (no <c>toOffset</c>) tails the ledger: it
/// replays what <see cref="FakeLedgerClient"/> staged, then stays open until the caller cancels.
/// A bounded read, a snapshot and the ledger end pass through to the fake unchanged. Every other
/// member throws via <see cref="NotSupportedLedgerClient"/>.
/// </summary>
internal sealed class OpenEndedStreamsLedgerClient(FakeLedgerClient fake) : NotSupportedLedgerClient
{
    public override IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter, LedgerOffset? activeAtOffset = null, bool includeDisclosure = false,
        CancellationToken cancellationToken = default) =>
        fake.SubscribeActiveAsync<T>(submitter, activeAtOffset, includeDisclosure, cancellationToken);

    public override IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view, SubmitterInfo submitter, LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false, CancellationToken cancellationToken = default) =>
        fake.SubscribeActiveAsync(view, submitter, activeAtOffset, includeDisclosure, cancellationToken);

    public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter, LedgerOffset? fromOffset = null, LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        TailWhenOpenEnded(fake.SubscribeAsync<T>(submitter, fromOffset, toOffset, cancellationToken), toOffset, cancellationToken);

    public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter, LedgerOffset? fromOffset = null, LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        TailWhenOpenEnded(
            fake.SubscribeLedgerEffectsAsync<T>(submitter, fromOffset, toOffset, cancellationToken), toOffset, cancellationToken);

    public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view, SubmitterInfo submitter, LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null, CancellationToken cancellationToken = default) =>
        TailWhenOpenEnded(
            fake.SubscribeAsync(view, submitter, fromOffset, toOffset, cancellationToken), toOffset, cancellationToken);

    public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view, SubmitterInfo submitter, LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null, CancellationToken cancellationToken = default) =>
        TailWhenOpenEnded(
            fake.SubscribeLedgerEffectsAsync(view, submitter, fromOffset, toOffset, cancellationToken), toOffset, cancellationToken);

    public override Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        fake.GetLedgerEndAsync(timeout, cancellationToken);

    private static IAsyncEnumerable<TEvent> TailWhenOpenEnded<TEvent>(
        IAsyncEnumerable<TEvent> staged, LedgerOffset? toOffset, CancellationToken cancellationToken) =>
        toOffset is null ? TailUntilCancelled(staged, cancellationToken) : staged;

    private static async IAsyncEnumerable<TEvent> TailUntilCancelled<TEvent>(
        IAsyncEnumerable<TEvent> staged, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var streamEvent in staged.WithCancellation(cancellationToken))
        {
            yield return streamEvent;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
