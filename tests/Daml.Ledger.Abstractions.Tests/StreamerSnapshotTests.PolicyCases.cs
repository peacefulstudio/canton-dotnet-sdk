// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using System.Runtime.CompilerServices;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Daml.Testing.SnapshotPolicy;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public sealed partial class StreamerSnapshotTests
{
    private delegate Task<SnapshotRead> EntryPoint(
        FakeStreamer streamer, TimeSpan? timeout, UnreadableRows policy, CancellationToken cancellationToken);

    private static readonly IReadOnlyDictionary<string, EntryPoint> EntryPoints =
        new Dictionary<string, EntryPoint>
        {
            ["SnapshotAsync"] = async (streamer, timeout, policy, cancellationToken) =>
            {
                var snapshot = await streamer.SnapshotAsync<Probe>(
                    Alice, timeout: timeout, unreadableRows: policy, cancellationToken: cancellationToken);
                return new SnapshotRead(snapshot.Select(contract => contract.Id.Value).ToList(), snapshot.Unreadable);
            },
            ["SnapshotActiveAsync"] = async (streamer, timeout, policy, cancellationToken) =>
            {
                var snapshot = await streamer.SnapshotActiveAsync<Probe>(
                    Alice, timeout: timeout, unreadableRows: policy, cancellationToken: cancellationToken);
                return new SnapshotRead(snapshot.Select(active => active.Contract.Id.Value).ToList(), snapshot.Unreadable);
            },
            ["SnapshotAsync_keyed"] = async (streamer, timeout, policy, cancellationToken) =>
            {
                var snapshot = await streamer.SnapshotAsync(
                    Probe.Key, Alice, timeout: timeout, unreadableRows: policy, cancellationToken: cancellationToken);
                return new SnapshotRead(snapshot.Select(contract => contract.Id.Value).ToList(), snapshot.Unreadable);
            },
            ["SnapshotActiveAsync_keyed"] = async (streamer, timeout, policy, cancellationToken) =>
            {
                var snapshot = await streamer.SnapshotActiveAsync(
                    Probe.Key, Alice, timeout: timeout, unreadableRows: policy, cancellationToken: cancellationToken);
                return new SnapshotRead(snapshot.Select(active => active.Contract.Id.Value).ToList(), snapshot.Unreadable);
            },
        };

    public static TheoryData<string, string, UnreadableRows> PolicyCaseOnEntryPoint()
    {
        var data = new TheoryData<string, string, UnreadableRows>();
        foreach (var entryPoint in EntryPoints.Keys)
        {
            foreach (var (policyCase, policy) in SnapshotPolicyCases.Combinations)
            {
                data.Add(entryPoint, policyCase, policy);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PolicyCaseOnEntryPoint))]
    public Task SnapshotPolicy_case_holds_on_entry_point(string entryPoint, string policyCase, UnreadableRows policy) =>
        SnapshotPolicyCases.RunAsync(policyCase, new TemplateEntryPointDriver(EntryPoints[entryPoint], policy));

    private sealed class TemplateEntryPointDriver(EntryPoint entryPoint, UnreadableRows policy) : ISnapshotPolicyDriver
    {
        private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public UnreadableRows Policy { get; } = policy;

        public SnapshotPolicySubject Subject { get; } =
            new("Probe", "contract", "SubscribeActiveAsync");

        public Task WhenStalled => _stalled.Task;

        public Task<SnapshotRead> DrainAsync(
            IReadOnlyList<SnapshotStep> script,
            bool streamHonoursCancellation,
            TimeSpan? timeout,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            var streamer = script.Any(step => step is SnapshotStep.Stall)
                ? new FakeStreamer { TimeProvider = timeProvider, Source = token => Replay(script, token) }
                : new FakeStreamer(script.Select(ToEntry).ToArray())
                {
                    TimeProvider = timeProvider,
                    ObservesCancellation = streamHonoursCancellation,
                };
            return entryPoint(streamer, timeout, Policy, cancellationToken);
        }

        private async IAsyncEnumerable<AcsSnapshotEntry<Probe>> Replay(
            IReadOnlyList<SnapshotStep> script,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var step in script)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (step is SnapshotStep.Stall stall)
                {
                    _stalled.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                        static _ => { },
                        CancellationToken.None);

                    if (stall.Ending == StallEnding.ThrowsCancellation)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (stall.Ending == StallEnding.ReportsCancelledFaultInBand)
                    {
                        yield return new AcsSnapshotEntry<Probe>.StreamError(
                            new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "CANCELLED");
                    }

                    yield break;
                }

                yield return ToEntry(step);
                await Task.Yield();
            }
        }

        private static AcsSnapshotEntry<Probe> ToEntry(SnapshotStep step) =>
            step switch
            {
                SnapshotStep.Row row => new AcsSnapshotEntry<Probe>.Created(
                    new ContractId<Probe>(row.ContractId),
                    new Probe(new Party("alice"), ProbeGrade.Low),
                    new ContractKey(DamlRecord.Create(new DamlField("owner", new DamlParty("alice")))),
                    LedgerOffset.At(row.Offset),
                    new SynchronizerId("sync"),
                    [new Party("alice")]),
                SnapshotStep.Checkpoint checkpoint => new AcsSnapshotEntry<Probe>.Checkpoint(
                    new StakeholderResume(LedgerOffset.At(checkpoint.Offset))),
                SnapshotStep.Fault fault => new AcsSnapshotEntry<Probe>.StreamError(
                    fault.Status, fault.Message, fault.Category, fault.ErrorId, fault.SourceException),
                SnapshotStep.Unclassified unclassified => new AcsSnapshotEntry<Probe>.Unclassified(
                    unclassified.Offset is { } offset ? LedgerOffset.At(offset) : null,
                    unclassified.Kind,
                    unclassified.RawKind),
                _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
            };
    }
}
