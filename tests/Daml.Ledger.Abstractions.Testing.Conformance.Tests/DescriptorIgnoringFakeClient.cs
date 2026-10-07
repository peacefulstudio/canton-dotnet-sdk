// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

internal enum InterfaceRead
{
    Snapshot,
    AcsDelta,
    LedgerEffects,
}

/// <summary>
/// Derives from <see cref="ConformingFakeClient"/> and breaks the missing-descriptor contract of
/// one interface read: the read either accepts a <c>null</c> descriptor without complaint, or
/// rejects it only once the stream is enumerated. The other two interface reads and the whole
/// template family keep conforming, so the only check it can fail is the descriptor check of the
/// read it breaks.
/// </summary>
internal sealed class DescriptorIgnoringFakeClient(InterfaceRead brokenRead, bool rejectsOnlyWhenEnumerated)
    : ConformingFakeClient
{
    public override IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default) =>
        brokenRead == InterfaceRead.Snapshot
            ? Unguarded(
                view,
                descriptor => base.SubscribeActiveAsync(
                    descriptor, submitter, activeAtOffset, includeDisclosure, cancellationToken))
            : base.SubscribeActiveAsync(view, submitter, activeAtOffset, includeDisclosure, cancellationToken);

    public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        brokenRead == InterfaceRead.AcsDelta
            ? Unguarded(
                view,
                descriptor => base.SubscribeAsync(descriptor, submitter, fromOffset, toOffset, cancellationToken))
            : base.SubscribeAsync(view, submitter, fromOffset, toOffset, cancellationToken);

    public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        brokenRead == InterfaceRead.LedgerEffects
            ? Unguarded(
                view,
                descriptor => base.SubscribeLedgerEffectsAsync(
                    descriptor, submitter, fromOffset, toOffset, cancellationToken))
            : base.SubscribeLedgerEffectsAsync(view, submitter, fromOffset, toOffset, cancellationToken);

    private IAsyncEnumerable<TItem> Unguarded<TInterface, TView, TItem>(
        ViewDescriptor<TInterface, TView>? view,
        Func<ViewDescriptor<TInterface, TView>, IAsyncEnumerable<TItem>> read)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        rejectsOnlyWhenEnumerated
            ? RejectWhenEnumerated(view, read)
            : read(view ?? new ViewDescriptor<TInterface, TView>());

    private static async IAsyncEnumerable<TItem> RejectWhenEnumerated<TInterface, TView, TItem>(
        ViewDescriptor<TInterface, TView>? view,
        Func<ViewDescriptor<TInterface, TView>, IAsyncEnumerable<TItem>> read,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(view);

        await foreach (var item in read(view).WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
