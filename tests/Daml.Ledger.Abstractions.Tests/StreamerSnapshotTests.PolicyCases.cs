// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Daml.Testing.SnapshotPolicy;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public sealed partial class StreamerSnapshotTests
{
    private static readonly IReadOnlyDictionary<string, Func<FakeStreamer, CancellationToken, Task<IReadOnlyList<string>>>> EntryPoints =
        new Dictionary<string, Func<FakeStreamer, CancellationToken, Task<IReadOnlyList<string>>>>
        {
            ["SnapshotAsync"] = async (streamer, cancellationToken) =>
                (await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: cancellationToken))
                    .Select(contract => contract.Id.Value).ToList(),
            ["SnapshotActiveAsync"] = async (streamer, cancellationToken) =>
                (await streamer.SnapshotActiveAsync<Probe>(Alice, cancellationToken: cancellationToken))
                    .Select(active => active.Contract.Id.Value).ToList(),
            ["SnapshotAsync_keyed"] = async (streamer, cancellationToken) =>
                (await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: cancellationToken))
                    .Select(contract => contract.Id.Value).ToList(),
            ["SnapshotActiveAsync_keyed"] = async (streamer, cancellationToken) =>
                (await streamer.SnapshotActiveAsync(Probe.Key, Alice, cancellationToken: cancellationToken))
                    .Select(active => active.Contract.Id.Value).ToList(),
        };

    public static TheoryData<string, string> PolicyCaseOnEntryPoint()
    {
        var data = new TheoryData<string, string>();
        foreach (var entryPoint in EntryPoints.Keys)
        {
            foreach (var policyCase in SnapshotPolicyCases.Names)
            {
                data.Add(entryPoint, policyCase);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PolicyCaseOnEntryPoint))]
    public Task SnapshotPolicy_case_holds_on_entry_point(string entryPoint, string policyCase) =>
        SnapshotPolicyCases.RunAsync(policyCase, new TemplateEntryPointDriver(EntryPoints[entryPoint]));

    private sealed class TemplateEntryPointDriver(
        Func<FakeStreamer, CancellationToken, Task<IReadOnlyList<string>>> entryPoint) : ISnapshotPolicyDriver
    {
        public SnapshotPolicySubject Subject { get; } =
            new("Probe", "contract", "SubscribeActiveAsync");

        public Task<IReadOnlyList<string>> DrainAsync(
            IReadOnlyList<SnapshotStep> script,
            bool streamHonoursCancellation,
            CancellationToken cancellationToken)
        {
            var entries = script.Select(ToEntry).ToArray();
            var streamer = streamHonoursCancellation
                ? new FakeStreamer(entries)
                : FakeStreamer.IgnoringCancellation(entries);
            return entryPoint(streamer, cancellationToken);
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
