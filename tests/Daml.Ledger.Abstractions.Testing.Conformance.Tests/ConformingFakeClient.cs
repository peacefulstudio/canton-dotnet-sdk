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

internal sealed class ConformingFakeClient : NotSupportedLedgerClient
{
    private static readonly LedgerOffset LedgerEnd = LedgerOffset.At(3);

    private readonly bool _faultsMidSnapshot;

    public ConformingFakeClient(bool faultsMidSnapshot = false) =>
        _faultsMidSnapshot = faultsMidSnapshot;

    public override async IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        bool includeDisclosure = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effective = activeAtOffset ?? LedgerEnd;
        await Task.CompletedTask;

        if (_faultsMidSnapshot)
        {
            yield return new AcsSnapshotEntry<T>.Created(
                new ContractId<T>("c1"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(1),
                new SynchronizerId("sync"), [new Party("alice")]);
            yield return new AcsSnapshotEntry<T>.StreamError(
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "UNAVAILABLE: transport fault mid-snapshot");
            yield break;
        }

        if (effective.Value >= 1)
        {
            yield return new AcsSnapshotEntry<T>.Created(
                new ContractId<T>("c1"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(1),
                new SynchronizerId("sync"), [new Party("alice")]);
        }

        if (effective.Value >= 2)
        {
            yield return new AcsSnapshotEntry<T>.Created(
                new ContractId<T>("c2"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(2),
                new SynchronizerId("sync"), [new Party("alice")]);
        }

        if (effective.Value >= 3)
        {
            yield return new AcsSnapshotEntry<T>.Unclassified(LedgerOffset.At(3), UnclassifiedKind.Unknown, "UNMAPPED");
        }

        yield return new AcsSnapshotEntry<T>.Checkpoint(new StakeholderResume(effective));
    }

    public override async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lower = (fromOffset ?? LedgerOffset.Begin).Value;
        await Task.CompletedTask;

        foreach (var (offset, evt) in SeededStream<T>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset <= lower)
            {
                continue;
            }

            if (toOffset is { } upper && offset > upper.Value)
            {
                yield break;
            }

            yield return evt;
        }
    }

    public override async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lower = (fromOffset ?? LedgerOffset.Begin).Value;
        await Task.CompletedTask;

        foreach (var (offset, evt) in SeededEffectsStream<T>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset <= lower)
            {
                continue;
            }

            if (toOffset is { } upper && offset > upper.Value)
            {
                yield break;
            }

            yield return evt;
        }
    }

    private static IEnumerable<(long Offset, ContractStreamEvent<T> Event)> SeededEffectsStream<T>()
        where T : ITemplate, IDamlRecord<T>
    {
        yield return (1, new ContractStreamEvent<T>.Created(
            new ContractId<T>("c1"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(1),
            new SynchronizerId("sync"), [new Party("alice")]));
        yield return (2, new ContractStreamEvent<T>.Exercised(
            new ContractId<T>("c1"), new ChoiceName("Archive"), DamlUnit.Instance, DamlUnit.Instance, true,
            LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]));
    }

    private static IEnumerable<(long Offset, ContractStreamEvent<T> Event)> SeededStream<T>()
        where T : ITemplate, IDamlRecord<T>
    {
        yield return (1, new ContractStreamEvent<T>.Created(
            new ContractId<T>("c1"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(1),
            new SynchronizerId("sync"), [new Party("alice")]));
        yield return (2, new ContractStreamEvent<T>.Created(
            new ContractId<T>("c2"), T.FromRecord(DamlRecord.Create()), null, LedgerOffset.At(2),
            new SynchronizerId("sync"), [new Party("alice")]));
        yield return (2, new ContractStreamEvent<T>.Archived(
            new ContractId<T>("c1"), LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]));
        yield return (3, new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(3), UnclassifiedKind.Unknown, "UNMAPPED"));
    }

    public override Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(LedgerEnd);
}
