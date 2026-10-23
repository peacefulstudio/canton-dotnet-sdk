// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

/// <summary>
/// Stream decorators that each break one thing a stream check must catch, shared by the template
/// and interface mutation clients so both families break the same way.
/// </summary>
internal static class BrokenStreams
{
    public static async IAsyncEnumerable<TEvent> Empty<TEvent>()
    {
        await Task.CompletedTask;
        yield break;
    }

    public static async IAsyncEnumerable<TEvent> FirstEventOnly<TEvent>(
        IAsyncEnumerable<TEvent> read,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var streamEvent in read.WithCancellation(cancellationToken))
        {
            yield return streamEvent;
            yield break;
        }
    }

    public static async IAsyncEnumerable<TEvent> Followed<TEvent>(
        IAsyncEnumerable<TEvent> read,
        TEvent trailing,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var streamEvent in read.WithCancellation(cancellationToken))
        {
            yield return streamEvent;
        }

        yield return trailing;
    }

    public static async IAsyncEnumerable<TEvent> ThenBlockedAfterCheckingTheTokenOnce<TEvent>(
        IAsyncEnumerable<TEvent> read,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await foreach (var streamEvent in read)
        {
            yield return streamEvent;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
    }

    public static async IAsyncEnumerable<TEvent> ThenHeldOpenUntilCancelled<TEvent>(
        IAsyncEnumerable<TEvent> read,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var streamEvent in read.WithCancellation(cancellationToken))
        {
            yield return streamEvent;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
