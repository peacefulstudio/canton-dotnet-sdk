// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Behavioural coverage for <see cref="StreamerSnapshot.SnapshotAsync{T}"/>: the typed
/// projection consumers would otherwise hand-roll, and the incomplete-snapshot shapes it
/// must refuse to hand back as a short list.
/// </summary>
public sealed partial class StreamerSnapshotTests
{
    private static readonly SubmitterInfo Alice = new(new Party("alice"));

    [Fact]
    public async Task SnapshotAsync_decodes_created_rows_into_typed_contracts()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            Created("cid-2", "bob", 2),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(2))));

        var contracts = await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().HaveCount(2);
        contracts[0].Id.Value.Should().Be("cid-1");
        contracts[0].Data.Owner.Should().Be(new Party("alice"));
        contracts[1].Data.Owner.Should().Be(new Party("bob"));
    }

    [Fact]
    public async Task SnapshotAsync_surfaces_the_contract_key_the_snapshot_row_carried()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                new ContractKey(DamlRecord.Create(new DamlField("owner", new DamlParty("alice")))),
                LedgerOffset.At(1),
                new SynchronizerId("sync"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var contracts = await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().ContainSingle().Which.Key.Value.Should().Be(
            new Party("alice"),
            "this is the entry point the key feature documents, so a key dropped anywhere along the "
            + "Created to Contract chain reaches the caller as a silently absent key");
    }

    [Fact]
    public async Task SnapshotAsync_infers_both_type_arguments_from_the_key_witness()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                new ContractKey(DamlRecord.Create(new DamlField("owner", new DamlParty("alice")))),
                LedgerOffset.At(1),
                new SynchronizerId("sync"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var contracts = await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().BeAssignableTo<IReadOnlyList<Contract<Probe, Party>>>(
            "the witness argument exists so one argument fixes both type parameters; C# performs no "
            + "partial type-argument inference, so a call site spelling neither must still bind");
    }

    [Fact]
    public async Task SnapshotAsync_reports_a_keyed_row_that_carried_no_key()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                null,
                LedgerOffset.At(1),
                new SynchronizerId("sync"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var draining = async () => await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        await draining.Should().ThrowAsync<LedgerOperationException>(
            "a keyed snapshot whose row carried no key would otherwise return contracts the caller "
            + "cannot address by key, which is the short-list failure this convenience prevents");
    }

    [Fact]
    public async Task SnapshotAsync_surfaces_the_key_hash_the_snapshot_row_carried()
    {
        const string ledgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                new ContractKey(
                    DamlRecord.Create(new DamlField("owner", new DamlParty("alice"))),
                    Probe.TemplateId)
                {
                    KeyHash = ledgerKeyHash,
                },
                LedgerOffset.At(1),
                new SynchronizerId("sync"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var contracts = await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().ContainSingle().Which.Key.Hash.Should().Be(
            ledgerKeyHash,
            "this drains the whole Created to Contract chain, so a hash dropped at "
            + "any hop reaches the caller as a silently absent hash");
    }

    [Fact]
    public async Task SnapshotAsync_returns_an_empty_list_for_an_empty_snapshot()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(7))));

        var contracts = await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().BeEmpty();
    }

    [Fact]
    public async Task SnapshotAsync_throws_on_a_terminal_stream_error()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable"));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await snapshot.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.Which.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task SnapshotAsync_sets_CommitState_NotCommitted_on_a_terminal_stream_error()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable"));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await snapshot.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.CommitState.Should().Be(
            CommitState.NotCommitted,
            "a snapshot read submits no command, so unlike a write-path InfraError there is nothing "
            + "the ledger could have committed and nothing to resubmit — retrying the read is always safe");
    }

    [Fact]
    public async Task SnapshotAsync_carries_the_stream_errors_classification_onto_the_thrown_exception()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.StreamError(
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable", DamlErrorCategory.TransientServerFailure));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await snapshot.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Category.Should().Be(
            DamlErrorCategory.TransientServerFailure,
            "a classification the transport determined is discarded unless this branch forwards it, "
            + "leaving a caller who catches the exception unable to tell a retryable transient fault "
            + "from a permanent one with only the status code to go on");
        thrown.Which.Status.Should().Be(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            "the classification has to arrive alongside the status code, not in place of it");
    }

    [Fact]
    public async Task SnapshotAsync_carries_the_stream_errors_source_exception_onto_the_thrown_exception()
    {
        var transportFault = new InvalidOperationException("the channel went away");
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.StreamError(
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable", DamlErrorCategory.TransientServerFailure, SourceException: transportFault));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await snapshot.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.InnerException.Should().BeSameAs(
            transportFault,
            "the exception the transport caught is lost unless this branch forwards it, leaving a caller "
            + "who catches the rethrow with none of the stack the fault actually came from");
    }

    [Fact]
    public async Task SnapshotAsync_carries_the_stream_errors_error_id_onto_the_thrown_exception()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.StreamError(
                new TransportStatus.Grpc(GrpcStatusCode.Aborted),
                "the stream authorization is stale",
                DamlErrorCategory.ContentionOnSharedResources,
                "STALE_STREAM_AUTHORIZATION"));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await snapshot.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be(
            "STALE_STREAM_AUTHORIZATION",
            "the error id the snapshot entry now carries is dropped again at this rethrow unless the branch "
            + "forwards it, and the exception already exposes the slot the caller would read it from");
    }

    [Fact]
    public async Task SnapshotAsync_throws_rather_than_dropping_an_unclassified_row()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.Unclassified(LedgerOffset.At(2), UnclassifiedKind.Unknown, "ACTIVE_CONTRACT"),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(2))));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshot.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*ACTIVE_CONTRACT*");
    }

    [Fact]
    public async Task SnapshotAsync_reports_an_unclassified_row_that_carries_no_offset()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.Unclassified(null, UnclassifiedKind.DecodeFailure),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(2))));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshot.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage(
                "*at an unreported offset*",
                "interpolating a null offset would render an empty position and read as 'at offset , so the returned' — the row's absent ledger position has to be said, not blanked");
    }

    [Fact]
    public async Task SnapshotAsync_throws_when_the_snapshot_ends_without_its_checkpoint()
    {
        var streamer = new FakeStreamer(Created("cid-1", "alice", 1));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshot.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*without its terminal checkpoint*");
    }

    [Fact]
    public async Task SnapshotAsync_throws_on_a_row_the_projector_could_not_decode()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.Unclassified(LedgerOffset.At(2), UnclassifiedKind.DecodeFailure),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(2))));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshot.Should().ThrowAsync<LedgerOperationException>(
                "a payload the projector could not decode reaches this layer as a DecodeFailure row "
                + "rather than a create row, and leaves the snapshot as unusable as any other "
                + "unclassified row — which is what publisher-ahead-of-reader drift looks like")
            .WithMessage("*DecodeFailure*");
    }

    [Fact]
    public async Task SnapshotAsync_reports_caller_cancellation_rather_than_an_in_band_transport_fault()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var streamer = FakeStreamer.IgnoringCancellation(new AcsSnapshotEntry<Probe>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "CANCELLED"));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: cts.Token);

        await snapshot.Should().ThrowAsync<OperationCanceledException>(
            "a transport that maps the cancelled call in-band would otherwise make the caller's "
            + "own cancellation read as an infrastructure failure");
    }

    [Fact]
    public async Task SnapshotAsync_reports_caller_cancellation_rather_than_a_truncated_snapshot()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var streamer = FakeStreamer.IgnoringCancellation(Created("cid-1", "alice", 1));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: cts.Token);

        await snapshot.Should().ThrowAsync<OperationCanceledException>(
            "a stream cut short by the caller's own token is not a transport truncation");
    }

    [Fact]
    public async Task SnapshotActiveAsync_carries_each_rows_last_update_offset_and_synchronizer()
    {
        var streamer = new FakeStreamer(
            CreatedOn("cid-1", "alice", 1, "sync-a"),
            CreatedOn("cid-2", "bob", 7, "sync-b"),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(7))));

        var active = await streamer.SnapshotActiveAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        active.Should().HaveCount(2);
        active[0].Contract.Id.Value.Should().Be("cid-1");
        active[0].LastUpdateOffset.Should().Be(LedgerOffset.At(1));
        active[0].SynchronizerId.Should().Be(new SynchronizerId("sync-a"));
        active[1].Contract.Id.Value.Should().Be("cid-2");
        active[1].LastUpdateOffset.Should().Be(
            LedgerOffset.At(7),
            "the rows carry distinct offsets and synchronizers so a field dropped, or read off the "
            + "wrong row, cannot pass unnoticed");
        active[1].SynchronizerId.Should().Be(new SynchronizerId("sync-b"));
    }

    [Fact]
    public async Task SnapshotActiveAsync_carries_the_decoded_key_alongside_the_provenance()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                new ContractKey(DamlRecord.Create(new DamlField("owner", new DamlParty("alice")))),
                LedgerOffset.At(3),
                new SynchronizerId("sync-a"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(3))));

        var active = await streamer.SnapshotActiveAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        active.Should().BeAssignableTo<IReadOnlyList<ActiveContract<Contract<Probe, Party>>>>(
            "the witness argument exists so one argument fixes both type parameters; C# performs no "
            + "partial type-argument inference, so a call site spelling neither must still bind");
        active[0].Contract.Key.Value.Should().Be(new Party("alice"));
        active[0].LastUpdateOffset.Should().Be(LedgerOffset.At(3));
        active[0].SynchronizerId.Should().Be(new SynchronizerId("sync-a"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SnapshotActiveAsync_forwards_the_created_event_blob_request(bool includeDisclosure)
    {
        var streamer = new FakeStreamer(new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        _ = await streamer.SnapshotActiveAsync<Probe>(
            Alice, includeDisclosure: includeDisclosure, cancellationToken: TestContext.Current.CancellationToken);

        streamer.RequestedCreatedEventBlob.Should().Be(includeDisclosure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SnapshotActiveAsync_keyed_forwards_the_created_event_blob_request(bool includeDisclosure)
    {
        var streamer = new FakeStreamer(new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        _ = await streamer.SnapshotActiveAsync(
            Probe.Key, Alice, includeDisclosure: includeDisclosure, cancellationToken: TestContext.Current.CancellationToken);

        streamer.RequestedCreatedEventBlob.Should().Be(includeDisclosure);
    }

    [Fact]
    public async Task SnapshotAsync_never_asks_for_the_created_event_blob()
    {
        var streamer = new FakeStreamer(new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        _ = await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        streamer.RequestedCreatedEventBlob.Should().BeFalse();
    }

    [Fact]
    public async Task SnapshotActiveAsync_keyed_carries_each_rows_Disclosure()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                new ContractKey(DamlRecord.Create(new DamlField("owner", new DamlParty("alice")))),
                LedgerOffset.At(3),
                new SynchronizerId("sync-a"),
                [new Party("alice")])
            {
                Disclosure = new DisclosedContract("cid-1", new Identifier("probe-pkg", "Probe.Mod", "Probe"), new byte[] { 0x07, 0x08 }),
            },
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(3))));

        var active = await streamer.SnapshotActiveAsync(
            Probe.Key, Alice, includeDisclosure: true, cancellationToken: TestContext.Current.CancellationToken);

        active.Should().ContainSingle().Which.Disclosure.Should().Be(
            new DisclosedContract("cid-1", new Identifier("probe-pkg", "Probe.Mod", "Probe"), new byte[] { 0x07, 0x08 }));
    }

    [Fact]
    public async Task SnapshotActiveAsync_carries_each_rows_Disclosure()
    {
        var streamer = new FakeStreamer(
            CreatedOn("cid-1", "alice", 1, "sync-a") with
            {
                Disclosure = new DisclosedContract("cid-1", new Identifier("probe-pkg", "Probe.Mod", "Probe"), new byte[] { 0x09 }),
            },
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var active = await streamer.SnapshotActiveAsync<Probe>(
            Alice, includeDisclosure: true, cancellationToken: TestContext.Current.CancellationToken);

        active.Should().ContainSingle().Which.Disclosure.Should().Be(
            new DisclosedContract("cid-1", new Identifier("probe-pkg", "Probe.Mod", "Probe"), new byte[] { 0x09 }));
    }

    [Fact]
    public async Task SnapshotActiveAsync_reports_a_keyed_row_that_carried_no_key()
    {
        var streamer = new FakeStreamer(
            CreatedOn("cid-1", "alice", 1, "sync-a"),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var draining = async () => await streamer.SnapshotActiveAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        await draining.Should().ThrowAsync<LedgerOperationException>(
            "carrying provenance must not soften the keyed drain's own refusal: contracts the "
            + "caller cannot address by key are the short-list failure this convenience prevents");
    }

    private static AcsSnapshotEntry<Probe>.Created CreatedOn(
        string contractId,
        string owner,
        long offset,
        string synchronizerId) =>
        new(
            new ContractId<Probe>(contractId),
            new Probe(new Party(owner), ProbeGrade.Low),
            null,
            LedgerOffset.At(offset),
            new SynchronizerId(synchronizerId),
            [new Party(owner)]);

    private static AcsSnapshotEntry<Probe>.Created Created(string contractId, string owner, long offset) =>
        new(
            new ContractId<Probe>(contractId),
            new Probe(new Party(owner), ProbeGrade.Low),
            null,
            LedgerOffset.At(offset),
            new SynchronizerId("sync"),
            [new Party(owner)]);

    private enum ProbeGrade
    {
        Low,
        High,
    }

    private sealed record Probe(Party Owner, ProbeGrade Grade)
        : ITemplate, IDamlRecord<Probe>, IHasKey<Probe, Party>
    {
        public static Identifier TemplateId { get; } = new("pkg", "Module", "Probe");
        public static string PackageId => "pkg";
        public static string PackageName => "probe";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<Probe, Party> Key { get; } = new()
        {
            KeyEncoder = key => DamlRecord.Create(new DamlField("owner", key.ToDamlValue())),
            KeyDecoder = value =>
                Party.FromDamlValue(value.As<DamlRecord>().GetRequiredField("owner").As<DamlParty>()),
            KeyJsonReader = (_, _) => throw new NotImplementedException(),
        };

        public DamlRecord ToRecord() => DamlRecord.Create(
            new DamlField("owner", Owner.ToDamlValue()),
            new DamlField("grade", new DamlEnum(null, Grade.ToString())));

        public static Probe FromRecord(DamlRecord record) =>
            new(
                Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("grade").As<DamlEnum>().Constructor switch
                {
                    "Low" => ProbeGrade.Low,
                    "High" => ProbeGrade.High,
                    var unknown => throw new ArgumentOutOfRangeException(nameof(record), unknown, null),
                });

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStreamer(params AcsSnapshotEntry<Probe>[] entries) : ILedgerStreamer
    {
        private bool ObservesCancellation { get; init; } = true;

        public bool? RequestedCreatedEventBlob { get; private set; }

        public static FakeStreamer IgnoringCancellation(params AcsSnapshotEntry<Probe>[] entries) =>
            new(entries) { ObservesCancellation = false };

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> => throw new NotSupportedException();

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> => throw new NotSupportedException();

        public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            bool includeDisclosure = false,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T>
        {
            RequestedCreatedEventBlob = includeDisclosure;
            return (IAsyncEnumerable<AcsSnapshotEntry<T>>)(object)Replay(cancellationToken);
        }

        public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> => throw new NotSupportedException();

        public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> => throw new NotSupportedException();

        public IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            bool includeDisclosure = false,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> => throw new NotSupportedException();

        private async IAsyncEnumerable<AcsSnapshotEntry<Probe>> Replay(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var entry in entries)
            {
                if (ObservesCancellation)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                yield return entry;
                await Task.Yield();
            }
        }
    }
}
