// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerReaderParityTests : LedgerReaderParityTests
{
    private static readonly Party Owner = new("fake::reads-owner");
    private static readonly SynchronizerId Synchronizer = new("fake::sync-1");
    private static readonly ContractId<Marker> First = new("00fake-first");
    private static readonly ContractId<Marker> Second = new("00fake-second");
    private static readonly ContractId<Marker> Archived = new("00fake-archived");

    private static readonly LedgerOffset BeforeSeeding = LedgerOffset.At(10);
    private static readonly LedgerOffset FirstCreatedAt = LedgerOffset.At(11);
    private static readonly LedgerOffset SecondCreatedAt = LedgerOffset.At(12);
    private static readonly LedgerOffset ArchivedCreatedAt = LedgerOffset.At(13);
    private static readonly LedgerOffset SeededThrough = LedgerOffset.At(14);

    protected override Task<CapabilityLane<ILedgerReader>> OpenReaderAsync(CancellationToken cancellationToken)
    {
        var client = FakeLedgerClient.Create().WithLedgerEnd(LedgerOffset.At(42)).Build();
        return Task.FromResult(new CapabilityLane<ILedgerReader>(client, client.DisposeAsync));
    }

    protected override Task<CapabilityLane<TypedReadsProbe>> OpenTypedReadsAsync(CancellationToken cancellationToken)
    {
        var firstPageToken = new LedgerPageToken("ZmFrZS1wYWdlLTE=");
        var firstUpdatesToken = new LedgerPageToken("ZmFrZS11cGRhdGVzLTE=");

        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(BeforeSeeding)
            .WithContract(new CreatedContract<Marker>(First, new Marker(Owner), null, [Owner]))
            .WithContractLifecycle(
                First,
                new ContractLifecycle<Marker>(CreatedEvent(First, FirstCreatedAt), null))
            .WithContractLifecycle(
                Archived,
                new ContractLifecycle<Marker>(
                    CreatedEvent(Archived, ArchivedCreatedAt),
                    new ContractStreamEvent<Marker>.Archived(Archived, SeededThrough, Synchronizer, [Owner])))
            .WithActiveContractsPages(
                new AcsPage<Marker>([ActiveEntry(First, FirstCreatedAt)], SeededThrough, firstPageToken),
                new AcsPage<Marker>([ActiveEntry(Second, SecondCreatedAt)], SeededThrough, null))
            .WithUpdatesPages(
                new UpdatesPage(
                    [CreationUpdate("fake-update-1", First, FirstCreatedAt), CreationUpdate("fake-update-2", Second, SecondCreatedAt)],
                    BeforeSeeding,
                    SecondCreatedAt,
                    firstUpdatesToken),
                new UpdatesPage(
                    [CreationUpdate("fake-update-3", Archived, ArchivedCreatedAt), ArchivalUpdate("fake-update-4", Archived, SeededThrough)],
                    SecondCreatedAt,
                    SeededThrough,
                    null))
            .WithPrunedOffsets(new PrunedOffsets(LedgerOffset.At(0), LedgerOffset.At(0)))
            .Build();

        var probe = new TypedReadsProbe(
            client, Owner, First, Second, Archived, BeforeSeeding, FirstCreatedAt, SeededThrough);
        return Task.FromResult(new CapabilityLane<TypedReadsProbe>(probe, client.DisposeAsync));
    }

    private static ContractStreamEvent<Marker>.Created CreatedEvent(ContractId<Marker> contractId, LedgerOffset offset) =>
        new(contractId, new Marker(Owner), null, offset, Synchronizer, [Owner]);

    private static AcsSnapshotEntry<Marker>.Created ActiveEntry(ContractId<Marker> contractId, LedgerOffset offset) =>
        new(contractId, new Marker(Owner), null, offset, Synchronizer, [Owner]);

    private static TransactionResult CreationUpdate(string updateId, ContractId<Marker> contractId, LedgerOffset offset) =>
        new(
            updateId,
            offset,
            [
                new Daml.Runtime.Contracts.CreatedContract(
                    $"#{updateId}:0",
                    contractId.Value,
                    Marker.TemplateId,
                    new Marker(Owner).ToRecord(),
                    [Owner],
                    [Owner],
                    []),
            ],
            [],
            null);

    private static TransactionResult ArchivalUpdate(string updateId, ContractId<Marker> contractId, LedgerOffset offset) =>
        new(updateId, offset, [], [], null)
        {
            ExercisedEvents =
            [
                new ExercisedEvent(
                    contractId.Value,
                    Marker.TemplateId,
                    null,
                    new ChoiceName("Archive"),
                    DamlRecord.Create(),
                    DamlUnit.Instance,
                    true,
                    [Owner],
                    [Owner]),
            ],
        };
}
