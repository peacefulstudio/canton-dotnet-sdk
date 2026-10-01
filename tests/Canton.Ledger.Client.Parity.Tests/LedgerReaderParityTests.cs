// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ILedgerReader"/>, run against every provider that
/// implements it (the in-memory Fake, REST, and gRPC) through one shared set of test bodies.
/// </summary>
public abstract class LedgerReaderParityTests
{
    /// <summary>Opens a lane over this provider's <see cref="ILedgerReader"/> for one test.</summary>
    protected abstract Task<CapabilityLane<ILedgerReader>> OpenReaderAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens a lane whose party has already had three <see cref="Marker"/> contracts created and one of
    /// them archived, over this provider's <see cref="ICantonLedgerClient"/>, for the typed-read rows.
    /// </summary>
    protected abstract Task<CapabilityLane<TypedReadsProbe>> OpenTypedReadsAsync(CancellationToken cancellationToken);

    private const int MostPagesAReadMayTake = 10;

    [Fact]
    public async Task GetLedgerEndAsync_returns_a_non_negative_offset()
    {
        await using var lane = await OpenReaderAsync(TestContext.Current.CancellationToken);

        var end = await lane.Capability.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        end.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetLedgerEndAsync_does_not_regress_across_two_consecutive_reads()
    {
        await using var lane = await OpenReaderAsync(TestContext.Current.CancellationToken);

        var first = await lane.Capability.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var second = await lane.Capability.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        second.Value.Should().BeGreaterThanOrEqualTo(first.Value);
    }

    [Fact]
    public async Task GetContractAsync_returns_the_created_Marker_with_its_owner_as_the_only_witness()
    {
        await using var lane = await OpenTypedReadsAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var contract = await probe.Client.GetContractAsync(
            probe.First, probe.Submitter, cancellationToken: TestContext.Current.CancellationToken);

        contract.ContractId.Value.Should().Be(probe.First.Value);
        contract.Payload.Owner.Value.Should().Be(probe.Owner.Value);
        contract.Key.Should().BeNull();
        contract.WitnessParties.Select(party => party.Value).Should().Equal(probe.Owner.Value);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_returns_the_creation_then_the_archival_of_the_archived_Marker()
    {
        await using var lane = await OpenTypedReadsAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var lifecycle = await probe.Client.GetEventsByContractIdAsync(
            probe.Archived, probe.Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created.Should().NotBeNull();
        lifecycle.Created!.ContractId.Value.Should().Be(probe.Archived.Value);
        lifecycle.Created.Payload.Owner.Value.Should().Be(probe.Owner.Value);
        lifecycle.Archived.Should().NotBeNull();
        lifecycle.Archived!.ContractId.Value.Should().Be(probe.Archived.Value);
        lifecycle.Archived.Offset.Value.Should().Be(probe.SeededThrough.Value);
        lifecycle.Created.Offset.Value.Should().BeLessThan(lifecycle.Archived.Offset.Value);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_returns_only_the_creation_of_an_active_Marker()
    {
        await using var lane = await OpenTypedReadsAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var lifecycle = await probe.Client.GetEventsByContractIdAsync(
            probe.First, probe.Submitter, cancellationToken: TestContext.Current.CancellationToken);

        lifecycle.Created.Should().NotBeNull();
        lifecycle.Created!.ContractId.Value.Should().Be(probe.First.Value);
        lifecycle.Created.Offset.Value.Should().Be(probe.FirstCreatedAt.Value);
        lifecycle.Archived.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_reads_the_two_active_Markers_in_pages_of_one_through_the_page_token()
    {
        await using var lane = await OpenTypedReadsAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var firstPage = await probe.Client.GetActiveContractsPageAsync<Marker>(
            probe.Submitter, probe.SeededThrough, maxPageSize: 1, cancellationToken: TestContext.Current.CancellationToken);
        var pages = new List<AcsPage<Marker>> { firstPage };
        while (pages[^1].NextPageToken is { } token && pages.Count < MostPagesAReadMayTake)
        {
            pages.Add(await probe.Client.GetActiveContractsPageAsync<Marker>(
                probe.Submitter,
                probe.SeededThrough,
                maxPageSize: 1,
                pageToken: token,
                cancellationToken: TestContext.Current.CancellationToken));
        }

        CreatedIds(firstPage).Should().HaveCount(1);
        firstPage.NextPageToken.Should().NotBeNull();
        pages.Should().OnlyContain(page => page.Entries.OfType<AcsSnapshotEntry<Marker>.Created>().Count() <= 1);
        pages.SelectMany(CreatedIds).Should().BeEquivalentTo([probe.First.Value, probe.Second.Value]);
        pages[^1].NextPageToken.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_at_ledger_begin_returns_an_empty_snapshot_without_a_next_page()
    {
        await using var lane = await OpenTypedReadsAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var page = await probe.Client.GetActiveContractsPageAsync<Marker>(
            probe.Submitter, LedgerOffset.Begin, cancellationToken: TestContext.Current.CancellationToken);

        page.Entries.Should().BeEmpty();
        page.NextPageToken.Should().BeNull();
        page.ActiveAtOffset.Value.Should().Be(0);
    }

    [Fact]
    public async Task GetUpdatesPageAsync_reads_the_four_seeded_updates_in_pages_of_two_within_the_bounds()
    {
        await using var lane = await OpenTypedReadsAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var pages = new List<UpdatesPage>();
        LedgerPageToken? token = null;
        do
        {
            pages.Add(await probe.Client.GetUpdatesPageAsync(
                probe.Submitter,
                probe.BeforeSeeding,
                probe.SeededThrough,
                maxPageSize: 2,
                pageToken: token,
                cancellationToken: TestContext.Current.CancellationToken));
            token = pages[^1].NextPageToken;
        }
        while (token is not null && pages.Count < MostPagesAReadMayTake);

        pages[0].Updates.Should().HaveCount(2);
        pages[0].NextPageToken.Should().NotBeNull();
        pages.Should().OnlyContain(page => page.Updates.Count <= 2);
        pages.Should().OnlyContain(page => page.LowestPageOffsetExclusive.Value >= probe.BeforeSeeding.Value);
        pages.Should().OnlyContain(page => page.HighestPageOffsetInclusive.Value <= probe.SeededThrough.Value);
        var updates = pages.SelectMany(page => page.Updates).ToList();
        updates.Should().HaveCount(4);
        updates[0].CreatedContracts.Select(created => created.ContractId).Should().Equal(probe.First.Value);
        updates[0].CompletionOffset.Value.Should().Be(probe.FirstCreatedAt.Value);
        updates[1].CreatedContracts.Select(created => created.ContractId).Should().Equal(probe.Second.Value);
        updates[2].CreatedContracts.Select(created => created.ContractId).Should().Equal(probe.Archived.Value);
        updates[3].ExercisedEvents.Should().ContainSingle(exercised => exercised.Consuming)
            .Which.ContractId.Should().Be(probe.Archived.Value);
        updates[3].CompletionOffset.Value.Should().Be(probe.SeededThrough.Value);
    }

    [Fact]
    public async Task GetLatestPrunedOffsetsAsync_reads_zero_for_both_offsets_on_an_unpruned_participant()
    {
        await using var lane = await OpenTypedReadsAsync(TestContext.Current.CancellationToken);
        var probe = lane.Capability;

        var pruned = await probe.Client.GetLatestPrunedOffsetsAsync(cancellationToken: TestContext.Current.CancellationToken);

        pruned.ParticipantPrunedUpToInclusive.Value.Should().Be(0);
        pruned.AllDivulgedContractsPrunedUpToInclusive.Value.Should().Be(0);
    }

    /// <summary>
    /// Seeds a live participant for the typed-read rows: two Markers that stay active, and a third that is
    /// archived straight after it is created. The four updates land in that order.
    /// </summary>
    protected static async Task<TypedReadsProbe> SeedAsync(
        ICantonLedgerClient client, Party owner, CancellationToken cancellationToken)
    {
        var beforeSeeding = await client.GetLedgerEndAsync(cancellationToken: cancellationToken);
        var first = await CreateMarkerAsync(client, owner, cancellationToken);
        var second = await CreateMarkerAsync(client, owner, cancellationToken);
        var archived = await CreateMarkerAsync(client, owner, cancellationToken);
        var archival = await SubmitAsync(
            client, CommandsSubmission.Single(archived.Id.ArchiveCommand()), owner, cancellationToken);

        return new TypedReadsProbe(
            client,
            owner,
            first.Id,
            second.Id,
            archived.Id,
            beforeSeeding,
            first.CreatedAt,
            archival.CompletionOffset);
    }

    private static async Task<(ContractId<Marker> Id, LedgerOffset CreatedAt)> CreateMarkerAsync(
        ICantonLedgerClient client, Party owner, CancellationToken cancellationToken)
    {
        var created = await SubmitAsync(
            client, CommandsSubmission.Single(CreateCommand.For(new Marker(owner))), owner, cancellationToken);
        return (new ContractId<Marker>(created.CreatedContracts.Single().ContractId), created.CompletionOffset);
    }

    private static async Task<TransactionResult> SubmitAsync(
        ICantonLedgerClient client, CommandsSubmission submission, Party owner, CancellationToken cancellationToken)
    {
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, owner, cancellationToken: cancellationToken);
        return outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject.Result;
    }

    private static IEnumerable<string> CreatedIds(AcsPage<Marker> page) =>
        page.Entries.OfType<AcsSnapshotEntry<Marker>.Created>().Select(created => created.ContractId.Value);
}

/// <summary>
/// The state a typed-read parity lane hands the shared bodies: the client under test, the party whose
/// visibility scopes every read, three seeded Markers, and the offsets the seeding writes landed at.
/// </summary>
/// <param name="Client">The client whose typed reads the bodies exercise.</param>
/// <param name="Owner">The Marker owner and the querying party.</param>
/// <param name="First">The first Marker created, still active.</param>
/// <param name="Second">The second Marker created, still active.</param>
/// <param name="Archived">The third Marker created, then archived.</param>
/// <param name="BeforeSeeding">The ledger end read before the first seeding write.</param>
/// <param name="FirstCreatedAt">The offset of the update that created <paramref name="First"/>.</param>
/// <param name="SeededThrough">The offset of the update that archived <paramref name="Archived"/>, the last seeding write.</param>
public sealed record TypedReadsProbe(
    ICantonLedgerClient Client,
    Party Owner,
    ContractId<Marker> First,
    ContractId<Marker> Second,
    ContractId<Marker> Archived,
    LedgerOffset BeforeSeeding,
    LedgerOffset FirstCreatedAt,
    LedgerOffset SeededThrough)
{
    /// <summary>The owner as the submitter whose parties scope each read.</summary>
    public SubmitterInfo Submitter => Owner;
}
