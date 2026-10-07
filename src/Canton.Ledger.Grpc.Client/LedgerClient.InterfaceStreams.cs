// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Telemetry;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal sealed partial class LedgerClient
{
    private const bool SubscribesAsAnInterface = true;

    /// <inheritdoc />
    /// <remarks>
    /// The interface-family counterpart of <see cref="SubscribeAsync{T}"/>: the request carries an
    /// <c>InterfaceFilter</c> for <typeparamref name="TInterface"/> with
    /// <c>include_interface_view</c> set, and each matching created event is projected onto the
    /// participant-computed view decoded into <typeparamref name="TView"/> — never the implementing
    /// template's create argument. An event delivered without a usable view arrives as
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Unclassified"/> with
    /// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/>. Fault contract is unchanged: a
    /// mid-stream transport fault is surfaced in-band as a terminal
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.StreamError"/>, never thrown, while a
    /// caller cancelling via <paramref name="cancellationToken"/> still gets an
    /// <see cref="OperationCanceledException"/>.
    /// </remarks>
    public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(view);

        return SubscribeInterfaceAsyncCore<TInterface, TView>(
            submitter,
            GrpcMarkerMatcher<TInterface>.StreamFilterIdentifier(),
            fromOffset?.Value,
            toOffset?.Value,
            TransactionShape.AcsDelta,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The interface-family counterpart of <see cref="SubscribeLedgerEffectsAsync{T}"/>, carrying
    /// the same <c>InterfaceFilter</c> and the same view-decoding contract as
    /// <see cref="SubscribeAsync{TInterface, TView}(ViewDescriptor{TInterface, TView}, RuntimeCommands.SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>.
    /// </remarks>
    public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(view);

        return SubscribeInterfaceAsyncCore<TInterface, TView>(
            submitter,
            GrpcMarkerMatcher<TInterface>.StreamFilterIdentifier(),
            fromOffset?.Value,
            toOffset?.Value,
            TransactionShape.LedgerEffects,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The interface-family counterpart of <see cref="SubscribeActiveAsync{T}"/>, with the same
    /// terminal contract: a successful snapshot ends with a single
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Checkpoint"/> — emitted even when the
    /// snapshot is empty — and a mid-snapshot transport fault ends it with a terminal
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.StreamError"/> instead. Each row
    /// carries the participant-computed view decoded into <typeparamref name="TView"/>; a row
    /// delivered without a usable view arrives as
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Unclassified"/> with
    /// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/>.
    /// </remarks>
    public IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(view);

        return SubscribeActiveInterfaceAsyncCore<TInterface, TView>(
            submitter,
            GrpcMarkerMatcher<TInterface>.StreamFilterIdentifier(),
            activeAtOffset?.Value,
            includeDisclosure,
            cancellationToken);
    }

    private async IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeInterfaceAsyncCore<TInterface, TView>(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier interfaceFilterId,
        long? fromOffset,
        long? toOffset,
        TransactionShape transactionShape,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(LedgerCallInvoker.Source);
        _invoker.TagServerCall(activity, UpdateService.Descriptor, "GetUpdates");
        activity?.SetTag(LedgerActivityTagNames.DamlTemplateId, typeof(TInterface).Name);
        activity?.SetTag(LedgerActivityTagNames.CantonFromOffset, fromOffset);
        activity.SetSubmitterTags(submitter, _options);

        var request = GrpcSubscribeRequestBuilder.BuildGetUpdatesRequest(
            submitter,
            interfaceFilterId,
            fromOffset,
            toOffset,
            SubscribesAsAnInterface,
            transactionShape);

        LogSubscribeStarted(_logger, typeof(TInterface).Name, fromOffset ?? 0L);

        using var call = _updateService.GetUpdates(
            request,
            headers: await _invoker.GetHeadersAsync(cancellationToken).ConfigureAwait(false),
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await StreamMoveResult.NextAsync(stream, cancellationToken).ConfigureAwait(false);
            if (step.RecordFault(activity) is { } fault)
            {
                LogSubscribeStreamError(_logger, typeof(TInterface).Name, fault.Status, fault.Message);
                yield return new InterfaceStreamEvent<TInterface, TView>.StreamError(
                    fault.Status, fault.Message, fault.Category, fault.ErrorId, fault.SourceException);
                yield break;
            }

            if (!step.Moved) yield break;

            foreach (var projected in GrpcInterfaceStreamProjector.ProjectUpdate<TInterface, TView>(stream.Current, _logger))
            {
                yield return projected;
            }
        }
    }

    private async IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveInterfaceAsyncCore<TInterface, TView>(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier interfaceFilterId,
        long? activeAtOffset,
        bool includeDisclosure,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(LedgerCallInvoker.Source);
        _invoker.TagServerCall(activity, StateService.Descriptor, "GetActiveContracts");
        activity?.SetTag(LedgerActivityTagNames.DamlTemplateId, typeof(TInterface).Name);
        activity.SetSubmitterTags(submitter, _options);

        var effectiveOffset = activeAtOffset
            ?? (await GetLedgerEndForSnapshotAsync(cancellationToken).ConfigureAwait(false)).Offset;
        var sharedHeaders = await _invoker.GetHeadersAsync(cancellationToken).ConfigureAwait(false);

        var request = GrpcSubscribeRequestBuilder.BuildGetActiveContractsRequest(
            submitter, interfaceFilterId, effectiveOffset, SubscribesAsAnInterface, includeDisclosure);

        LogSubscribeActiveStarted(_logger, typeof(TInterface).Name, effectiveOffset);

        using var call = _stateService.GetActiveContracts(
            request,
            headers: sharedHeaders,
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await StreamMoveResult.NextAsync(stream, cancellationToken).ConfigureAwait(false);
            if (step.RecordFault(activity) is { } fault)
            {
                LogSubscribeStreamError(_logger, typeof(TInterface).Name, fault.Status, fault.Message);
                yield return new InterfaceAcsSnapshotEntry<TInterface, TView>.StreamError(
                    fault.Status, fault.Message, fault.Category, fault.ErrorId, fault.SourceException);
                yield break;
            }

            if (!step.Moved)
            {
                yield return new InterfaceAcsSnapshotEntry<TInterface, TView>.Checkpoint(
                    new StakeholderResume(LedgerOffset.At(effectiveOffset)));
                yield break;
            }

            var disclosure = GrpcContractStreamProjector.DisclosureOf(stream.Current);
            foreach (var projected in GrpcInterfaceStreamProjector.ProjectActiveContractEntry<TInterface, TView>(
                stream.Current, _logger, LedgerOffset.At(effectiveOffset)))
            {
                if (projected is InterfaceStreamEvent<TInterface, TView>.Unclassified unclassified)
                {
                    LogInterfaceEntryUnclassified(
                        _logger,
                        typeof(TInterface).Name,
                        stream.Current.ContractEntryCase,
                        unclassified.Kind,
                        unclassified.Offset?.Value);
                }
                yield return InterfaceSnapshotEntryArms<TInterface, TView>.From(projected, disclosure);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Active contracts snapshot for interface {InterfaceType} could not classify entry {ContractEntryCase} — surfaced as Unclassified ({Kind}) carrying offset {Offset}")]
    private static partial void LogInterfaceEntryUnclassified(ILogger logger, string interfaceType, GetActiveContractsResponse.ContractEntryOneofCase contractEntryCase, UnclassifiedKind kind, long? offset);
}
