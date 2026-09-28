// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
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
/// Behavioral parity suite over <see cref="IAdminClient"/>, run against every provider that
/// implements it (the in-memory Fake, REST, and gRPC) through one shared set of test bodies.
/// </summary>
public abstract class LedgerAdminParityTests
{
    /// <summary>Opens a lane over this provider's <see cref="IAdminClient"/> for one scenario.</summary>
    protected abstract Task<CapabilityLane<IAdminClient>> OpenAdminAsync(
        AdminParityScenario scenario, CancellationToken cancellationToken);

    [Fact]
    public async Task GetParticipantIdAsync_returns_a_non_empty_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var participantId = await lane.Capability.GetParticipantIdAsync(cancellationToken);

        participantId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetPartiesAsync_reads_back_an_allocated_party_as_local()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var allocated = await lane.Capability.AllocatePartyAsync(scenario.PartyHint, cancellationToken: cancellationToken);
        var parties = await lane.Capability.GetPartiesAsync([allocated.Party], cancellationToken);

        allocated.Party.Value.Should().StartWith(scenario.PartyHint + "::");
        parties.Should().ContainSingle().Which.Should().Be(new PartyDetails(allocated.Party, IsLocal: true));
    }

    [Fact]
    public async Task ListKnownPartiesAsync_contains_an_allocated_party()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var allocated = await lane.Capability.AllocatePartyAsync(scenario.PartyHint, cancellationToken: cancellationToken);
        var known = await lane.Capability.ListKnownPartiesAsync(cancellationToken);

        known.Select(details => details.Party).Should().Contain(allocated.Party);
    }

    [Fact]
    public async Task GetUserAsync_and_ListUserRightsAsync_read_back_a_created_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var allocated = await lane.Capability.AllocatePartyAsync(scenario.PartyHint, cancellationToken: cancellationToken);
        var party = allocated.Party;
        var created = await lane.Capability.CreateUserAsync(
            scenario.UserId, party, [new UserRight.ActAs(party)], cancellationToken);
        var read = await lane.Capability.GetUserAsync(scenario.UserId, cancellationToken);
        var rights = await lane.Capability.ListUserRightsAsync(scenario.UserId, cancellationToken);

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

        var user = await lane.Capability.GetUserAsync(scenario.UnknownUserId, cancellationToken);

        user.Should().BeNull();
    }

    [Fact]
    public async Task ListUserRightsAsync_returns_null_for_an_unknown_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = AdminParityScenario.CreateUnique();
        await using var lane = await OpenAdminAsync(scenario, cancellationToken);

        var rights = await lane.Capability.ListUserRightsAsync(scenario.UnknownUserId, cancellationToken);

        rights.Should().BeNull();
    }

    [Fact]
    public async Task ValidateDarAsync_accepts_the_rich_types_corpus_DAR()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);
        var dar = await File.ReadAllBytesAsync(RichTypesDar.Path, cancellationToken);

        var validate = () => lane.Capability.ValidateDarAsync(dar, cancellationToken);

        await validate.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListVettedPackagesAsync_returns_a_non_empty_list_without_a_name_prefix_filter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var vetted = await lane.Capability.ListVettedPackagesAsync(cancellationToken: cancellationToken);

        vetted.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetPackageAsync_downloads_an_archive_whose_hash_matches_the_vetted_package_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);
        var vetted = await lane.Capability.ListVettedPackagesAsync(cancellationToken: cancellationToken);
        var packageId = vetted[0].PackageId;

        var archive = await lane.Capability.GetPackageAsync(packageId, cancellationToken);

        archive.Payload.Length.Should().BeGreaterThan(0);
        archive.Hash.Should().Be(packageId);
    }
}

/// <summary>
/// The <see cref="LedgerAdminParityTests"/> facts only a live participant can answer, such as
/// resolving an empty user id to the user the lane authenticates as.
/// </summary>
public abstract class LiveLedgerAdminParityTests : LedgerAdminParityTests
{
    [Fact]
    public async Task GetUserAsync_and_ListUserRightsAsync_resolve_an_empty_user_id_to_the_authenticated_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAdminAsync(AdminParityScenario.CreateUnique(), cancellationToken);

        var user = await lane.Capability.GetUserAsync(string.Empty, cancellationToken);
        var rights = await lane.Capability.ListUserRightsAsync(string.Empty, cancellationToken);

        user.Should().NotBeNull();
        user!.UserId.Should().NotBeNullOrWhiteSpace();
        rights.Should().NotBeNull();
    }
}
