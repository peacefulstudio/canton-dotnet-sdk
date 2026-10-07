// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private static DisclosedContract SampleDisclosedContract =>
        new("00c0ffee", SampleIdentifier, new byte[] { 1, 2, 3, 250 }) { SynchronizerId = GlobalSynchronizer };

    private static DamlRecord SampleRecord =>
        DamlRecord.Create(SampleIdentifier, DamlField.Create("amount", new DamlInt64(42)), DamlField.Create("owner", new DamlParty(Alice.Value)));

    private static void AddCommands(Dictionary<Type, object[]> samples)
    {
        samples[typeof(ChoiceName)] = [new ChoiceName("Transfer"), new ChoiceName("Archive")];
        samples[typeof(CommandId)] = [new CommandId("cmd-1"), new CommandId("cmd-2")];
        samples[typeof(WorkflowId)] = [new WorkflowId("wf-1"), new WorkflowId("wf-2")];
        samples[typeof(SubmitterInfo)] =
        [
            new SubmitterInfo(new HashSet<Party> { Alice, Bob }, new HashSet<Party> { Carol, new Party("Dave::1220ff") }),
        ];
        samples[typeof(DeduplicationPeriod)] =
        [
            new DeduplicationPeriod.Offset(LedgerOffset.At(15)),
            new DeduplicationPeriod.Duration(TimeSpan.FromSeconds(90)),
        ];
        samples[typeof(MinLedgerTime.Absolute)] = [new MinLedgerTime.Absolute(SampleInstant)];
        samples[typeof(MinLedgerTime.Relative)] = [new MinLedgerTime.Relative(TimeSpan.FromMinutes(5))];
        samples[typeof(DisclosedContract)] = [SampleDisclosedContract];
    }
}
