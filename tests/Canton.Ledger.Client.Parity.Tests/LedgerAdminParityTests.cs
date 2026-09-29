// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// The ids one admin parity test allocates, unique per test so live runs never collide with
/// earlier ones, and staged verbatim by providers that cannot allocate for real.
/// </summary>
/// <param name="PartyHint">The party id hint the test allocates.</param>
/// <param name="UserId">The user the test creates with the allocated party as primary party.</param>
/// <param name="UnknownUserId">A user id no provider knows.</param>
public sealed record AdminParityScenario(string PartyHint, string UserId, string UnknownUserId)
{
    /// <summary>Creates a scenario whose ids are unique to this call.</summary>
    public static AdminParityScenario CreateUnique()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new AdminParityScenario(
            $"admin-parity-{suffix}", $"admin-parity-user-{suffix}", $"admin-parity-unknown-{suffix}");
    }
}

/// <summary>
/// An <see cref="IAdminClient"/> paired with the synchronizer every admin parity test allocates
/// and vets against, so a multi-synchronizer participant exercises the same path a single-synchronizer
/// participant exercises implicitly by omitting the parameter.
/// </summary>
/// <param name="Admin">The provider's admin client.</param>
/// <param name="GlobalSynchronizerId">The participant's global synchronizer.</param>
/// <param name="IsMultiSynchronizer">
/// Whether the participant is connected to more than one synchronizer.
/// </param>
public sealed record AdminCapability(IAdminClient Admin, SynchronizerId GlobalSynchronizerId, bool IsMultiSynchronizer);

/// <summary>
/// Behavioral parity suite over <see cref="IAdminClient"/>, run against every provider that
/// implements it (the in-memory Fake, REST, and gRPC) through one shared set of test bodies.
/// </summary>
public abstract class LedgerAdminParityTests
{
    /// <summary>Opens a lane over this provider's <see cref="AdminCapability"/> for one scenario.</summary>
    protected abstract Task<CapabilityLane<AdminCapability>> OpenAdminAsync(
        AdminParityScenario scenario, CancellationToken cancellationToken);

    [Fact]
    public async Task GetParticipantIdAsync_returns_a_non_empty_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var participantId = await lane.Capability.Admin.GetParticipantIdAsync(cancellationToken);

        participantId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetPartiesAsync_reads_back_an_allocated_party_as_local()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var allocated = await lane.Capability.Admin.AllocatePartyAsync(
            scenario.PartyHint, synchronizerId: lane.Capability.GlobalSynchronizerId, cancellationToken: cancellationToken);
        var parties = await lane.Capability.Admin.GetPartiesAsync([allocated.Party], cancellationToken);

        allocated.Party.Value.Should().StartWith(scenario.PartyHint + "::");
        parties.Should().ContainSingle().Which.Should().Be(new PartyDetails(allocated.Party, IsLocal: true));
    }

    [Fact]
    public async Task ListKnownPartiesAsync_contains_an_allocated_party()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var allocated = await lane.Capability.Admin.AllocatePartyAsync(
            scenario.PartyHint, synchronizerId: lane.Capability.GlobalSynchronizerId, cancellationToken: cancellationToken);
        var known = await lane.Capability.Admin.ListKnownPartiesAsync(cancellationToken);

        known.Select(details => details.Party).Should().Contain(allocated.Party);
    }

    [Fact]
    public async Task GetUserAsync_and_ListUserRightsAsync_read_back_a_created_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var allocated = await lane.Capability.Admin.AllocatePartyAsync(
            scenario.PartyHint, synchronizerId: lane.Capability.GlobalSynchronizerId, cancellationToken: cancellationToken);
        var party = allocated.Party;
        var created = await lane.Capability.Admin.CreateUserAsync(
            scenario.UserId, party, [new UserRight.ActAs(party)], cancellationToken);
        var read = await lane.Capability.Admin.GetUserAsync(scenario.UserId, cancellationToken);
        var rights = await lane.Capability.Admin.ListUserRightsAsync(scenario.UserId, cancellationToken);

        created.Should().Be(new UserDetails(scenario.UserId, party));
        read.Should().Be(new UserDetails(scenario.UserId, party));
        rights.Should().ContainSingle().Which.Should().Be(new UserRight.ActAs(party));
    }

    [Fact]
    public async Task GetUserAsync_returns_null_for_an_unknown_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var user = await lane.Capability.Admin.GetUserAsync(scenario.UnknownUserId, cancellationToken);

        user.Should().BeNull();
    }

    [Fact]
    public async Task ListUserRightsAsync_returns_null_for_an_unknown_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var rights = await lane.Capability.Admin.ListUserRightsAsync(scenario.UnknownUserId, cancellationToken);

        rights.Should().BeNull();
    }

    [Fact]
    public async Task ValidateDarAsync_accepts_the_rich_types_corpus_DAR()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);
        var dar = await File.ReadAllBytesAsync(RichTypesDar.Path, cancellationToken);

        var validate = () => lane.Capability.Admin.ValidateDarAsync(
            dar, synchronizerId: lane.Capability.GlobalSynchronizerId, cancellationToken: cancellationToken);

        await validate.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListVettedPackagesAsync_returns_a_non_empty_list_without_a_name_prefix_filter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var vetted = await lane.Capability.Admin.ListVettedPackagesAsync(cancellationToken: cancellationToken);

        vetted.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetPackageAsync_downloads_an_archive_whose_hash_matches_the_vetted_package_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);
        var vetted = await lane.Capability.Admin.ListVettedPackagesAsync(cancellationToken: cancellationToken);
        var packageId = vetted[0].PackageId;

        var archive = await lane.Capability.Admin.GetPackageAsync(packageId, cancellationToken);

        archive.Payload.Length.Should().BeGreaterThan(0);
        archive.Hash.Should().Be(packageId);
    }
}

/// <summary>
/// The <see cref="LedgerAdminParityTests"/> facts only a live participant can answer, such as
/// resolving an empty user id to the user the lane authenticates as, and the multi-synchronizer
/// facts a live gRPC or REST lane can put on a real second synchronizer.
/// </summary>
public abstract class LiveLedgerAdminParityTests : LedgerAdminParityTests
{
    /// <summary>The alias LocalNet gives the synchronizer every admin parity test targets.</summary>
    protected const string GlobalSynchronizerAlias = "global";

    /// <summary>
    /// Resolves <paramref name="admin"/>'s <see cref="AdminCapability"/> from the synchronizers
    /// <paramref name="ledgerClient"/> reports the participant connected to.
    /// </summary>
    protected static async Task<AdminCapability> ResolveAdminCapabilityAsync(
        IAdminClient admin, ICantonLedgerClient ledgerClient, CancellationToken cancellationToken)
    {
        var connected = await ledgerClient.GetConnectedSynchronizersAsync(cancellationToken: cancellationToken);
        var global = connected.FirstOrDefault(
            synchronizer => synchronizer.SynchronizerAlias == GlobalSynchronizerAlias);

        if (global is null)
        {
            var aliases = string.Join(", ", connected.Select(synchronizer => synchronizer.SynchronizerAlias));
            throw new InvalidOperationException(
                $"No connected synchronizer with alias '{GlobalSynchronizerAlias}'. Connected: [{aliases}].");
        }

        return new AdminCapability(admin, new SynchronizerId(global.SynchronizerId), connected.Count > 1);
    }

    [Fact]
    public async Task GetUserAsync_and_ListUserRightsAsync_resolve_an_empty_user_id_to_the_authenticated_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var user = await lane.Capability.Admin.GetUserAsync(string.Empty, cancellationToken);
        var rights = await lane.Capability.Admin.ListUserRightsAsync(string.Empty, cancellationToken);

        user.Should().NotBeNull();
        user!.UserId.Should().NotBeNullOrWhiteSpace();
        rights.Should().NotBeNull();
    }

    [Fact]
    public async Task AllocatePartyAsync_without_a_synchronizerId_succeeds_on_a_single_synchronizer_participant_and_fails_on_a_multi_synchronizer_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var allocate = () => lane.Capability.Admin.AllocatePartyAsync(
            scenario.PartyHint, cancellationToken: cancellationToken);

        if (lane.Capability.IsMultiSynchronizer)
        {
            var thrown = (await allocate.Should().ThrowAsync<LedgerOperationException>()).Which;
            thrown.ErrorId.Should().Be("PARTY_ALLOCATION_CANNOT_DETERMINE_SYNCHRONIZER");
        }
        else
        {
            var allocated = await allocate();
            allocated.Party.Value.Should().StartWith(scenario.PartyHint + "::");
        }
    }
}
