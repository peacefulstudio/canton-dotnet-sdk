// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

internal enum ViewMisrendering
{
    WrongAmount,
    ForeignContractId,
    ForeignSynchronizer,
    DroppedView,
}

/// <summary>
/// Derives from <see cref="ConformingFakeClient"/> and misrenders the <c>Created</c> rows of one
/// interface read: a view amount of 42.6, a contract id the template family never serves, a synchronizer id the template family never
/// serves (snapshot only), or no view at all, which the read surfaces as <c>Unclassified</c>. The other two interface reads and
/// the whole template family keep conforming, so the only check it can fail is the view check of
/// the read it breaks.
/// </summary>
internal sealed class ViewMisrenderingFakeClient(InterfaceRead brokenRead, ViewMisrendering misrendering)
    : ConformingFakeClient
{
    public override async IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conforming = base.SubscribeActiveAsync(view, submitter, activeAtOffset, includeDisclosure, cancellationToken);

        await foreach (var entry in conforming.WithCancellation(cancellationToken))
        {
            yield return brokenRead == InterfaceRead.Snapshot && entry is InterfaceAcsSnapshotEntry<TInterface, TView>.Created created
                ? Misrender(created)
                : entry;
        }
    }

    public override async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conforming = base.SubscribeAsync(view, submitter, fromOffset, toOffset, cancellationToken);

        await foreach (var streamEvent in conforming.WithCancellation(cancellationToken))
        {
            yield return brokenRead == InterfaceRead.AcsDelta && streamEvent is InterfaceStreamEvent<TInterface, TView>.Created created
                ? Misrender(created)
                : streamEvent;
        }
    }

    public override async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conforming = base.SubscribeLedgerEffectsAsync(view, submitter, fromOffset, toOffset, cancellationToken);

        await foreach (var streamEvent in conforming.WithCancellation(cancellationToken))
        {
            yield return brokenRead == InterfaceRead.LedgerEffects && streamEvent is InterfaceStreamEvent<TInterface, TView>.Created created
                ? Misrender(created)
                : streamEvent;
        }
    }

    private InterfaceAcsSnapshotEntry<TInterface, TView> Misrender<TInterface, TView>(
        InterfaceAcsSnapshotEntry<TInterface, TView>.Created created)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        misrendering switch
        {
            ViewMisrendering.WrongAmount => created with { Payload = TView.FromRecord(new ConformanceProbeView(42.6m).ToRecord()) },
            ViewMisrendering.ForeignContractId => created with { ContractId = new ContractId<TInterface>("c9") },
            ViewMisrendering.ForeignSynchronizer => created with { SynchronizerId = new SynchronizerId("foreign-sync") },
            _ => new InterfaceAcsSnapshotEntry<TInterface, TView>.Unclassified(
                created.Offset, UnclassifiedKind.InterfaceViewUnavailable),
        };

    private InterfaceStreamEvent<TInterface, TView> Misrender<TInterface, TView>(
        InterfaceStreamEvent<TInterface, TView>.Created created)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        misrendering switch
        {
            ViewMisrendering.WrongAmount => created with { Payload = TView.FromRecord(new ConformanceProbeView(42.6m).ToRecord()) },
            ViewMisrendering.ForeignContractId => created with { ContractId = new ContractId<TInterface>("c9") },
            _ => new InterfaceStreamEvent<TInterface, TView>.Unclassified(
                created.Offset, UnclassifiedKind.InterfaceViewUnavailable),
        };
}
