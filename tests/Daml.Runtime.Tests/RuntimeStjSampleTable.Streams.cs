// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private static TransportStatus SampleTransportStatus => new TransportStatus.Grpc(GrpcStatusCode.Unavailable);

    private static void AddStreams(Dictionary<Type, object[]> samples)
    {
        samples[typeof(UnclassifiedKind)] = [UnclassifiedKind.DecodeFailure];
        AddContractStreamEvents(samples);
        AddAcsSnapshotEntries(samples);
        AddInterfaceStreamEvents(samples);
        AddInterfaceAcsSnapshotEntries(samples);
    }

    private static void AddContractStreamEvents(Dictionary<Type, object[]> samples)
    {
        var template = new SampleTemplate(Alice, 42);
        var contractId = new ContractId<SampleTemplate>("00c0ffee");
        samples[typeof(ContractStreamEvent<SampleTemplate>)] =
        [
            new ContractStreamEvent<SampleTemplate>.Created(contractId, template, SampleContractKey, LedgerOffset.At(11), GlobalSynchronizer, Witnesses),
            new ContractStreamEvent<SampleTemplate>.Archived(contractId, LedgerOffset.At(12), GlobalSynchronizer, Witnesses),
            new ContractStreamEvent<SampleTemplate>.Exercised(contractId, new ChoiceName("Transfer"), new DamlParty(Bob.Value), new DamlInt64(5), true, LedgerOffset.At(13), GlobalSynchronizer, Witnesses),
            new ContractStreamEvent<SampleTemplate>.Assigned(contractId, template, SampleContractKey, LedgerOffset.At(14), GlobalSynchronizer, PrivateSynchronizer, "reassignment-1", 3, Witnesses),
            new ContractStreamEvent<SampleTemplate>.Unassigned(contractId, LedgerOffset.At(15), GlobalSynchronizer, PrivateSynchronizer, "reassignment-1", 3, Witnesses),
            new ContractStreamEvent<SampleTemplate>.Checkpoint(LedgerOffset.At(16)),
            new ContractStreamEvent<SampleTemplate>.StreamError(SampleTransportStatus, "stream broke", DamlErrorCategory.TransientServerFailure, "STREAM_BROKE", SampleSourceException),
            new ContractStreamEvent<SampleTemplate>.Unclassified(LedgerOffset.At(17), UnclassifiedKind.Unknown, "TopologyEvent"),
            new ContractStreamEvent<SampleTemplate>.Unclassified(LedgerOffset.At(18), UnclassifiedKind.DecodeFailure),
        ];
    }

    private static void AddAcsSnapshotEntries(Dictionary<Type, object[]> samples)
    {
        var template = new SampleTemplate(Alice, 42);
        samples[typeof(AcsSnapshotEntry<SampleTemplate>)] =
        [
            new AcsSnapshotEntry<SampleTemplate>.Created(new ContractId<SampleTemplate>("00c0ffee"), template, SampleContractKey, LedgerOffset.At(11), GlobalSynchronizer, Witnesses)
            {
                Disclosure = SampleDisclosedContract,
            },
            new AcsSnapshotEntry<SampleTemplate>.Unclassified(LedgerOffset.At(17), UnclassifiedKind.Unknown, "TopologyEvent"),
            new AcsSnapshotEntry<SampleTemplate>.Unclassified(LedgerOffset.At(18), UnclassifiedKind.DecodeFailure),
            new AcsSnapshotEntry<SampleTemplate>.Checkpoint(new StakeholderResume(LedgerOffset.At(16))),
            new AcsSnapshotEntry<SampleTemplate>.StreamError(SampleTransportStatus, "stream broke", DamlErrorCategory.TransientServerFailure, "STREAM_BROKE", SampleSourceException),
        ];
    }

    private static void AddInterfaceStreamEvents(Dictionary<Type, object[]> samples)
    {
        var view = new SampleView("gold");
        var contractId = new ContractId<ISampleInterface>("00facade");
        samples[typeof(InterfaceStreamEvent<ISampleInterface, SampleView>)] =
        [
            new InterfaceStreamEvent<ISampleInterface, SampleView>.Created(contractId, view, SampleContractKey, LedgerOffset.At(11), GlobalSynchronizer, Witnesses),
            new InterfaceStreamEvent<ISampleInterface, SampleView>.Archived(contractId, LedgerOffset.At(12), GlobalSynchronizer, Witnesses),
            new InterfaceStreamEvent<ISampleInterface, SampleView>.Exercised(contractId, new ChoiceName("Transfer"), new DamlParty(Bob.Value), new DamlInt64(5), true, LedgerOffset.At(13), GlobalSynchronizer, Witnesses),
            new InterfaceStreamEvent<ISampleInterface, SampleView>.Assigned(contractId, view, SampleContractKey, LedgerOffset.At(14), GlobalSynchronizer, PrivateSynchronizer, "reassignment-1", 3, Witnesses),
            new InterfaceStreamEvent<ISampleInterface, SampleView>.Unassigned(contractId, LedgerOffset.At(15), GlobalSynchronizer, PrivateSynchronizer, "reassignment-1", 3, Witnesses),
            new InterfaceStreamEvent<ISampleInterface, SampleView>.Checkpoint(LedgerOffset.At(16)),
            new InterfaceStreamEvent<ISampleInterface, SampleView>.StreamError(SampleTransportStatus, "stream broke", DamlErrorCategory.TransientServerFailure, "STREAM_BROKE", SampleSourceException),
            new InterfaceStreamEvent<ISampleInterface, SampleView>.Unclassified(LedgerOffset.At(17), UnclassifiedKind.Unknown, "TopologyEvent"),
            new InterfaceStreamEvent<ISampleInterface, SampleView>.Unclassified(LedgerOffset.At(18), UnclassifiedKind.InterfaceViewUnavailable),
        ];
    }

    private static void AddInterfaceAcsSnapshotEntries(Dictionary<Type, object[]> samples)
    {
        var view = new SampleView("gold");
        samples[typeof(InterfaceAcsSnapshotEntry<ISampleInterface, SampleView>)] =
        [
            new InterfaceAcsSnapshotEntry<ISampleInterface, SampleView>.Created(new ContractId<ISampleInterface>("00facade"), view, SampleContractKey, LedgerOffset.At(11), GlobalSynchronizer, Witnesses)
            {
                Disclosure = SampleDisclosedContract,
            },
            new InterfaceAcsSnapshotEntry<ISampleInterface, SampleView>.Unclassified(LedgerOffset.At(17), UnclassifiedKind.Unknown, "TopologyEvent"),
            new InterfaceAcsSnapshotEntry<ISampleInterface, SampleView>.Unclassified(LedgerOffset.At(18), UnclassifiedKind.InterfaceViewUnavailable),
            new InterfaceAcsSnapshotEntry<ISampleInterface, SampleView>.Checkpoint(new StakeholderResume(LedgerOffset.At(16))),
            new InterfaceAcsSnapshotEntry<ISampleInterface, SampleView>.StreamError(SampleTransportStatus, "stream broke", DamlErrorCategory.TransientServerFailure, "STREAM_BROKE", SampleSourceException),
        ];
    }
}
