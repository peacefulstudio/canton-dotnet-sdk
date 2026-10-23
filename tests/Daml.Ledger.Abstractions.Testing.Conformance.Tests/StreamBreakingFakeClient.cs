// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

internal enum BrokenFamily
{
    Template,
    Interface,
}

internal enum StreamBreakage
{
    None,
    EmptyWhenResumed,
    OnlyTheFirstEvent,
    TransportErrorAfterTheAcsDeltaWindow,
    TransportErrorAfterTheLedgerEffectsWindow,
    BlockedAfterCheckingTheTokenOnce,
    HeldOpenUntilCancelled,
}

internal enum StreamShape
{
    AcsDelta,
    LedgerEffects,
}

/// <summary>
/// Wraps the shipped <c>FakeLedgerClient</c>, seeded by <see cref="SeededShippedFake"/>, and
/// breaks one thing on the stream reads of one family, leaving the other family conforming, so
/// the only checks it can fail are that family's checks on that thing.
/// </summary>
internal sealed class StreamBreakingFakeClient(BrokenFamily family, StreamBreakage breakage)
    : DelegatingLedgerClient(SeededShippedFake.Create())
{
    private static readonly TransportStatus Aborted = new TransportStatus.Grpc(GrpcStatusCode.Unavailable);

    private const string AbortedMessage = "bounded read aborted";

    public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        Break(
            BrokenFamily.Template,
            StreamShape.AcsDelta,
            fromOffset,
            toOffset,
            new ContractStreamEvent<T>.StreamError(Aborted, AbortedMessage),
            token => Inner.SubscribeAsync<T>(submitter, fromOffset, toOffset, token),
            cancellationToken);

    public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        Break(
            BrokenFamily.Template,
            StreamShape.LedgerEffects,
            fromOffset,
            toOffset,
            new ContractStreamEvent<T>.StreamError(Aborted, AbortedMessage),
            token => Inner.SubscribeLedgerEffectsAsync<T>(submitter, fromOffset, toOffset, token),
            cancellationToken);

    public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        Break(
            BrokenFamily.Interface,
            StreamShape.AcsDelta,
            fromOffset,
            toOffset,
            new InterfaceStreamEvent<TInterface, TView>.StreamError(Aborted, AbortedMessage),
            token => Inner.SubscribeAsync(view, submitter, fromOffset, toOffset, token),
            cancellationToken);

    public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
        ViewDescriptor<TInterface, TView> view,
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default) =>
        Break(
            BrokenFamily.Interface,
            StreamShape.LedgerEffects,
            fromOffset,
            toOffset,
            new InterfaceStreamEvent<TInterface, TView>.StreamError(Aborted, AbortedMessage),
            token => Inner.SubscribeLedgerEffectsAsync(view, submitter, fromOffset, toOffset, token),
            cancellationToken);

    private IAsyncEnumerable<TEvent> Break<TEvent>(
        BrokenFamily readFamily,
        StreamShape shape,
        LedgerOffset? fromOffset,
        LedgerOffset? toOffset,
        TEvent transportError,
        Func<CancellationToken, IAsyncEnumerable<TEvent>> read,
        CancellationToken cancellationToken)
    {
        if (readFamily != family)
        {
            return read(cancellationToken);
        }

        var isLive = toOffset is null;
        var isResumed = fromOffset is { Value: > 0 };

        return (breakage, shape) switch
        {
            (StreamBreakage.EmptyWhenResumed, StreamShape.AcsDelta) when isResumed =>
                BrokenStreams.Empty<TEvent>(),
            (StreamBreakage.OnlyTheFirstEvent, StreamShape.AcsDelta) =>
                BrokenStreams.FirstEventOnly(read(cancellationToken), cancellationToken),
            (StreamBreakage.TransportErrorAfterTheAcsDeltaWindow, StreamShape.AcsDelta) =>
                BrokenStreams.Followed(read(cancellationToken), transportError, cancellationToken),
            (StreamBreakage.TransportErrorAfterTheLedgerEffectsWindow, StreamShape.LedgerEffects) =>
                BrokenStreams.Followed(read(cancellationToken), transportError, cancellationToken),
            (StreamBreakage.BlockedAfterCheckingTheTokenOnce, StreamShape.AcsDelta) when isLive =>
                BrokenStreams.ThenBlockedAfterCheckingTheTokenOnce(read(CancellationToken.None), cancellationToken),
            (StreamBreakage.HeldOpenUntilCancelled, StreamShape.AcsDelta) when isLive =>
                BrokenStreams.ThenHeldOpenUntilCancelled(read(cancellationToken), cancellationToken),
            _ => read(cancellationToken),
        };
    }
}
