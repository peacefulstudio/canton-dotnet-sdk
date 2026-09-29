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

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// An <see cref="ILedgerClient"/> whose full 12-member surface throws
/// <see cref="NotSupportedException"/> by default. A fake that exists to prove one
/// behavior derives from this and overrides only the members it exercises, rather than
/// writing a stub for every member it does not care about.
/// </summary>
/// <remarks>
/// This is the base the conformance kit's own command-id, submitter-authority and
/// cancellation fakes derive from — published here, rather than kept
/// test-project-private, so a third-party transport author writing conformance fakes of
/// their own pays the same twelve-member tax exactly once.
/// </remarks>
public abstract class NotSupportedLedgerClient : ILedgerClient
{
    /// <inheritdoc />
    public virtual Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where TTemplate : ITemplate =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter, LedgerOffset? fromOffset = null, LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter, LedgerOffset? fromOffset = null, LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter, LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public virtual IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        throw new NotSupportedException();

    /// <summary>No-op: none of the fakes deriving from this base own disposable resources.</summary>
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
