// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

/// <summary>
/// Wraps <see cref="ConformingFakeClient"/> with a projector that silently drops every
/// archival signal: no <see cref="ContractStreamEvent{T}.Archived"/> reaches the ACS-delta
/// subscription and no consuming <see cref="ContractStreamEvent{T}.Exercised"/> reaches the
/// ledger-effects subscription. Every other member delegates untouched, so the only checks it
/// can fail are the two stream-shape checks — used to prove those checks assert the archival
/// signal is present rather than only that the wrong variant is absent.
/// </summary>
internal sealed class ArchivalDroppingFakeClient : ILedgerClient
{
    private readonly ConformingFakeClient _seeded = new();

    public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        _seeded.SubscribeActiveAsync<T>(submitter, activeAtOffset, includeDisclosure, cancellationToken);

    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        WithoutArchivals(
            _seeded.SubscribeAsync<T>(submitter, fromOffset, toOffset, cancellationToken),
            cancellationToken);

    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        WithoutArchivals(
            _seeded.SubscribeLedgerEffectsAsync<T>(submitter, fromOffset, toOffset, cancellationToken),
            cancellationToken);

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

    public Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _seeded.GetLedgerEndAsync(timeout, cancellationToken);

    public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _seeded.TryExerciseAsync<TResult>(
            command, submitter, workflowId, commandId, timeout, cancellationToken);

    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _seeded.SubmitAndWaitAsync(submission, submitter, timeout, cancellationToken);

    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _seeded.TrySubmitAndWaitForTransactionAsync(submission, submitter, timeout, cancellationToken);

    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where TTemplate : ITemplate =>
        _seeded.TryCreateAsync(payload, submitter, workflowId, commandId, timeout, cancellationToken);

    public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        _seeded.SubscribeAsync(view, submitter, fromOffset, toOffset, cancellationToken);

    public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        _seeded.SubscribeLedgerEffectsAsync(view, submitter, fromOffset, toOffset, cancellationToken);

    public IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        _seeded.SubscribeActiveAsync(view, submitter, activeAtOffset, includeDisclosure, cancellationToken);

    public void Dispose() => _seeded.Dispose();
}
