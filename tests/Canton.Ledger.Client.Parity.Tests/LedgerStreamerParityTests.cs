// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ILedgerStreamer"/>, run against every provider that
/// implements it (the in-memory Fake, REST, and gRPC) through one shared set of test bodies. Each
/// lane creates a <see cref="Marker"/> contract through the <see cref="ILedgerWriter"/> capability,
/// then confirms the streaming reads surface that contract the same way regardless of transport.
/// The interface-family rows create an <see cref="Asset"/> instead and read it back through the
/// <see cref="IHolding"/> interface it implements, so each read is proved on both the template
/// family and the interface family of the same streaming member.
/// </summary>
public abstract class LedgerStreamerParityTests
{
    /// <summary>
    /// The amount the interface-family rows create their <see cref="Asset"/> with, and therefore the
    /// amount the <see cref="HoldingView"/> the participant computes for it must carry. Lanes that
    /// stage their own view stage this same amount.
    /// </summary>
    protected const decimal AssetAmount = 12.5m;

    /// <summary>
    /// Whether this provider's ledger end advances across a write, so that a contract created
    /// between two <see cref="ILedgerReader.GetLedgerEndAsync"/> reads lands strictly inside the
    /// resulting <c>(fromOffset, toOffset]</c> window. Lanes whose ledger end is static cannot
    /// express a bounded range around a write at all and opt out of the four checks below.
    /// </summary>
    protected virtual bool SupportsAdvancingLedgerEnd => true;

    /// <summary>
    /// How far past the ledger end a read has to reach for the participant to reject it outright,
    /// rather than merely reaching an offset the ledger has since caught up to.
    /// </summary>
    private const long OffsetsPastTheLedgerEnd = 1_000_000L;

    /// <summary>
    /// Whether this provider has a participant that can reject a read at all. A lane serving
    /// canned data answers every range it is asked for, so it cannot express a rejection and opts
    /// out of the fault-shape check below.
    /// </summary>
    protected virtual bool SupportsParticipantRejectedReads => true;

    /// <summary>
    /// Whether this provider has a participant that returns a <c>created_event_blob</c> and
    /// validates a disclosed contract. A lane replaying canned rows has neither, so it opts out of
    /// the explicit-disclosure checks below.
    /// </summary>
    protected virtual bool SupportsExplicitDisclosure => true;

    /// <summary>Opens a lane over this provider's reader/writer/streaming capabilities for one test.</summary>
    protected abstract Task<CapabilityLane<(ILedgerReader Reader, ILedgerWriter Writer, ICantonLedgerClient Client, Party Owner)>>
        OpenStreamerAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task SubscribeActiveAsync_returns_a_snapshot_containing_the_created_Marker_and_ends_with_a_checkpoint()
    {
        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        var markerCid = await CreateMarkerAsync(writer, owner);
        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var entries = new List<AcsSnapshotEntry<Marker>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync<Marker>(
            owner, ledgerEnd, cancellationToken: TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.OfType<AcsSnapshotEntry<Marker>.Created>().Should().Contain(created => created.ContractId.Equals(markerCid));
        entries[^1].Should().BeOfType<AcsSnapshotEntry<Marker>.Checkpoint>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_returns_the_IHolding_view_of_the_created_Asset_and_ends_with_a_checkpoint()
    {
        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        var assetCid = await CreateAssetAsync(writer, owner);
        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var entries = new List<InterfaceAcsSnapshotEntry<IHolding, HoldingView>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync(
            IHolding.View, owner, ledgerEnd, cancellationToken: TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.OfType<InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created>()
            .Should().ContainSingle(created => created.ContractId.Value == assetCid.Value)
            .Which.Payload.Amount.Should().Be(AssetAmount);
        entries[^1].Should().BeOfType<InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Checkpoint>();
    }

    [Fact]
    public async Task SubscribeAsync_returns_the_created_Marker_as_an_ACS_delta_event_over_the_bounded_range()
    {
        Assert.SkipUnless(
            SupportsAdvancingLedgerEnd,
            "this lane's ledger end does not advance across a write, so the bounded range is empty by construction");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        await CreateMarkerBeforeTheWindowAsync(writer, owner);
        var fromOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var markerCid = await CreateMarkerAsync(writer, owner);
        var toOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var events = new List<ContractStreamEvent<Marker>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var evt in client.SubscribeAsync<Marker>(
            owner, fromOffset, toOffset, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.OfType<ContractStreamEvent<Marker>.Created>().Should().Contain(created => created.ContractId.Equals(markerCid));
        events.OfType<ContractStreamEvent<Marker>.Created>().Should().OnlyContain(
            created => created.Offset.Value > fromOffset.Value && created.Offset.Value <= toOffset.Value);
    }

    [Fact]
    public async Task SubscribeAsync_returns_the_IHolding_view_of_the_created_Asset_over_the_bounded_range()
    {
        Assert.SkipUnless(
            SupportsAdvancingLedgerEnd,
            "this lane's ledger end does not advance across a write, so the bounded range is empty by construction");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        await CreateAssetBeforeTheWindowAsync(writer, owner);
        var fromOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var assetCid = await CreateAssetAsync(writer, owner);
        var toOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var events = new List<InterfaceStreamEvent<IHolding, HoldingView>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var evt in client.SubscribeAsync(
            IHolding.View, owner, fromOffset, toOffset, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        var created = events.OfType<InterfaceStreamEvent<IHolding, HoldingView>.Created>().ToList();
        created.Should().ContainSingle(entry => entry.ContractId.Value == assetCid.Value)
            .Which.Payload.Amount.Should().Be(AssetAmount);
        created.Should().OnlyContain(
            entry => entry.Offset.Value > fromOffset.Value && entry.Offset.Value <= toOffset.Value);
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_returns_the_created_Marker_as_a_ledger_effects_event_over_the_bounded_range()
    {
        Assert.SkipUnless(
            SupportsAdvancingLedgerEnd,
            "this lane's ledger end does not advance across a write, so the bounded range is empty by construction");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        await CreateMarkerBeforeTheWindowAsync(writer, owner);
        var fromOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var markerCid = await CreateMarkerAsync(writer, owner);
        var toOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var events = new List<ContractStreamEvent<Marker>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var evt in client.SubscribeLedgerEffectsAsync<Marker>(
            owner, fromOffset, toOffset, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        events.OfType<ContractStreamEvent<Marker>.Created>().Should().Contain(created => created.ContractId.Equals(markerCid));
        events.OfType<ContractStreamEvent<Marker>.Created>().Should().OnlyContain(
            created => created.Offset.Value > fromOffset.Value && created.Offset.Value <= toOffset.Value);
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_returns_the_IHolding_view_of_the_created_Asset_over_the_bounded_range()
    {
        Assert.SkipUnless(
            SupportsAdvancingLedgerEnd,
            "this lane's ledger end does not advance across a write, so the bounded range is empty by construction");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        await CreateAssetBeforeTheWindowAsync(writer, owner);
        var fromOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var assetCid = await CreateAssetAsync(writer, owner);
        var toOffset = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var events = new List<InterfaceStreamEvent<IHolding, HoldingView>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var evt in client.SubscribeLedgerEffectsAsync(
            IHolding.View, owner, fromOffset, toOffset, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        var created = events.OfType<InterfaceStreamEvent<IHolding, HoldingView>.Created>().ToList();
        created.Should().ContainSingle(entry => entry.ContractId.Value == assetCid.Value)
            .Which.Payload.Amount.Should().Be(AssetAmount);
        created.Should().OnlyContain(
            entry => entry.Offset.Value > fromOffset.Value && entry.Offset.Value <= toOffset.Value);
    }

    [Fact]
    public async Task QueryActiveAsync_materializes_the_IHolding_view_of_the_created_Asset()
    {
        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        var assetCid = await CreateAssetAsync(writer, owner);
        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        var holdings = await client.QueryActiveAsync<IHolding, HoldingView>(
            owner, ledgerEnd, cancellationToken: TestContext.Current.CancellationToken);

        var holding = holdings.Should().ContainSingle(
            holding => holding.Contract.Id.Value == assetCid.Value).Subject;
        holding.Contract.View.Amount.Should().Be(AssetAmount);
        holding.LastUpdateOffset.Value.Should().BeGreaterThan(0);
        holding.SynchronizerId.Should().NotBe(default(SynchronizerId));
    }

    [Fact]
    public async Task SubscribeActiveAsync_asked_for_the_created_event_blob_returns_the_created_Marker_ready_for_disclosure()
    {
        Assert.SkipUnless(
            SupportsExplicitDisclosure,
            "this lane has no participant to return a created_event_blob or to validate a disclosure");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        var markerCid = await CreateMarkerAsync(writer, owner);
        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var disclosure = await DisclosureOfTheActiveMarkerAsync(lane, client, owner, ledgerEnd, markerCid);

        disclosure.ContractId.Should().Be(markerCid.Value);
        disclosure.TemplateId.Should().Be(Marker.TemplateId);
        disclosure.CreatedEventBlob.IsEmpty.Should().BeFalse();

        var archived = await writer.TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission.Single(markerCid.ArchiveCommand()).WithDisclosedContracts(disclosure),
            owner,
            cancellationToken: TestContext.Current.CancellationToken);

        archived.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the participant accepts the disclosure the typed read built")
            .Which.Result.ArchivedContractIds.Should().Contain(markerCid.Value);
    }

    [Fact]
    public async Task A_submission_disclosing_the_Marker_with_a_tampered_created_event_blob_is_rejected()
    {
        Assert.SkipUnless(
            SupportsExplicitDisclosure,
            "this lane has no participant to return a created_event_blob or to validate a disclosure");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        var markerCid = await CreateMarkerAsync(writer, owner);
        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var disclosure = await DisclosureOfTheActiveMarkerAsync(lane, client, owner, ledgerEnd, markerCid);

        var archived = await writer.TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission.Single(markerCid.ArchiveCommand())
                .WithDisclosedContracts(disclosure with { CreatedEventBlob = WithMiddleByteFlipped(disclosure.CreatedEventBlob) }),
            owner,
            cancellationToken: TestContext.Current.CancellationToken);

        archived.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>(
            "the participant validates a disclosed contract's blob rather than trusting it");
    }

    [Fact]
    public async Task SubscribeActiveAsync_not_asked_for_the_created_event_blob_returns_the_created_Marker_without_a_Disclosure()
    {
        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        var markerCid = await CreateMarkerAsync(writer, owner);
        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var entries = new List<AcsSnapshotEntry<Marker>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync<Marker>(
            owner, ledgerEnd, cancellationToken: TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        entries.OfType<AcsSnapshotEntry<Marker>.Created>()
            .Should().ContainSingle(created => created.ContractId.Equals(markerCid))
            .Which.Disclosure.Should().BeNull();
    }

    [Fact]
    public async Task QueryActiveAsync_asked_for_the_created_event_blob_discloses_the_Asset_behind_the_IHolding_view()
    {
        Assert.SkipUnless(
            SupportsExplicitDisclosure,
            "this lane has no participant to return a created_event_blob or to validate a disclosure");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, writer, client, owner) = lane.Capability;

        var assetCid = await CreateAssetAsync(writer, owner);
        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        IReadOnlyList<ActiveContract<InterfaceContract<IHolding, HoldingView>>> holdings;
        using (await lane.HoldStreamAsync(TestContext.Current.CancellationToken))
        {
            holdings = await client.QueryActiveAsync<IHolding, HoldingView>(
                owner, ledgerEnd, includeDisclosure: true, TestContext.Current.CancellationToken);
        }

        var disclosure = holdings.Should().ContainSingle(holding => holding.Contract.Id.Value == assetCid.Value)
            .Which.Disclosure.Should().NotBeNull().And.Subject.As<DisclosedContract>();
        disclosure.ContractId.Should().Be(assetCid.Value);
        disclosure.TemplateId.Should().Be(Asset.TemplateId);
        disclosure.CreatedEventBlob.IsEmpty.Should().BeFalse();

        var archived = await writer.TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission.Single(assetCid.ArchiveCommand()).WithDisclosedContracts(disclosure),
            owner,
            cancellationToken: TestContext.Current.CancellationToken);

        archived.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the participant accepts the disclosure the interface read built")
            .Which.Result.ArchivedContractIds.Should().Contain(assetCid.Value);
    }

    [Fact]
    public async Task SubscribeAsync_reports_a_bounded_read_the_participant_rejects_as_a_terminal_StreamError()
    {
        Assert.SkipUnless(
            SupportsParticipantRejectedReads,
            "this lane has no participant to reject a read, so it cannot express this failure at all");

        await using var lane = await OpenStreamerAsync(TestContext.Current.CancellationToken);
        var (reader, _, client, owner) = lane.Capability;

        var ledgerEnd = await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var pastTheLedgerEnd = LedgerOffset.At(ledgerEnd.Value + OffsetsPastTheLedgerEnd);

        var events = new List<ContractStreamEvent<Marker>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var evt in client.SubscribeAsync<Marker>(
            owner, ledgerEnd, pastTheLedgerEnd, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        var error = events.Should().ContainSingle(
            "a rejected read reports the rejection and nothing else").Subject
            .Should().BeOfType<ContractStreamEvent<Marker>.StreamError>(
            "every transport reports a rejected read in band rather than by throwing").Subject;
        error.Status.Should().NotBeOfType<TransportStatus.UndecodableBody>(
            "the participant reported this failure over the transport, so it carries the transport's status");
        error.Message.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<DisclosedContract> DisclosureOfTheActiveMarkerAsync(
        CapabilityLane<(ILedgerReader Reader, ILedgerWriter Writer, ICantonLedgerClient Client, Party Owner)> lane,
        ICantonLedgerClient client,
        Party owner,
        LedgerOffset ledgerEnd,
        ContractId<Marker> markerCid)
    {
        var entries = new List<AcsSnapshotEntry<Marker>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync<Marker>(
            owner, ledgerEnd, includeDisclosure: true, TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        return entries.OfType<AcsSnapshotEntry<Marker>.Created>()
            .Should().ContainSingle(created => created.ContractId.Equals(markerCid))
            .Which.Disclosure.Should().NotBeNull().And.Subject.As<DisclosedContract>();
    }

    private static byte[] WithMiddleByteFlipped(ReadOnlyMemory<byte> blob)
    {
        var tampered = blob.ToArray();
        tampered[tampered.Length / 2] ^= 0xFF;
        return tampered;
    }

    private static Task CreateMarkerBeforeTheWindowAsync(ILedgerWriter writer, Party owner) =>
        CreateMarkerAsync(writer, owner);

    private static Task CreateAssetBeforeTheWindowAsync(ILedgerWriter writer, Party owner) =>
        CreateAssetAsync(writer, owner);

    private static async Task<ContractId<Marker>> CreateMarkerAsync(ILedgerWriter writer, Party owner)
    {
        var outcome = await writer.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);
        return outcome.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>().Subject.Result;
    }

    private static async Task<ContractId<Asset>> CreateAssetAsync(ILedgerWriter writer, Party owner)
    {
        var outcome = await writer.TryCreateAsync(
            new Asset(owner, AssetAmount), owner, cancellationToken: TestContext.Current.CancellationToken);
        return outcome.Should().BeOfType<ExerciseOutcome<ContractId<Asset>>.One>().Subject.Result;
    }
}
