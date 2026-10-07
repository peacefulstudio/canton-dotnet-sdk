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
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// Throwing convenience over <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> for the
/// common case: a caller who wants the active contracts and nothing else.
/// </summary>
public static class StreamerSnapshot
{
    /// <summary>
    /// Drains an active-contract-set snapshot into a materialized list of typed
    /// contracts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A snapshot that faults, carries a row the projector could not classify, or ends
    /// without its terminal checkpoint throws <see cref="LedgerOperationException"/>
    /// rather than shortening the returned list: a terminal
    /// <see cref="AcsSnapshotEntry{T}.StreamError"/> means the snapshot is incomplete, an
    /// <see cref="AcsSnapshotEntry{T}.Unclassified"/> row means a row was not projected,
    /// and a stream that ends without the documented terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/> means the transport truncated it.
    /// <see cref="AcsSnapshotEntry{T}.Created"/> rows are collected, and the checkpoint
    /// terminates the drain.
    /// A short list that looks complete is the failure mode this convenience exists to
    /// prevent; use <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> directly for
    /// value-shaped handling of those cases.
    /// </para>
    /// <para>
    /// A create row whose payload did not decode into <typeparamref name="T"/> never reaches
    /// here as a create row: the projector surfaces it as
    /// <see cref="AcsSnapshotEntry{T}.Unclassified"/> with
    /// <see cref="UnclassifiedKind.DecodeFailure"/>, which this method reports as
    /// <see cref="LedgerOperationException"/> along with every other unclassified row.
    /// </para>
    /// <para>
    /// Cancelling <paramref name="cancellationToken"/> surfaces as
    /// <see cref="OperationCanceledException"/> even when the transport reports the cancelled
    /// call in-band as a <see cref="AcsSnapshotEntry{T}.StreamError"/> or by ending the stream
    /// short, matching the write-path conveniences in this namespace.
    /// </para>
    /// <para>
    /// The terminal checkpoint's <see cref="StakeholderResume"/> ticket is consumed and
    /// discarded, so this method cannot feed the gapless snapshot-to-stream handover.
    /// Stay on <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> when the resume
    /// offset matters.
    /// </para>
    /// <para>
    /// Each row's own offset, synchronizer id and witness parties are discarded too, by design:
    /// this method hands back contracts, not observations of them. Use
    /// <see cref="SnapshotActiveAsync{T}(ILedgerStreamer, SubmitterInfo, LedgerOffset?, bool, CancellationToken)"/>
    /// to keep the last-update offset and the synchronizer id.
    /// </para>
    /// <para>
    /// The returned contracts carry no contract key. A keyed template's snapshot goes through
    /// the <see cref="SnapshotAsync{T, TKey}(ILedgerStreamer, KeyDescriptor{T, TKey}, SubmitterInfo, LedgerOffset?, CancellationToken)"/>
    /// overload, which yields <see cref="Contract{T, TKey}"/> with the key decoded.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The Daml template the snapshot is filtered to.</typeparam>
    /// <param name="streamer">The streaming capability.</param>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="streamer"/> is <c>null</c>.</exception>
    /// <exception cref="LedgerOperationException">
    /// The snapshot faulted, carried an unclassified row, or ended without its terminal
    /// checkpoint.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    public static async Task<IReadOnlyList<Contract<T>>> SnapshotAsync<T>(
        this ILedgerStreamer streamer,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(streamer);

        var rows = await TemplateSnapshotArmReader<T>.DrainAsync(streamer, submitter, activeAtOffset, includeDisclosure: false, cancellationToken)
            .ConfigureAwait(false);

        var contracts = new List<Contract<T>>(rows.Count);
        foreach (var row in rows)
        {
            contracts.Add(row.ToContract());
        }

        return contracts;
    }

    /// <summary>
    /// Drains an active-contract-set snapshot into a materialized list of typed contracts, each
    /// paired with the provenance its snapshot row carried.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provenance-keeping twin of
    /// <see cref="SnapshotAsync{T}(ILedgerStreamer, SubmitterInfo, LedgerOffset?, CancellationToken)"/>,
    /// with identical fault, unclassified-row, truncation and cancellation behaviour. A distinct
    /// name rather than an overload: C# does not overload on return type.
    /// </para>
    /// <para>
    /// <see cref="ActiveContract{TContract}.LastUpdateOffset"/> is a per-contract fact, not a
    /// resume point. The terminal checkpoint's <see cref="StakeholderResume"/> ticket is
    /// consumed and discarded here too, so this method cannot feed the gapless
    /// snapshot-to-stream handover; stay on
    /// <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> when the resume offset matters.
    /// The rows' witness parties are not carried either.
    /// </para>
    /// <para>
    /// The returned contracts carry no contract key. A keyed template's snapshot goes through
    /// <see cref="SnapshotActiveAsync{T, TKey}(ILedgerStreamer, KeyDescriptor{T, TKey}, SubmitterInfo, LedgerOffset?, bool, CancellationToken)"/>,
    /// which yields <see cref="Contract{T, TKey}"/> with the key decoded.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The Daml template the snapshot is filtered to.</typeparam>
    /// <param name="streamer">The streaming capability.</param>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="includeDisclosure">
    /// <c>true</c> asks the participant for each contract's <c>created_event_blob</c>, so every
    /// returned <see cref="ActiveContract{TContract}"/> carries a
    /// <see cref="ActiveContract{TContract}.Disclosure"/> another party can attach to its
    /// submission. <c>false</c>, the default, leaves it <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="streamer"/> is <c>null</c>.</exception>
    /// <exception cref="LedgerOperationException">
    /// The snapshot faulted, carried an unclassified row, or ended without its terminal
    /// checkpoint.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    public static async Task<IReadOnlyList<ActiveContract<Contract<T>>>> SnapshotActiveAsync<T>(
        this ILedgerStreamer streamer,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(streamer);

        var rows = await TemplateSnapshotArmReader<T>.DrainAsync(streamer, submitter, activeAtOffset, includeDisclosure, cancellationToken)
            .ConfigureAwait(false);

        var contracts = new List<ActiveContract<Contract<T>>>(rows.Count);
        foreach (var row in rows)
        {
            contracts.Add(row.ToActiveContract());
        }

        return contracts;
    }

    /// <summary>
    /// Drains an active-contract-set snapshot of a keyed template into a materialized list of
    /// typed contracts, each carrying its contract key decoded into
    /// <typeparamref name="TKey"/>.
    /// </summary>
    /// <remarks>
    /// Behaves exactly as
    /// <see cref="SnapshotAsync{T}(ILedgerStreamer, SubmitterInfo, LedgerOffset?, CancellationToken)"/>
    /// for every fault, unclassified row and truncation case, and additionally throws
    /// <see cref="LedgerOperationException"/> when a create row of a keyed template arrives
    /// without the key the keyed shape requires. It discards the same per-row provenance;
    /// <see cref="SnapshotActiveAsync{T, TKey}(ILedgerStreamer, KeyDescriptor{T, TKey}, SubmitterInfo, LedgerOffset?, bool, CancellationToken)"/>
    /// keeps it.
    /// </remarks>
    /// <typeparam name="T">The keyed Daml template the snapshot is filtered to.</typeparam>
    /// <typeparam name="TKey">The template's contract key type.</typeparam>
    /// <param name="streamer">The streaming capability.</param>
    /// <param name="key">The template's key witness. It is the type argument carrier — passing
    /// <c>Account.Key</c> infers both <typeparamref name="T"/> and <typeparamref name="TKey"/>
    /// from one argument, which C# cannot do from a partial type-argument list. The decode is
    /// taken from the template's own <see cref="IHasKey{TSelf, TKey}.Key"/> witness, so this
    /// overload and <see cref="Contract{T, TKey}.FromCreatedEvent"/> decode identically.</param>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="streamer"/> or
    /// <paramref name="key"/> is <c>null</c>.</exception>
    /// <exception cref="LedgerOperationException">
    /// The snapshot faulted, carried an unclassified row, ended without its terminal
    /// checkpoint, or carried a create row with no contract key.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    public static async Task<IReadOnlyList<Contract<T, TKey>>> SnapshotAsync<T, TKey>(
        this ILedgerStreamer streamer,
        KeyDescriptor<T, TKey> key,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        ArgumentNullException.ThrowIfNull(streamer);
        ArgumentNullException.ThrowIfNull(key);
        _ = key; // type-inference carrier only; decode uses T.Key per IHasKey

        var rows = await TemplateSnapshotArmReader<T>.DrainAsync(streamer, submitter, activeAtOffset, includeDisclosure: false, cancellationToken)
            .ConfigureAwait(false);

        var contracts = new List<Contract<T, TKey>>(rows.Count);
        foreach (var row in rows)
        {
            contracts.Add(ToKeyedContract<T, TKey>(row));
        }

        return contracts;
    }

    /// <summary>
    /// Drains an active-contract-set snapshot of a keyed template into a materialized list of
    /// typed contracts, each carrying its contract key decoded into <typeparamref name="TKey"/>
    /// and paired with the provenance its snapshot row carried.
    /// </summary>
    /// <remarks>
    /// The provenance-keeping twin of
    /// <see cref="SnapshotAsync{T, TKey}(ILedgerStreamer, KeyDescriptor{T, TKey}, SubmitterInfo, LedgerOffset?, CancellationToken)"/>,
    /// with identical behaviour for every fault, unclassified row, truncation and missing-key
    /// case. <see cref="ActiveContract{TContract}.LastUpdateOffset"/> is a per-contract fact and
    /// not a resume point; the terminal checkpoint's <see cref="StakeholderResume"/> ticket and
    /// the rows' witness parties are still discarded.
    /// </remarks>
    /// <typeparam name="T">The keyed Daml template the snapshot is filtered to.</typeparam>
    /// <typeparam name="TKey">The template's contract key type.</typeparam>
    /// <param name="streamer">The streaming capability.</param>
    /// <param name="key">The template's key witness. It is the type argument carrier — passing
    /// <c>Account.Key</c> infers both <typeparamref name="T"/> and <typeparamref name="TKey"/>
    /// from one argument, which C# cannot do from a partial type-argument list. The decode is
    /// taken from the template's own <see cref="IHasKey{TSelf, TKey}.Key"/> witness, so this
    /// method and <see cref="Contract{T, TKey}.FromCreatedEvent"/> decode identically.</param>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="includeDisclosure">
    /// <c>true</c> asks the participant for each contract's <c>created_event_blob</c>, so every
    /// returned <see cref="ActiveContract{TContract}"/> carries a
    /// <see cref="ActiveContract{TContract}.Disclosure"/> another party can attach to its
    /// submission. <c>false</c>, the default, leaves it <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream, which surfaces as an
    /// <see cref="OperationCanceledException"/> rather than a gracefully-completed stream.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="streamer"/> or
    /// <paramref name="key"/> is <c>null</c>.</exception>
    /// <exception cref="LedgerOperationException">
    /// The snapshot faulted, carried an unclassified row, ended without its terminal
    /// checkpoint, or carried a create row with no contract key.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled. Cancellation is surfaced as an
    /// exception, never as a gracefully-completed stream and never as an in-band terminal
    /// error event, so a caller draining into a list never observes a silently partial result.
    /// </exception>
    public static async Task<IReadOnlyList<ActiveContract<Contract<T, TKey>>>> SnapshotActiveAsync<T, TKey>(
        this ILedgerStreamer streamer,
        KeyDescriptor<T, TKey> key,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        ArgumentNullException.ThrowIfNull(streamer);
        ArgumentNullException.ThrowIfNull(key);

        var rows = await TemplateSnapshotArmReader<T>.DrainAsync(streamer, submitter, activeAtOffset, includeDisclosure, cancellationToken)
            .ConfigureAwait(false);

        var contracts = new List<ActiveContract<Contract<T, TKey>>>(rows.Count);
        foreach (var row in rows)
        {
            contracts.Add(new ActiveContract<Contract<T, TKey>>(
                ToKeyedContract<T, TKey>(row),
                row.Offset,
                row.SynchronizerId)
            {
                Disclosure = row.Disclosure,
            });
        }

        return contracts;
    }

    private static Contract<T, TKey> ToKeyedContract<T, TKey>(AcsSnapshotEntry<T>.Created created)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        if (created.Key is null)
            throw new LedgerOperationException(
                $"The active-contract-set snapshot for keyed template {typeof(T).Name} carried a "
                + $"create row for contract '{created.ContractId.Value}' with no contract key, so "
                + "the keyed contracts would be incomplete. Use the keyless SnapshotAsync overload "
                + "when the transport does not supply keys.");
        return created.ToContract<T, TKey>();
    }
}
