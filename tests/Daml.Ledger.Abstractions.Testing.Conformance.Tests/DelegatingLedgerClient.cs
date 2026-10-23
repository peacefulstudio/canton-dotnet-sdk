// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
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
/// Forwards every <see cref="ILedgerClient"/> member to <paramref name="inner"/> and lets a
/// subclass override just the member it breaks, so a mutation kit stays conforming everywhere
/// else.
/// </summary>
internal abstract class DelegatingLedgerClient(ILedgerClient inner) : ILedgerClient
{
    protected ILedgerClient Inner { get; } = inner;

    public virtual IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        Inner.SubscribeActiveAsync<T>(submitter, activeAtOffset, includeDisclosure, cancellationToken);

    public virtual IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        Inner.SubscribeAsync<T>(submitter, fromOffset, toOffset, cancellationToken);

    public virtual IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        Inner.SubscribeLedgerEffectsAsync<T>(submitter, fromOffset, toOffset, cancellationToken);

    public virtual IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        Inner.SubscribeActiveAsync(view, submitter, activeAtOffset, includeDisclosure, cancellationToken);

    public virtual IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        Inner.SubscribeAsync(view, submitter, fromOffset, toOffset, cancellationToken);

    public virtual IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        Inner.SubscribeLedgerEffectsAsync(view, submitter, fromOffset, toOffset, cancellationToken);

    public virtual Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Inner.GetLedgerEndAsync(timeout, cancellationToken);

    public virtual Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        Inner.TryExerciseAsync<TResult>(
            command, submitter, workflowId, commandId, timeout, cancellationToken);

    public virtual Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Inner.SubmitAndWaitAsync(submission, submitter, timeout, cancellationToken);

    public virtual Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Inner.TrySubmitAndWaitForTransactionAsync(submission, submitter, timeout, cancellationToken);

    public virtual Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where TTemplate : ITemplate =>
        Inner.TryCreateAsync(payload, submitter, workflowId, commandId, timeout, cancellationToken);

    public void Dispose() => Inner.Dispose();
}
