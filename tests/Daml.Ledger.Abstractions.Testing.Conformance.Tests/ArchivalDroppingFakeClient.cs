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

/// <summary>
/// Wraps the shipped <c>FakeLedgerClient</c>, seeded by <see cref="SeededShippedFake"/>, with a
/// projector that silently drops every archival signal: no <see cref="ContractStreamEvent{T}.Archived"/> reaches the ACS-delta
/// subscription and no consuming <see cref="ContractStreamEvent{T}.Exercised"/> reaches the
/// ledger-effects subscription of the family it is built to break, and leaves the other family
/// conforming. Every other member delegates untouched, so the only checks it can fail are that
/// family's two stream-shape checks — used to prove those checks assert the archival signal is
/// present rather than only that the wrong variant is absent.
/// </summary>
internal sealed class ArchivalDroppingFakeClient : DelegatingLedgerClient
{
    private readonly bool _dropsTemplateArchivals;

    private readonly bool _dropsInterfaceArchivals;

    public ArchivalDroppingFakeClient(bool dropsTemplateArchivals = true, bool dropsInterfaceArchivals = false)
        : base(SeededShippedFake.Create())
    {
        _dropsTemplateArchivals = dropsTemplateArchivals;
        _dropsInterfaceArchivals = dropsInterfaceArchivals;
    }

    public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        _dropsTemplateArchivals
            ? WithoutArchivals(
                Inner.SubscribeAsync<T>(submitter, fromOffset, toOffset, cancellationToken),
                cancellationToken)
            : Inner.SubscribeAsync<T>(submitter, fromOffset, toOffset, cancellationToken);

    public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        _dropsTemplateArchivals
            ? WithoutArchivals(
                Inner.SubscribeLedgerEffectsAsync<T>(submitter, fromOffset, toOffset, cancellationToken),
                cancellationToken)
            : Inner.SubscribeLedgerEffectsAsync<T>(submitter, fromOffset, toOffset, cancellationToken);

    private static async IAsyncEnumerable<ContractStreamEvent<T>> WithoutArchivals<T>(
        IAsyncEnumerable<ContractStreamEvent<T>> projected,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T>
    {
        await foreach (var evt in projected.WithCancellation(cancellationToken))
        {
            if (evt is ContractStreamEvent<T>.Archived
                or ContractStreamEvent<T>.Exercised { Consuming: true })
            {
                continue;
            }

            yield return evt;
        }
    }

    private static async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> WithoutArchivals<TInterface, TView>(
        IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> projected,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        await foreach (var evt in projected.WithCancellation(cancellationToken))
        {
            if (evt is InterfaceStreamEvent<TInterface, TView>.Archived
                or InterfaceStreamEvent<TInterface, TView>.Exercised { Consuming: true })
            {
                continue;
            }

            yield return evt;
        }
    }

    public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        _dropsInterfaceArchivals
            ? WithoutArchivals(
                Inner.SubscribeAsync(view, submitter, fromOffset, toOffset, cancellationToken),
                cancellationToken)
            : Inner.SubscribeAsync(view, submitter, fromOffset, toOffset, cancellationToken);

    public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        _dropsInterfaceArchivals
            ? WithoutArchivals(
                Inner.SubscribeLedgerEffectsAsync(view, submitter, fromOffset, toOffset, cancellationToken),
                cancellationToken)
            : Inner.SubscribeLedgerEffectsAsync(view, submitter, fromOffset, toOffset, cancellationToken);
}
