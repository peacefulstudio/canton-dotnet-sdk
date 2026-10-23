// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

/// <summary>
/// Builds the shipped <see cref="FakeLedgerClient"/> seeded with the scenario the conformance
/// kit documents, so a mutation kit starts from a client the kit already passes on.
/// </summary>
internal static class SeededShippedFake
{
    private static readonly Party Alice = new("alice");
    private static readonly SynchronizerId Synchronizer = (SynchronizerId)"sync-1";
    private static readonly ContractId<ConformanceProbe> Probe = new("00probe");
    private static readonly ContractId<IConformanceProbe> InterfaceProbe = new("00probe");
    private static readonly ConformanceProbe Payload = new("alice");
    private static readonly ConformanceProbeView View = new(42.5m);
    private static readonly LedgerOffset Created = LedgerOffset.At(1);
    private static readonly LedgerOffset Unclassifiable = LedgerOffset.At(2);
    private static readonly LedgerOffset Consumed = LedgerOffset.At(3);
    private static readonly LedgerOffset LedgerEnd = LedgerOffset.At(5);

    public static ILedgerClient Create() =>
        FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerEnd)
            .WithActiveContracts(
                LedgerEvents.Created(Probe, Payload, null, Created, Synchronizer, [Alice]),
                LedgerEvents.Unclassified<ConformanceProbe>(Unclassifiable, UnclassifiedKind.MissingSynchronizerId),
                LedgerEvents.Checkpoint<ConformanceProbe>(LedgerEnd))
            .WithActiveInterfaceContracts<IConformanceProbe, ConformanceProbeView>(
                new InterfaceAcsSnapshotEntry<IConformanceProbe, ConformanceProbeView>.Created(
                    InterfaceProbe, View, null, Created, Synchronizer, [Alice]),
                new InterfaceAcsSnapshotEntry<IConformanceProbe, ConformanceProbeView>.Unclassified(
                    Unclassifiable, UnclassifiedKind.MissingSynchronizerId),
                new InterfaceAcsSnapshotEntry<IConformanceProbe, ConformanceProbeView>.Checkpoint(
                    new StakeholderResume(LedgerEnd)))
            .WithContractEvents(
                ContractEvents.Created(Probe, Payload, null, Created, Synchronizer, [Alice]),
                ContractEvents.Archived(Probe, Consumed, Synchronizer, [Alice]))
            .WithInterfaceEvents<IConformanceProbe, ConformanceProbeView>(
                new InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Created(
                    InterfaceProbe, View, null, Created, Synchronizer, [Alice]),
                new InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Archived(
                    InterfaceProbe, Consumed, Synchronizer, [Alice]))
            .WithLedgerEffects(
                ContractEvents.Created(Probe, Payload, null, Created, Synchronizer, [Alice]),
                ContractEvents.Exercised(
                    Probe,
                    new ChoiceName("Archive"),
                    DamlRecord.Create(),
                    DamlUnit.Instance,
                    consuming: true,
                    Consumed,
                    Synchronizer,
                    [Alice]))
            .WithInterfaceLedgerEffects<IConformanceProbe, ConformanceProbeView>(
                new InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Created(
                    InterfaceProbe, View, null, Created, Synchronizer, [Alice]),
                new InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Exercised(
                    InterfaceProbe,
                    new ChoiceName("Archive"),
                    DamlRecord.Create(),
                    DamlUnit.Instance,
                    Consuming: true,
                    Consumed,
                    Synchronizer,
                    [Alice]))
            .Build();
}
